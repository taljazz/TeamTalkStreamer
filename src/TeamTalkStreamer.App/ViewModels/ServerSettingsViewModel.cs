#nullable enable

#region Usings
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TeamTalkStreamer.Accessibility.Speech;
using TeamTalkStreamer.Persistence.Config;
using TeamTalkStreamer.TeamTalk;
using TeamTalkStreamer.TeamTalk.Client;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: ServerSettingsViewModel
/// <summary>
/// Backing view model for <c>ServerSettingsWindow</c>. Exposes every
/// <see cref="TeamTalkServerSettings"/> field as a two-way bindable
/// property, plus Save / Cancel commands. Loads the current settings
/// synchronously on construction and persists on Save.
/// </summary>
/// <remarks>
/// Kept as a non-partial class because the whole VM fits comfortably
/// in one file. If credential validation, test-connect, or advanced
/// options grow this past ~400 lines we can split by concern like
/// <c>MainViewModel</c> does.
/// </remarks>
public sealed class ServerSettingsViewModel : INotifyPropertyChanged
{
    #region Fields

    #region Injected services
    // Store is used for both load (ctor) and save (SaveCommand).
    private readonly IAppSettingsStore _store;
    private readonly ISpeechService _speech;
    #endregion

    #region Cached root settings
    // We load the whole AppSettings on construct so Save can write
    // back without clobbering sections we don't own (Audio,
    // MobileBridge, Accessibility). Only the TeamTalk section is
    // mutated by this VM.
    private AppSettings _rootSettings;
    #endregion

    #region Backing fields for bindable properties
    // One field per editable setting. Defaults come from the loaded
    // AppSettings in the ctor so the dialog shows current values.
    private string _host = "";
    private int _tcpPort;
    private int _udpPort;
    private string _serverPassword = "";
    private string _nickname = "";
    private string _username = "";
    private string _password = "";
    private string _channelPath = "";
    private string _channelPassword = "";
    #endregion

    #region Channel browser state
    // Populated by BrowseChannelsAsync. SelectedChannel writes its path
    // back into ChannelPath so the save-path stays consistent whether
    // the user picked from the list or typed a path manually.
    private ChannelInfo? _selectedChannel;
    private bool _isBrowsing;
    private string _browseStatusText = "";
    #endregion

    #endregion

    #region Events

    /// <summary>Bindings wire up to this.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the VM wants the dialog to close. Argument
    /// is the DialogResult: <c>true</c> for Save, <c>false</c> for
    /// Cancel. <c>ServerSettingsWindow.xaml.cs</c> subscribes and calls
    /// <see cref="System.Windows.Window.Close"/>.</summary>
    public event EventHandler<bool>? CloseRequested;

    #endregion

    #region Constructor
    // Loads current settings synchronously. The settings file is tiny
    // (~1 KB) so a brief blocking read on the UI thread is acceptable
    // — no value in overcomplicating the dialog open path.

    public ServerSettingsViewModel(
        IAppSettingsStore store,
        ISpeechService speech)
    {
        _store = store;
        _speech = speech;
        _rootSettings = store.LoadAsync().GetAwaiter().GetResult();

        var tt = _rootSettings.TeamTalk;
        _host = tt.Host;
        _tcpPort = tt.TcpPort;
        _udpPort = tt.UdpPort;
        _serverPassword = tt.ServerPassword;
        _nickname = tt.Nickname;
        _username = tt.Username;
        _password = tt.Password;
        _channelPath = tt.ChannelPath;
        _channelPassword = tt.ChannelPassword;

        Channels = new ObservableCollection<ChannelInfo>();
    }

    #endregion

    #region Bindable properties

    #region Server endpoint

    /// <summary>Hostname or IP address of the TeamTalk server.</summary>
    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value ?? "");
    }

    /// <summary>TCP port (typically 10333).</summary>
    public int TcpPort
    {
        get => _tcpPort;
        set => SetProperty(ref _tcpPort, value);
    }

    /// <summary>UDP port for voice data (typically same as TCP).</summary>
    public int UdpPort
    {
        get => _udpPort;
        set => SetProperty(ref _udpPort, value);
    }

    /// <summary>Optional server-level password.</summary>
    public string ServerPassword
    {
        get => _serverPassword;
        set => SetProperty(ref _serverPassword, value ?? "");
    }

    #endregion

    #region User identity

    /// <summary>Display name shown to others in the channel.</summary>
    public string Nickname
    {
        get => _nickname;
        set => SetProperty(ref _nickname, value ?? "");
    }

    /// <summary>Account username. Blank = anonymous.</summary>
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value ?? "");
    }

    /// <summary>Account password.</summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? "");
    }

    #endregion

    #region Channel target

    /// <summary>Raw channel path saved to settings and handed to the
    /// SDK at join time (e.g. <c>/Lobby/Music</c>). Not shown directly
    /// in the UI any more — the dialog surfaces
    /// <see cref="SelectedChannelFriendly"/> instead so the user never
    /// has to look at the raw slash-delimited string.</summary>
    public string ChannelPath
    {
        get => _channelPath;
        set
        {
            if (SetProperty(ref _channelPath, value ?? ""))
                OnPropertyChanged(nameof(SelectedChannelFriendly));
        }
    }

    /// <summary>Friendly human-readable label for the currently-
    /// configured channel. Prefers the browser-selected
    /// <see cref="SelectedChannel"/>'s <see cref="ChannelInfo.DisplayText"/>,
    /// falls back to synthesizing the same formatting from the saved
    /// <see cref="ChannelPath"/> when no browser pick is active (e.g.
    /// right after the dialog opens with an existing saved path).</summary>
    public string SelectedChannelFriendly
    {
        get
        {
            if (_selectedChannel is not null)
                return _selectedChannel.DisplayText;

            if (string.IsNullOrWhiteSpace(_channelPath))
                return "(none configured — use Browse channels below)";

            // Synthesize a ChannelInfo from just the path so the same
            // formatting rules apply whether the user picked from the
            // list or had a saved path in settings from a previous run.
            return new ChannelInfo(
                Id: 0,
                Path: _channelPath,
                Name: string.Empty,
                RequiresPassword: false).DisplayText;
        }
    }

    /// <summary>Channel password if the channel requires one.</summary>
    public string ChannelPassword
    {
        get => _channelPassword;
        set => SetProperty(ref _channelPassword, value ?? "");
    }

    #endregion

    #endregion

    #region Channel browser bindables

    /// <summary>List populated by <see cref="BrowseChannelsAsync"/>.
    /// Bound to the ListBox in the Browse Channels section of the
    /// dialog; clicking an entry updates <see cref="SelectedChannel"/>.</summary>
    public ObservableCollection<ChannelInfo> Channels { get; }

    /// <summary>Currently highlighted channel in the list. Setting it
    /// copies <see cref="ChannelInfo.Path"/> into <see cref="ChannelPath"/>
    /// so Save picks up the selection automatically, and re-notifies
    /// <see cref="SelectedChannelFriendly"/> so the friendly display
    /// refreshes for both set and clear transitions.</summary>
    public ChannelInfo? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (!SetProperty(ref _selectedChannel, value)) return;

            // Always notify the friendly label — covers both "user
            // picked a channel" and "user cleared the selection."
            OnPropertyChanged(nameof(SelectedChannelFriendly));

            if (value is not null)
            {
                ChannelPath = value.Path;
                _speech.Speak(value.DisplayText);
            }
        }
    }

    /// <summary>True while <see cref="BrowseChannelsAsync"/> is
    /// running. UI binds this to show a "probing..." indicator; the
    /// inverted <see cref="CanBrowse"/> property drives IsEnabled on
    /// the browse button.</summary>
    public bool IsBrowsing
    {
        get => _isBrowsing;
        private set
        {
            if (SetProperty(ref _isBrowsing, value))
                OnPropertyChanged(nameof(CanBrowse));
        }
    }

    /// <summary>Negation of <see cref="IsBrowsing"/>. Exposed so the
    /// XAML can bind IsEnabled without needing a value converter.</summary>
    public bool CanBrowse => !_isBrowsing;

    /// <summary>Latest browse-status message (success / error / progress).
    /// Bound to a live-region TextBlock so screen readers announce it.</summary>
    public string BrowseStatusText
    {
        get => _browseStatusText;
        private set => SetProperty(ref _browseStatusText, value);
    }

    #endregion

    #region Commands

    private RelayCommand? _saveCommand;
    public ICommand SaveCommand =>
        _saveCommand ??= new RelayCommand(async _ => await SaveAsync());

    private RelayCommand? _cancelCommand;
    public ICommand CancelCommand =>
        _cancelCommand ??= new RelayCommand(_ => Cancel());

    private RelayCommand? _browseChannelsCommand;
    public ICommand BrowseChannelsCommand =>
        _browseChannelsCommand ??= new RelayCommand(
            async _ => await BrowseChannelsAsync(),
            _ => !IsBrowsing);

    #endregion

    #region Command handlers

    /// <summary>Copy VM state back into <see cref="AppSettings"/>,
    /// persist it, then ask the window to close with
    /// <c>DialogResult = true</c>.</summary>
    private async System.Threading.Tasks.Task SaveAsync()
    {
        // Mutate only the TeamTalk section; leave everything else
        // (Audio, MobileBridge, Accessibility) as-loaded.
        _rootSettings.TeamTalk.Host = _host;
        _rootSettings.TeamTalk.TcpPort = _tcpPort;
        _rootSettings.TeamTalk.UdpPort = _udpPort;
        _rootSettings.TeamTalk.ServerPassword = _serverPassword;
        _rootSettings.TeamTalk.Nickname = _nickname;
        _rootSettings.TeamTalk.Username = _username;
        _rootSettings.TeamTalk.Password = _password;
        _rootSettings.TeamTalk.ChannelPath = _channelPath;
        _rootSettings.TeamTalk.ChannelPassword = _channelPassword;

        try
        {
            await _store.SaveAsync(_rootSettings).ConfigureAwait(true);
            _speech.Output("Server settings saved.");
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            // Stay open on failure so the user can retry. Speak the
            // problem so blind users know what happened without
            // tabbing to an error label.
            _speech.Output($"Could not save settings: {ex.Message}");
        }
    }

    /// <summary>Close without writing anything.</summary>
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    #endregion

    #region Channel browser — probe logic

    /// <summary>
    /// Spin up a transient <see cref="TeamTalkClient"/>, connect with
    /// the form's current server/user fields, enumerate every channel
    /// the server advertises, then tear the connection back down and
    /// populate <see cref="Channels"/>.
    /// </summary>
    /// <remarks>
    /// Uses a dedicated client instance (not the app-wide singleton)
    /// so the probe never interferes with an in-progress streaming
    /// session. The user's typed values are taken live from the VM
    /// so they can browse without hitting Save first.
    /// </remarks>
    private async Task BrowseChannelsAsync()
    {
        if (IsBrowsing) return;

        #region Validation
        if (string.IsNullOrWhiteSpace(Host))
        {
            BrowseStatusText = "Enter a host address first.";
            _speech.Output(BrowseStatusText);
            return;
        }
        #endregion

        #region Probe session
        IsBrowsing = true;
        BrowseStatusText = "Connecting…";
        _speech.Output("Probing server for channels.");

        var probe = new TeamTalkClient();
        try
        {
            var config = new TeamTalkServerConfig
            {
                Host = _host,
                TcpPort = _tcpPort,
                UdpPort = _udpPort,
                UseEncryption = false,
                ServerPassword = _serverPassword,
                Nickname = string.IsNullOrWhiteSpace(_nickname) ? "Channel browser" : _nickname,
                Username = _username,
                Password = _password,
                ClientName = "TeamTalkStreamer (channel browser)",
                ChannelPath = "/",        // unused; we never join
                ChannelPassword = "",
            };

            await probe.ConnectAsync(config).ConfigureAwait(true);

            BrowseStatusText = "Fetching channel list…";
            var found = await probe.EnumerateChannelsAsync().ConfigureAwait(true);

            #region Replace Channels collection
            Channels.Clear();
            foreach (var ch in found) Channels.Add(ch);

            // Auto-select the channel that matches the currently saved
            // path, if any, so the user immediately sees what they had.
            var existing = Channels.FirstOrDefault(c => c.Path == _channelPath);
            if (existing is not null) SelectedChannel = existing;
            #endregion

            BrowseStatusText = $"Found {Channels.Count} channel{(Channels.Count == 1 ? "" : "s")}.";
            _speech.Output(BrowseStatusText);
        }
        catch (Exception ex)
        {
            BrowseStatusText = $"Probe failed: {ex.Message}";
            _speech.Output(BrowseStatusText);
        }
        finally
        {
            // Always disconnect + dispose the transient client,
            // regardless of success or failure, so the session leaves
            // no lingering resources.
            try { await probe.DisposeAsync().ConfigureAwait(true); }
            catch { /* best effort */ }
            IsBrowsing = false;
        }
        #endregion
    }

    #endregion

    #region INotifyPropertyChanged plumbing
    // Shared helpers identical to MainViewModel's — kept local rather
    // than in a common base class because there are only two VMs so
    // far and an inheritance layer would be premature.

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
