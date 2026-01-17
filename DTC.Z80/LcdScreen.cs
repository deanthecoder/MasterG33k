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
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DTC.Z80;

/// <summary>
/// Simple LCD surface that copies raw BGRA frame buffers into a writeable bitmap.
/// </summary>
public sealed class LcdScreen : IDisposable
{
    public WriteableBitmap Display { get; }

    public LcdScreen(int width, int height)
    {
        var pixelSize = new PixelSize(width, height);
        Display = new WriteableBitmap(pixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        FillBlack();
    }

    public bool Update(byte[] frameBuffer)
    {
        if (frameBuffer == null)
            throw new ArgumentNullException(nameof(frameBuffer));

        using var fb = Display.Lock();
        var length = Math.Min(frameBuffer.Length, fb.RowBytes * fb.Size.Height);
        Marshal.Copy(frameBuffer, 0, fb.Address, length);
        return true;
    }

    public void Dispose() => Display?.Dispose();

    private void FillBlack()
    {
        using var fb = Display.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        for (var i = 3; i < bytes.Length; i += 4)
            bytes[i] = 255;
        Marshal.Copy(bytes, 0, fb.Address, bytes.Length);
    }
}
