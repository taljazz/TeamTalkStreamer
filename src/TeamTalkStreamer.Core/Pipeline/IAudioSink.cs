#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
using TeamTalkStreamer.Core.Audio;
#endregion

namespace TeamTalkStreamer.Core.Pipeline;

#region Interface: IAudioSink
/// <summary>
/// Abstraction over anything that consumes PCM audio. In v1 the only
/// sink is <c>TeamTalkSink</c>, but future sinks (file recorder, UDP
/// broadcast, playback preview for debugging) plug in through this
/// contract.
/// </summary>
/// <remarks>
/// Sinks are attached to the router via <c>AudioRouter.AddSink</c>. The
/// router calls <see cref="WriteAsync"/> for every frame from every
/// active source — implementations buffer and flush on their own
/// schedule.
/// </remarks>
public interface IAudioSink : IAsyncDisposable
{
    #region Identity & state
    // Surfaced in UI so users can tell which sinks are active.

    /// <summary>Human-readable name for UI and screen reader.</summary>
    string DisplayName { get; }

    /// <summary>True once <see cref="OpenAsync"/> has succeeded and the
    /// sink is ready to accept frames.</summary>
    bool IsOpen { get; }

    #endregion

    #region Lifecycle
    // Explicit open/close rather than implicit-on-first-write so the
    // router can pre-validate the pipeline before streaming starts.

    /// <summary>Open the underlying transport and prepare to receive
    /// frames in the given format.</summary>
    Task OpenAsync(AudioFormat expectedFormat, CancellationToken cancellationToken = default);

    /// <summary>Close the transport and release resources.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Data path

    /// <summary>Write a single PCM frame. Must be safe to call from any
    /// thread. Implementations buffer internally — if the buffer is
    /// full it's the sink's decision whether to block, drop, or
    /// overflow, not the router's.</summary>
    ValueTask WriteAsync(AudioFrame frame, CancellationToken cancellationToken = default);

    #endregion
}
#endregion
