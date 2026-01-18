// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using CSharp.Core.Image;

using CSharp.Core.Extensions;

namespace DTC.Z80.Devices;

/// <summary>
/// Minimal Sega Master System VDP implementation for BIOS bring-up.
/// </summary>
/// <remarks>
/// Handles VRAM/CRAM access, basic VBlank timing, and a simple background/sprite renderer.
/// </remarks>
public sealed class SmsVdp
{
    public const int FrameWidth = 256;
    public const int FrameHeight = 192;

    private const int VramSize = 0x4000;
    private const int CramSize = 32;
    private const int RegisterCount = 16;
    private const int CyclesPerScanline = 228;
    private const int TotalScanlines = 262;
    private const int VblankStartLine = 192;
    private const byte StatusVblankBit = 0x80;

    // Background tile attribute bit positions in Mode 4
    private const int AttrBitTileIndexMsb = 0;   // Name table high byte bit 0 -> tile index bit 8
    private const int AttrBitHFlip        = 1;   // Horizontal flip
    private const int AttrBitVFlip        = 2;   // Vertical flip
    private const int AttrBitPalette      = 3;   // Palette select (0/1)
    private const int AttrBitPriority     = 4;   // BG priority over sprites

    private readonly byte[] m_vram = new byte[VramSize];
    private readonly byte[] m_cram = new byte[CramSize];
    private readonly byte[] m_registers = new byte[RegisterCount];
    private readonly byte[] m_frameBuffer = new byte[FrameWidth * FrameHeight * 4];

    // Per-pixel metadata buffers used for correct sprite/background compositing.
    // Priority buffer: 1 when BG tile priority bit is set for the pixel; 0 otherwise.
    private readonly byte[] m_bgPriority = new byte[FrameWidth * FrameHeight];

    // Tracks how many sprites are present on each scanline (for 8 sprites/line rule).
    private readonly int[] m_spritesOnLine = new int[FrameHeight];

    private ushort m_address;
    private byte m_readBuffer;
    private byte m_controlLatchLow;
    private bool m_isControlLatchFull;
    private AccessMode m_accessMode = AccessMode.VramRead;

    private int m_cycleAccumulator;
    private int m_vCounter;
    private int m_lineCounter;
    private byte m_status;
    private bool m_interruptPending;

    public event EventHandler<byte[]> FrameRendered;

    public bool IsBackgroundVisible { get; set; } = true;

    public bool AreSpritesVisible { get; set; } = true;

    public void Reset()
    {
        Array.Clear(m_vram, 0, m_vram.Length);
        Array.Clear(m_cram, 0, m_cram.Length);
        Array.Clear(m_registers, 0, m_registers.Length);

        // Approx power-on defaults. These keep name/sprite tables in the expected high VRAM regions for early BIOS bring-up.
        m_registers[2] = 0xFF; // Name table base -> 0x3800.
        m_registers[4] = 0xFF; // Pattern base bit set (commonly 0x0000/0x2000 depending on mode); corrected by fallback in RenderFrame().
        m_registers[5] = 0xFF; // Sprite attribute table base -> 0x3F00.
        m_registers[6] = 0xFF; // Sprite pattern base bit.
        Array.Clear(m_frameBuffer, 0, m_frameBuffer.Length);
        Array.Clear(m_bgPriority, 0, m_bgPriority.Length);
        Array.Clear(m_spritesOnLine, 0, m_spritesOnLine.Length);
        m_address = 0;
        m_readBuffer = 0;
        m_controlLatchLow = 0;
        m_isControlLatchFull = false;
        m_accessMode = AccessMode.VramRead;
        m_cycleAccumulator = 0;
        m_vCounter = 0;
        m_lineCounter = m_registers[10];
        m_status = 0;
        m_interruptPending = false;
    }

    public void AdvanceCycles(long tStates)
    {
        m_cycleAccumulator += (int)tStates;
        while (m_cycleAccumulator >= CyclesPerScanline)
        {
            m_cycleAccumulator -= CyclesPerScanline;
            AdvanceScanline();
        }
    }

    public byte ReadData()
    {
        // Any access via the data port resets the 2-byte control word latch.
        m_isControlLatchFull = false;

        if (m_accessMode != AccessMode.VramRead)
            return 0;

        // VRAM reads are buffered: return the current read buffer, then fetch the next byte.
        var value = m_readBuffer;
        m_readBuffer = m_vram[m_address & 0x3FFF];
        m_address = (ushort)((m_address + 1) & 0x3FFF);
        return value;
    }

    public void WriteData(byte value)
    {
        m_isControlLatchFull = false;
        switch (m_accessMode)
        {
            case AccessMode.VramWrite:
                m_vram[m_address & 0x3FFF] = value;
                m_address = (ushort)((m_address + 1) & 0x3FFF);
                break;
            case AccessMode.CramWrite:
                m_cram[m_address & 0x1F] = value;
                m_address = (ushort)((m_address + 1) & 0x1F);
                break;
        }
        // Writes update the read buffer on real hardware.
        m_readBuffer = value;
    }

    public byte ReadStatus()
    {
        var status = m_status;
        m_status &= unchecked((byte)~StatusVblankBit);
        m_isControlLatchFull = false;
        m_interruptPending = false;
        return status;
    }

    public byte ReadVCounter() => (byte)m_vCounter;

    public byte ReadHCounter()
    {
        var position = (m_cycleAccumulator * 256) / CyclesPerScanline;
        if (position < 0)
            position = 0;
        if (position > 255)
            position = 255;
        return (byte)position;
    }

    public void WriteControl(byte value)
    {
        if (!m_isControlLatchFull)
        {
            m_controlLatchLow = value;
            m_isControlLatchFull = true;

            // The low byte is latched immediately into the address register (upper bits preserved).
            m_address = (ushort)((m_address & 0x3F00) | value);
            return;
        }

        var high = value;
        var command = (byte)(high >> 6);
        if (command == 0b10)
        {
            var regIndex = high & 0x0F;
            if (regIndex < m_registers.Length)
            {
                m_registers[regIndex] = m_controlLatchLow;
                if (regIndex == 10)
                    m_lineCounter = m_controlLatchLow;
            }
        }
        else
        {
            m_address = (ushort)((m_controlLatchLow | ((high & 0x3F) << 8)) & 0x3FFF);
            m_accessMode = command switch
            {
                0b00 => AccessMode.VramRead,
                0b01 => AccessMode.VramWrite,
                0b11 => AccessMode.CramWrite,
                _ => m_accessMode
            };

            // Entering VRAM read mode preloads the read buffer and increments the address.
            if (m_accessMode == AccessMode.VramRead)
            {
                m_readBuffer = m_vram[m_address & 0x3FFF];
                m_address = (ushort)((m_address + 1) & 0x3FFF);
            }
        }

        m_isControlLatchFull = false;
    }

    private void AdvanceScanline()
    {
        m_vCounter++;
        if (m_vCounter < VblankStartLine)
            AdvanceLineCounter();
        if (m_vCounter == VblankStartLine)
        {
            m_status |= StatusVblankBit;
            if ((m_registers[1] & 0x20) != 0)
                m_interruptPending = true;
            RenderFrame();
            FrameRendered?.Invoke(this, m_frameBuffer);
        }
        else if (m_vCounter >= TotalScanlines)
        {
            m_vCounter = 0;
            m_lineCounter = m_registers[10];
        }
    }

    private void AdvanceLineCounter()
    {
        if (m_lineCounter > 0)
            m_lineCounter--;

        if (m_lineCounter == 0)
        {
            if (m_registers[0].IsBitSet(4))
                m_interruptPending = true;
            m_lineCounter = m_registers[10];
        }
    }

    public bool TryConsumeInterrupt()
    {
        if (!m_interruptPending)
            return false;

        m_interruptPending = false;
        return true;
    }

    private void RenderFrame()
    {
        // Use the VDP registers directly. Heuristics are useful for bring-up, but can break when games switch tables mid-frame or between screens.
        var nameTableBase = ((m_registers[2] & 0x0E) << 10) & 0x3FFF;

        // Mode 4: Background tile pattern addresses are derived directly from the tile index.
        // (i.e. tile N starts at VRAM offset N*32.)

        var scrollX = m_registers[8];
        var scrollY = m_registers[9];

        // Clear per-frame metadata buffers.
        Array.Clear(m_bgPriority, 0, m_bgPriority.Length);
        Array.Clear(m_spritesOnLine, 0, m_spritesOnLine.Length);

        for (var y = 0; y < FrameHeight; y++)
        {
            var sourceY = (y + scrollY) & 0xFF;
            var tileY = sourceY >> 3;
            var rowInTile = sourceY & 7;
            var rowBase = tileY * 32;
            for (var x = 0; x < FrameWidth; x++)
            {
                var (b, g, r) = DecodeBackdropColor();
                var bgPriority = false;
                var colorIndex = 0;

                if (IsBackgroundVisible)
                {
                    var sourceX = (x + scrollX) & 0xFF;
                    var tileX = sourceX >> 3;
                    var colInTile = sourceX & 7;

                    var entryAddr = (nameTableBase + (rowBase + tileX) * 2) & 0x3FFF;
                    var low = m_vram[entryAddr];
                    var high = m_vram[(entryAddr + 1) & 0x3FFF];

                    var tileIndex = low | (((high >> AttrBitTileIndexMsb) & 0x01) << 8);
                    var hFlip = high.IsBitSet(AttrBitHFlip);
                    var vFlip = high.IsBitSet(AttrBitVFlip);
                    var palette = high.IsBitSet(AttrBitPalette) ? 1 : 0;
                    bgPriority = high.IsBitSet(AttrBitPriority); // BG priority over sprites

                    var tileBase = (tileIndex * 32) & 0x3FFF;
                    var sourceRow = vFlip ? 7 - rowInTile : rowInTile;
                    var rowAddr = (tileBase + sourceRow * 4) & 0x3FFF;
                    var plane0 = m_vram[rowAddr];
                    var plane1 = m_vram[(rowAddr + 1) & 0x3FFF];
                    var plane2 = m_vram[(rowAddr + 2) & 0x3FFF];
                    var plane3 = m_vram[(rowAddr + 3) & 0x3FFF];

                    var bit = hFlip ? colInTile : 7 - colInTile;
                    colorIndex = ((plane0 >> bit) & 0x01) |
                                 (((plane1 >> bit) & 0x01) << 1) |
                                 (((plane2 >> bit) & 0x01) << 2) |
                                 (((plane3 >> bit) & 0x01) << 3);

                    if (colorIndex != 0)
                    {
                        var decoded = DecodeColor(palette, colorIndex);
                        b = decoded.b;
                        g = decoded.g;
                        r = decoded.r;
                    }
                }

                var pixelOffset = (y * FrameWidth + x) * 4;
                m_frameBuffer[pixelOffset] = b;
                m_frameBuffer[pixelOffset + 1] = g;
                m_frameBuffer[pixelOffset + 2] = r;
                m_frameBuffer[pixelOffset + 3] = 255;

                // Item (1): Record BG priority only when BG pixel is visible (color != 0).
                m_bgPriority[(y * FrameWidth) + x] = (byte)(bgPriority && colorIndex != 0 ? 1 : 0);
            }
        }

        if (AreSpritesVisible)
            RenderSprites();
    }

    private (byte b, byte g, byte r) DecodeColor(int palette, int colorIndex)
    {
        var cramIndex = (palette * 16 + colorIndex) & 0x1F;
        var value = m_cram[cramIndex];

        var r = (byte)((value & 0x03) * 85);
        var g = (byte)(((value >> 2) & 0x03) * 85);
        var b = (byte)(((value >> 4) & 0x03) * 85);
        return (b, g, r);
    }

    private (byte b, byte g, byte r) DecodeBackdropColor()
    {
        var index = m_registers[7] & 0x0F;
        var value = m_cram[index & 0x1F];
        var r = (byte)((value & 0x03) * 85);
        var g = (byte)(((value >> 2) & 0x03) * 85);
        var b = (byte)(((value >> 4) & 0x03) * 85);
        return (b, g, r);
    }


    /// <summary>
    /// Dump the current frame buffer to disk (.tga).
    /// </summary>
    public void DumpFrame(FileInfo tgaFile)
    {
        if (tgaFile == null)
            throw new ArgumentNullException(nameof(tgaFile));
        var rgba = ConvertBgraToRgba(m_frameBuffer);
        TgaWriter.Write(tgaFile, rgba, FrameWidth, FrameHeight, 4);
    }

    /// <summary>
    /// Dump the sprite pattern table as a tile map to disk (.tga).
    /// </summary>
    public void DumpSpriteTileMap(FileInfo tgaFile)
    {
        if (tgaFile == null)
            throw new ArgumentNullException(nameof(tgaFile));

        const int tilesPerRow = 16;
        const int tileSize = 8;
        const int tileCount = 256;
        var width = tilesPerRow * tileSize;
        var height = (tileCount / tilesPerRow) * tileSize;
        var buffer = new byte[width * height * 4];

        var spritePatternBase = (m_registers[6] & 0x04) << 11;
        for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            var tileX = (tileIndex % tilesPerRow) * tileSize;
            var tileY = (tileIndex / tilesPerRow) * tileSize;
            var tileBase = (spritePatternBase + tileIndex * 32) & 0x3FFF;

            for (var row = 0; row < tileSize; row++)
            {
                var rowAddr = (tileBase + row * 4) & 0x3FFF;
                var plane0 = m_vram[rowAddr];
                var plane1 = m_vram[(rowAddr + 1) & 0x3FFF];
                var plane2 = m_vram[(rowAddr + 2) & 0x3FFF];
                var plane3 = m_vram[(rowAddr + 3) & 0x3FFF];

                for (var col = 0; col < tileSize; col++)
                {
                    var bit = 7 - col;
                    var colorIndex = ((plane0 >> bit) & 0x01) |
                                     (((plane1 >> bit) & 0x01) << 1) |
                                     (((plane2 >> bit) & 0x01) << 2) |
                                     (((plane3 >> bit) & 0x01) << 3);

                    var (b, g, r) = colorIndex == 0
                        ? DecodeBackdropColor()
                        : DecodeColor(1, colorIndex);

                    var destX = tileX + col;
                    var destY = tileY + row;
                    var offset = (destY * width + destX) * 4;
                    buffer[offset] = b;
                    buffer[offset + 1] = g;
                    buffer[offset + 2] = r;
                    buffer[offset + 3] = 255;
                }
            }
        }

        var rgba = ConvertBgraToRgba(buffer);
        TgaWriter.Write(tgaFile, rgba, width, height, 4);
    }

    private static byte[] ConvertBgraToRgba(byte[] buffer)
    {
        var converted = new byte[buffer.Length];
        for (var i = 0; i + 3 < buffer.Length; i += 4)
        {
            converted[i] = buffer[i + 2];
            converted[i + 1] = buffer[i + 1];
            converted[i + 2] = buffer[i];
            converted[i + 3] = buffer[i + 3];
        }

        return converted;
    }

    private void RenderSprites()
    {
        var spriteTableBase = (m_registers[5] & 0x7E) << 7;
        var spritePatternBase = (m_registers[6] & 0x04) << 11;
        var spriteHeight = (m_registers[1] & 0x02) != 0 ? 16 : 8;
        var zoom = m_registers[1].IsBitSet(0);
        var spriteShift = m_registers[0].IsBitSet(3) ? -8 : 0;

        // Build list until terminator (0xD0), then draw in reverse order so low indices appear on top.
        Span<int> indices = stackalloc int[64];
        var count = 0;
        for (var i = 0; i < 64; i++)
        {
            var yTerm = m_vram[(spriteTableBase + i) & 0x3FFF];
            if (yTerm == 0xD0)
                break;
            indices[count++] = i;
        }

        for (var idx = count - 1; idx >= 0; idx--)
        {
            var i = indices[idx];
            var y = m_vram[(spriteTableBase + i) & 0x3FFF];
            var spriteY = y + 1;
            if (spriteY > 240)
                spriteY -= 256;
            if (spriteY >= FrameHeight)
                continue;

            var entryBase = (spriteTableBase + 0x80 + i * 2) & 0x3FFF;
            var spriteX = m_vram[entryBase] + spriteShift;
            var tileIndex = m_vram[(entryBase + 1) & 0x3FFF];
            if (spriteHeight == 16)
                tileIndex &= 0xFE;

            DrawSprite(spriteX, spriteY, tileIndex, spriteHeight, spritePatternBase, zoom);
        }
    }

    private void DrawSprite(int x, int y, int tileIndex, int height, int patternBase, bool zoom)
    {
        if (x >= FrameWidth || y >= FrameHeight)
            return;

        var scale = zoom ? 2 : 1;
        Span<int> lineYs = stackalloc int[2];
        Span<bool> lineDraw = stackalloc bool[2];
        for (var row = 0; row < height; row++)
        {
            var tileOffset = row >= 8 ? 1 : 0;
            var rowInTile = row & 7;
            var tileBase = (patternBase + (tileIndex + tileOffset) * 32) & 0x3FFF;
            var rowAddr = (tileBase + rowInTile * 4) & 0x3FFF;
            var plane0 = m_vram[rowAddr];
            var plane1 = m_vram[(rowAddr + 1) & 0x3FFF];
            var plane2 = m_vram[(rowAddr + 2) & 0x3FFF];
            var plane3 = m_vram[(rowAddr + 3) & 0x3FFF];

            var destY = y + row * scale;
            if (destY >= FrameHeight)
                break;

            var maxY = Math.Min(FrameHeight, destY + scale);
            var lineCount = 0;
            for (var py = destY; py < maxY; py++)
            {
                var canDraw = m_spritesOnLine[py] < 8;
                if (!canDraw)
                {
                    m_status |= 0x20; // sprite overflow
                }
                else
                {
                    m_spritesOnLine[py]++;
                }

                lineYs[lineCount] = py;
                lineDraw[lineCount] = canDraw;
                lineCount++;
            }

            for (var col = 0; col < 8; col++)
            {
                var bit = 7 - col;
                var colorIndex = ((plane0 >> bit) & 0x01) |
                                 (((plane1 >> bit) & 0x01) << 1) |
                                 (((plane2 >> bit) & 0x01) << 2) |
                                 (((plane3 >> bit) & 0x01) << 3);
                if (colorIndex == 0)
                    continue;

                var (b, g, r) = DecodeColor(1, colorIndex);
                var destX = x + col * scale;
                if (destX < 0 || destX >= FrameWidth)
                    continue;

                var maxX = Math.Min(FrameWidth, destX + scale);
                for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
                {
                    if (!lineDraw[lineIndex])
                        continue;

                    var py = lineYs[lineIndex];
                    var rowOffset = (py * FrameWidth + destX) * 4;
                    for (var px = destX; px < maxX; px++)
                    {
                        // Item (5): Apply BG priority masking per pixel (px,py), including when sprites are zoomed.
                        // This ensures scaled sprite blocks respect BG priority at every covered pixel.
                        if (m_bgPriority[(py * FrameWidth) + px] != 0)
                            continue;

                        var offset = rowOffset + (px - destX) * 4;
                        m_frameBuffer[offset] = b;
                        m_frameBuffer[offset + 1] = g;
                        m_frameBuffer[offset + 2] = r;
                        m_frameBuffer[offset + 3] = 255;
                    }
                }
            }
        }
    }

    private enum AccessMode : byte
    {
        VramRead = 0,
        VramWrite = 1,
        CramWrite = 3
    }
}
