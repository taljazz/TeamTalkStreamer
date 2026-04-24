#region Usings
// Pure enum — no imports required. Regions still declared for consistency
// with every other file in the project.
#endregion

namespace TeamTalkStreamer.Core.Audio;

#region Enum: AudioSourceType
/// <summary>
/// Categorizes an <see cref="IAudioSource"/> so the UI, router, and
/// persistence layer can tell different kinds apart at a glance.
/// The screen reader uses the enum name to announce source types
/// ("loopback source attached", "mobile source attached", etc.).
/// </summary>
/// <remarks>
/// Values are persisted in preset JSON, so DO NOT renumber or remove
/// existing members. Add new kinds at the end of a group.
/// </remarks>
public enum AudioSourceType
{
    #region Local Windows sources
    // Anything captured from the host PC itself.

    /// <summary>Default render endpoint captured via WASAPI loopback —
    /// i.e. "whatever the speakers are currently playing." The primary
    /// input for v1 and the whole reason this app exists.</summary>
    Loopback = 0,

    /// <summary>A specific (non-default) render endpoint captured via
    /// WASAPI loopback. Reserved for users who want to stream only a
    /// particular device's output (secondary speakers, virtual cable,
    /// etc.) rather than the default mix.</summary>
    DeviceLoopback = 1,

    /// <summary>Default render endpoint captured via the Windows 10
    /// 20H1+ Process Loopback API with an EXCLUDE target. Lets the
    /// user stream "everything except my screen reader / this app"
    /// without disturbing their own system audio.</summary>
    ProcessLoopbackExclude = 2,

    #endregion

    #region Remote / companion sources
    // Audio originating on another device, transported over the network.

    /// <summary>Audio streamed from a paired mobile companion over the
    /// LAN using the MobileBridge protocol (WebSocket + Opus).</summary>
    Mobile = 10,

    #endregion

    #region Future / auxiliary sources
    // Reserved slots so presets continue to round-trip cleanly when we
    // add new source kinds later.

    /// <summary>Audio read from a local file. Not wired in v1 — listed
    /// here so serialized presets stay stable when the feature lands.</summary>
    File = 20,

    #endregion
}
#endregion
