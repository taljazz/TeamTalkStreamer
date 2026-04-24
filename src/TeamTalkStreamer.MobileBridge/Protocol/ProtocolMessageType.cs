#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.MobileBridge.Protocol;

#region Enum: ProtocolMessageType
/// <summary>
/// First byte of every binary frame on the mobile-bridge WebSocket tells
/// the receiver how to parse the rest. Text frames are always JSON and
/// their <c>"type"</c> field uses the same values (as strings).
/// </summary>
/// <remarks>
/// Values are on the wire, so do NOT renumber existing members. Add
/// new types at the end of their logical group.
/// </remarks>
public enum ProtocolMessageType : byte
{
    #region Handshake

    /// <summary>Client -> server. Opens the session with device info
    /// and an HMAC-signed PIN proof. First message on any new socket.</summary>
    Hello = 0x01,

    /// <summary>Server -> client. Accepts the Hello; echoes negotiated
    /// audio format (sample rate, channels).</summary>
    Ack = 0x02,

    /// <summary>Server -> client. Rejects the connection (bad PIN,
    /// unsupported codec, version mismatch, server full).</summary>
    Reject = 0x03,

    #endregion

    #region Data

    /// <summary>Client -> server. Binary frame: 1-byte type, 2-byte
    /// sequence number (big-endian), N-byte Opus-encoded payload.
    /// Payload decodes to a 20 ms PCM chunk.</summary>
    AudioFrame = 0x10,

    #endregion

    #region Control

    /// <summary>Either direction. Keepalive request.</summary>
    Ping = 0x20,

    /// <summary>Either direction. Keepalive reply.</summary>
    Pong = 0x21,

    /// <summary>Client -> server. Update client-side volume (0..1) so
    /// the server can surface it in UI.</summary>
    Volume = 0x22,

    #endregion

    #region Shutdown

    /// <summary>Either direction. Polite close-before-socket-close.</summary>
    Bye = 0x30,

    /// <summary>Either direction. Fatal error; peer may close immediately.</summary>
    Error = 0x31,

    #endregion
}
#endregion
