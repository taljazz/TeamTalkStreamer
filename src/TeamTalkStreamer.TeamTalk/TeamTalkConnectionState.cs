#nullable enable

#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.TeamTalk;

#region Enum: TeamTalkConnectionState
/// <summary>
/// Lifecycle of the TeamTalk client. Finer-grained than the app-level
/// <c>SessionState</c> because we want to distinguish "TCP connected"
/// from "logged in" from "joined a channel" from "sending audio" for
/// diagnostic messages and screen-reader announcements.
/// </summary>
public enum TeamTalkConnectionState
{
    #region Resting states

    /// <summary>No connection. Initial state and post-disconnect.</summary>
    Disconnected = 0,

    #endregion

    #region Transient (connecting)

    /// <summary>TCP handshake with the server in progress.</summary>
    Connecting = 1,

    /// <summary>TCP connected; logging in with credentials.</summary>
    Authenticating = 2,

    #endregion

    #region Active states

    /// <summary>Logged in but not in a channel. Idle-connected state.</summary>
    LoggedIn = 3,

    /// <summary>Joined a channel; ready to send/receive audio.</summary>
    Joined = 4,

    /// <summary>Joined + actively transmitting PCM frames from a sink.</summary>
    Streaming = 5,

    #endregion

    #region Transient (disconnecting)

    /// <summary>Disconnect requested; draining and closing the socket.</summary>
    Disconnecting = 6,

    #endregion

    #region Terminal / error

    /// <summary>Connection dropped or login/join failed. Inspect
    /// <c>TeamTalkClient.LastError</c> for details.</summary>
    Faulted = 99,

    #endregion
}
#endregion
