#nullable enable

#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.TeamTalk.Client;

#region Class: TeamTalkClient (partial — connection)
/// <summary>
/// Connection concerns: open a TCP/UDP pair to a TeamTalk server, log
/// in, and tear it down. State-machine transitions come from the SDK's
/// event callbacks (see <c>TeamTalkClient.Events.cs</c>) or from
/// explicit error handling here.
/// </summary>
public sealed partial class TeamTalkClient
{
    #region Public API: ConnectAsync

    /// <summary>
    /// Open a connection and log in using the supplied config. The task
    /// completes when we have reached <see cref="TeamTalkConnectionState.LoggedIn"/>
    /// or faults if the connect / login fails.
    /// </summary>
    /// <param name="config">Server endpoint and credentials.</param>
    /// <param name="cancellationToken">Aborts the wait; does not
    /// guarantee the underlying socket is torn down instantly.</param>
    public async Task ConnectAsync(
        TeamTalkServerConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        #region Pre-transition guard
        lock (_stateLock)
        {
            if (_state is TeamTalkConnectionState.Connecting or
                          TeamTalkConnectionState.Authenticating or
                          TeamTalkConnectionState.LoggedIn or
                          TeamTalkConnectionState.Joined or
                          TeamTalkConnectionState.Streaming)
                return; // already working on it / already connected

            _currentConfig = config;
            _lastError = null;
        }
        TransitionTo(TeamTalkConnectionState.Connecting);
        #endregion

        #region Open connection + log in
#if TT_SDK_REFERENCED
        try
        {
            // poll_based: false = SDK marshals events to the creating
            // thread's message pump (so ConnectAsync must be invoked
            // from the UI dispatcher). poll_based: true would require
            // a manual event loop — revisit if we ever call Connect
            // from a pure background thread.
            _native = new BearWare.TeamTalk5(poll_based: false);
            AttachSdkEventHandlers(_native);  // see TeamTalkClient.Events.cs

            // Kick off TCP/UDP. The SDK is asynchronous — actual
            // connected/disconnected notifications arrive as events.
            if (!_native.Connect(
                    config.Host, config.TcpPort, config.UdpPort,
                    0, 0, config.UseEncryption))
            {
                throw new InvalidOperationException(
                    "TeamTalk.Connect returned false — check host/port.");
            }

            await WaitForStateAsync(
                TeamTalkConnectionState.Authenticating,
                cancellationToken).ConfigureAwait(false);

            // DoLoginEx is the 4-arg variant that accepts the client
            // name (vs. the 3-arg DoLogin kept for backwards compat).
            if (_native.DoLoginEx(
                    config.Nickname, config.Username,
                    config.Password, config.ClientName) < 0)
            {
                throw new InvalidOperationException("DoLoginEx returned an error cmd id.");
            }

            await WaitForStateAsync(
                TeamTalkConnectionState.LoggedIn,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TransitionTo(TeamTalkConnectionState.Faulted, ex);
            throw;
        }
#else
        // SDK not referenced yet — the project builds, but Connect is
        // a no-op that immediately faults with a clear message.
        await Task.Yield();
        var ex = new InvalidOperationException(
            "TeamTalk5.NET SDK is not referenced. See TeamTalkStreamer.TeamTalk.csproj " +
            "for instructions on adding the SDK DLL to libs/teamtalk/.");
        TransitionTo(TeamTalkConnectionState.Faulted, ex);
        throw ex;
#endif
        #endregion
    }

    #endregion

    #region Public API: DisconnectAsync

    /// <summary>
    /// Leave the channel (if any), log out, and close the socket.
    /// Idempotent: safe to call on an already-disconnected client.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state is TeamTalkConnectionState.Disconnected or
                          TeamTalkConnectionState.Disconnecting)
                return;
        }
        TransitionTo(TeamTalkConnectionState.Disconnecting);

#if TT_SDK_REFERENCED
        try
        {
            _native?.DoLeaveChannel();
            _native?.DoLogout();
            _native?.Disconnect();
        }
        catch (Exception ex)
        {
            // Non-fatal — we're going to Disconnected regardless.
            _lastError = ex;
        }
        finally
        {
            _native?.Dispose();
            _native = null;
        }
#else
        await Task.Yield();
#endif

        TransitionTo(TeamTalkConnectionState.Disconnected);
    }

    #endregion

    #region Private: wait for state
    // Small helper so the connect / join paths can await a particular
    // state transition without polling. Backed by the StateChanged
    // event so completion is immediate when the SDK fires.

    private Task WaitForStateAsync(
        TeamTalkConnectionState target,
        CancellationToken cancellationToken)
    {
        // Fast path: already there.
        if (State == target) return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<TeamTalkConnectionState>? handler = null;
        handler = (_, s) =>
        {
            if (s == target)
            {
                StateChanged -= handler;
                tcs.TrySetResult();
            }
            else if (s == TeamTalkConnectionState.Faulted)
            {
                StateChanged -= handler;
                tcs.TrySetException(_lastError ?? new Exception("TeamTalk faulted."));
            }
        };
        StateChanged += handler;

        // Wire cancellation so we don't hang forever on a stuck SDK.
        cancellationToken.Register(() =>
        {
            StateChanged -= handler;
            tcs.TrySetCanceled(cancellationToken);
        });

        return tcs.Task;
    }

    #endregion
}
#endregion
