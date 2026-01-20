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
using DTC.Core.Image;

namespace DTC.Z80;

/// <summary>
/// Simple LCD surface that copies RGBA frame buffers into a writeable bitmap with optional CRT effects.
/// </summary>
public sealed class LcdScreen : IDisposable
{
    public WriteableBitmap Display { get; }
    public CrtFrameBuffer FrameBuffer { get; }

    public LcdScreen(int width, int height)
    {
        FrameBuffer = new CrtFrameBuffer(width, height);
        var pixelSize = new PixelSize(FrameBuffer.OutputWidth, FrameBuffer.OutputHeight);
        Display = new WriteableBitmap(pixelSize, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        FillBlack();
    }

    public void Update(byte[] frameBuffer)
    {
        if (frameBuffer == null)
            throw new ArgumentNullException(nameof(frameBuffer));

        var output = FrameBuffer.Apply(frameBuffer);

        using var fb = Display.Lock();
        var length = Math.Min(output.Length, fb.RowBytes * fb.Size.Height);
        Marshal.Copy(output, 0, fb.Address, length);
    }

    public void Dispose() => Display?.Dispose();

    private void FillBlack()
    {
        using var fb = Display.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        for (var i = 3; i < bytes.Length; i += CrtFrameBuffer.BytesPerPixel)
            bytes[i] = 255; // Alpha channel is always opaque.
        Marshal.Copy(bytes, 0, fb.Address, bytes.Length);
    }
}
