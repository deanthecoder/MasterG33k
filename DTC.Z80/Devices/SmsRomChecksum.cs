// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
namespace DTC.Z80.Devices;

/// <summary>
/// Utility helpers for Master System ROM headers/checksums.
/// </summary>
public static class SmsRomChecksum
{
    private static readonly byte[] Signature = "TMR SEGA"u8.ToArray();

    public static int FindHeaderOffset(byte[] romData)
    {
        if (romData == null || romData.Length < 0x2000)
            return -1;

        var offsets = new[] { 0x7FF0, 0x3FF0, 0x1FF0 };
        foreach (var offset in offsets)
        {
            if (offset + 16 > romData.Length)
                continue;
            var match = true;
            for (var i = 0; i < Signature.Length; i++)
            {
                if (romData[offset + i] != Signature[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return offset;
        }

        return -1;
    }

    public static ushort ComputeChecksum(byte[] romData, byte sizeNibble)
    {
        if (romData == null || romData.Length == 0)
            return 0;

        var ranges = sizeNibble switch
        {
            0xA => new[] { (0x0000, 0x1FEF) },
            0xB => new[] { (0x0000, 0x3FEF) },
            0xC => new[] { (0x0000, 0x7FEF) },
            0xD => new[] { (0x0000, 0xBFEF) },
            0xE => new[] { (0x0000, 0x7FEF), (0x8000, 0x0FFFF) },
            0xF => new[] { (0x0000, 0x7FEF), (0x8000, 0x1FFFF) },
            0x0 => new[] { (0x0000, 0x7FEF), (0x8000, 0x3FFFF) },
            0x1 => new[] { (0x0000, 0x7FEF), (0x8000, 0x7FFFF) },
            0x2 => new[] { (0x0000, 0x7FEF), (0x8000, 0xFFFFF) },
            _ => Array.Empty<(int start, int end)>()
        };

        var sum = 0;
        foreach (var (start, end) in ranges)
        {
            var cappedEnd = Math.Min(end, romData.Length - 1);
            for (var i = start; i <= cappedEnd; i++)
                sum = (sum + romData[i]) & 0xFFFF;
        }

        return (ushort)sum;
    }

    public static bool TryPatchChecksum(byte[] romData, bool patchBothFields = false)
    {
        var headerOffset = FindHeaderOffset(romData);
        if (headerOffset < 0)
            return false;

        var sizeNibble = (byte)(romData[headerOffset + 0x0F] & 0x0F);
        var computed = ComputeChecksum(romData, sizeNibble);
        var low = (byte)(computed & 0xFF);
        var high = (byte)(computed >> 8);

        var patched = false;
        var checksumOffset = headerOffset + 0x0A;
        if (romData[checksumOffset] != low || romData[checksumOffset + 1] != high)
        {
            romData[checksumOffset] = low;
            romData[checksumOffset + 1] = high;
            patched = true;
        }

        if (patchBothFields)
        {
            var checksumOffsetAlt = headerOffset + 0x08;
            if (romData[checksumOffsetAlt] != low || romData[checksumOffsetAlt + 1] != high)
            {
                romData[checksumOffsetAlt] = low;
                romData[checksumOffsetAlt + 1] = high;
                patched = true;
            }
        }

        return patched;
    }
}
