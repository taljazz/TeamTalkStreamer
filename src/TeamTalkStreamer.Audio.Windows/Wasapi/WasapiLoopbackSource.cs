#region Usings
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TeamTalkStreamer.Core.Audio;
#endregion

namespace TeamTalkStreamer.Audio.Windows.Wasapi;

#region Class: WasapiLoopbackSource
/// <summary>
/// Captures "whatever is playing on the default render device" via
/// WASAPI loopback and publishes it as <see cref="AudioFrame"/>s. Core
/// of the Windows-side data path — this is the primary input for v1.
/// </summary>
/// <remarks>
/// Implementation notes:
/// <list type="bullet">
///   <item><description>Inherits from <see cref="AudioSourceBase"/> so
///     the state machine, events, and disposal are all free.</description></item>
///   <item><description>WASAPI loopback returns the shared-mix format,
///     which on most modern Windows systems is 32-bit float. We convert
///     to 16-bit signed PCM here so the pipeline standardizes on a
///     single sample type.</description></item>
///   <item><description>The source emits the device's native sample
///     rate and channel count. Downstream (future mixer/resampler)
///     normalizes to <see cref="AudioFormat.TeamTalkDefault"/> before
///     the TeamTalk sink.</description></item>
/// </list>
/// </remarks>
public sealed class WasapiLoopbackSource : AudioSourceBase
{
    #region Fields

    #region Optional target device
    // When null, we capture the current default render endpoint. When
    // set (by the constructor), we capture that specific device — used
    // by the future DeviceLoopback variant in v2+.
    private readonly string? _deviceId;
    #endregion

    #region NAudio capture handle
    // Lazy: created in OnStartAsync, disposed in OnStopAsync. Nulled
    // out between runs so the state machine can re-Start cleanly.
    private WasapiLoopbackCapture? _capture;
    #endregion

    #region Stopwatch baseline for frame timestamps
    // Stopwatch.GetTimestamp is high-resolution and monotonic — ideal
    // for AudioFrame.CaptureTimestampTicks, which is used for drift
    // measurement, not wall-clock time.
    #endregion

    #endregion

    #region Constructors

    /// <summary>Capture the current default render device.</summary>
    public WasapiLoopbackSource()
        : base(Guid.NewGuid(), "System Audio (Default Device)", AudioSourceType.Loopback)
    {
        _deviceId = null;
    }

    /// <summary>Capture a specific render device by its stable
    /// <c>MMDevice.ID</c>. Used by the future DeviceLoopback UI.</summary>
    public WasapiLoopbackSource(string deviceId, string friendlyName)
        : base(Guid.NewGuid(), friendlyName, AudioSourceType.DeviceLoopback)
    {
        _deviceId = deviceId;
    }

    #endregion

    #region Template methods (AudioSourceBase)

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        #region Create capture
        // Loopback capture = read-back of whatever the render endpoint
        // is mixing. Uses shared mode, so it never blocks other apps
        // from playing audio.
        if (_deviceId is null)
        {
            _capture = new WasapiLoopbackCapture();
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(_deviceId);
            _capture = new WasapiLoopbackCapture(device);
            // NAudio keeps the MMDevice alive internally via the capture;
            // we dispose the enumerator but NOT the device here.
        }
        #endregion

        #region Publish format
        // Announce the format BEFORE hooking events so the first frame
        // has a valid Format property. We always emit 16-bit PCM
        // regardless of the device's native float/int format.
        var wf = _capture.WaveFormat;
        SetFormat(new AudioFormat(
            SampleRate: wf.SampleRate,
            Channels: wf.Channels,
            BitsPerSample: 16));
        #endregion

        #region Hook events and start
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        #endregion

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken cancellationToken)
    {
        if (_capture is null) return Task.CompletedTask;

        // StopRecording is async internally — RecordingStopped fires on
        // the capture thread once draining is done. For our purposes,
        // calling Dispose is sufficient: NAudio blocks until capture
        // has actually stopped.
        try
        {
            _capture.StopRecording();
        }
        catch
        {
            // Best-effort: fall through to Dispose, which is what
            // actually releases the WASAPI handle.
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _capture = null;

        return Task.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        // If we were Stopped cleanly _capture is already null. If not,
        // tear it down here as a safety net.
        _capture?.Dispose();
        _capture = null;
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Event handlers (NAudio -> AudioFrame)

    /// <summary>
    /// Called by NAudio on its dedicated capture thread every ~10 ms
    /// with a chunk of audio. We convert to 16-bit PCM and forward.
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // Build a 16-bit PCM buffer. Path depends on what the device
        // is handing us.
        byte[] pcm16 = ConvertToPcm16(e.Buffer, e.BytesRecorded, _capture!.WaveFormat);

        var frame = new AudioFrame(
            data: pcm16,
            format: Format,
            captureTimestampTicks: Stopwatch.GetTimestamp());

        EmitFrame(frame);
    }

    /// <summary>
    /// NAudio raises this when the capture thread exits — either because
    /// we called StopRecording, or because a fault bubbled up. We don't
    /// do anything here beyond letting the base class manage state; a
    /// faulted capture will surface via OnStopAsync / OnStartAsync.
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Intentionally empty — AudioSourceBase's state machine is
        // driven from OnStopAsync, not from this callback.
    }

    #endregion

    #region Format conversion
    // Shared-mix WASAPI is usually IEEE float 32-bit; occasionally it's
    // already 16-bit PCM. Handle both; fall back to zero-filled buffer
    // for any other format (extremely rare).

    private static byte[] ConvertToPcm16(byte[] src, int srcCount, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            #region 32-bit float -> 16-bit PCM
            // 4 bytes per source sample, 2 bytes per destination sample.
            int samples = srcCount / 4;
            var dst = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                float f = BitConverter.ToSingle(src, i * 4);
                // Clamp and scale. 32767 (not short.MaxValue + 1) to
                // avoid wrap-around on f == 1.0f exactly.
                short s = (short)(Math.Clamp(f, -1f, 1f) * short.MaxValue);
                dst[i * 2] = (byte)(s & 0xFF);
                dst[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return dst;
            #endregion
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            #region Already 16-bit PCM — just copy
            var dst = new byte[srcCount];
            Buffer.BlockCopy(src, 0, dst, 0, srcCount);
            return dst;
            #endregion
        }

        #region Fallback: unsupported format
        // In practice we never hit this with modern Windows devices.
        // Returning silence is safer than throwing from the capture
        // thread, which would Fault the source.
        int safeSamples = srcCount / format.BlockAlign * format.Channels;
        return new byte[safeSamples * 2];
        #endregion
    }

    #endregion
}
#endregion
