#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.Core.Audio;

#region Interface: IAudioSource
/// <summary>
/// Abstraction over anything that produces PCM audio — WASAPI loopback,
/// a mobile device over WebSocket, a future file player, etc. The router
/// attaches to sources only through this contract, so new input kinds
/// drop in without touching the pipeline.
/// </summary>
/// <remarks>
/// Do not implement this interface directly; derive from
/// <see cref="AudioSourceBase"/>, which provides the state machine,
/// event plumbing, and disposal.
/// </remarks>
public interface IAudioSource : IAsyncDisposable
{
    #region Identity
    // Immutable values set at construction. Safe to read from any thread.

    /// <summary>Unique identifier for this source instance. Persisted in
    /// settings/presets so sources can be re-attached across restarts.</summary>
    Guid Id { get; }

    /// <summary>Human-readable name shown in UI and announced by the
    /// screen reader.</summary>
    string DisplayName { get; }

    /// <summary>Category tag — drives routing rules, icons, and
    /// announcements.</summary>
    AudioSourceType Type { get; }

    #endregion

    #region Live state
    // Observe changes via <see cref="StateChanged"/>; don't tight-poll.

    /// <summary>Current lifecycle state; see <see cref="AudioSourceState"/>
    /// for the transition diagram.</summary>
    AudioSourceState State { get; }

    /// <summary>PCM format this source emits once running. May be
    /// <c>default</c> before <c>StartAsync</c> returns.</summary>
    AudioFormat Format { get; }

    /// <summary>Last error if the source is in <c>Faulted</c>; otherwise null.</summary>
    Exception? LastError { get; }

    #endregion

    #region Events
    // Subscribers: AudioRouter (consumes frames), UI (state indicators),
    // accessibility services (announce transitions).

    /// <summary>Raised for each captured PCM frame. Handlers MUST be fast
    /// and non-blocking — they run on the source's capture thread.</summary>
    event EventHandler<AudioFrame>? FrameCaptured;

    /// <summary>Raised whenever <see cref="State"/> transitions. The new
    /// state is the event argument.</summary>
    event EventHandler<AudioSourceState>? StateChanged;

    #endregion

    #region Lifecycle
    // Start/Stop are async because capture setup can involve device
    // enumeration or network handshakes that must not block the UI thread.

    /// <summary>Begin capturing. Transitions Stopped -> Starting -> Capturing.
    /// Idempotent: if already running, returns immediately.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop capturing. Transitions Capturing -> Stopping -> Stopped.
    /// Idempotent: if already stopped, returns immediately.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    #endregion
}
#endregion
