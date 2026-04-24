#nullable enable

#region Usings
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TeamTalkStreamer.Audio.Windows.Wasapi;
using TeamTalkStreamer.Core.Audio;
using TeamTalkStreamer.TeamTalk;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: MainViewModel (partial — streaming & settings commands)
/// <summary>
/// User-facing actions that drive the full "start/stop streaming" flow,
/// plus the commands that open the server-settings and excluded-apps
/// dialogs. Collapses the four-step manual sequence (connect, login,
/// join, start) into a single button, and — when the user has picked an
/// excluded app — swaps the default-loopback source for a
/// <see cref="WasapiProcessLoopbackSource"/> so the excluded app's
/// audio is subtracted from the captured mix.
/// </summary>
public sealed partial class MainViewModel
{
    #region Commands
    // Three commands bound to MainWindow buttons:
    //   * ToggleStreamingCommand      — flips start/stop based on IsStreamActive.
    //   * OpenServerSettingsCommand   — launches the credentials dialog.
    //   * OpenExcludedAppsCommand     — launches the excluded-apps picker.

    private RelayCommand? _toggleStreamingCommand;
    public ICommand ToggleStreamingCommand =>
        _toggleStreamingCommand ??= new RelayCommand(async _ => await ToggleStreamingAsync());

    private RelayCommand? _openServerSettingsCommand;
    public ICommand OpenServerSettingsCommand =>
        _openServerSettingsCommand ??= new RelayCommand(_ => OpenServerSettings());

    private RelayCommand? _openExcludedAppsCommand;
    public ICommand OpenExcludedAppsCommand =>
        _openExcludedAppsCommand ??= new RelayCommand(_ => OpenExcludedApps());

    #endregion

    #region Toggle dispatcher
    // Pick start or stop based on current state so the single button
    // feels like a natural play/pause toggle.

    private Task ToggleStreamingAsync() =>
        IsStreamActive ? StopStreamingAsync() : StartStreamingAsync();

    #endregion

    #region Start streaming (unified flow)

    /// <summary>
    /// One-click "stream my system audio to the configured channel."
    /// Runs the full sequence idempotently — any step that's already
    /// done is skipped, so repeated clicks are safe.
    /// </summary>
    /// <remarks>
    /// Sequence:
    /// <list type="number">
    ///   <item><description>Load credentials; bail out with a spoken
    ///     prompt if Host is blank.</description></item>
    ///   <item><description>Resolve which system-audio source to use —
    ///     plain loopback or process-loopback with exclusion —
    ///     based on <c>AudioSettings.ExcludedProcessName</c>.</description></item>
    ///   <item><description>Attach the chosen source to the router.</description></item>
    ///   <item><description>Connect + login if not already, then join
    ///     the target channel.</description></item>
    ///   <item><description>Register the <c>TeamTalkSink</c>, open it,
    ///     and start the source capturing.</description></item>
    /// </list>
    /// </remarks>
    private async Task StartStreamingAsync()
    {
        try
        {
            #region 1. Load settings
            var settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
            var tt = settings.TeamTalk;

            if (string.IsNullOrWhiteSpace(tt.Host))
            {
                _speech.Output(
                    "No server is configured. Please open Server settings and enter a host first.");
                return;
            }
            #endregion

            #region 2. Resolve system-audio source (with optional exclusion)
            // If the user has configured an excluded app and it's running
            // right now, create a transient WasapiProcessLoopbackSource
            // for it. Otherwise fall through to the singleton plain
            // loopback source.
            _activeSystemAudioSource = ResolveSystemAudioSource(settings);
            #endregion

            #region 3. Attach to router + add to visible Sources list
            if (!Sources.Contains(_activeSystemAudioSource))
            {
                _router.AttachSource(_activeSystemAudioSource);
                Sources.Add(_activeSystemAudioSource);
            }
            #endregion

            #region 4. Connect + login (if needed)
            if (_teamTalk.State is TeamTalkConnectionState.Disconnected or
                                   TeamTalkConnectionState.Faulted)
            {
                var config = new TeamTalkServerConfig
                {
                    Host = tt.Host,
                    TcpPort = tt.TcpPort,
                    UdpPort = tt.UdpPort,
                    UseEncryption = false,
                    ServerPassword = tt.ServerPassword,
                    Nickname = tt.Nickname,
                    Username = tt.Username,
                    Password = tt.Password,
                    ClientName = "TeamTalkStreamer",
                    ChannelPath = tt.ChannelPath,
                    ChannelPassword = tt.ChannelPassword,
                };
                await _teamTalk.ConnectAsync(config).ConfigureAwait(true);
            }
            #endregion

            #region 5. Join channel (if needed)
            if (_teamTalk.State == TeamTalkConnectionState.LoggedIn)
            {
                await _teamTalk.JoinChannelAsync(tt.ChannelPath, tt.ChannelPassword)
                    .ConfigureAwait(true);
            }
            #endregion

            #region 6. Wire sink + start capture
            _router.AddSink(_sink);
            await _sink.OpenAsync(AudioFormat.TeamTalkDefault).ConfigureAwait(true);

            if (_activeSystemAudioSource.State is not AudioSourceState.Capturing)
                await _activeSystemAudioSource.StartAsync().ConfigureAwait(true);
            #endregion
        }
        catch (Exception ex)
        {
            _speech.Output($"Start streaming failed: {ex.Message}");
        }
    }

    #endregion

    #region Stop streaming (unified teardown)

    /// <summary>
    /// Reverse of <see cref="StartStreamingAsync"/>: stop capture, close
    /// the sink, disconnect from TeamTalk, detach the active source
    /// from the Sources list, and dispose it if it was a transient
    /// <see cref="WasapiProcessLoopbackSource"/> (the singleton
    /// <see cref="WasapiLoopbackSource"/> is left alive for reuse).
    /// </summary>
    private async Task StopStreamingAsync()
    {
        try
        {
            var active = _activeSystemAudioSource;

            #region Stop source + close sink
            if (active is not null && active.State is AudioSourceState.Capturing)
                await active.StopAsync().ConfigureAwait(true);

            if (_sink.IsOpen)
                await _sink.CloseAsync().ConfigureAwait(true);

            _router.RemoveSink(_sink);
            #endregion

            #region Detach from TeamTalk
            await _teamTalk.DisconnectAsync().ConfigureAwait(true);
            #endregion

            #region Detach source from router + Sources list
            if (active is not null && Sources.Contains(active))
            {
                _router.DetachSource(active.Id);
                Sources.Remove(active);
            }
            #endregion

            #region Dispose the process-loopback source if it was transient
            // The plain WasapiLoopbackSource is a DI singleton — do NOT
            // dispose it. WasapiProcessLoopbackSource is built per-start
            // and owns native COM objects that must be released.
            if (active is WasapiProcessLoopbackSource disposable)
            {
                try { await disposable.DisposeAsync().ConfigureAwait(true); }
                catch { /* best-effort */ }
            }
            #endregion

            _activeSystemAudioSource = null;
        }
        catch (Exception ex)
        {
            _speech.Output($"Stop streaming failed: {ex.Message}");
        }
    }

    #endregion

    #region System-audio source resolution

    /// <summary>
    /// Decide which <see cref="IAudioSource"/> to use for the default
    /// playback device based on the user's exclusion settings.
    /// <list type="bullet">
    ///   <item><description>No excluded app configured → the singleton
    ///     <see cref="WasapiLoopbackSource"/> (full mix).</description></item>
    ///   <item><description>Excluded app configured and running → a
    ///     fresh <see cref="WasapiProcessLoopbackSource"/> that skips
    ///     that PID's tree.</description></item>
    ///   <item><description>Excluded app configured but not running →
    ///     fall back to the full-mix loopback; Tolk-announce so the
    ///     user knows the exclusion wasn't applied.</description></item>
    /// </list>
    /// </summary>
    private IAudioSource ResolveSystemAudioSource(Persistence.Config.AppSettings settings)
    {
        string excludedName = settings.Audio.ExcludedProcessName;
        if (string.IsNullOrWhiteSpace(excludedName))
            return _loopbackSource;

        // Process.GetProcessesByName is case-insensitive on Windows and
        // wants the base name without ".exe". Settings already stores
        // it that way (see AudioSessionInfo.ProcessName).
        Process[] matches;
        try { matches = Process.GetProcessesByName(excludedName); }
        catch { matches = Array.Empty<Process>(); }

        if (matches.Length == 0)
        {
            _speech.Output(
                $"{settings.Audio.ExcludedProcessDisplayName} is not running; " +
                "streaming the full system audio mix.");
            return _loopbackSource;
        }

        int pid = matches[0].Id;
        foreach (var p in matches) p.Dispose();

        string display = string.IsNullOrWhiteSpace(settings.Audio.ExcludedProcessDisplayName)
            ? excludedName
            : settings.Audio.ExcludedProcessDisplayName;

        _speech.Output($"Streaming default playback device, excluding {display}.");
        return new WasapiProcessLoopbackSource(
            excludedProcessId: pid,
            displayName: $"System audio (excluding {display})");
    }

    #endregion

    #region Dialog openers

    private void OpenServerSettings()
    {
        var window = _serverSettingsWindowFactory();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    private void OpenExcludedApps()
    {
        var window = _excludedProcessWindowFactory();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    #endregion
}
#endregion
