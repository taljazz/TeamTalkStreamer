#nullable enable

#region Usings
using System;
#endregion

namespace TeamTalkStreamer.TeamTalk;

#region Record: ChannelInfo
/// <summary>
/// Flat summary of one channel on the TeamTalk server, produced by
/// <c>TeamTalkClient.EnumerateChannelsAsync</c>. Used by the settings
/// dialog so the user can pick a channel from a list instead of
/// hand-typing the path.
/// </summary>
/// <param name="Id">Server-assigned channel ID. Not persisted — IDs
/// can differ after server restarts; <see cref="Path"/> is the stable
/// handle.</param>
/// <param name="Path">Full path from the root (e.g. <c>/Lobby/Music</c>).
/// This is what gets saved back to settings.</param>
/// <param name="Name">Bare channel name without the parent chain.</param>
/// <param name="RequiresPassword">True if the server marks this
/// channel as password-protected. Surfaces in <see cref="DisplayText"/>
/// so users know before selecting.</param>
public sealed record ChannelInfo(
    int Id,
    string Path,
    string Name,
    bool RequiresPassword)
{
    #region Display helpers

    /// <summary>
    /// Human-friendly label for the settings-dialog list and the
    /// screen reader. Prefers the bare channel name over the raw path,
    /// adds parent context only when it's needed to disambiguate
    /// nested channels, and appends a lock hint when the channel is
    /// password-protected.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// <list type="bullet">
    ///   <item><description><c>/</c> &#8594; <c>"Root"</c></description></item>
    ///   <item><description><c>/Lobby</c> &#8594; <c>"Lobby"</c></description></item>
    ///   <item><description><c>/Lobby/Music</c> &#8594; <c>"Music  (under Lobby)"</c></description></item>
    ///   <item><description><c>/Lobby/Music</c> (locked) &#8594;
    ///     <c>"Music  (under Lobby) — locked"</c></description></item>
    /// </list>
    /// </remarks>
    public string DisplayText
    {
        get
        {
            #region Split path into friendly name + parent name
            // The root is special: no segments, no parent. Everything
            // else has at least one segment; a parent label only
            // appears when the channel is nested at least two deep.
            string friendly;
            string? parent;

            if (string.IsNullOrEmpty(Path) || Path == "/")
            {
                friendly = "Root";
                parent = null;
            }
            else
            {
                var segments = Path.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

                // Prefer the bare Name from the SDK when it's there;
                // fall back to the last path segment for robustness.
                friendly = !string.IsNullOrEmpty(Name)
                    ? Name
                    : (segments.Length > 0 ? segments[^1] : Path);

                parent = segments.Length >= 2 ? segments[^2] : null;
            }
            #endregion

            #region Assemble the final label
            // Two spaces before the parenthesized parent read better
            // aloud by screen readers than one — a subtle pause.
            string body = parent is null
                ? friendly
                : $"{friendly}  (under {parent})";

            return RequiresPassword
                ? $"{body} — locked"
                : body;
            #endregion
        }
    }

    #endregion
}
#endregion
