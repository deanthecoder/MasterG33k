// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using DTC.Core;
using DTC.Core.Image;

using DTC.Core.Extensions;

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
    private const byte StatusSpriteOverflowBit = 0x40;
    private const byte StatusSpriteCollisionBit = 0x20;

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
    private readonly ulong[] m_spriteMaskPerLine = new ulong[FrameHeight];
    private readonly byte[] m_spriteCollision = new byte[FrameWidth * FrameHeight];

    private ushort m_address;
    private byte m_readBuffer;
    private byte m_controlLatchLow;
    private bool m_isControlLatchFull;
    private AccessMode m_accessMode = AccessMode.VramRead;
    private byte m_hCounterLatched;

    private int m_cycleAccumulator;
    private int m_vCounter;
    private int m_lineCounter;
    private byte m_status;
    private bool m_interruptPending;
    private bool m_wasMode4;
    private int m_lastDisplayHeight;
    private bool m_warnedNonMode4;
    private bool m_warnedMode2;
    private bool m_warnedMode4WithoutMode2;

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
        m_registers[4] = 0xFF; // Pattern/colour table base (used by legacy VDP modes; Mode 4 BG tiles are addressed as tileIndex*32).
        m_registers[5] = 0xFF; // Sprite attribute table base -> 0x3F00.
        m_registers[6] = 0xFF; // Sprite pattern base bit.
        Array.Clear(m_frameBuffer, 0, m_frameBuffer.Length);
        Array.Clear(m_bgPriority, 0, m_bgPriority.Length);
        Array.Clear(m_spritesOnLine, 0, m_spritesOnLine.Length);
        Array.Clear(m_spriteCollision, 0, m_spriteCollision.Length);
        m_address = 0;
        m_readBuffer = 0;
        m_controlLatchLow = 0;
        m_isControlLatchFull = false;
        m_accessMode = AccessMode.VramRead;
        m_hCounterLatched = 0;
        m_cycleAccumulator = 0;
        m_vCounter = 0;
        m_lineCounter = m_registers[10];
        m_status = 0;
        m_interruptPending = false;
        m_wasMode4 = true;
        m_lastDisplayHeight = FrameHeight;
        m_warnedNonMode4 = false;
        m_warnedMode2 = false;
        m_warnedMode4WithoutMode2 = false;
    }

    public void ApplyPostBiosState()
    {
        Reset();

        // Post-BIOS defaults expected by most Master System titles.
        m_registers[0] = 0x36;
        m_registers[1] = 0xA0;
        m_registers[2] = 0xFF;
        m_registers[3] = 0xFF;
        m_registers[4] = 0xFF;
        m_registers[5] = 0xFF;
        m_registers[6] = 0xFB;
        m_registers[7] = 0x00;
        m_registers[8] = 0x00;
        m_registers[9] = 0x00;
        m_registers[10] = 0xFF;
        m_lineCounter = m_registers[10];

        MonitorVdpMode();
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
            case AccessMode.VramRead:
            case AccessMode.VramWrite:
                m_vram[m_address & 0x3FFF] = value;
                m_address = (ushort)((m_address + 1) & 0x3FFF);
                break;
            case AccessMode.CramWrite:
                m_cram[m_address & 0x1F] = value;
                m_address = (ushort)((m_address + 1) & 0x3FFF);
                break;
        }
        // Writes update the read buffer on real hardware.
        m_readBuffer = value;
    }

    public byte ReadStatus()
    {
        var status = m_status;
        m_status &= 0x1F; // Clear VBlank + sprite flags on read.
        m_isControlLatchFull = false;
        m_interruptPending = false;
        return status;
    }

    public byte ReadVCounter()
    {
        if (m_vCounter <= 218)
            return (byte)m_vCounter;

        return (byte)(m_vCounter - 219 + 0xD5);
    }

    public byte ReadHCounter()
    {
        return m_hCounterLatched;
    }

    public void LatchHCounter()
    {
        var position = (m_cycleAccumulator * 256) / CyclesPerScanline;
        if (position < 0)
            position = 0;
        if (position > 255)
            position = 255;
        m_hCounterLatched = (byte)position;
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
                MonitorVdpMode();
            }

            m_address = (ushort)((m_controlLatchLow | ((high & 0x3F) << 8)) & 0x3FFF);
            m_accessMode = AccessMode.VramWrite;
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

    private void MonitorVdpMode()
    {
        // Mode 4 is enabled by setting Reg 0 bit 2 (M4). Sega docs also recommend keeping Reg 0 bit 1 set in Mode 4.
        var isMode4 = m_registers[0].IsBitSet(2);

        WarnIfUnsupportedHeight();
        WarnIfUnsupportedModeAssumptions();

        if (isMode4 == m_wasMode4)
            return;

        m_wasMode4 = isMode4;

        var modeText = isMode4 ? "Mode 4" : "Non-Mode4";
        Logger.Instance.Warn($"[VDP] Mode changed to {modeText}. R0=0x{m_registers[0]:X2} R1=0x{m_registers[1]:X2} line={m_vCounter}.");
    }

    private void WarnIfUnsupportedModeAssumptions()
    {
        var mode2 = m_registers[0].IsBitSet(1);
        var mode4 = m_registers[0].IsBitSet(2);

        if (!mode4)
        {
            if (!m_warnedNonMode4)
            {
                Logger.Instance.Warn($"[VDP] Non-Mode4 selected but renderer assumes Mode 4. R0=0x{m_registers[0]:X2} R1=0x{m_registers[1]:X2} line={m_vCounter}.");
                m_warnedNonMode4 = true;
            }
        }
        else
        {
            m_warnedNonMode4 = false;
        }

        if (mode2 && !mode4)
        {
            if (!m_warnedMode2)
            {
                Logger.Instance.Warn($"[VDP] Mode 2 selected but renderer assumes Mode 4. R0=0x{m_registers[0]:X2} R1=0x{m_registers[1]:X2} line={m_vCounter}.");
                m_warnedMode2 = true;
            }
        }
        else
        {
            m_warnedMode2 = false;
        }

        if (mode4 && !mode2)
        {
            if (!m_warnedMode4WithoutMode2)
            {
                Logger.Instance.Warn($"[VDP] Mode 4 bit set without Mode 2 bit; renderer assumes Mode 4 configuration. R0=0x{m_registers[0]:X2} R1=0x{m_registers[1]:X2} line={m_vCounter}.");
                m_warnedMode4WithoutMode2 = true;
            }
        }
        else
        {
            m_warnedMode4WithoutMode2 = false;
        }
    }

    private void WarnIfUnsupportedHeight()
    {
        var mode2Enabled = m_registers[0].IsBitSet(1);
        var isMedium = mode2Enabled && m_registers[1].IsBitSet(4);
        var isLarge = mode2Enabled && m_registers[1].IsBitSet(3);
        var height = isLarge ? 240 : isMedium ? 224 : FrameHeight;

        if (height == m_lastDisplayHeight)
            return;

        m_lastDisplayHeight = height;

        if (height != FrameHeight)
        {
            Logger.Instance.Warn($"[VDP] Unsupported display height selected ({height} lines). R0=0x{m_registers[0]:X2} R1=0x{m_registers[1]:X2} line={m_vCounter}.");
        }
    }

    private void AdvanceScanline()
    {
        var line = m_vCounter;

        // Start-of-frame housekeeping.
        if (line == 0)
        {
            BeginFrame();
        }

        var displayEnabled = m_registers[1].IsBitSet(6);

        // Visible area work.
        if (line < VblankStartLine)
        {
            AdvanceLineCounter();
            RenderBackgroundScanline(line, displayEnabled);
        }

        // Advance to next line.
        m_vCounter++;

        if (m_vCounter == VblankStartLine)
        {
            m_status |= StatusVblankBit;
            if ((m_registers[1] & 0x20) != 0)
                m_interruptPending = true;

            // Composite sprites once the background priority buffer is fully populated.
            if (AreSpritesVisible)
                RenderSprites(displayEnabled);

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

    private void BeginFrame()
    {
        Array.Clear(m_frameBuffer, 0, m_frameBuffer.Length);
        Array.Clear(m_bgPriority, 0, m_bgPriority.Length);
        Array.Clear(m_spritesOnLine, 0, m_spritesOnLine.Length);
        Array.Clear(m_spriteCollision, 0, m_spriteCollision.Length);
        Array.Clear(m_spriteMaskPerLine, 0, m_spriteMaskPerLine.Length);
    }

    private void RenderBackgroundScanline(int y, bool displayEnabled)
    {
        // Use the VDP registers directly.
        var nameTableBase = ((m_registers[2] & 0x0E) << 10) & 0x3FFF;

        // Mode 4: Background tile pattern addresses are derived directly from the tile index.
        // (i.e. tile N starts at VRAM offset N*32.)

        // Scroll registers.
        // Note: In Mode 4, Reg 0 contains scroll masking controls.
        //  - Bit 6: Disable horizontal scrolling for the top 2 tile rows (16 pixels).
        //  - Bit 7: Disable vertical scrolling for the rightmost 8 tile columns (64 pixels).
        var scrollX = m_registers[8];
        var scrollY = m_registers[9];

        // (1) Horizontal scroll lock for top 2 rows when enabled.
        if (m_registers[0].IsBitSet(6) && y < 16)
            scrollX = 0;

        // (3) Left column blanking (8 pixels) when enabled.
        var isLeftColumnBlanked = m_registers[0].IsBitSet(5);

        if (!displayEnabled || !IsBackgroundVisible)
        {
            var (bb, bg, br) = DecodeBackdropColor();
            for (var x = 0; x < FrameWidth; x++)
            {
                var offset = (y * FrameWidth + x) * 4;
                m_frameBuffer[offset] = bb;
                m_frameBuffer[offset + 1] = bg;
                m_frameBuffer[offset + 2] = br;
                m_frameBuffer[offset + 3] = 255;
                m_bgPriority[(y * FrameWidth) + x] = 0;
            }

            return;
        }

        for (var x = 0; x < FrameWidth; x++)
        {
            var b = (byte)0;
            var g = (byte)0;
            var r = (byte)0;
            var bgPriority = false;
            var colorIndex = 0;

            if (isLeftColumnBlanked && x < 8)
            {
                // Force backdrop colour; BG pixel is treated as transparent for priority purposes.
                var offset = (y * FrameWidth + x) * 4;
                m_frameBuffer[offset] = b;
                m_frameBuffer[offset + 1] = g;
                m_frameBuffer[offset + 2] = r;
                m_frameBuffer[offset + 3] = 255;
                m_bgPriority[(y * FrameWidth) + x] = 0;
                continue;
            }

            if (IsBackgroundVisible)
            {
                // (2) Vertical scroll lock for rightmost 8 tile columns when enabled.
                // Reference behaviour: columns 24-31 are not affected by vertical scroll.
                var effectiveScrollY = (m_registers[0].IsBitSet(7) && x >= 192) ? (byte)0 : scrollY;

                var sourceX = (x - scrollX) & 0xFF;

                // Vertical scroll wraps at 224 pixels (28 tile rows) in 256x192 mode.
                var sourceY2 = y + effectiveScrollY;
                if (sourceY2 >= 224)
                    sourceY2 -= 224;

                var tileX = sourceX >> 3;
                var tileY2 = sourceY2 >> 3; // 0-27
                var colInTile = sourceX & 7;
                var rowInTile2 = sourceY2 & 7;

                var rowBase = tileY2 * 32;

                var entryAddr = (nameTableBase + (rowBase + tileX) * 2) & 0x3FFF;
                var low = m_vram[entryAddr];
                var high = m_vram[(entryAddr + 1) & 0x3FFF];

                var tileIndex = low | (((high >> AttrBitTileIndexMsb) & 0x01) << 8);
                var hFlip = high.IsBitSet(AttrBitHFlip);
                var vFlip = high.IsBitSet(AttrBitVFlip);
                var palette = high.IsBitSet(AttrBitPalette) ? 1 : 0;
                bgPriority = high.IsBitSet(AttrBitPriority);

                var tileBase = (tileIndex * 32) & 0x3FFF;
                var sourceRow = vFlip ? 7 - rowInTile2 : rowInTile2;
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

                var decoded = DecodeColor(palette, colorIndex);
                b = decoded.b;
                g = decoded.g;
                r = decoded.r;
            }

            var pixelOffset = (y * FrameWidth + x) * 4;
            m_frameBuffer[pixelOffset] = b;
            m_frameBuffer[pixelOffset + 1] = g;
            m_frameBuffer[pixelOffset + 2] = r;
            m_frameBuffer[pixelOffset + 3] = 255;

            m_bgPriority[(y * FrameWidth) + x] = (byte)(bgPriority && colorIndex != 0 ? 1 : 0);
        }
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
        // Mode 4: Reg 7 selects the backdrop/overscan colour from the sprite palette (CRAM 16-31).
        var index = (m_registers[7] & 0x0F) | 0x10;
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
        // todo - remove this functionality
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

    // todo - remove
    public void DumpBackgroundTileMapWithTiles(FileInfo outputFile)
    {
        if (outputFile == null)
            throw new ArgumentNullException(nameof(outputFile));
        if (outputFile.Directory == null)
            throw new InvalidOperationException("Output file must include a directory.");

        outputFile.Directory.Create();

        var nameTableBase = ((m_registers[2] & 0x0E) << 10) & 0x3FFF;
        var patternBase = ((m_registers[4] & 0x04) << 11) & 0x3FFF;
        var alternatePatternBase = patternBase ^ 0x2000;

        var scrollX = m_registers[8];
        var scrollY = m_registers[9];

        const int tilesWide = 32;
        const int tilesHigh = 24;
        var builder = new StringBuilder();
        var tilesToDump = new HashSet<BackgroundTileKey>();
        var tileIndices = new HashSet<int>();

        for (var row = 0; row < tilesHigh; row++)
        {
            var sourceY = (row * 8 + scrollY) & 0xFF;
            var tileY = sourceY >> 3;
            var rowBase = tileY * tilesWide;

            for (var col = 0; col < tilesWide; col++)
            {
                var sourceX = (col * 8 + scrollX) & 0xFF;
                var tileX = sourceX >> 3;

                var entryAddr = (nameTableBase + (rowBase + tileX) * 2) & 0x3FFF;
                var low = m_vram[entryAddr];
                var high = m_vram[(entryAddr + 1) & 0x3FFF];

                var tileIndex = low | (((high >> AttrBitTileIndexMsb) & 0x01) << 8);
                var palette = high.IsBitSet(AttrBitPalette) ? 1 : 0;
                var hFlip = high.IsBitSet(AttrBitHFlip);
                var vFlip = high.IsBitSet(AttrBitVFlip);

                tilesToDump.Add(new BackgroundTileKey(tileIndex, palette, hFlip, vFlip));
                tileIndices.Add(tileIndex);

                if (col > 0)
                    builder.Append(' ');
                builder.Append(tileIndex.ToString("X3"));
            }

            builder.AppendLine();
        }

        File.WriteAllText(outputFile.FullName, builder.ToString());

        var baseName = Path.GetFileNameWithoutExtension(outputFile.Name);
        var summaryFile = outputFile.Directory.GetFile($"{baseName}-summary.json");
        File.WriteAllText(summaryFile.FullName, BuildBackgroundSummaryJson(nameTableBase, patternBase, alternatePatternBase, scrollX, scrollY));

        var cramFile = outputFile.Directory.GetFile($"{baseName}-cram.txt");
        File.WriteAllText(cramFile.FullName, BuildCramDumpText());

        var tileDataFile = outputFile.Directory.GetFile($"{baseName}-tiles.txt");
        File.WriteAllText(tileDataFile.FullName, BuildBackgroundTileDataText(tileIndices, patternBase));

        var debugFile = outputFile.Directory.GetFile($"{baseName}-debug.txt");
        File.WriteAllText(debugFile.FullName, BuildBackgroundDebugText(0x044, nameTableBase, patternBase, alternatePatternBase, scrollX, scrollY));

        var screenshotFile = outputFile.Directory.GetFile($"{baseName}-frame.tga");
        DumpFrame(screenshotFile);

        foreach (var tile in tilesToDump
                     .OrderBy(key => key.TileIndex)
                     .ThenBy(key => key.Palette)
                     .ThenBy(key => key.HFlip)
                     .ThenBy(key => key.VFlip))
        {
            var tileFile = outputFile.Directory.GetFile(
                $"{tile.TileIndex:X3}-p{tile.Palette}-h{(tile.HFlip ? 1 : 0)}-v{(tile.VFlip ? 1 : 0)}.tga");
            DumpBackgroundTile(tileFile, tile.TileIndex, patternBase, tile.Palette, tile.HFlip, tile.VFlip);

            var altTileFile = outputFile.Directory.GetFile(
                $"{tile.TileIndex:X3}-p{tile.Palette}-h{(tile.HFlip ? 1 : 0)}-v{(tile.VFlip ? 1 : 0)}-alt.tga");
            DumpBackgroundTile(altTileFile, tile.TileIndex, alternatePatternBase, tile.Palette, tile.HFlip, tile.VFlip);
        }
    }

    // todo - remove
    private string BuildBackgroundSummaryJson(int nameTableBase, int patternBase, int alternatePatternBase, int scrollX, int scrollY)
    {
        var registers = string.Join(", ", m_registers.Select(value => $"\"0x{value:X2}\""));
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine($"  \"nameTableBase\": \"0x{nameTableBase:X4}\",");
        builder.AppendLine($"  \"patternBaseReg\": \"0x{(((m_registers[4] & 0x04) << 11) & 0x3FFF):X4}\",");
        builder.AppendLine($"  \"patternBaseSelected\": \"0x{patternBase:X4}\",");
        builder.AppendLine($"  \"patternBaseAlternate\": \"0x{alternatePatternBase:X4}\",");
        builder.AppendLine($"  \"scroll\": {{ \"x\": {scrollX}, \"y\": {scrollY} }},");
        builder.AppendLine($"  \"registers\": [{registers}]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private string BuildCramDumpText()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < m_cram.Length; i += 16)
        {
            builder.Append($"{i:X2}:");
            for (var j = 0; j < 16 && i + j < m_cram.Length; j++)
            {
                builder.Append($" {m_cram[i + j]:X2}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private string BuildBackgroundTileDataText(IEnumerable<int> tileIndices, int patternBase)
    {
        var builder = new StringBuilder();
        foreach (var tileIndex in tileIndices.OrderBy(index => index))
        {
            builder.AppendLine($"Tile {tileIndex:X3} (0x{tileIndex:X3})");
            var tileBase = (patternBase + tileIndex * 32) & 0x3FFF;
            for (var row = 0; row < 8; row++)
            {
                var rowAddr = (tileBase + row * 4) & 0x3FFF;
                builder.Append($"  Row {row}:");
                for (var plane = 0; plane < 4; plane++)
                {
                    builder.Append($" {m_vram[(rowAddr + plane) & 0x3FFF]:X2}");
                }

                builder.AppendLine();
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private string BuildBackgroundDebugText(int focusTileIndex, int nameTableBase, int patternBase, int alternatePatternBase, int scrollX, int scrollY)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Focus tile: {focusTileIndex:X3} (0x{focusTileIndex:X3})");
        builder.AppendLine();
        builder.AppendLine("Summary:");
        builder.AppendLine(BuildBackgroundSummaryJson(nameTableBase, patternBase, alternatePatternBase, scrollX, scrollY));
        builder.AppendLine();
        builder.AppendLine("CRAM:");
        builder.AppendLine(BuildCramDumpText());
        builder.AppendLine();
        builder.AppendLine("Name table occurrences (visible 32x24):");
        AppendFocusTileOccurrences(builder, focusTileIndex, nameTableBase, scrollX, scrollY);
        builder.AppendLine();
        builder.AppendLine("Pattern data @ selected base:");
        AppendFocusTilePatternData(builder, focusTileIndex, patternBase);
        builder.AppendLine();
        builder.AppendLine("Pattern data @ alternate base:");
        AppendFocusTilePatternData(builder, focusTileIndex, alternatePatternBase);
        return builder.ToString();
    }

    private void AppendFocusTileOccurrences(StringBuilder builder, int focusTileIndex, int nameTableBase, int scrollX, int scrollY)
    {
        var foundAny = false;
        for (var row = 0; row < 24; row++)
        {
            var sourceY = (row * 8 + scrollY) & 0xFF;
            var tileY = sourceY >> 3;
            var rowBase = tileY * 32;
            for (var col = 0; col < 32; col++)
            {
                var sourceX = (col * 8 + scrollX) & 0xFF;
                var tileX = sourceX >> 3;
                var entryAddr = (nameTableBase + (rowBase + tileX) * 2) & 0x3FFF;
                var low = m_vram[entryAddr];
                var high = m_vram[(entryAddr + 1) & 0x3FFF];
                var tileIndex = low | (((high >> AttrBitTileIndexMsb) & 0x01) << 8);
                if (tileIndex != focusTileIndex)
                    continue;

                var palette = high.IsBitSet(AttrBitPalette) ? 1 : 0;
                var hFlip = high.IsBitSet(AttrBitHFlip);
                var vFlip = high.IsBitSet(AttrBitVFlip);
                var priority = high.IsBitSet(AttrBitPriority);
                builder.AppendLine($"  ({col},{row}) addr=0x{entryAddr:X4} low=0x{low:X2} high=0x{high:X2} pal={palette} h={BoolToInt(hFlip)} v={BoolToInt(vFlip)} pri={BoolToInt(priority)}");
                foundAny = true;
            }
        }

        if (!foundAny)
            builder.AppendLine("  (none)");
    }

    private void AppendFocusTilePatternData(StringBuilder builder, int focusTileIndex, int patternBase)
    {
        var tileBase = (patternBase + focusTileIndex * 32) & 0x3FFF;
        builder.AppendLine($"  tileBase=0x{tileBase:X4}");
        for (var row = 0; row < 8; row++)
        {
            var rowAddr = (tileBase + row * 4) & 0x3FFF;
            builder.Append($"  Row {row}:");
            for (var plane = 0; plane < 4; plane++)
                builder.Append($" {m_vram[(rowAddr + plane) & 0x3FFF]:X2}");
            builder.AppendLine();
        }
    }

    private static int BoolToInt(bool value) => value ? 1 : 0;

    private void DumpBackgroundTile(FileInfo tgaFile, int tileIndex, int patternBase, int palette, bool hFlip, bool vFlip)
    {
        const int tileSize = 8;
        var buffer = new byte[tileSize * tileSize * 4];

        for (var row = 0; row < tileSize; row++)
        {
            var sourceRow = vFlip ? 7 - row : row;
            var tileBase = (patternBase + tileIndex * 32) & 0x3FFF;
            var rowAddr = (tileBase + sourceRow * 4) & 0x3FFF;
            var plane0 = m_vram[rowAddr];
            var plane1 = m_vram[(rowAddr + 1) & 0x3FFF];
            var plane2 = m_vram[(rowAddr + 2) & 0x3FFF];
            var plane3 = m_vram[(rowAddr + 3) & 0x3FFF];

            for (var col = 0; col < tileSize; col++)
            {
                var bit = hFlip ? col : 7 - col;
                var colorIndex = ((plane0 >> bit) & 0x01) |
                                 (((plane1 >> bit) & 0x01) << 1) |
                                 (((plane2 >> bit) & 0x01) << 2) |
                                 (((plane3 >> bit) & 0x01) << 3);

                var (b, g, r) = colorIndex == 0
                    ? DecodeBackdropColor()
                    : DecodeColor(palette, colorIndex);

                var offset = (row * tileSize + col) * 4;
                buffer[offset] = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = 255;
            }
        }

        var rgba = ConvertBgraToRgba(buffer);
        TgaWriter.Write(tgaFile, rgba, tileSize, tileSize, 4);
    }

    // todo - Store as RGB. Update CrtFrameBuffer to use RGB.
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

    private readonly record struct BackgroundTileKey(int TileIndex, int Palette, bool HFlip, bool VFlip);

    private void RenderSprites(bool displayEnabled)
    {
        var spriteTableBase = (m_registers[5] & 0x7E) << 7;
        var spritePatternBase = (m_registers[6] & 0x04) << 11;
        var spriteHeight = (m_registers[1] & 0x02) != 0 ? 16 : 8;
        var zoom = m_registers[1].IsBitSet(0);
        var spriteShift = m_registers[0].IsBitSet(3) ? -8 : 0;
        var scale = zoom ? 2 : 1;
        var spritePixelHeight = spriteHeight * scale;

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

        // SAT order determines which sprites are allowed on each scanline (first 8 only).
        Array.Clear(m_spritesOnLine, 0, m_spritesOnLine.Length);
        Array.Clear(m_spriteMaskPerLine, 0, m_spriteMaskPerLine.Length);
        for (var idx = 0; idx < count; idx++)
        {
            var i = indices[idx];
            var y = m_vram[(spriteTableBase + i) & 0x3FFF];
            var spriteY = y + 1;
            if (spriteY > 240)
                spriteY -= 256;

            var start = spriteY;
            var end = spriteY + spritePixelHeight;
            if (end <= 0 || start >= FrameHeight)
                continue;

            if (start < 0)
                start = 0;
            if (end > FrameHeight)
                end = FrameHeight;

            for (var py = start; py < end; py++)
            {
                if (m_spritesOnLine[py] < 8)
                {
                    m_spritesOnLine[py]++;
                    m_spriteMaskPerLine[py] |= 1UL << i;
                }
                else
                {
                    m_status |= StatusSpriteOverflowBit;
                }
            }
        }

        if (!displayEnabled)
            return;

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

            DrawSprite(i, spriteX, spriteY, tileIndex, spriteHeight, spritePatternBase, zoom);
        }
    }

    private void DrawSprite(int spriteIndex, int x, int y, int tileIndex, int height, int patternBase, bool zoom)
    {
        var scale = zoom ? 2 : 1;
        var spritePixelHeight = height * scale;
        if (x >= FrameWidth || y >= FrameHeight || y + spritePixelHeight <= 0)
            return;

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

            var rowStart = destY;
            var rowEnd = destY + scale;
            if (rowEnd <= 0)
                continue;

            if (rowStart < 0)
                rowStart = 0;

            var maxY = Math.Min(FrameHeight, rowEnd);
            var lineCount = 0;
            for (var py = rowStart; py < maxY; py++)
            {
                var canDraw = (m_spriteMaskPerLine[py] & (1UL << spriteIndex)) != 0;

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
                // Reg 0 bit 5: left column blanking hides sprites in the leftmost 8 pixels.
                if (m_registers[0].IsBitSet(5) && destX < 8)
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
                        var collisionIndex = py * FrameWidth + px;
                        if (m_spriteCollision[collisionIndex] != 0)
                            m_status |= StatusSpriteCollisionBit;
                        else
                            m_spriteCollision[collisionIndex] = 1;

                        // Apply BG priority masking per pixel (px,py), including when sprites are zoomed.
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
