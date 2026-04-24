#region Usings
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TeamTalkStreamer.Core.Audio;
#endregion

namespace TeamTalkStreamer.MobileBridge.Server;

#region Class: MobileAudioSource
/// <summary>
/// <see cref="IAudioSource"/> representing one connected mobile device.
/// Unlike WASAPI, this source doesn't capture anything itself — the
/// WebSocket session owns the transport and pushes decoded PCM into
/// the source via <see cref="SubmitPcm"/>.
/// </summary>
/// <remarks>
/// Lifetime: the <c>MobileBridgeServer</c> creates a source when the
/// WebSocket handshake succeeds, attaches it to the router, and disposes
/// it when the socket closes. The template methods
/// (<see cref="OnStartAsync"/>, <see cref="OnStopAsync"/>) don't need
/// to do much because capture is already "running" by virtue of the
/// socket being open.
/// </remarks>
public sealed class MobileAudioSource : AudioSourceBase
{
    #region Fields

    #region Submit lock
    // Not strictly required — EmitFrame is thread-safe — but holding
    // a short lock makes it cleaner to observe that "source must be
    // Capturing before frames flow" in the submit path.
    private readonly object _submitLock = new();
    #endregion

    #endregion

    #region Constructor

    /// <param name="id">Stable device Guid (from the Hello handshake).</param>
    /// <param name="displayName">Human-readable, e.g. "Thomas's iPhone".</param>
    public MobileAudioSource(Guid id, string displayName)
        : base(id, displayName, AudioSourceType.Mobile)
    {
    }

    #endregion

    #region Template methods (AudioSourceBase)

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        // The socket is already open by the time the server calls
        // StartAsync. We just publish the format we expect frames in.
        SetFormat(AudioFormat.TeamTalkDefault);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken cancellationToken)
    {
        // Nothing to release here — the server closes the socket on
        // its own timetable.
        return Task.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Public API: frame submission
    // Called by the WebSocket handler on its own task for each decoded
    // Opus frame. Packages the samples into an AudioFrame and emits.

    /// <summary>Push a chunk of decoded PCM into the pipeline. Bytes
    /// MUST already be in <see cref="AudioFormat.TeamTalkDefault"/>
    /// (48 kHz mono 16-bit) — decoding happens upstream in the server.</summary>
    public void SubmitPcm(ReadOnlyMemory<byte> pcm16)
    {
        lock (_submitLock)
        {
            if (State != AudioSourceState.Capturing) return;

            var frame = new AudioFrame(
                data: pcm16,
                format: Format,
                captureTimestampTicks: Stopwatch.GetTimestamp());

            EmitFrame(frame);
        }
    }

    #endregion
}
#endregion
