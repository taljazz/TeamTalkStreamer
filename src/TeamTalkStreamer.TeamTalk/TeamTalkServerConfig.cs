#nullable enable

#region Usings
// Plain POCO — no imports.
#endregion

namespace TeamTalkStreamer.TeamTalk;

#region Class: TeamTalkServerConfig
/// <summary>
/// Connection parameters for a single TeamTalk server. Separate from
/// <c>AppSettings.TeamTalkServerSettings</c> in the Persistence project
/// so the TeamTalk library doesn't depend on Persistence — the App
/// layer translates between the two at startup.
/// </summary>
/// <remarks>
/// Everything here is input to <c>TeamTalkClient.ConnectAsync</c>. The
/// split in responsibilities:
/// <list type="bullet">
///   <item><description>Persistence: how to load / save these values.</description></item>
///   <item><description>TeamTalk project: how to use them at runtime.</description></item>
/// </list>
/// </remarks>
public sealed class TeamTalkServerConfig
{
    #region Server endpoint
    public string Host { get; init; } = "";
    public int TcpPort { get; init; } = 10_333;
    public int UdpPort { get; init; } = 10_333;
    public bool UseEncryption { get; init; } = false;
    public string ServerPassword { get; init; } = "";
    #endregion

    #region User identity
    public string Nickname { get; init; } = "TeamTalk Streamer";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string ClientName { get; init; } = "TeamTalkStreamer";
    #endregion

    #region Channel target
    public string ChannelPath { get; init; } = "/";
    public string ChannelPassword { get; init; } = "";
    #endregion
}
#endregion
