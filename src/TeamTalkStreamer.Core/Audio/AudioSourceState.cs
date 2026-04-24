#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.Core.Audio;

#region Enum: AudioSourceState
/// <summary>
/// Lifecycle of a single <see cref="IAudioSource"/>. Drives UI text,
/// screen-reader announcements, and the router's decision about whether
/// to pull frames from the source. The state machine is enforced by
/// <c>AudioSourceBase</c>; concrete sources never mutate <c>State</c>
/// directly.
/// </summary>
/// <remarks>
/// Legal transitions:
/// <code>
///   Stopped   -> Starting
///   Starting  -> Capturing  (or Faulted on error)
///   Capturing -> Paused | Stopping | Faulted
///   Paused    -> Capturing | Stopping
///   Stopping  -> Stopped (or Faulted on error)
///   Faulted   -> Stopped (after user-initiated reset)
/// </code>
/// </remarks>
public enum AudioSourceState
{
    #region Resting states

    /// <summary>Default state before <c>StartAsync</c> has been called,
    /// or after <c>StopAsync</c> completes cleanly.</summary>
    Stopped = 0,

    #endregion

    #region Transient (entering capture)

    /// <summary>Transient: <c>StartAsync</c> has been invoked but the
    /// source has not yet produced its first frame. Useful for UI
    /// spinners and "connecting..." announcements.</summary>
    Starting = 1,

    #endregion

    #region Active states

    /// <summary>Source is actively producing <c>AudioFrame</c>s on its
    /// <c>FrameCaptured</c> event. Router is pulling from it.</summary>
    Capturing = 2,

    /// <summary>Capture continues internally but frames are dropped
    /// before reaching the router (effectively muted). Reserved for
    /// later use; not surfaced in v1 UI.</summary>
    Paused = 3,

    #endregion

    #region Transient (leaving capture)

    /// <summary>Transient: <c>StopAsync</c> invoked; the source is
    /// draining in-flight buffers before returning to <c>Stopped</c>.</summary>
    Stopping = 4,

    #endregion

    #region Terminal / error

    /// <summary>An unrecoverable capture error occurred. Inspect
    /// <see cref="IAudioSource.LastError"/> for the exception. User
    /// can reset the source to <c>Stopped</c> and retry.</summary>
    Faulted = 99,

    #endregion
}
#endregion
