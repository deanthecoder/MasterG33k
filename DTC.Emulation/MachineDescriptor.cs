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
/// Default descriptor implementation for machine timing and display metadata.
/// </summary>
public sealed class MachineDescriptor : IMachineDescriptor
{
    public string Name { get; init; }
    public double CpuHz { get; init; }
    public double VideoHz { get; init; }
    public int AudioSampleRateHz { get; init; }
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
}
