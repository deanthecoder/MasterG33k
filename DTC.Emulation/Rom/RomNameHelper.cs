// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Core.Extensions;

namespace DTC.Emulation.Rom;

/// <summary>
/// Provides consistent ROM display and file name formatting.
/// </summary>
public static class RomNameHelper
{
    public static string GetDisplayName(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
            return null;

        var fileName = Path.GetFileName(nameOrPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = Path.GetFileNameWithoutExtension(fileName);
        int i;
        while ((i = name.IndexOfAny(['(', '['])) > 0)
            name = name[..i];

        name = name.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static string GetSafeFileBaseName(string nameOrPath, string fallback)
    {
        var display = GetDisplayName(nameOrPath);
        var baseName = string.IsNullOrWhiteSpace(display) ? fallback : display;
        return baseName.ToSafeFileName();
    }

    public static string BuildWindowTitle(string baseTitle, string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(baseTitle))
            baseTitle = "Emulator";

        var display = GetDisplayName(nameOrPath);
        if (string.IsNullOrWhiteSpace(display) ||
            string.Equals(display, baseTitle, StringComparison.OrdinalIgnoreCase))
            return baseTitle;

        return $"{baseTitle} - {display}";
    }
}
