#region Usings
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TeamTalkStreamer.Core.Audio;
using static TeamTalkStreamer.Audio.Windows.Wasapi.ProcessLoopbackNative;
#endregion

namespace TeamTalkStreamer.Audio.Windows.Wasapi;

#region Class: WasapiProcessLoopbackSource
/// <summary>
/// <see cref="IAudioSource"/> that captures the default render endpoint
/// with one process's audio tree EXCLUDED from the mix. Used when the
/// user has configured an excluded app (typically their screen reader)
/// so its speech is not broadcast into the TeamTalk channel along with
/// the rest of their system audio.
/// </summary>
/// <remarks>
/// Uses the Windows 10 2004+ Process Loopback API (via
/// <see cref="ProcessLoopbackNative.ActivateProcessLoopbackClient"/>).
/// Emits 16-bit PCM at 48 kHz stereo — matches the format requested in
/// <see cref="ProcessLoopbackNative.WAVEFORMATEX"/> below; Windows
/// resamples/converts internally as needed.
/// </remarks>
public sealed class WasapiProcessLoopbackSource : AudioSourceBase
{
    #region Constants

    #region Audio format
    // Process loopback doesn't let us query the device's native format;
    // we request what we want and Windows handles the conversion. Pick
    // the pipeline's canonical format (48 kHz stereo 16-bit) so there's
    // zero post-capture conversion before the router hands off to the
    // TeamTalk sink.
    private const int CaptureSampleRate = 48_000;
    private const int CaptureChannels   = 2;
    private const int CaptureBitsPerSample = 16;

    // 200 ms shared-mode buffer in 100-ns units. Large enough to avoid
    // starvation on a busy system, small enough that stop latency is
    // humanly imperceptible.
    private const long BufferDuration100Ns = 2_000_000;
    #endregion

    #region Activation timeout
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(5);
    #endregion

    #endregion

    #region Fields

    #region Input parameters
    // Captured in the constructor; never change afterwards.
    private readonly int _excludedProcessId;
    #endregion

    #region Live capture state
    // Populated on OnStartAsync, cleared on OnStopAsync. Readers use
    // _captureCts.IsCancellationRequested to know when to bail out.
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private AutoResetEvent? _captureEvent;
    private Thread? _captureThread;
    private CancellationTokenSource? _captureCts;
    #endregion

    #endregion

    #region Constructor

    /// <param name="excludedProcessId">PID whose process tree should be
    /// EXCLUDED from the capture. The caller is responsible for
    /// resolving this from a name (<c>Process.GetProcessesByName</c>)
    /// — this source takes the PID as-is.</param>
    /// <param name="displayName">Human-readable label shown in UI /
    /// spoken by Tolk (e.g. "System audio — excluding NVDA").</param>
    public WasapiProcessLoopbackSource(int excludedProcessId, string displayName)
        : base(Guid.NewGuid(), displayName, AudioSourceType.ProcessLoopbackExclude)
    {
        _excludedProcessId = excludedProcessId;
    }

    #endregion

    #region Template methods (AudioSourceBase)

    /// <summary>
    /// Full activation dance: activate the audio client, initialize with
    /// loopback + event-callback flags, get a capture client, wire up
    /// the event handle, and start a background thread that drains the
    /// capture buffer into <see cref="AudioFrame"/>s.
    /// </summary>
    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        #region Activate the process-loopback audio client
        _audioClient = ActivateProcessLoopbackClient(
            _excludedProcessId,
            ActivationTimeout,
            cancellationToken);
        #endregion

        #region Initialize with our preferred PCM format
        IntPtr formatPtr = AllocateWaveFormatEx(
            CaptureSampleRate, CaptureChannels, CaptureBitsPerSample);
        try
        {
            int hr = _audioClient.Initialize(
                shareMode: AUDCLNT_SHAREMODE_SHARED,
                streamFlags: AUDCLNT_STREAMFLAGS_LOOPBACK
                           | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                bufferDuration: BufferDuration100Ns,
                periodicity: 0,
                format: formatPtr,
                audioSessionGuid: IntPtr.Zero);

            if (hr < 0)
            {
                throw new InvalidOperationException(
                    $"IAudioClient.Initialize failed (HRESULT 0x{hr:X8}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }
        #endregion

        #region Get the capture client + wire the event handle
        Guid iidCapture = IID_IAudioCaptureClient;
        int hrService = _audioClient.GetService(ref iidCapture, out object captureObj);
        if (hrService < 0 || captureObj is null)
        {
            throw new InvalidOperationException(
                $"IAudioClient.GetService(IAudioCaptureClient) failed (HRESULT 0x{hrService:X8}).");
        }

        _captureClient = (IAudioCaptureClient)captureObj;

        _captureEvent = new AutoResetEvent(initialState: false);
        int hrEvent = _audioClient.SetEventHandle(
            _captureEvent.SafeWaitHandle.DangerousGetHandle());
        if (hrEvent < 0)
        {
            throw new InvalidOperationException(
                $"IAudioClient.SetEventHandle failed (HRESULT 0x{hrEvent:X8}).");
        }
        #endregion

        #region Publish format to the base class + start the capture thread
        SetFormat(new AudioFormat(
            SampleRate: CaptureSampleRate,
            Channels: CaptureChannels,
            BitsPerSample: CaptureBitsPerSample));

        _captureCts = new CancellationTokenSource();
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = $"WasapiProcessLoopback-exclude-{_excludedProcessId}",
        };
        _captureThread.Start();

        int hrStart = _audioClient.Start();
        if (hrStart < 0)
        {
            throw new InvalidOperationException(
                $"IAudioClient.Start failed (HRESULT 0x{hrStart:X8}).");
        }
        #endregion

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken cancellationToken)
    {
        // Signal cancel first so the capture thread exits its loop on
        // the next wait, then Stop the audio client so SetEventHandle
        // fires one last time (lets the thread wake and see the cancel).
        _captureCts?.Cancel();

        try { _audioClient?.Stop(); } catch { /* best-effort */ }

        if (_captureThread is { IsAlive: true })
        {
            _captureThread.Join(TimeSpan.FromSeconds(2));
        }
        _captureThread = null;

        ReleaseNativeResources();
        return Task.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        // Safety net in case OnStopAsync wasn't called (e.g., Dispose
        // before Stop). Idempotent — ReleaseNativeResources nulls out
        // the fields it releases.
        _captureCts?.Cancel();
        if (_captureThread is { IsAlive: true })
        {
            _captureThread.Join(TimeSpan.FromSeconds(1));
        }
        ReleaseNativeResources();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Capture loop
    // Runs on a dedicated background thread. Waits for the audio client
    // to raise its event (new buffer available), drains every packet
    // currently in the ring, emits each as an AudioFrame, then loops.

    private void CaptureLoop()
    {
        // Copy the fields once so the loop doesn't have to deal with
        // them going null if OnStopAsync races us mid-iteration.
        var captureEvent = _captureEvent;
        var captureClient = _captureClient;
        var ct = _captureCts?.Token ?? CancellationToken.None;

        if (captureEvent is null || captureClient is null) return;

        // Bytes per frame across all channels — used for the GetBuffer
        // -> byte[] conversion. 16-bit stereo = 4 bytes per frame.
        int bytesPerFrame = CaptureChannels * (CaptureBitsPerSample / 8);

        while (!ct.IsCancellationRequested)
        {
            // Wait up to 200 ms so we also check the cancel token
            // periodically in case the event is starved.
            if (!captureEvent.WaitOne(200)) continue;
            if (ct.IsCancellationRequested) break;

            #region Drain every packet currently queued
            while (true)
            {
                int hrSize = captureClient.GetNextPacketSize(out uint packetFrames);
                if (hrSize < 0 || packetFrames == 0) break;

                int hrBuffer = captureClient.GetBuffer(
                    out IntPtr nativeBuffer,
                    out uint framesRead,
                    out uint bufferFlags,
                    out _,
                    out _);
                if (hrBuffer < 0) break;

                try
                {
                    int byteCount = (int)framesRead * bytesPerFrame;
                    if (byteCount <= 0) continue;

                    // Copy native -> managed so the AudioFrame can outlive
                    // ReleaseBuffer. The SILENT flag means Windows has
                    // inserted filler (no real audio) — we emit silence
                    // in that case so downstream timing stays stable.
                    byte[] pcm = new byte[byteCount];
                    if ((bufferFlags & AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                    {
                        Marshal.Copy(nativeBuffer, pcm, 0, byteCount);
                    }

                    var frame = new AudioFrame(
                        data: pcm,
                        format: Format,
                        captureTimestampTicks: Stopwatch.GetTimestamp());
                    EmitFrame(frame);
                }
                finally
                {
                    // ALWAYS ReleaseBuffer the same frame count we got
                    // from GetBuffer, even on exception — otherwise the
                    // capture pipeline stalls.
                    captureClient.ReleaseBuffer(framesRead);
                }
            }
            #endregion
        }
    }

    #endregion

    #region Native helpers

    /// <summary>
    /// Allocate + populate a <see cref="WAVEFORMATEX"/> in unmanaged
    /// memory so we can pass a pointer to <c>IAudioClient.Initialize</c>.
    /// Caller is responsible for <see cref="Marshal.FreeHGlobal"/>.
    /// </summary>
    private static IntPtr AllocateWaveFormatEx(
        int sampleRate, int channels, int bitsPerSample)
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag      = WAVE_FORMAT_PCM,
            nChannels       = (ushort)channels,
            nSamplesPerSec  = (uint)sampleRate,
            wBitsPerSample  = (ushort)bitsPerSample,
            nBlockAlign     = (ushort)(channels * (bitsPerSample / 8)),
            nAvgBytesPerSec = (uint)(sampleRate * channels * (bitsPerSample / 8)),
            cbSize          = 0,
        };

        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEX>());
        Marshal.StructureToPtr(format, ptr, fDeleteOld: false);
        return ptr;
    }

    /// <summary>Release every COM / sync primitive the source owns.
    /// Idempotent — safe to call multiple times.</summary>
    private void ReleaseNativeResources()
    {
        if (_captureClient is not null)
        {
            try { Marshal.ReleaseComObject(_captureClient); } catch { }
            _captureClient = null;
        }

        if (_audioClient is not null)
        {
            try { Marshal.ReleaseComObject(_audioClient); } catch { }
            _audioClient = null;
        }

        _captureEvent?.Dispose();
        _captureEvent = null;

        _captureCts?.Dispose();
        _captureCts = null;
    }

    #endregion
}
#endregion
