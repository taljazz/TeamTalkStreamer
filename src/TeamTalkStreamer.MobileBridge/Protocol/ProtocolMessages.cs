#region Usings
// JSON records — no imports needed.
#endregion

namespace TeamTalkStreamer.MobileBridge.Protocol;

#region Record: HelloMessage
/// <summary>
/// Client -> server handshake payload. Sent as the first text frame
/// on a newly-opened WebSocket; binary frames only begin after an
/// <see cref="AckMessage"/> response.
/// </summary>
public sealed record HelloMessage(
    string Type,
    string DeviceName,
    string DeviceId,
    int ProtocolVersion,
    int SampleRate,
    int Channels,
    string Codec,
    string PinHmac);
#endregion

#region Record: AckMessage
/// <summary>
/// Server -> client handshake acceptance. Echoes back negotiated
/// audio format (which may be coerced to something the server supports
/// if the client's preference wasn't compatible).
/// </summary>
public sealed record AckMessage(
    string Type,
    string SessionId,
    int SampleRate,
    int Channels);
#endregion

#region Record: RejectMessage
/// <summary>
/// Server -> client. Handshake failure or any server-initiated refusal.
/// </summary>
public sealed record RejectMessage(
    string Type,
    string Reason,
    int Code);
#endregion

#region Record: VolumeMessage
/// <summary>
/// Client -> server control update. Range 0..1.
/// </summary>
public sealed record VolumeMessage(string Type, float Volume);
#endregion

#region Record: PingMessage / PongMessage
public sealed record PingMessage(string Type, long SentTicks);
public sealed record PongMessage(string Type, long SentTicks);
#endregion

#region Record: ByeMessage / ErrorMessage
public sealed record ByeMessage(string Type, string? Reason);
public sealed record ErrorMessage(string Type, string Message, int Code);
#endregion
