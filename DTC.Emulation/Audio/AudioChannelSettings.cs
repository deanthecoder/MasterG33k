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
/// Tracks per-channel audio enabled state and applies it to an audio source.
/// </summary>
public sealed class AudioChannelSettings
{
    private readonly IAudioSource m_audioSource;
    private readonly bool[] m_enabled;

    public AudioChannelSettings(IAudioSource audioSource)
    {
        m_audioSource = audioSource ?? throw new ArgumentNullException(nameof(audioSource));
        if (audioSource.ChannelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(audioSource.ChannelCount));

        m_enabled = new bool[audioSource.ChannelCount];
        for (var i = 0; i < m_enabled.Length; i++)
            m_enabled[i] = true;
    }

    public int ChannelCount => m_enabled.Length;

    public bool IsEnabled(int channel) =>
        TryGetIndex(channel, out var index) && m_enabled[index];

    public void SetChannelEnabled(int channel, bool isEnabled)
    {
        if (!TryGetIndex(channel, out var index))
            return;
        if (m_enabled[index] == isEnabled)
            return;

        m_enabled[index] = isEnabled;
        m_audioSource.SetChannelEnabled(channel, isEnabled);
    }

    public void ToggleChannel(int channel)
    {
        if (TryGetIndex(channel, out var index))
            SetChannelEnabled(channel, !m_enabled[index]);
    }

    public void ApplyAll()
    {
        for (var channel = 1; channel <= m_enabled.Length; channel++)
            m_audioSource.SetChannelEnabled(channel, m_enabled[channel - 1]);
    }

    private bool TryGetIndex(int channel, out int index)
    {
        index = channel - 1;
        return (uint)index < (uint)m_enabled.Length;
    }
}
