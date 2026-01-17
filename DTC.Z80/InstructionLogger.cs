// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
namespace DTC.Z80;

/// <summary>
/// Collects a rolling history of CPU instructions and related events.
/// </summary>
public sealed class InstructionLogger
{
    private readonly string[] m_log = new string[1024];
    private int m_index;
    private int m_count;
    private readonly object m_lock = new();

    public bool IsEnabled { get; set; }

    public void Write(Func<string> message)
    {
        if (!IsEnabled)
            return;

        lock (m_lock)
        {
            m_log[m_index] = message();
            m_index = (m_index + 1) % m_log.Length;
            if (m_count < m_log.Length)
                m_count++;
        }
    }

    public void DumpToConsole(int maxLines = -1)
    {
        lock (m_lock)
        {
            Console.WriteLine($"----- CPU instruction history @ {DateTime.Now:O} -----");
            if (m_count == 0)
                return;

            var count = maxLines < 0 ? m_count : Math.Min(m_count, maxLines);
            var start = (m_index - m_count + m_log.Length) % m_log.Length;
            for (var i = 0; i < count; i++)
            {
                var idx = (start + i) % m_log.Length;
                Console.WriteLine(m_log[idx]);
            }

            m_index = 0;
            m_count = 0;
        }
    }
}
