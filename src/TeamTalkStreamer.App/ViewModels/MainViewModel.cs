#region Usings
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TeamTalkStreamer.Accessibility.Feedback;
using TeamTalkStreamer.Accessibility.Speech;
using TeamTalkStreamer.App.Views;
using TeamTalkStreamer.Audio.Windows.Wasapi;
using TeamTalkStreamer.Core.Audio;
using TeamTalkStreamer.Core.Pipeline;
using TeamTalkStreamer.Core.Session;
using TeamTalkStreamer.Persistence.Config;
using TeamTalkStreamer.TeamTalk;
using TeamTalkStreamer.TeamTalk.Client;
using TeamTalkStreamer.TeamTalk.Sink;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: MainViewModel (partial — shared infrastructure)
/// <summary>
/// View model backing <c>MainWindow</c>. Holds all the pipeline
/// services and surfaces them to XAML via properties and commands.
/// Split across three partial files by concern so no single file owns
/// the entire UI story.
/// </summary>
/// <remarks>
/// Partial split:
/// <list type="bullet">
///   <item><description><c>MainViewModel.cs</c> — shared fields, ctor,
///     INotifyPropertyChanged plumbing, displayed status strings.</description></item>
///   <item><description><c>MainViewModel.Sources.cs</c> — source list
///     and add/remove commands.</description></item>
///   <item><description><c>MainViewModel.Connection.cs</c> — TeamTalk
///     connect/disconnect + streaming commands.</description></item>
///   <item><description><c>MainViewModel.Help.cs</c> — F1 "open
///     user guide" command.</description></item>
///   <item><description><c>MainViewModel.Feedback.cs</c> — feedback-
///     tone volume control bound to the <c>=</c> / <c>-</c> keys,
///     including the startup load of the saved volume.</description></item>
///   <item><description><c>MainViewModel.MasterGain.cs</c> — master
///     stream-gain slider value, live sink update, and debounced
///     save to settings.</description></item>
/// </list>
///
/// Note: the mobile-bridge UI (and its <c>MainViewModel.MobileBridge.cs</c>
/// partial) was removed in favor of the recoverable-hide approach while
/// iOS toolchain access is unavailable. The <c>TeamTalkStreamer.MobileBridge</c>
/// project still exists in the solution with all its server / protocol /
/// source code intact — when the iOS companion app is ready to build,
/// restore the ProjectReference in <c>TeamTalkStreamer.App.csproj</c>,
/// re-register the services in <c>AppHost</c>, and re-add the partial
/// + XAML GroupBox.
/// </remarks>
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    #region Fields

    #region Injected services
    // Captured in the constructor. All read-only so no partial can
    // accidentally replace a reference mid-run.
    private readonly ISpeechService _speech;
    private readonly IAudioFeedbackService _feedback;
    private readonly AudioRouter _router;
    private readonly WasapiLoopbackSource _loopbackSource;
    private readonly TeamTalkClient _teamTalk;
    private readonly TeamTalkSink _sink;
    private readonly IAppSettingsStore _settingsStore;

    // Factories (not singleton references) so each click gets a fresh,
    // fully-initialized dialog. Registered in AppHost as small lambdas
    // that resolve the transient window types.
    private readonly Func<ServerSettingsWindow> _serverSettingsWindowFactory;
    private readonly Func<ExcludedProcessWindow> _excludedProcessWindowFactory;
    #endregion

    #region Active system-audio source
    // Holds whichever IAudioSource is currently attached for the default
    // playback device: either the plain `_loopbackSource` singleton or
    // a transient WasapiProcessLoopbackSource when the user has set an
    // excluded app. Set in StartStreamingAsync, cleared in
    // StopStreamingAsync.
    private IAudioSource? _activeSystemAudioSource;
    #endregion

    #region UI-bound state
    // Everything the XAML binds to lives here as a field + property pair.
    private SessionState _sessionState = SessionState.Idle;
    private TeamTalkConnectionState _ttState = TeamTalkConnectionState.Disconnected;
    #endregion

    #endregion

    #region Constructor
    // All services injected by the DI container (AppHost). Post-
    // construction we subscribe to state-changed events from the
    // pipeline components and mirror them into view-model properties.

    public MainViewModel(
        ISpeechService speech,
        IAudioFeedbackService feedback,
        AudioRouter router,
        WasapiLoopbackSource loopbackSource,
        TeamTalkClient teamTalk,
        TeamTalkSink sink,
        IAppSettingsStore settingsStore,
        Func<ServerSettingsWindow> serverSettingsWindowFactory,
        Func<ExcludedProcessWindow> excludedProcessWindowFactory)
    {
        _speech = speech;
        _feedback = feedback;
        _router = router;
        _loopbackSource = loopbackSource;
        _teamTalk = teamTalk;
        _sink = sink;
        _settingsStore = settingsStore;
        _serverSettingsWindowFactory = serverSettingsWindowFactory;
        _excludedProcessWindowFactory = excludedProcessWindowFactory;

        // Observe TeamTalk connection transitions and mirror them
        // into bound UI properties plus Tolk / feedback announcements.
        _teamTalk.StateChanged += (_, s) =>
        {
            TeamTalkState = s;
            SpeakConnectionTransition(s);
        };

        Sources = new ObservableCollection<IAudioSource>();

        // Restore user-saved audio preferences before any tone plays
        // or any streaming starts. Each method lives in its own partial
        // so the respective concerns stay wholly in one file each.
        ApplySavedFeedbackVolume();
        ApplySavedMasterGain();
    }

    #endregion

    #region Bindable state properties

    public SessionState Session
    {
        get => _sessionState;
        private set => SetProperty(ref _sessionState, value);
    }

    public TeamTalkConnectionState TeamTalkState
    {
        get => _ttState;
        private set
        {
            if (SetProperty(ref _ttState, value))
            {
                // Anything downstream that derives from TeamTalkState
                // re-notifies here so the bound UI stays in sync.
                OnPropertyChanged(nameof(SessionStatusText));
                OnPropertyChanged(nameof(IsStreamActive));
                OnPropertyChanged(nameof(StreamToggleLabel));
            }
        }
    }

    #region Streaming toggle helpers
    // IsStreamActive captures "anything but fully offline" so the
    // toggle button reads "Stop streaming" during the whole session,
    // including Connecting / Authenticating / Joined — clicking it in
    // those transient states cancels the in-progress flow. Faulted
    // falls back to "Start streaming" so the user can retry.

    /// <summary>True while we're either streaming or on the way
    /// there. Drives <see cref="StreamToggleLabel"/>.</summary>
    public bool IsStreamActive => _ttState is
        TeamTalkConnectionState.Connecting or
        TeamTalkConnectionState.Authenticating or
        TeamTalkConnectionState.LoggedIn or
        TeamTalkConnectionState.Joined or
        TeamTalkConnectionState.Streaming;

    /// <summary>Label for the single toggle button in the main window.
    /// Re-evaluated on every <see cref="TeamTalkState"/> transition.</summary>
    public string StreamToggleLabel =>
        IsStreamActive ? "Stop streaming" : "Start streaming";

    #endregion

    /// <summary>Derived status line for the top of the window.</summary>
    public string SessionStatusText => TeamTalkState switch
    {
        TeamTalkConnectionState.Disconnected => "Disconnected",
        TeamTalkConnectionState.Connecting   => "Connecting…",
        TeamTalkConnectionState.Authenticating => "Logging in…",
        TeamTalkConnectionState.LoggedIn     => "Logged in",
        TeamTalkConnectionState.Joined       => "In channel (not streaming)",
        TeamTalkConnectionState.Streaming    => "Streaming to channel",
        TeamTalkConnectionState.Disconnecting => "Disconnecting…",
        TeamTalkConnectionState.Faulted      => $"Error: {_teamTalk.LastError?.Message ?? "unknown"}",
        _ => "Idle",
    };

    #endregion

    #region Speech helpers
    // Announce major transitions via Tolk so blind users hear every
    // connection-level event without tabbing to the status text.

    private void SpeakConnectionTransition(TeamTalkConnectionState state)
    {
        string? line = state switch
        {
            TeamTalkConnectionState.Connecting      => "Connecting to TeamTalk.",
            TeamTalkConnectionState.LoggedIn        => "Logged in.",
            TeamTalkConnectionState.Joined          => "Joined channel.",
            TeamTalkConnectionState.Streaming       => "Streaming started.",
            TeamTalkConnectionState.Disconnected    => "Disconnected.",
            TeamTalkConnectionState.Faulted         => $"Error: {_teamTalk.LastError?.Message}.",
            _ => null,
        };
        if (line is not null) _speech.Output(line);

        FeedbackTone? tone = state switch
        {
            TeamTalkConnectionState.LoggedIn        => FeedbackTone.ConnectChime,
            TeamTalkConnectionState.Streaming       => FeedbackTone.StreamingStarted,
            TeamTalkConnectionState.Disconnected    => FeedbackTone.DisconnectTone,
            TeamTalkConnectionState.Faulted         => FeedbackTone.Error,
            _ => null,
        };
        if (tone is not null) _ = _feedback.PlayAsync(tone.Value);
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
#endregion
