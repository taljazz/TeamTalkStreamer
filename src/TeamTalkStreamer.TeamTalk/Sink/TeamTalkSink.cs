#nullable enable

#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
using TeamTalkStreamer.Core.Audio;
using TeamTalkStreamer.Core.Pipeline;
using TeamTalkStreamer.TeamTalk.Client;
#endregion

namespace TeamTalkStreamer.TeamTalk.Sink;

#region Class: TeamTalkSink
/// <summary>
/// <see cref="IAudioSink"/> that forwards PCM frames from the router
/// into a <see cref="TeamTalkClient"/> for transmission on the joined
/// channel. Lives in the TeamTalk project (not Core) so Core stays
/// SDK-free.
/// </summary>
/// <remarks>
/// The sink is a thin adapter — it holds a reference to the shared
/// <see cref="TeamTalkClient"/> and delegates actual frame submission
/// to <c>SendFrameAsync</c>. Opening the sink turns on transmission;
/// closing it turns transmission off. The client itself must already
/// be joined to a channel before <see cref="OpenAsync"/> is called.
/// </remarks>
public sealed class TeamTalkSink : IAudioSink
{
    #region Fields

    #region Dependencies
    // Non-owning reference — the TeamTalkClient outlives the sink and
    // may have multiple sinks attached (future feature). Dispose does
    // not dispose the client.
    private readonly TeamTalkClient _client;
    #endregion

    #region State
    // _isOpen flips on OpenAsync and off on CloseAsync.
    private bool _isOpen;

    // Master gain multiplier applied to every sample before it leaves
    // for TeamTalk. 1.0 = unity (no change); clamped to [0, 4] as a
    // defense against UI bugs sending wild values. Read with a plain
    // field read on the hot path — property access and the clamp live
    // on the setter, not the getter.
    private float _gain = 1.0f;
    #endregion

    #endregion

    #region Constructor

    /// <param name="client">Shared TeamTalk client. Must be connected
    /// and joined to a channel before this sink is opened.</param>
    public TeamTalkSink(TeamTalkClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    #endregion

    #region Properties (IAudioSink)

    public string DisplayName => "TeamTalk Channel";
    public bool IsOpen => _isOpen;

    /// <summary>Linear gain multiplier applied to every PCM sample
    /// before it's handed off to <see cref="TeamTalkClient.SendFrameAsync"/>.
    /// 1.0 = unity. 0.0 = silent. Values above 1.0 boost and clip to
    /// ±32767 to avoid wrap-around distortion. Set from the UI's
    /// master-gain slider; safe to change at any time, including while
    /// streaming is live.</summary>
    public float Gain
    {
        get => _gain;
        set => _gain = Math.Clamp(value, 0f, 4f);
    }

    #endregion

    #region Lifecycle

    public Task OpenAsync(
        AudioFormat expectedFormat,
        CancellationToken cancellationToken = default)
    {
        // Format check: the sink only accepts TeamTalkDefault. Upstream
        // should resample before writing. We still let it through if
        // the rate/channels match closely; bit depth must be 16 or the
        // SDK's InsertAudioBlock will be unhappy.
        if (expectedFormat.BitsPerSample != 16)
            throw new NotSupportedException(
                "TeamTalkSink requires 16-bit PCM — resample upstream.");

        _client.BeginTransmission();
        _isOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (!_isOpen) return Task.CompletedTask;

        _client.EndTransmission();
        _isOpen = false;
        return Task.CompletedTask;
    }

    public ValueTask WriteAsync(
        AudioFrame frame,
        CancellationToken cancellationToken = default)
    {
        if (!_isOpen) return ValueTask.CompletedTask;

        // Apply master gain if it's meaningfully different from unity.
        // Epsilon check so a near-1.0 float (rounding artifact from the
        // slider) doesn't trigger the per-frame allocation path.
        float gain = _gain;
        if (Math.Abs(gain - 1.0f) > 0.001f)
        {
            frame = ApplyGain(frame, gain);
        }

        return _client.SendFrameAsync(frame, cancellationToken);
    }

    #endregion

    #region Private helpers: gain application
    // Per-frame allocation is acceptable here: ~50 frames/sec at the
    // 20 ms buffers TeamTalk expects, so a short[] per frame is noise
    // compared to the JIT overhead of dispatching through the SDK. If
    // a future profile shows this hot, switch to ArrayPool<byte>.

    /// <summary>Return a new <see cref="AudioFrame"/> whose PCM payload
    /// is <paramref name="frame"/>.Data scaled by <paramref name="gain"/>,
    /// clamped to 16-bit signed range so out-of-range samples hard-clip
    /// instead of wrap-around distorting.</summary>
    private static AudioFrame ApplyGain(AudioFrame frame, float gain)
    {
        var src = frame.Data.Span;
        int byteCount = src.Length;
        int sampleCount = byteCount / 2;

        byte[] dst = new byte[byteCount];

        for (int i = 0; i < sampleCount; i++)
        {
            // Little-endian 16-bit read.
            short sample = (short)(src[i * 2] | (src[i * 2 + 1] << 8));

            // Scale, clamp, write back little-endian.
            float scaled = sample * gain;
            short clamped = (short)Math.Clamp(
                scaled, (float)short.MinValue, (float)short.MaxValue);
            dst[i * 2]     = (byte)(clamped & 0xFF);
            dst[i * 2 + 1] = (byte)((clamped >> 8) & 0xFF);
        }

        return new AudioFrame(
            data: dst,
            format: frame.Format,
            captureTimestampTicks: frame.CaptureTimestampTicks);
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync()
    {
        if (_isOpen)
        {
            try { await CloseAsync().ConfigureAwait(false); }
            catch { /* dispose must not throw */ }
        }
        GC.SuppressFinalize(this);
    }

    #endregion
}
#endregion
