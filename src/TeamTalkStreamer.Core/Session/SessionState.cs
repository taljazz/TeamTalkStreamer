#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.Core.Session;

#region Enum: SessionState
/// <summary>
/// Top-level application session state — the lifecycle of the whole
/// "connected to TeamTalk and streaming audio" experience. Composed
/// from the TeamTalk connection state plus the pipeline state; exposed
/// as a single enum so the UI has one thing to announce.
/// </summary>
public enum SessionState
{
    /// <summary>No TeamTalk connection, no sources active. Default on
    /// app startup and after a clean stop.</summary>
    Idle = 0,

    /// <summary>Connecting to the TeamTalk server and spinning up the
    /// configured sources. Transient.</summary>
    Starting = 1,

    /// <summary>Connected to TeamTalk and actively streaming audio into
    /// the joined channel. The steady state during normal use.</summary>
    Running = 2,

    /// <summary>User paused — still connected to TeamTalk but not
    /// sending audio. Quick-resume state.</summary>
    Paused = 3,

    /// <summary>Tearing down sources and disconnecting from TeamTalk.
    /// Transient.</summary>
    Stopping = 4,

    /// <summary>Session ended due to an unrecoverable error. Inspect
    /// logs; user can reset to <c>Idle</c> and restart.</summary>
    Faulted = 99,
}
#endregion
