#nullable enable

#region Usings
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using TeamTalkStreamer.Accessibility.Feedback;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: MainViewModel (partial — feedback-tone volume)
/// <summary>
/// Feedback-tone volume control. Bound in XAML to the <c>=</c> and
/// <c>-</c> keys (plus their numpad equivalents) so the user can
/// nudge the OpenAL feedback volume up or down from anywhere in the
/// main window without having to leave the keyboard or open a
/// settings dialog. Every change persists to
/// <see cref="Accessibility.AccessibilitySettings.FeedbackVolume"/>
/// so the preference survives a restart.
/// </summary>
/// <remarks>
/// Mirrors the convention the user established in CSharp Academy
/// (and Aircraft Explorer before it): 10 % steps across the full
/// range, a navigation tick at the new level so the user hears the
/// result, and a spoken percentage through Tolk.
/// </remarks>
public sealed partial class MainViewModel
{
    #region Constants

    /// <summary>Fraction of the full 0–1 range to move per key press.
    /// 10 presses span silent to max, matching the granularity the
    /// user is already used to from their other accessible apps.</summary>
    private const float FeedbackVolumeStep = 0.1f;

    #endregion

    #region Commands

    private RelayCommand? _increaseFeedbackVolumeCommand;
    public ICommand IncreaseFeedbackVolumeCommand =>
        _increaseFeedbackVolumeCommand ??= new RelayCommand(
            async _ => await AdjustFeedbackVolumeAsync(+FeedbackVolumeStep));

    private RelayCommand? _decreaseFeedbackVolumeCommand;
    public ICommand DecreaseFeedbackVolumeCommand =>
        _decreaseFeedbackVolumeCommand ??= new RelayCommand(
            async _ => await AdjustFeedbackVolumeAsync(-FeedbackVolumeStep));

    #endregion

    #region Startup: apply saved volume
    // Called once from the main ctor. Sync load is fine — settings
    // file is tiny and startup is the only call site. If anything
    // fails we quietly leave the AudioFeedbackService at its default.

    private void ApplySavedFeedbackVolume()
    {
        try
        {
            var settings = _settingsStore.LoadAsync()
                .GetAwaiter().GetResult();
            _feedback.Volume = settings.Accessibility.FeedbackVolume;
        }
        catch
        {
            // Non-fatal: fall back to whatever default the service
            // initialized with. The user can always readjust with the
            // = / - keys and the next change will persist.
        }
    }

    #endregion

    #region Adjust + persist

    /// <summary>
    /// Core handler for both commands. Applies the delta, clamps to
    /// [0, 1], plays a navigation tick at the NEW level so the user
    /// hears the result, speaks the percentage through Tolk, and
    /// persists the new value to settings.
    /// </summary>
    private async Task AdjustFeedbackVolumeAsync(float delta)
    {
        #region Apply to the live service
        float next = Math.Clamp(_feedback.Volume + delta, 0f, 1f);
        _feedback.Volume = next;
        #endregion

        #region Feedback: audible tick + spoken percentage
        // Tick first so the user hears the new volume immediately,
        // then the announcement. Tick is ~35 ms — short enough that
        // Tolk's speech won't feel delayed behind it.
        _ = _feedback.PlayAsync(FeedbackTone.NavigationTick);

        int percent = (int)Math.Round(next * 100f);
        // Output (not Speak) so rapid key presses interrupt the
        // previous announcement rather than queueing — the user
        // only cares about the most recent level.
        _speech.Output($"Feedback tone volume: {percent} percent.");
        #endregion

        #region Persist
        // Load + mutate + save so we don't clobber other settings
        // sections. The volume section lives under Accessibility.
        try
        {
            var settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
            settings.Accessibility.FeedbackVolume = next;
            await _settingsStore.SaveAsync(settings).ConfigureAwait(true);
        }
        catch
        {
            // Non-fatal: the live volume is already applied. A save
            // failure just means the preference doesn't survive a
            // restart — not worth interrupting the user for.
        }
        #endregion
    }

    #endregion
}
#endregion
