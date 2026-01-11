// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.Z80.Devices;

/// <summary>
/// Minimal Sega Master System port device wrapper for the VDP.
/// </summary>
/// <remarks>
/// Routes common VDP I/O ports to the VDP and forwards everything else to a fallback device.
/// </remarks>
public sealed class SmsPortDevice : IPortDevice
{
    private readonly SmsVdp m_vdp;
    private readonly IPortDevice m_fallback;
    private int m_dataWrites;
    private int m_controlWrites;
    private int m_dataReads;
    private int m_controlReads;
    private int m_otherReads;
    private int m_otherWrites;
    private int m_beWrites;
    private int m_bfWrites;
    private int m_bcWrites;
    private int m_bdWrites;

    public SmsPortDevice(SmsVdp vdp, IPortDevice fallback = null)
    {
        m_vdp = vdp ?? throw new ArgumentNullException(nameof(vdp));
        m_fallback = fallback ?? DefaultPortDevice.Instance;
    }

    public byte Read8(ushort portAddress)
    {
        var port = (byte)portAddress;
        switch (port)
        {
            case 0xBE:
            case 0xBC:
                m_dataReads++;
                return m_vdp.ReadData();
            case 0xBF:
            case 0xBD:
                m_controlReads++;
                return m_vdp.ReadStatus();
            case 0x7E:
                return m_vdp.ReadVCounter();
            case 0x7F:
                return m_vdp.ReadHCounter();
            default:
                m_otherReads++;
                return m_fallback.Read8(portAddress);
        }
    }

    public void Write8(ushort portAddress, byte value)
    {
        var port = (byte)portAddress;
        switch (port)
        {
            case 0xBE:
                m_beWrites++;
                m_dataWrites++;
                m_vdp.WriteData(value);
                return;
            case 0xBC:
                m_bcWrites++;
                m_dataWrites++;
                m_vdp.WriteData(value);
                return;
            case 0xBF:
                m_bfWrites++;
                m_controlWrites++;
                m_vdp.WriteControl(value);
                return;
            case 0xBD:
                m_bdWrites++;
                m_controlWrites++;
                m_vdp.WriteControl(value);
                return;
            default:
                m_otherWrites++;
                m_fallback.Write8(portAddress, value);
                return;
        }
    }

    public string GetDebugSummary() =>
        $"Ports: dataR={m_dataReads}, dataW={m_dataWrites} (BE={m_beWrites}, BC={m_bcWrites}) " +
        $"ctrlR={m_controlReads}, ctrlW={m_controlWrites} (BF={m_bfWrites}, BD={m_bdWrites}) " +
        $"otherR={m_otherReads}, otherW={m_otherWrites}";
}
