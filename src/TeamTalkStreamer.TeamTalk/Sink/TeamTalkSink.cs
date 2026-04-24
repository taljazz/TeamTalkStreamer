#nullable enable

#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
using TeamTalkStreamer.Core.Audio;
using TeamTalkStreamer.Core.Pipeline;
using TeamTalkStreamer.TeamTalk.Client;
#endregion

namespace TeamTalkStreamer.TeamTalk.Sink;

#region Class: TeamTalkSink
/// <summary>
/// <see cref="IAudioSink"/> that forwards PCM frames from the router
/// into a <see cref="TeamTalkClient"/> for transmission on the joined
/// channel. Lives in the TeamTalk project (not Core) so Core stays
/// SDK-free.
/// </summary>
/// <remarks>
/// The sink is a thin adapter — it holds a reference to the shared
/// <see cref="TeamTalkClient"/> and delegates actual frame submission
/// to <c>SendFrameAsync</c>. Opening the sink turns on transmission;
/// closing it turns transmission off. The client itself must already
/// be joined to a channel before <see cref="OpenAsync"/> is called.
/// </remarks>
public sealed class TeamTalkSink : IAudioSink
{
    #region Fields

    #region Dependencies
    // Non-owning reference — the TeamTalkClient outlives the sink and
    // may have multiple sinks attached (future feature). Dispose does
    // not dispose the client.
    private readonly TeamTalkClient _client;
    #endregion

    #region State
    // _isOpen flips on OpenAsync and off on CloseAsync.
    private bool _isOpen;
    #endregion

    #endregion

    #region Constructor

    /// <param name="client">Shared TeamTalk client. Must be connected
    /// and joined to a channel before this sink is opened.</param>
    public TeamTalkSink(TeamTalkClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    #endregion

    #region Properties (IAudioSink)

    public string DisplayName => "TeamTalk Channel";
    public bool IsOpen => _isOpen;

    #endregion

    #region Lifecycle

    public Task OpenAsync(
        AudioFormat expectedFormat,
        CancellationToken cancellationToken = default)
    {
        // Format check: the sink only accepts TeamTalkDefault. Upstream
        // should resample before writing. We still let it through if
        // the rate/channels match closely; bit depth must be 16 or the
        // SDK's InsertAudioBlock will be unhappy.
        if (expectedFormat.BitsPerSample != 16)
            throw new NotSupportedException(
                "TeamTalkSink requires 16-bit PCM — resample upstream.");

        _client.BeginTransmission();
        _isOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (!_isOpen) return Task.CompletedTask;

        _client.EndTransmission();
        _isOpen = false;
        return Task.CompletedTask;
    }

    public ValueTask WriteAsync(
        AudioFrame frame,
        CancellationToken cancellationToken = default)
    {
        if (!_isOpen) return ValueTask.CompletedTask;

        // Forward the frame. The client handles SDK marshaling and
        // the "not in Streaming state" short-circuit itself.
        return _client.SendFrameAsync(frame, cancellationToken);
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync()
    {
        if (_isOpen)
        {
            try { await CloseAsync().ConfigureAwait(false); }
            catch { /* dispose must not throw */ }
        }
        GC.SuppressFinalize(this);
    }

    #endregion
}
#endregion
