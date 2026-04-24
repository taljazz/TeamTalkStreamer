#region Usings
using System;
#endregion

namespace TeamTalkStreamer.Persistence.Config;

#region Class: AppSettings
/// <summary>
/// Root settings object persisted to <c>%APPDATA%\TeamTalkStreamer\settings.json</c>.
/// Every user-tunable value hangs off this class, grouped into nested
/// POCOs by concern so the JSON file stays readable even as options grow.
/// </summary>
/// <remarks>
/// Design choices:
/// <list type="bullet">
///   <item><description>Plain mutable properties with default values —
///     <c>System.Text.Json</c> handles round-tripping without attributes.</description></item>
///   <item><description>Passwords / PINs live on the settings object only
///     in memory; the store hashes them on write (future work).</description></item>
///   <item><description>Schema version lets us migrate old settings files
///     forward without crashing when we add new sections.</description></item>
/// </list>
/// </remarks>
public sealed class AppSettings
{
    #region Metadata

    /// <summary>Schema version of this settings file. Bump when adding
    /// or renaming sections so the loader can migrate older files.</summary>
    public int Version { get; set; } = 1;

    #endregion

    #region Sections
    // Each logical group is its own nested POCO (below). Keeping them
    // grouped in separate classes makes DI-based options injection
    // straightforward — each service only depends on its slice.

    public TeamTalkServerSettings TeamTalk { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public MobileBridgeSettings MobileBridge { get; set; } = new();
    public AccessibilitySettings Accessibility { get; set; } = new();

    #endregion
}
#endregion

#region Class: TeamTalkServerSettings
/// <summary>
/// Connection details for the target TeamTalk server / channel.
/// </summary>
public sealed class TeamTalkServerSettings
{
    #region Server endpoint

    /// <summary>Host name or IP of the TeamTalk server.</summary>
    public string Host { get; set; } = "";

    /// <summary>TCP port (typically 10333 for default installs).</summary>
    public int TcpPort { get; set; } = 10_333;

    /// <summary>UDP port for voice data (typically same as TCP).</summary>
    public int UdpPort { get; set; } = 10_333;

    /// <summary>Server-level password. Blank = no password.</summary>
    public string ServerPassword { get; set; } = "";

    #endregion

    #region User identity

    /// <summary>Display name shown to other users in the channel.</summary>
    public string Nickname { get; set; } = "TeamTalk Streamer";

    /// <summary>Account username. Blank = anonymous.</summary>
    public string Username { get; set; } = "";

    /// <summary>Account password.</summary>
    public string Password { get; set; } = "";

    #endregion

    #region Channel target

    /// <summary>Full path to the channel to join, e.g. <c>/Lobby</c>.</summary>
    public string ChannelPath { get; set; } = "/";

    /// <summary>Per-channel password, if required.</summary>
    public string ChannelPassword { get; set; } = "";

    #endregion
}
#endregion

#region Class: AudioSettings
/// <summary>
/// Pipeline-wide audio preferences (source defaults, gain, exclusions).
/// Source-specific settings live alongside the source itself.
/// </summary>
public sealed class AudioSettings
{
    /// <summary>If true, start capturing system audio as soon as the app
    /// launches (rather than waiting for the user to click Start).</summary>
    public bool AutoStartLoopback { get; set; } = false;

    /// <summary>Overall gain multiplier applied to the mixed output before
    /// it reaches the TeamTalk sink. 1.0 = unity; 0.0 = silent.</summary>
    public float MasterGain { get; set; } = 1.0f;

    /// <summary>Process name (without extension, e.g. <c>nvda</c>) whose
    /// audio should be excluded from the loopback capture. Primary use
    /// case: stop the user's own screen reader from being broadcast
    /// into the TeamTalk channel. Blank = no exclusion; the standard
    /// full-mix loopback capture is used.</summary>
    public string ExcludedProcessName { get; set; } = "";

    /// <summary>Human-readable label for <see cref="ExcludedProcessName"/>
    /// — typically the window / session display name, shown in the
    /// picker dialog and spoken by Tolk when streaming starts. Saved
    /// alongside the process name so the UI can render it even when
    /// the excluded app isn't currently running.</summary>
    public string ExcludedProcessDisplayName { get; set; } = "";
}
#endregion

#region Class: MobileBridgeSettings
/// <summary>
/// LAN server settings used by the MobileBridge project.
/// </summary>
public sealed class MobileBridgeSettings
{
    /// <summary>Enable the WebSocket server at startup.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Port the WebSocket server listens on. 0 = pick a free
    /// ephemeral port at startup and announce it over mDNS.</summary>
    public int ListenPort { get; set; } = 0;

    /// <summary>mDNS service instance name — this is what mobile clients
    /// see in their "pick a PC" list. Defaults to the machine name.</summary>
    public string ServiceName { get; set; } = Environment.MachineName;

    /// <summary>6-digit PIN required for pairing. Regenerated per session
    /// at startup if this is blank.</summary>
    public string PairingPin { get; set; } = "";
}
#endregion

#region Class: AccessibilitySettings
/// <summary>
/// Screen-reader verbosity and feedback-tone tuning.
/// </summary>
public sealed class AccessibilitySettings
{
    /// <summary>Volume [0, 1] for the OpenAL feedback tones.</summary>
    public float FeedbackVolume { get; set; } = 0.6f;

    /// <summary>If true, announce every source state change verbally.
    /// If false, only session-level changes (connected, streaming,
    /// error) are spoken.</summary>
    public bool VerboseAnnouncements { get; set; } = true;
}
#endregion
