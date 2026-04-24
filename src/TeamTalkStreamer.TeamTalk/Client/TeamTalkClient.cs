#nullable enable

#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.TeamTalk.Client;

#region Class: TeamTalkClient (partial — state & lifetime)
/// <summary>
/// High-level wrapper over the BearWare TeamTalk5.NET SDK. Exposes a
/// task-based connect / join / stream API and raises .NET events for
/// state transitions, so the rest of the app never touches SDK types.
/// </summary>
/// <remarks>
/// Split across five partial files:
/// <list type="bullet">
///   <item><description><c>TeamTalkClient.cs</c> — fields, properties,
///     events, state machine, disposal.</description></item>
///   <item><description><c>TeamTalkClient.Connection.cs</c> —
///     ConnectAsync / DisconnectAsync.</description></item>
///   <item><description><c>TeamTalkClient.Channels.cs</c> —
///     JoinChannelAsync / LeaveChannelAsync.</description></item>
///   <item><description><c>TeamTalkClient.Audio.cs</c> — PCM injection.</description></item>
///   <item><description><c>TeamTalkClient.Events.cs</c> — SDK-event
///     plumbing that feeds the state machine.</description></item>
/// </list>
///
/// SDK wiring: methods that touch the native TeamTalk5.NET types are
/// guarded by <c>#if TT_SDK_REFERENCED</c>. The symbol is defined in
/// the csproj only after the user uncomments the SDK reference; until
/// then, SDK calls are replaced with a single throwing stub so the
/// code still compiles and the structure stays intact.
/// </remarks>
public sealed partial class TeamTalkClient : IAsyncDisposable
{
    #region Fields

    #region Synchronization
    // Single lock for state transitions — the SDK isn't thread-safe
    // so we serialize all interactions through this object.
    private readonly object _stateLock = new();
    #endregion

    #region Backing state
    private TeamTalkConnectionState _state = TeamTalkConnectionState.Disconnected;
    private Exception? _lastError;
    private TeamTalkServerConfig? _currentConfig;
    #endregion

    #region Native SDK handle & pending-command tracking
    // Only declared when TT_SDK_REFERENCED is defined so the build stays
    // warning-clean without the SDK. Every partial that touches _native
    // is behind the same #if guard.
    //
    // _pendingJoinCmdId lets us correlate OnCmdSuccess/OnCmdError events
    // back to the join we issued, since the SDK no longer raises a
    // dedicated OnCmdMyselfJoinedChannel event.
#if TT_SDK_REFERENCED
    private BearWare.TeamTalk5? _native;
    private int _pendingJoinCmdId = -1;
    private System.Threading.Tasks.TaskCompletionSource? _pendingJoinTcs;
#endif
    #endregion

    #endregion

    #region Events

    /// <summary>Raised whenever <see cref="State"/> changes. Subscribers:
    /// UI status binding, Tolk speech announcer, audio feedback tones.</summary>
    public event EventHandler<TeamTalkConnectionState>? StateChanged;

    /// <summary>Raised when the server sends a text message, error code,
    /// kick, or other noteworthy event that doesn't fit the state enum.
    /// Passed as a human-readable string; consumers decide whether to
    /// announce it.</summary>
    public event EventHandler<string>? Notice;

    #endregion

    #region Properties

    /// <summary>Current connection state; thread-safe snapshot.</summary>
    public TeamTalkConnectionState State
    {
        get { lock (_stateLock) return _state; }
    }

    /// <summary>Last exception that caused a Faulted transition, or null.</summary>
    public Exception? LastError
    {
        get { lock (_stateLock) return _lastError; }
    }

    /// <summary>Config supplied to the most recent <c>ConnectAsync</c>.
    /// Null before the first connect.</summary>
    public TeamTalkServerConfig? CurrentConfig
    {
        get { lock (_stateLock) return _currentConfig; }
    }

    #endregion

    #region State-machine helpers
    // All partials transition through these helpers so the event is
    // raised consistently and _lastError is reset / captured uniformly.

    /// <summary>Move to a new state. Caller must NOT hold
    /// <see cref="_stateLock"/>; this method acquires it itself.</summary>
    private void TransitionTo(TeamTalkConnectionState next, Exception? error = null)
    {
        TeamTalkConnectionState previous;
        lock (_stateLock)
        {
            previous = _state;
            _state = next;
            if (error is not null) _lastError = error;
            else if (next != TeamTalkConnectionState.Faulted) _lastError = null;
        }

        if (previous != next)
            StateChanged?.Invoke(this, next);
    }

    /// <summary>Publish a free-form notice to subscribers (server text,
    /// error descriptions, etc.).</summary>
    private void RaiseNotice(string message) => Notice?.Invoke(this, message);

    #endregion

    #region Disposal (IAsyncDisposable)

    public async ValueTask DisposeAsync()
    {
        // If still connected, disconnect politely so the server doesn't
        // log us as a broken client. DisposeAsync must not throw.
        if (State is not (TeamTalkConnectionState.Disconnected or
                          TeamTalkConnectionState.Faulted))
        {
            try { await DisconnectAsync().ConfigureAwait(false); }
            catch { /* swallow — dispose must not throw */ }
        }

#if TT_SDK_REFERENCED
        _native?.Dispose();
        _native = null;
#endif

        GC.SuppressFinalize(this);
    }

    #endregion
}
#endregion
