#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.MobileBridge.Protocol;

#region Enum: MobileDeviceState
/// <summary>
/// Lifecycle of a single mobile device connection from the server's
/// point of view. Surfaced in UI so the user can see each paired
/// device's individual state.
/// </summary>
public enum MobileDeviceState
{
    /// <summary>Device found via mDNS but hasn't attempted connection yet.</summary>
    Discovered = 0,

    /// <summary>Socket upgraded; validating Hello / PIN.</summary>
    Pairing = 1,

    /// <summary>Accepted and paired; not yet streaming audio.</summary>
    Paired = 2,

    /// <summary>Sending audio frames into the pipeline.</summary>
    Streaming = 3,

    /// <summary>Socket closed cleanly.</summary>
    Disconnected = 4,

    /// <summary>Connection was rejected (bad PIN, etc.) or errored out.</summary>
    Rejected = 99,
}
#endregion
