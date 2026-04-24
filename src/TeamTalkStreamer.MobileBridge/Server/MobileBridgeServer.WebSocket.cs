#region Usings
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using TeamTalkStreamer.MobileBridge.Protocol;
#endregion

namespace TeamTalkStreamer.MobileBridge.Server;

#region Class: MobileBridgeServer (partial — per-connection handler)
/// <summary>
/// Per-WebSocket session loop: receive Hello, authenticate, create a
/// <see cref="MobileAudioSource"/>, attach to the router, decode Opus
/// frames, and clean up on close.
/// </summary>
public sealed partial class MobileBridgeServer
{
    #region Constants

    #region Buffer sizes
    // 4 KB is enough for Hello / JSON control messages. 2 KB is enough
    // for one Opus frame (real frames are ~20-120 bytes). Buffers are
    // per-session so reuse is cheap.
    private const int TextBufferBytes = 4 * 1024;
    private const int BinaryBufferBytes = 2 * 1024;
    #endregion

    #region PCM framing
    // 20 ms @ 48 kHz mono = 960 samples = 1920 bytes (16-bit).
    private const int SamplesPerFrame = 960;
    private const int PcmBytesPerFrame = SamplesPerFrame * 2;
    #endregion

    #region JSON options
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    #endregion

    #endregion

    #region Session loop

    private async Task HandleWebSocketSessionAsync(
        HttpContext context,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        MobileAudioSource? source = null;
        Guid deviceId = Guid.Empty;

        try
        {
            #region Handshake
            // First message MUST be a text Hello within 5 seconds.
            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(5));

            var hello = await ReceiveHelloAsync(socket, handshakeTimeout.Token)
                .ConfigureAwait(false);

            if (hello is null ||
                !Guid.TryParse(hello.DeviceId, out deviceId) ||
                !VerifyPinProof(hello.DeviceId, hello.PinHmac))
            {
                await SendRejectAsync(socket, "Invalid PIN or Hello", 401, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            #endregion

            #region Source setup
            // Create and attach the source. The registry is informed
            // so the UI can show the device as paired.
            source = new MobileAudioSource(deviceId, hello.DeviceName);
            _router.AttachSource(source);
            _registry.Upsert(deviceId, hello.DeviceName, MobileDeviceState.Pairing);

            await source.StartAsync(cancellationToken).ConfigureAwait(false);
            _registry.TransitionState(deviceId, MobileDeviceState.Streaming);

            await SendAckAsync(socket, Guid.NewGuid().ToString(),
                48_000, 1, cancellationToken).ConfigureAwait(false);
            #endregion

            #region Frame loop
            // Continuously receive binary (audio) and text (control)
            // frames until the socket closes or errors.
            var opus = new OpusDecoderWrapper(sampleRate: 48_000, channels: 1);
            byte[] binaryBuffer = new byte[BinaryBufferBytes];

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(binaryBuffer, cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Binary &&
                    result.Count > 0 &&
                    binaryBuffer[0] == (byte)ProtocolMessageType.AudioFrame)
                {
                    ProcessAudioFrame(binaryBuffer.AsSpan(0, result.Count), opus, source);
                }
                // Text control frames (Ping / Volume / Bye) are handled
                // in a future pass — v1 just ignores them and relies on
                // the WebSocket keepalive for liveness.
            }
            #endregion
        }
        catch (OperationCanceledException)
        {
            // Normal on shutdown; no special handling needed.
        }
        catch (WebSocketException)
        {
            // Client dropped mid-frame; cleanup below.
        }
        finally
        {
            #region Teardown
            if (source is not null)
            {
                _router.DetachSource(source.Id);
                try { await source.StopAsync().ConfigureAwait(false); } catch { }
                await source.DisposeAsync().ConfigureAwait(false);
            }
            if (deviceId != Guid.Empty)
                _registry.Remove(deviceId);

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "bye",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* best effort */ }
            }
            #endregion
        }
    }

    #endregion

    #region Message I/O helpers

    /// <summary>Read the initial text Hello message. Returns null if
    /// the first frame isn't a valid Hello.</summary>
    private static async Task<HelloMessage?> ReceiveHelloAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[TextBufferBytes];
        var result = await socket.ReceiveAsync(buffer, cancellationToken)
            .ConfigureAwait(false);

        if (result.MessageType != WebSocketMessageType.Text) return null;

        string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        try
        {
            return JsonSerializer.Deserialize<HelloMessage>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Send an <see cref="AckMessage"/> to the client.</summary>
    private static Task SendAckAsync(
        WebSocket socket,
        string sessionId,
        int sampleRate,
        int channels,
        CancellationToken cancellationToken)
    {
        var msg = new AckMessage("ack", sessionId, sampleRate, channels);
        return SendJsonAsync(socket, msg, cancellationToken);
    }

    /// <summary>Send a <see cref="RejectMessage"/> then close the socket.</summary>
    private static async Task SendRejectAsync(
        WebSocket socket,
        string reason,
        int code,
        CancellationToken cancellationToken)
    {
        var msg = new RejectMessage("reject", reason, code);
        await SendJsonAsync(socket, msg, cancellationToken).ConfigureAwait(false);

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                reason,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Serialize and send a JSON text frame.</summary>
    private static async Task SendJsonAsync<T>(
        WebSocket socket,
        T message,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOpts);
        await socket.SendAsync(
            payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion

    #region Audio frame decode

    /// <summary>Parse one binary AudioFrame envelope, decode its Opus
    /// payload, and push the resulting PCM into the source.</summary>
    private static void ProcessAudioFrame(
        ReadOnlySpan<byte> envelope,
        OpusDecoderWrapper opus,
        MobileAudioSource source)
    {
        // Envelope: 1 byte type + 2 bytes seq (big-endian) + payload.
        // We already matched the type byte; skip over seq for now (v1
        // doesn't do loss concealment based on seq).
        const int HeaderBytes = 3;
        if (envelope.Length <= HeaderBytes) return;

        var opusPayload = envelope[HeaderBytes..];

        // Decode straight into a PCM buffer we then hand to the source.
        Span<short> pcmSamples = stackalloc short[SamplesPerFrame];
        int samplesDecoded = opus.DecodeFrame(opusPayload, pcmSamples);
        if (samplesDecoded <= 0) return;

        // Convert short[] -> byte[] for the AudioFrame payload.
        byte[] pcmBytes = new byte[samplesDecoded * 2];
        for (int i = 0; i < samplesDecoded; i++)
        {
            pcmBytes[i * 2] = (byte)(pcmSamples[i] & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((pcmSamples[i] >> 8) & 0xFF);
        }

        source.SubmitPcm(pcmBytes);
    }

    #endregion
}
#endregion
