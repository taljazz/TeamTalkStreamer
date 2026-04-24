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
using TeamTalkStreamer.Audio.Windows.Wasapi;
using TeamTalkStreamer.Persistence.Config;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: ExcludedProcessViewModel
/// <summary>
/// Backing view model for <c>ExcludedProcessWindow</c>. Lets the user
/// pick one currently-running audio session (typically their screen
/// reader) whose audio should be removed from the loopback capture
/// before streaming into TeamTalk.
/// </summary>
/// <remarks>
/// The picker only shows sessions that are actively producing sound,
/// enumerated through <see cref="AudioSessionEnumerator"/>. If the
/// saved exclusion refers to an app that isn't running right now, the
/// "Currently excluded" header still shows it so the user knows what's
/// in effect — they just can't re-pick it until the app starts making
/// sound again.
/// </remarks>
public sealed class ExcludedProcessViewModel : INotifyPropertyChanged
{
    #region Fields

    #region Injected services
    private readonly IAppSettingsStore _store;
    private readonly ISpeechService _speech;
    #endregion

    #region Cached root settings
    // We mutate the Audio section and save the whole tree back so the
    // other sections (TeamTalk, MobileBridge, Accessibility) are
    // preserved untouched.
    private AppSettings _rootSettings;
    #endregion

    #region Backing fields
    private AudioSessionInfo? _selectedSession;
    private string _statusText = "";
    #endregion

    #endregion

    #region Events

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired when the dialog should close.
    /// Carries the DialogResult: true for Save / Clear, false for Cancel.</summary>
    public event EventHandler<bool>? CloseRequested;

    #endregion

    #region Constructor

    public ExcludedProcessViewModel(
        IAppSettingsStore store,
        ISpeechService speech)
    {
        _store = store;
        _speech = speech;
        _rootSettings = store.LoadAsync().GetAwaiter().GetResult();

        Sessions = new ObservableCollection<AudioSessionInfo>();

        RefreshSessions();                       // populate the list
        PreselectCurrentExclusion();             // highlight saved entry, if live
    }

    #endregion

    #region Bindable properties

    /// <summary>Active audio sessions, produced by
    /// <see cref="AudioSessionEnumerator"/>. Bound to the picker ListBox.</summary>
    public ObservableCollection<AudioSessionInfo> Sessions { get; }

    /// <summary>Currently highlighted session in the list.</summary>
    public AudioSessionInfo? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value) && value is not null)
                _speech.Speak($"Selected {value.DisplayText}.");
        }
    }

    /// <summary>Header text showing what exclusion is currently saved.
    /// Re-rendered whenever the cached <see cref="AppSettings"/> change.</summary>
    public string CurrentExclusionLabel
    {
        get
        {
            var audio = _rootSettings.Audio;
            if (string.IsNullOrWhiteSpace(audio.ExcludedProcessName))
                return "Currently excluded: (none)";

            return string.IsNullOrWhiteSpace(audio.ExcludedProcessDisplayName)
                ? $"Currently excluded: {audio.ExcludedProcessName}"
                : $"Currently excluded: {audio.ExcludedProcessDisplayName}";
        }
    }

    /// <summary>Live status line for Refresh feedback. Bound to a
    /// polite-live-region TextBlock so screen readers announce changes.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    #endregion

    #region Commands

    private RelayCommand? _refreshCommand;
    public ICommand RefreshCommand =>
        _refreshCommand ??= new RelayCommand(_ => RefreshSessions(announce: true));

    private RelayCommand? _saveCommand;
    public ICommand SaveCommand =>
        _saveCommand ??= new RelayCommand(
            async _ => await SaveAsync(),
            _ => SelectedSession is not null);

    private RelayCommand? _clearCommand;
    public ICommand ClearCommand =>
        _clearCommand ??= new RelayCommand(async _ => await ClearAsync());

    private RelayCommand? _cancelCommand;
    public ICommand CancelCommand =>
        _cancelCommand ??= new RelayCommand(_ => CloseRequested?.Invoke(this, false));

    #endregion

    #region Implementation: refresh

    /// <summary>Re-enumerate active audio sessions and refresh the
    /// bound <see cref="Sessions"/> collection. Optionally announces
    /// the result so a screen reader picks it up when triggered from
    /// the Refresh button.</summary>
    private void RefreshSessions(bool announce = false)
    {
        try
        {
            var current = AudioSessionEnumerator.EnumerateActiveSessions();

            Sessions.Clear();
            foreach (var session in current) Sessions.Add(session);

            StatusText = Sessions.Count switch
            {
                0 => "No active audio sessions. Start your screen reader or an app that makes sound, then Refresh.",
                1 => "Found 1 active audio session.",
                _ => $"Found {Sessions.Count} active audio sessions.",
            };

            if (announce) _speech.Output(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not enumerate sessions: {ex.Message}";
            _speech.Output(StatusText);
        }
    }

    /// <summary>If the currently-saved exclusion matches a live session,
    /// auto-select it so the user sees their existing choice.</summary>
    private void PreselectCurrentExclusion()
    {
        string saved = _rootSettings.Audio.ExcludedProcessName;
        if (string.IsNullOrWhiteSpace(saved)) return;

        var match = Sessions.FirstOrDefault(s =>
            s.ProcessName.Equals(saved, StringComparison.OrdinalIgnoreCase));
        if (match is not null) SelectedSession = match;
    }

    #endregion

    #region Implementation: save / clear

    /// <summary>Persist the selected session as the exclusion target and
    /// close the dialog.</summary>
    private async Task SaveAsync()
    {
        if (SelectedSession is null) return;

        _rootSettings.Audio.ExcludedProcessName = SelectedSession.ProcessName;
        _rootSettings.Audio.ExcludedProcessDisplayName = SelectedSession.DisplayName;

        try
        {
            await _store.SaveAsync(_rootSettings).ConfigureAwait(true);
            _speech.Output(
                $"Excluded app set to {SelectedSession.DisplayText}. " +
                "Its audio will be skipped the next time streaming starts.");
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not save settings: {ex.Message}";
            _speech.Output(StatusText);
        }
    }

    /// <summary>Remove the exclusion entirely (revert to full-mix loopback)
    /// and close the dialog.</summary>
    private async Task ClearAsync()
    {
        _rootSettings.Audio.ExcludedProcessName = "";
        _rootSettings.Audio.ExcludedProcessDisplayName = "";

        try
        {
            await _store.SaveAsync(_rootSettings).ConfigureAwait(true);
            _speech.Output("Excluded app cleared. Future streams will include the full system audio mix.");
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not save settings: {ex.Message}";
            _speech.Output(StatusText);
        }
    }

    #endregion

    #region INotifyPropertyChanged plumbing

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
