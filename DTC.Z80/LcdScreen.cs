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
    private byte[] m_previousOutput;

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
        var blended = output;
        if (FrameBuffer.IsCrt)
            blended = BlendWithPrevious(output);
        else
            CachePreviousOutput(output);

        using var fb = Display.Lock();
        var length = Math.Min(blended.Length, fb.RowBytes * fb.Size.Height);
        Marshal.Copy(blended, 0, fb.Address, length);
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

    private byte[] BlendWithPrevious(byte[] output)
    {
        if (output == null)
            return null;

        if (m_previousOutput == null || m_previousOutput.Length != output.Length)
        {
            m_previousOutput = new byte[output.Length];
            Buffer.BlockCopy(output, 0, m_previousOutput, 0, output.Length);
            return m_previousOutput;
        }

        for (var i = 0; i < output.Length; i++)
            m_previousOutput[i] = (byte)((m_previousOutput[i] * 3 + output[i] * 2) / 5);

        return m_previousOutput;
    }

    private void CachePreviousOutput(byte[] output)
    {
        if (output == null)
            return;

        if (m_previousOutput == null || m_previousOutput.Length != output.Length)
            m_previousOutput = new byte[output.Length];
        Buffer.BlockCopy(output, 0, m_previousOutput, 0, output.Length);
    }
}
