// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.Emulation;

/// <summary>
/// Represents a fully wired emulated machine with CPU, video, audio, and input.
/// </summary>
public interface IMachine
{
    string Name { get; }
    IMachineDescriptor Descriptor { get; }
    long CpuTicks { get; }
    bool HasLoadedCartridge { get; }
    IVideoSource Video { get; }
    IAudioSource Audio { get; }
    IMachineSnapshotter Snapshotter { get; }

    void Reset();
    void LoadRom(byte[] romData, string romName);
    void StepCpu();
    void AdvanceDevices(long deltaTicks);
    bool TryConsumeInterrupt();
    void RequestInterrupt();
    void SetInputActive(bool isActive);
}
