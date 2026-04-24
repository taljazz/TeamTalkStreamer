#nullable enable

#region Usings
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.TeamTalk.Client;

#region Class: TeamTalkClient (partial — channels)
/// <summary>
/// Channel join / leave. Must only be called while the client is in
/// <see cref="TeamTalkConnectionState.LoggedIn"/> or deeper; earlier
/// states throw.
/// </summary>
public sealed partial class TeamTalkClient
{
    #region Public API: JoinChannelAsync

    /// <summary>
    /// Join a channel by path (e.g. <c>/Lobby/StreamRoom</c>) using the
    /// supplied password. Transitions to <see cref="TeamTalkConnectionState.Joined"/>
    /// on success.
    /// </summary>
    public async Task JoinChannelAsync(
        string channelPath,
        string channelPassword = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelPath);

        #region Pre-transition guard
        switch (State)
        {
            case TeamTalkConnectionState.LoggedIn:
                break; // fine — proceed
            case TeamTalkConnectionState.Joined:
            case TeamTalkConnectionState.Streaming:
                return; // already in a channel; treat as no-op
            default:
                throw new InvalidOperationException(
                    $"Cannot join channel from state {State}. Connect first.");
        }
        #endregion

        #region Issue join command
#if TT_SDK_REFERENCED
        try
        {
            // TT accepts either channel ID or path; path is nicer for
            // config files, so resolve it here. BUT: right after login
            // the server is still pushing the channel tree, so
            // GetChannelIDFromPath can return 0 for a channel that
            // genuinely exists. Poll briefly instead of failing.
            int channelId = await WaitForChannelIdAsync(
                channelPath, timeoutMs: 4_000, cancellationToken)
                .ConfigureAwait(false);

            if (channelId <= 0)
                throw new InvalidOperationException(
                    $"Channel path '{channelPath}' not found on server.");

            // Capture the cmd id so OnCmdSuccess/OnCmdError in
            // TeamTalkClient.Events.cs can correlate back to this join
            // — the SDK doesn't fire a dedicated "myself-joined" event.
            int cmdId = _native!.DoJoinChannelByID(channelId, channelPassword);
            if (cmdId < 0)
                throw new InvalidOperationException("DoJoinChannelByID returned error.");

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_stateLock)
            {
                _pendingJoinCmdId = cmdId;
                _pendingJoinTcs = tcs;
            }

            // Also wire cancellation so we don't hang forever.
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            await tcs.Task.ConfigureAwait(false);

            TransitionTo(TeamTalkConnectionState.Joined);
        }
        catch (Exception ex)
        {
            TransitionTo(TeamTalkConnectionState.Faulted, ex);
            throw;
        }
#else
        await Task.Yield();
        var ex = new InvalidOperationException("TeamTalk SDK not referenced — see csproj.");
        TransitionTo(TeamTalkConnectionState.Faulted, ex);
        throw ex;
#endif
        #endregion
    }

    #endregion

    #region Public API: LeaveChannelAsync

    /// <summary>
    /// Leave the current channel. Returns to <see cref="TeamTalkConnectionState.LoggedIn"/>.
    /// Idempotent.
    /// </summary>
    public async Task LeaveChannelAsync(CancellationToken cancellationToken = default)
    {
        if (State is TeamTalkConnectionState.LoggedIn or
                     TeamTalkConnectionState.Disconnected or
                     TeamTalkConnectionState.Disconnecting)
            return;

#if TT_SDK_REFERENCED
        _native?.DoLeaveChannel();
        await WaitForStateAsync(
            TeamTalkConnectionState.LoggedIn,
            cancellationToken).ConfigureAwait(false);
#else
        await Task.Yield();
#endif
    }

    #endregion

    #region Public API: EnumerateChannelsAsync

    /// <summary>
    /// Fetch every channel the server advertises, flattened into a
    /// list of <see cref="ChannelInfo"/>s. Callers use this to present
    /// a pick-list in the settings dialog so users don't have to
    /// hand-type channel paths.
    /// </summary>
    /// <remarks>
    /// Must be called while the client is at least
    /// <see cref="TeamTalkConnectionState.LoggedIn"/>. Gives the server
    /// a short grace period to push the channel tree (channels arrive
    /// asynchronously after login) before calling
    /// <c>GetServerChannels</c>.
    /// </remarks>
    public async Task<IReadOnlyList<ChannelInfo>> EnumerateChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        // Pre-condition: must be logged in.
        if (State is not (TeamTalkConnectionState.LoggedIn or
                          TeamTalkConnectionState.Joined or
                          TeamTalkConnectionState.Streaming))
        {
            throw new InvalidOperationException(
                "Must be logged in before enumerating channels.");
        }

#if TT_SDK_REFERENCED
        // Poll until the server has finished pushing its channel tree.
        // Retry up to 3 seconds (15 attempts * 200 ms) but exit early
        // as soon as at least one channel has arrived.
        BearWare.Channel[]? channels = null;
        for (int attempt = 0; attempt < 15; attempt++)
        {
            if (_native!.GetServerChannels(out channels) &&
                channels is { Length: > 0 })
            {
                break;
            }
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        if (channels is null || channels.Length == 0)
            return Array.Empty<ChannelInfo>();

        // Build the flat list of ChannelInfo records. GetChannelPath
        // fills a ref-string with the full "/foo/bar" path from root.
        var result = new List<ChannelInfo>(channels.Length);
        foreach (var ch in channels)
        {
            string path = string.Empty;
            _native!.GetChannelPath(ch.nChannelID, ref path);

            result.Add(new ChannelInfo(
                Id: ch.nChannelID,
                Path: string.IsNullOrEmpty(path) ? "/" : path,
                Name: ch.szName ?? string.Empty,
                RequiresPassword: ch.bPassword));
        }
        return result;
#else
        await Task.Yield();
        return Array.Empty<ChannelInfo>();
#endif
    }

    #endregion

    #region Private: WaitForChannelIdAsync

    /// <summary>
    /// Poll <c>GetChannelIDFromPath</c> until it returns a positive id
    /// or the timeout elapses. Solves the classic "channel not found
    /// immediately after login" race where the server hasn't yet
    /// pushed the channel list.
    /// </summary>
    private async Task<int> WaitForChannelIdAsync(
        string channelPath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
#if TT_SDK_REFERENCED
        int elapsed = 0;
        const int stepMs = 150;
        while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
        {
            int id = _native!.GetChannelIDFromPath(channelPath);
            if (id > 0) return id;

            await Task.Delay(stepMs, cancellationToken).ConfigureAwait(false);
            elapsed += stepMs;
        }
        // Final attempt in case the loop exited right on the boundary.
        return _native!.GetChannelIDFromPath(channelPath);
#else
        await Task.Yield();
        return 0;
#endif
    }

    #endregion
}
#endregion
