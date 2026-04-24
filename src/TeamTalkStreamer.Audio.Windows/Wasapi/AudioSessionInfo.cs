#region Usings
// Pure record — no imports.
#endregion

namespace TeamTalkStreamer.Audio.Windows.Wasapi;

#region Record: AudioSessionInfo
/// <summary>
/// Flat descriptor of one active audio session on the default render
/// endpoint, produced by <see cref="AudioSessionEnumerator"/>. Used
/// by the exclusion-picker dialog so the user sees only apps that
/// are actually making sound right now.
/// </summary>
/// <param name="ProcessId">OS process id of the session's owner.</param>
/// <param name="ProcessName">Short process name without extension,
/// e.g. <c>nvda</c>. Stable across sessions — this is what we store
/// in settings.</param>
/// <param name="DisplayName">Human-friendly label — either the session's
/// own DisplayName (when the app sets one) or the process name with
/// the first letter capitalized as a fallback. Shown in the UI and
/// spoken by Tolk.</param>
public sealed record AudioSessionInfo(
    int ProcessId,
    string ProcessName,
    string DisplayName)
{
    #region Display helpers

    /// <summary>Label for the picker list. Prefers DisplayName when
    /// it differs from ProcessName; otherwise shows a title-cased
    /// ProcessName so "nvda" reads as "Nvda" in the list.</summary>
    public string DisplayText =>
        string.IsNullOrWhiteSpace(DisplayName) ||
        DisplayName.Equals(ProcessName, System.StringComparison.OrdinalIgnoreCase)
            ? TitleCase(ProcessName)
            : $"{DisplayName}  ({ProcessName})";

    private static string TitleCase(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];

    #endregion
}
#endregion
