// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation.Snapshot;

namespace DTC.Emulation;

/// <summary>
/// Handles serialization of machine state for rollback and save states.
/// </summary>
public interface IMachineSnapshotter
{
    int GetStateSize();
    void Save(MachineState state, Span<byte> frameBuffer);
    void Load(MachineState state);
}
