#region Usings
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TeamTalkStreamer.Core.Pipeline;
#endregion

namespace TeamTalkStreamer.MobileBridge.Server;

#region Class: MobileBridgeServer (partial — lifetime & state)
/// <summary>
/// Kestrel-hosted WebSocket server that accepts mobile audio streams
/// and forwards them into the app's pipeline. Advertises itself on the
/// LAN via mDNS so mobile clients can discover it without typing IPs.
/// </summary>
/// <remarks>
/// Split across four partial files:
/// <list type="bullet">
///   <item><description><c>MobileBridgeServer.cs</c> — fields, ctor,
///     start/stop lifecycle, PIN bookkeeping.</description></item>
///   <item><description><c>MobileBridgeServer.Http.cs</c> — Kestrel /
///     WebApplication wiring.</description></item>
///   <item><description><c>MobileBridgeServer.WebSocket.cs</c> —
///     per-connection handshake and frame loop.</description></item>
///   <item><description><c>MobileBridgeServer.Discovery.cs</c> — mDNS
///     advertise / unadvertise.</description></item>
/// </list>
/// </remarks>
public sealed partial class MobileBridgeServer : IAsyncDisposable
{
    #region Fields

    #region Dependencies
    // Router is where decoded frames land. The server attaches a
    // MobileAudioSource to it per connection, then detaches on close.
    private readonly AudioRouter _router;
    private readonly MobileDeviceRegistry _registry;
    #endregion

    #region Config
    // Captured at startup; treated as immutable for the server's lifetime.
    private readonly string _serviceName;
    private readonly int _requestedPort;
    private string _pairingPin = "";
    #endregion

    #region Runtime state
    // _app is the Kestrel WebApplication; _runTask is its RunAsync task.
    // _listeningPort is resolved at startup (we bind :0 to get an
    // ephemeral port, then announce it over mDNS).
    private WebApplication? _app;
    private Task? _runTask;
    private int _listeningPort;
    private readonly CancellationTokenSource _cts = new();
    #endregion

    #endregion

    #region Events

    /// <summary>Raised once Kestrel is bound and we know the port.</summary>
    public event EventHandler<int>? Started;

    /// <summary>Raised after Kestrel has stopped.</summary>
    public event EventHandler? Stopped;

    #endregion

    #region Properties

    /// <summary>Port the server is listening on, or 0 if not started.</summary>
    public int ListeningPort => _listeningPort;

    /// <summary>Current pairing PIN. Regenerated on every
    /// <see cref="StartAsync"/> if not supplied via config.</summary>
    public string PairingPin => _pairingPin;

    /// <summary>mDNS service instance name advertised on the LAN.</summary>
    public string ServiceName => _serviceName;

    /// <summary>True while Kestrel is running.</summary>
    public bool IsRunning => _runTask is { IsCompleted: false };

    #endregion

    #region Constructor

    /// <param name="router">Pipeline destination for decoded frames.</param>
    /// <param name="registry">Registry to reflect UI state of connected devices.</param>
    /// <param name="serviceName">mDNS service instance name (e.g. machine name).</param>
    /// <param name="port">TCP port to listen on. 0 = pick a free ephemeral port.</param>
    /// <param name="pairingPin">Pre-shared PIN. Empty = generate a new one at start.</param>
    public MobileBridgeServer(
        AudioRouter router,
        MobileDeviceRegistry registry,
        string serviceName,
        int port = 0,
        string pairingPin = "")
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(registry);

        _router = router;
        _registry = registry;
        _serviceName = serviceName;
        _requestedPort = port;
        _pairingPin = pairingPin;
    }

    #endregion

    #region Public lifecycle

    /// <summary>Start Kestrel and begin advertising via mDNS.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        // Generate a fresh PIN if the caller didn't supply one. Six
        // decimal digits is enough entropy for LAN-only scope since the
        // attacker has to already be on the network.
        if (string.IsNullOrWhiteSpace(_pairingPin))
            _pairingPin = GeneratePin();

        BuildAndStartHost();          // in MobileBridgeServer.Http.cs
        StartMdnsAdvertisement();     // in MobileBridgeServer.Discovery.cs

        Started?.Invoke(this, _listeningPort);
        await Task.CompletedTask;
    }

    /// <summary>Stop Kestrel and tear down the mDNS advertisement.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;

        StopMdnsAdvertisement();      // in MobileBridgeServer.Discovery.cs

        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken).ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }

        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); }
            catch { /* host errors on shutdown are not interesting */ }
            _runTask = null;
        }

        Stopped?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Auth helpers
    // PIN generation and HMAC verification. Kept here (not in a separate
    // Auth partial) because the whole auth story is ~20 lines.

    private static string GeneratePin()
    {
        // 6 decimal digits, uniformly distributed. RandomNumberGenerator
        // gives us crypto-strong randomness without leaking timing info.
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        uint n = BitConverter.ToUInt32(bytes) % 1_000_000u;
        return n.ToString("D6");
    }

    /// <summary>Verify that <paramref name="clientHmacHex"/> matches an
    /// HMAC-SHA256 of <paramref name="deviceId"/> using the current PIN
    /// as the key. The client computes this in its own Hello message.</summary>
    internal bool VerifyPinProof(string deviceId, string clientHmacHex)
    {
        if (string.IsNullOrEmpty(clientHmacHex)) return false;

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(_pairingPin);
        var dataBytes = System.Text.Encoding.UTF8.GetBytes(deviceId);
        using var hmac = new HMACSHA256(keyBytes);
        var expected = hmac.ComputeHash(dataBytes);
        var expectedHex = Convert.ToHexString(expected);

        // Constant-time compare so we don't leak PIN info via timing.
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(expectedHex),
            System.Text.Encoding.ASCII.GetBytes(clientHmacHex));
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await StopAsync().ConfigureAwait(false); }
        catch { /* dispose must not throw */ }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
#endregion
