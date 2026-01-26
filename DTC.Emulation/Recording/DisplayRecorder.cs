// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DTC.Core;
using DTC.Core.Commands;
using DTC.Core.Extensions;
using DTC.Core.Recording;
using DTC.Core.UI;
using DTC.Emulation.Audio;
using DTC.Emulation.Rom;

namespace DTC.Emulation.Recording;

/// <summary>
/// Manages screen recording sessions and file output for emulator displays.
/// </summary>
public sealed class DisplayRecorder : IDisposable
{
    private const short RecordingAudioChannels = 2;
    private RecordingSession m_session;
    private IAudioSampleSink m_audioSink;
    private DispatcherTimer m_indicatorTimer;
    private bool m_isIndicatorOn;
    private IAudioOutputDevice m_audioDevice;
    private Func<string> m_romTitleProvider;

    public bool IsRecording => m_session?.IsRecording == true;

    public bool IsIndicatorOn
    {
        get => m_isIndicatorOn;
        private set
        {
            if (m_isIndicatorOn == value)
                return;
            m_isIndicatorOn = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler StateChanged;

    public void Start(WriteableBitmap display, double frameRate, IAudioOutputDevice audioDevice, Func<string> romTitleProvider)
    {
        if (IsRecording)
            return;

        if (!RecordingSession.IsFfmpegAvailable(out _))
        {
            DialogService.Instance.ShowMessage(
                "Recording unavailable",
                "FFmpeg was not detected. Install FFmpeg and ensure it's on your PATH, then try again.");
            return;
        }

        if (display == null)
            throw new ArgumentNullException(nameof(display));

        m_audioDevice = audioDevice ?? throw new ArgumentNullException(nameof(audioDevice));
        m_romTitleProvider = romTitleProvider;

        try
        {
            m_session?.Dispose();
            var audioSettings = new RecordingAudioSettings(audioDevice.SampleRateHz, RecordingAudioChannels);
            m_session = new RecordingSession(display, frameRate, audioSettings);
            m_session.Start();
            m_audioSink = new RecordingAudioSink(m_session);
            audioDevice.SetCaptureSink(m_audioSink);
            StartIndicator();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            m_session?.Dispose();
            m_session = null;
            m_audioSink = null;
            audioDevice.SetCaptureSink(null);
            StopIndicator();
            DialogService.Instance.ShowMessage("Unable to start recording", ex.Message);
        }
    }

    public void Stop()
    {
        if (!IsRecording)
            return;

        var session = m_session;
        m_session = null;
        m_audioDevice.FlushCapture();
        m_audioDevice.SetCaptureSink(null);
        m_audioSink = null;
        StopIndicator();
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (session == null)
            return;

        var progress = new ProgressToken();
        var busyDialog = DialogService.Instance.ShowBusy("Finalizing recording...", progress);
        var stopTask = session.StopAsync(progress);
        stopTask.ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Dispatcher.UIThread.Post(() => busyDialog.Dispose());
                Logger.Instance.Warn($"Recording failed: {task.Exception?.GetBaseException().Message}");
                session.Dispose();
                return;
            }

            var result = task.Result;
            if (result == null || result.TempFile?.Exists != true)
            {
                Dispatcher.UIThread.Post(() => busyDialog.Dispose());
                Logger.Instance.Warn("Recording failed to produce an output file.");
                session.Dispose();
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                busyDialog.Dispose();
                var prefix = RomNameHelper.GetSafeFileBaseName(m_romTitleProvider?.Invoke(), "Recording");
                var defaultName = $"{prefix}.mp4";
                var command = new FileSaveCommand("Save Recording", "MP4 Files", ["*.mp4"], defaultName);
                command.FileSelected += (_, info) =>
                {
                    try
                    {
                        File.Move(result.TempFile.FullName, info.FullName, overwrite: true);
                        var fileInfo = new FileInfo(info.FullName);
                        var size = fileInfo.Exists ? fileInfo.Length.ToSize() : "unknown size";
                        Logger.Instance.Info($"Recording stopped. Saved to '{fileInfo.FullName}' ({result.Duration:hh\\:mm\\:ss}, {size}).");
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Warn($"Failed to save recording: {ex.Message}");
                    }
                    finally
                    {
                        session.Dispose();
                    }
                };
                command.Cancelled += (_, _) =>
                {
                    var size = result.TempFile.Exists ? result.TempFile.Length.ToSize() : "unknown size";
                    Logger.Instance.Info($"Recording stopped. Discarded temp output ({result.Duration:hh\\:mm\\:ss}, {size}).");
                    session.Dispose();
                };
                command.Execute(null);
            });
        });
    }

    public void CaptureFrame() => m_session?.CaptureFrame();

    public void Dispose()
    {
        m_session?.Dispose();
        m_session = null;
        m_audioSink = null;
        StopIndicator();
    }

    private void StartIndicator()
    {
        m_indicatorTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        m_indicatorTimer.Stop();
        m_indicatorTimer.Tick -= OnIndicatorTick;
        m_indicatorTimer.Tick += OnIndicatorTick;
        IsIndicatorOn = true;
        m_indicatorTimer.Start();
    }

    private void StopIndicator()
    {
        if (m_indicatorTimer != null)
        {
            m_indicatorTimer.Tick -= OnIndicatorTick;
            m_indicatorTimer.Stop();
        }

        IsIndicatorOn = false;
    }

    private void OnIndicatorTick(object sender, EventArgs e)
    {
        if (!IsRecording)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            StopIndicator();
            return;
        }

        IsIndicatorOn = !IsIndicatorOn;
    }

    private sealed class RecordingAudioSink : IAudioSampleSink
    {
        private readonly RecordingSession m_session;

        public RecordingAudioSink(RecordingSession session)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public void OnSamples(ReadOnlySpan<short> samples, int sampleRate) =>
            m_session.OnAudioSamples(samples, sampleRate);
    }
}
