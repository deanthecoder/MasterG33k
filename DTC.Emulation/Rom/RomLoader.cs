// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using System.IO.Compression;
using DTC.Core.Extensions;

namespace DTC.Emulation.Rom;

/// <summary>
/// Loads ROM data from files or supported entries within zip archives.
/// </summary>
public static class RomLoader
{
    public static (string RomName, byte[] RomData) ReadRomData(FileInfo romFile, IReadOnlyList<string> supportedExtensions)
    {
        var romName = romFile?.Name;
        if (romFile == null)
            return (null, null);

        var extensionSet = CreateExtensionSet(supportedExtensions);
        if (!romFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsSupportedExtension(romFile.Extension, extensionSet))
                return (romName, null);
            romName = Path.GetFileNameWithoutExtension(romName);
            return (romName, romFile.ReadAllBytes());
        }

        using var archive = ZipFile.OpenRead(romFile.FullName);
        foreach (var entry in archive.Entries)
        {
            if (!IsSupportedExtension(Path.GetExtension(entry.Name), extensionSet))
                continue;

            var buffer = new byte[(int)entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(buffer.AsSpan());

            romName = Path.GetFileNameWithoutExtension(entry.Name);
            return (romName, buffer);
        }

        return (romName, null);
    }

    public static bool IsSupportedRom(string path, IReadOnlyList<string> supportedExtensions)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return true;

        var extensionSet = CreateExtensionSet(supportedExtensions);
        return IsSupportedExtension(extension, extensionSet);
    }

    private static bool IsSupportedExtension(string extension, HashSet<string> extensionSet)
    {
        if (extensionSet == null || extensionSet.Count == 0)
            return true;
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return extensionSet.Contains(extension);
    }

    private static HashSet<string> CreateExtensionSet(IReadOnlyList<string> supportedExtensions)
    {
        if (supportedExtensions == null || supportedExtensions.Count == 0)
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in supportedExtensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
                continue;
            set.Add(extension.StartsWith('.') ? extension : $".{extension}");
        }

        return set;
    }
}
