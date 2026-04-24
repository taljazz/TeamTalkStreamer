#nullable enable

#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: MainViewModel (partial — master stream gain)
/// <summary>
/// Master stream-gain slider. Multiplies every PCM sample captured
/// from the system audio by this factor before the frame reaches the
/// <c>TeamTalkSink</c>, so the user can boost quiet audio or duck
/// loud audio on the fly without leaving the app. The live value
/// applies to <c>TeamTalkSink.Gain</c> instantly; saves to settings
/// are debounced so a rapid slider drag doesn't thrash the JSON file.
/// </summary>
/// <remarks>
/// The bound property is an integer percent (0..100) — not a float —
/// so screen readers announce "80", "90", "100" as the user arrows
/// through the slider, rather than "0.8", "0.9", "1.0". The value is
/// converted to / from <see cref="Persistence.Config.AudioSettings.MasterGain"/>
/// (which stays a <c>float</c> in the JSON file) at the persistence
/// boundary only, so existing <c>settings.json</c> files keep working.
///
/// Default is 100 (full volume / unity). The range is attenuate-only
/// — 100 passes audio through unchanged, anything below quiets the
/// outgoing stream. Slider step is 10 percent (arrow keys) / 50 percent
/// (Page Up / Page Down).
/// </remarks>
public sealed partial class MainViewModel
{
    #region Constants

    private const int MasterGainPercentMin = 0;
    private const int MasterGainPercentMax = 100;
    private const int MasterGainPercentDefault = 100;

    /// <summary>Quiet period after the last slider change before we
    /// persist to disk. 500 ms matches typical drag-release cadence.</summary>
    private static readonly TimeSpan MasterGainSaveDebounce =
        TimeSpan.FromMilliseconds(500);

    #endregion

    #region Fields

    #region Backing state
    // Integer percent (0..200). Converted to the float factor
    // (percent / 100f) only at the boundaries: setting _sink.Gain,
    // and reading/writing AudioSettings.MasterGain.
    private int _masterGainPercent = MasterGainPercentDefault;
    #endregion

    #region Debounce cancellation
    // Reset on every slider change. The in-flight Task.Delay observes
    // the token and exits early, so only the last change's save runs.
    private CancellationTokenSource? _masterGainSaveCts;
    #endregion

    #endregion

    #region Bindable properties

    /// <summary>Master gain as an integer percent (0..200). Bound to
    /// <c>Slider.Value</c> in <c>MainWindow.xaml</c>. Setting clamps,
    /// applies the float equivalent to <c>_sink.Gain</c> immediately,
    /// and debounces the persistence write.</summary>
    public int MasterGainPercent
    {
        get => _masterGainPercent;
        set
        {
            int clamped = Math.Clamp(value, MasterGainPercentMin, MasterGainPercentMax);
            if (!SetProperty(ref _masterGainPercent, clamped)) return;

            // Apply to the sink first so the effect is immediate,
            // then notify the dependent text, then schedule the save.
            _sink.Gain = clamped / 100f;
            OnPropertyChanged(nameof(MasterGainPercentText));
            DebounceSaveMasterGain();
        }
    }

    /// <summary>Formatted text for the percentage label next to the
    /// slider. "100 %", "50 %", etc. Recomputed on every
    /// <see cref="MasterGainPercent"/> change.</summary>
    public string MasterGainPercentText =>
        $"{_masterGainPercent} %";

    #endregion

    #region Startup: load saved master gain
    // Called once from the MainViewModel ctor, right after
    // ApplySavedFeedbackVolume. Sync load — tiny settings file, startup
    // only; blocking briefly on the UI thread is acceptable.

    private void ApplySavedMasterGain()
    {
        try
        {
            var settings = _settingsStore.LoadAsync().GetAwaiter().GetResult();

            // Clamp the float from settings to [0, 1], then round to
            // the nearest integer percent. Old settings files that had
            // values above 1.0 (from the previous 0..200 % range) are
            // silently capped to 100 % rather than throwing.
            float savedFactor = Math.Clamp(settings.Audio.MasterGain, 0f, 1f);
            _masterGainPercent = (int)Math.Round(savedFactor * 100f);
            _sink.Gain = savedFactor;

            OnPropertyChanged(nameof(MasterGainPercent));
            OnPropertyChanged(nameof(MasterGainPercentText));
        }
        catch
        {
            // Non-fatal: fall back to 100 % (the field default).
        }
    }

    #endregion

    #region Debounced persistence

    /// <summary>Schedule a save <see cref="MasterGainSaveDebounce"/>
    /// from now, cancelling any earlier pending save. While the user
    /// drags the slider, each change resets the clock; only the final
    /// value actually hits disk. No data is lost — the live
    /// <c>_sink.Gain</c> was already updated in the setter.</summary>
    private void DebounceSaveMasterGain()
    {
        _masterGainSaveCts?.Cancel();
        _masterGainSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _masterGainSaveCts = cts;
        _ = SaveMasterGainAfterDelayAsync(cts.Token);
    }

    private async Task SaveMasterGainAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(MasterGainSaveDebounce, ct).ConfigureAwait(true);

            var settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
            // Convert back to the float factor at the storage boundary
            // so settings.json stays compatible with the original shape.
            settings.Audio.MasterGain = _masterGainPercent / 100f;
            await _settingsStore.SaveAsync(settings).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change — expected in a drag.
        }
        catch (Exception ex)
        {
            _speech.Output($"Could not save master gain: {ex.Message}");
        }
    }

    #endregion
}
#endregion
