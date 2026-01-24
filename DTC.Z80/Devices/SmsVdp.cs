// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Runtime.InteropServices;
using DTC.Core;
using DTC.Core.Extensions;
using DTC.Z80.Snapshot;

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
    public const int CyclesPerScanline = 228;
    private const int NtscTotalScanlines = 262;
    private const int PalTotalScanlines = 313;
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
    private bool m_isPal;

    public event EventHandler<byte[]> FrameRendered;

    public bool IsBackgroundVisible { get; set; } = true;

    public bool AreSpritesVisible { get; set; } = true;

    public int TotalScanlines { get; private set; } = NtscTotalScanlines;

    public void SetIsPal(bool isPal)
    {
        if (m_isPal == isPal)
            return;

        m_isPal = isPal;
        TotalScanlines = isPal ? PalTotalScanlines : NtscTotalScanlines;
        if (m_vCounter >= TotalScanlines)
            m_vCounter = 0;
    }

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

        var command = (byte)(value >> 6);
        if (command == 0b10)
        {
            var regIndex = value & 0x0F;
            if (regIndex < m_registers.Length)
            {
                m_registers[regIndex] = m_controlLatchLow;
                if (regIndex == 10)
                    m_lineCounter = m_controlLatchLow;
                MonitorVdpMode();
            }

            m_address = (ushort)((m_controlLatchLow | ((value & 0x3F) << 8)) & 0x3FFF);
            m_accessMode = AccessMode.VramWrite;
        }
        else
        {
            m_address = (ushort)((m_controlLatchLow | ((value & 0x3F) << 8)) & 0x3FFF);
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
            var (r, g, b) = DecodeBackdropColor();
            for (var x = 0; x < FrameWidth; x++)
            {
                var offset = (y * FrameWidth + x) * 4;
                m_frameBuffer[offset] = r;
                m_frameBuffer[offset + 1] = g;
                m_frameBuffer[offset + 2] = b;
                m_frameBuffer[offset + 3] = 255;
                m_bgPriority[(y * FrameWidth) + x] = 0;
            }

            return;
        }

        for (var x = 0; x < FrameWidth; x++)
        {
            var r = (byte)0;
            var g = (byte)0;
            var b = (byte)0;
            var bgPriority = false;
            var colorIndex = 0;

            if (isLeftColumnBlanked && x < 8)
            {
                // Force backdrop colour; BG pixel is treated as transparent for priority purposes.
                var backdrop = DecodeBackdropColor();
                var offset = (y * FrameWidth + x) * 4;
                m_frameBuffer[offset] = backdrop.r;
                m_frameBuffer[offset + 1] = backdrop.g;
                m_frameBuffer[offset + 2] = backdrop.b;
                m_frameBuffer[offset + 3] = 255;
                m_bgPriority[(y * FrameWidth) + x] = 0;
                continue;
            }

            if (IsBackgroundVisible)
            {
                // (2) Vertical scroll lock for rightmost 8 tile columns when enabled.
                // Reference behaviour: columns 24-31 are not affected by vertical scroll.
                var effectiveScrollY = m_registers[0].IsBitSet(7) && x >= 192 ? (byte)0 : scrollY;

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
                r = decoded.r;
                g = decoded.g;
                b = decoded.b;
            }

            var pixelOffset = (y * FrameWidth + x) * 4;
            m_frameBuffer[pixelOffset] = r;
            m_frameBuffer[pixelOffset + 1] = g;
            m_frameBuffer[pixelOffset + 2] = b;
            m_frameBuffer[pixelOffset + 3] = 255;

            m_bgPriority[(y * FrameWidth) + x] = (byte)(bgPriority && colorIndex != 0 ? 1 : 0);
        }
    }

    private (byte r, byte g, byte b) DecodeColor(int palette, int colorIndex)
    {
        var cramIndex = (palette * 16 + colorIndex) & 0x1F;
        var value = m_cram[cramIndex];

        var r = (byte)((value & 0x03) * 85);
        var g = (byte)(((value >> 2) & 0x03) * 85);
        var b = (byte)(((value >> 4) & 0x03) * 85);
        return (r, g, b);
    }

    private (byte r, byte g, byte b) DecodeBackdropColor()
    {
        // Mode 4: Reg 7 selects the backdrop/overscan color from the sprite palette (CRAM 16-31).
        var index = (m_registers[7] & 0x0F) | 0x10;
        var value = m_cram[index & 0x1F];
        var r = (byte)((value & 0x03) * 85);
        var g = (byte)(((value >> 2) & 0x03) * 85);
        var b = (byte)(((value >> 4) & 0x03) * 85);
        return (r, g, b);
    }

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

                var (r, g, b) = DecodeColor(1, colorIndex);
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
                        m_frameBuffer[offset] = r;
                        m_frameBuffer[offset + 1] = g;
                        m_frameBuffer[offset + 2] = b;
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

    internal int GetStateSize() =>
        sizeof(byte) + // m_isPal.
        sizeof(ushort) + // m_address.
        sizeof(byte) * 4 + // m_readBuffer, m_controlLatchLow, m_accessMode, m_hCounterLatched.
        sizeof(byte) * 6 + // m_isControlLatchFull, m_status, m_interruptPending, m_wasMode4, m_warnedNonMode4, m_warnedMode2.
        sizeof(byte) * 1 + // m_warnedMode4WithoutMode2.
        sizeof(int) * 4 + // m_cycleAccumulator, m_vCounter, m_lineCounter, m_lastDisplayHeight.
        m_vram.Length +
        m_cram.Length +
        m_registers.Length +
        m_frameBuffer.Length +
        m_bgPriority.Length +
        m_spriteCollision.Length +
        m_spritesOnLine.Length * sizeof(int) +
        m_spriteMaskPerLine.Length * sizeof(ulong);

    internal void SaveState(ref StateWriter writer)
    {
        writer.WriteBool(m_isPal);
        writer.WriteUInt16(m_address);
        writer.WriteByte(m_readBuffer);
        writer.WriteByte(m_controlLatchLow);
        writer.WriteBool(m_isControlLatchFull);
        writer.WriteByte((byte)m_accessMode);
        writer.WriteByte(m_hCounterLatched);
        writer.WriteInt32(m_cycleAccumulator);
        writer.WriteInt32(m_vCounter);
        writer.WriteInt32(m_lineCounter);
        writer.WriteByte(m_status);
        writer.WriteBool(m_interruptPending);
        writer.WriteBool(m_wasMode4);
        writer.WriteInt32(m_lastDisplayHeight);
        writer.WriteBool(m_warnedNonMode4);
        writer.WriteBool(m_warnedMode2);
        writer.WriteBool(m_warnedMode4WithoutMode2);

        writer.WriteBytes(m_vram);
        writer.WriteBytes(m_cram);
        writer.WriteBytes(m_registers);
        writer.WriteBytes(m_frameBuffer);
        writer.WriteBytes(m_bgPriority);
        writer.WriteBytes(m_spriteCollision);
        writer.WriteBytes(MemoryMarshal.AsBytes(m_spritesOnLine.AsSpan()));
        writer.WriteBytes(MemoryMarshal.AsBytes(m_spriteMaskPerLine.AsSpan()));
    }

    internal void LoadState(ref StateReader reader)
    {
        var isPal = reader.ReadBool();
        SetIsPal(isPal);
        m_address = reader.ReadUInt16();
        m_readBuffer = reader.ReadByte();
        m_controlLatchLow = reader.ReadByte();
        m_isControlLatchFull = reader.ReadBool();
        m_accessMode = (AccessMode)reader.ReadByte();
        m_hCounterLatched = reader.ReadByte();
        m_cycleAccumulator = reader.ReadInt32();
        m_vCounter = reader.ReadInt32();
        m_lineCounter = reader.ReadInt32();
        m_status = reader.ReadByte();
        m_interruptPending = reader.ReadBool();
        m_wasMode4 = reader.ReadBool();
        m_lastDisplayHeight = reader.ReadInt32();
        m_warnedNonMode4 = reader.ReadBool();
        m_warnedMode2 = reader.ReadBool();
        m_warnedMode4WithoutMode2 = reader.ReadBool();

        reader.ReadBytes(m_vram);
        reader.ReadBytes(m_cram);
        reader.ReadBytes(m_registers);
        reader.ReadBytes(m_frameBuffer);
        reader.ReadBytes(m_bgPriority);
        reader.ReadBytes(m_spriteCollision);
        reader.ReadBytes(MemoryMarshal.AsBytes(m_spritesOnLine.AsSpan()));
        reader.ReadBytes(MemoryMarshal.AsBytes(m_spriteMaskPerLine.AsSpan()));
    }

    public void CopyFrameBuffer(Span<byte> destination)
    {
        if (destination.Length != m_frameBuffer.Length)
            throw new ArgumentException("Frame buffer size mismatch.", nameof(destination));
        m_frameBuffer.AsSpan().CopyTo(destination);
    }
}
