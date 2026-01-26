// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.Emulation.Audio;

/// <summary>
/// Provides audio output and optional capture for emulated machines.
/// </summary>
public interface IAudioOutputDevice : IDisposable
{
    int SampleRateHz { get; }

    void Start();
    void SetEnabled(bool isSoundEnabled);
    void SetLowPassFilterEnabled(bool isEnabled);
    void SetCaptureSink(IAudioSampleSink value);
    void FlushCapture();
}
