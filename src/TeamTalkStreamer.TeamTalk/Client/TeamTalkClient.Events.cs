#nullable enable

#region Usings
using System;
#endregion

namespace TeamTalkStreamer.TeamTalk.Client;

#region Class: TeamTalkClient (partial — SDK event pump)
/// <summary>
/// Glue between the SDK's event callbacks and our managed state
/// machine. Kept isolated in its own partial so the SDK-specific
/// delegate signatures are in one place and easy to update when the
/// SDK API shifts.
/// </summary>
public sealed partial class TeamTalkClient
{
    #region SDK event wiring
    // Called from TeamTalkClient.Connection.cs right after we construct
    // the native client. Each line below maps one native event to one
    // managed handler below.

#if TT_SDK_REFERENCED
    private void AttachSdkEventHandlers(BearWare.TeamTalk5 native)
    {
        native.OnConnectionSuccess  += HandleConnectionSuccess;
        native.OnConnectionFailed   += HandleConnectionFailed;
        native.OnConnectionLost     += HandleConnectionLost;

        native.OnCmdMyselfLoggedIn  += HandleLoggedIn;
        native.OnCmdMyselfLoggedOut += HandleLoggedOut;

        native.OnCmdSuccess         += HandleCmdSuccess;
        native.OnCmdError           += HandleCmdError;

        // No "myself joined/left" events exist in v5.22 — we track join
        // completion via OnCmdSuccess matched against _pendingJoinCmdId.
    }
#endif

    #endregion

    #region Handlers

#if TT_SDK_REFERENCED

    #region Connection lifecycle

    /// <summary>TCP handshake completed; ready to issue DoLoginEx.</summary>
    private void HandleConnectionSuccess()
    {
        TransitionTo(TeamTalkConnectionState.Authenticating);
    }

    /// <summary>TCP handshake failed — couldn't reach the server.</summary>
    private void HandleConnectionFailed()
    {
        TransitionTo(
            TeamTalkConnectionState.Faulted,
            new Exception("Could not connect to TeamTalk server."));
    }

    /// <summary>Connection dropped after previously succeeding.</summary>
    private void HandleConnectionLost()
    {
        TransitionTo(
            TeamTalkConnectionState.Faulted,
            new Exception("Connection to TeamTalk server was lost."));
    }

    #endregion

    #region Login lifecycle

    /// <summary>SDK signature: MyselfLoggedIn(int, UserAccount).</summary>
    private void HandleLoggedIn(int myUserId, BearWare.UserAccount account)
    {
        _ = myUserId;
        _ = account;
        TransitionTo(TeamTalkConnectionState.LoggedIn);
    }

    private void HandleLoggedOut()
    {
        // Reached only if the server logs us out unexpectedly — the
        // user-initiated path goes through DisconnectAsync.
        TransitionTo(TeamTalkConnectionState.Disconnected);
    }

    #endregion

    #region Command correlation
    // Channel joins (and anything else fired via DoXxx) return a cmd id.
    // The SDK later reports completion via OnCmdSuccess / OnCmdError
    // with that id — so correlate by matching against _pendingJoinCmdId.

    private void HandleCmdSuccess(int cmdId)
    {
        System.Threading.Tasks.TaskCompletionSource? tcs = null;
        lock (_stateLock)
        {
            if (cmdId == _pendingJoinCmdId)
            {
                tcs = _pendingJoinTcs;
                _pendingJoinCmdId = -1;
                _pendingJoinTcs = null;
            }
        }
        tcs?.TrySetResult();
    }

    private void HandleCmdError(int cmdId, BearWare.ClientErrorMsg err)
    {
        string text = err.szErrorMsg ?? "Unknown server error";
        RaiseNotice(text);

        System.Threading.Tasks.TaskCompletionSource? tcs = null;
        lock (_stateLock)
        {
            if (cmdId == _pendingJoinCmdId)
            {
                tcs = _pendingJoinTcs;
                _pendingJoinCmdId = -1;
                _pendingJoinTcs = null;
            }
        }
        tcs?.TrySetException(new Exception(text));

        // During connect/login, a command error ends the session.
        if (State is TeamTalkConnectionState.Connecting or
                     TeamTalkConnectionState.Authenticating)
        {
            TransitionTo(
                TeamTalkConnectionState.Faulted,
                new Exception(text));
        }
    }

    #endregion

#endif

    #endregion
}
#endregion
