// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Buffers.Binary;

namespace DTC.Emulation.Snapshot;

/// <summary>
/// Reads primitive values from a fixed snapshot buffer.
/// </summary>
public struct StateReader
{
    private readonly byte[] m_buffer;

    public StateReader(byte[] buffer)
    {
        m_buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Offset = 0;
    }

    public int Offset { get; private set; }

    public byte ReadByte()
    {
        Ensure(1);
        return m_buffer[Offset++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUInt16()
    {
        Ensure(2);
        var result = BinaryPrimitives.ReadUInt16LittleEndian(m_buffer.AsSpan(Offset, 2));
        Offset += 2;
        return result;
    }

    public int ReadInt32()
    {
        Ensure(4);
        var result = BinaryPrimitives.ReadInt32LittleEndian(m_buffer.AsSpan(Offset, 4));
        Offset += 4;
        return result;
    }

    public uint ReadUInt32()
    {
        Ensure(4);
        var result = BinaryPrimitives.ReadUInt32LittleEndian(m_buffer.AsSpan(Offset, 4));
        Offset += 4;
        return result;
    }

    public long ReadInt64()
    {
        Ensure(8);
        var result = BinaryPrimitives.ReadInt64LittleEndian(m_buffer.AsSpan(Offset, 8));
        Offset += 8;
        return result;
    }

    public ulong ReadUInt64()
    {
        Ensure(8);
        var result = BinaryPrimitives.ReadUInt64LittleEndian(m_buffer.AsSpan(Offset, 8));
        Offset += 8;
        return result;
    }

    public double ReadDouble() =>
        BitConverter.Int64BitsToDouble(ReadInt64());

    public void ReadBytes(Span<byte> destination)
    {
        Ensure(destination.Length);
        m_buffer.AsSpan(Offset, destination.Length).CopyTo(destination);
        Offset += destination.Length;
    }

    private void Ensure(int count)
    {
        if (Offset + count > m_buffer.Length)
            throw new InvalidOperationException("State buffer is too small.");
    }
}
