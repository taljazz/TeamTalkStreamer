#nullable enable

#region Usings
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: MainViewModel (partial — help / documentation)
/// <summary>
/// Help-related commands. Currently exposes <see cref="OpenGuideCommand"/>,
/// which is bound to the F1 <c>KeyBinding</c> in <c>MainWindow.xaml</c>
/// and opens the bundled <c>guide.html</c> in the user's default browser.
/// </summary>
/// <remarks>
/// Kept in its own partial so "helping the user" concerns don't leak
/// into the streaming / connection / sources partials. If more
/// help-related commands appear (context help, "What's new", etc.)
/// they belong here.
/// </remarks>
public sealed partial class MainViewModel
{
    #region Commands

    private RelayCommand? _openGuideCommand;
    public ICommand OpenGuideCommand =>
        _openGuideCommand ??= new RelayCommand(_ => OpenGuide());

    #endregion

    #region Implementation

    /// <summary>
    /// Locate <c>guide.html</c> next to the executable and shell-launch
    /// it. Uses <see cref="AppContext.BaseDirectory"/> rather than
    /// <c>Assembly.Location</c> so it works under both normal builds
    /// and single-file self-contained publishing (where
    /// <c>Assembly.Location</c> returns an empty string).
    /// </summary>
    private void OpenGuide()
    {
        try
        {
            #region Resolve guide path
            // AppContext.BaseDirectory points at the folder containing
            // the exe — the same folder where guide.html is copied by
            // the csproj's None-include with CopyToOutputDirectory.
            string guidePath = Path.Combine(
                AppContext.BaseDirectory, "guide.html");

            if (!File.Exists(guidePath))
            {
                _speech.Output(
                    "User guide is missing. Please ensure guide.html " +
                    "is next to the executable.");
                return;
            }
            #endregion

            #region Launch via the shell
            // UseShellExecute=true lets Windows pick the user's default
            // browser for the .html association rather than us having
            // to resolve it ourselves.
            Process.Start(new ProcessStartInfo
            {
                FileName = guidePath,
                UseShellExecute = true,
            });

            _speech.Speak("Opening user guide.");
            #endregion
        }
        catch (Exception ex)
        {
            _speech.Output($"Could not open guide: {ex.Message}");
        }
    }

    #endregion
}
#endregion
