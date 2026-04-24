#region Usings
using System;
using Makaretu.Dns;
#endregion

namespace TeamTalkStreamer.MobileBridge.Server;

#region Class: MobileBridgeServer (partial — mDNS advertisement)
/// <summary>
/// LAN discovery via mDNS / DNS-SD using the Makaretu.Dns library.
/// Advertises a single service instance so mobile companion apps can
/// find the PC by name without typing IPs.
/// </summary>
/// <remarks>
/// Service type: <c>_ttstreamer._tcp.local</c> (private namespace —
/// we don't use a registered SRV type). TXT record carries the
/// protocol version so a future v2 mobile app can skip incompatible
/// v1 servers gracefully.
/// </remarks>
public sealed partial class MobileBridgeServer
{
    #region Constants
    // Service type string is the Apple Bonjour convention: underscore
    // prefix, lowercase, tcp or udp transport.
    private const string ServiceType = "_ttstreamer._tcp";
    private const int ProtocolVersion = 1;
    #endregion

    #region Fields
    // mDNS bits are only instantiated between Start and Stop. Null
    // outside that window so we can detect double-start / double-stop.
    private MulticastService? _multicast;
    private ServiceDiscovery? _discovery;
    private ServiceProfile? _profile;
    #endregion

    #region Start / Stop

    /// <summary>Begin advertising. Safe to call while already running
    /// (no-op) thanks to the _multicast null check.</summary>
    private void StartMdnsAdvertisement()
    {
        if (_multicast is not null) return;

        try
        {
            _multicast = new MulticastService();
            _discovery = new ServiceDiscovery(_multicast);

            _profile = new ServiceProfile(
                instanceName: _serviceName,
                serviceName: ServiceType,
                port: (ushort)_listeningPort);

            // TXT records — key=value strings surfaced to the client.
            _profile.AddProperty("version", ProtocolVersion.ToString());
            _profile.AddProperty("path", "/stream");

            _discovery.Advertise(_profile);
            _multicast.Start();
        }
        catch (Exception)
        {
            // mDNS is best-effort — if it fails (firewall, adapter
            // problem) we still run, the user can connect by IP.
            StopMdnsAdvertisement();
        }
    }

    /// <summary>Remove the advertisement and release mDNS resources.</summary>
    private void StopMdnsAdvertisement()
    {
        try
        {
            if (_discovery is not null && _profile is not null)
            {
                _discovery.Unadvertise(_profile);
            }
        }
        catch
        {
            // Best-effort teardown.
        }

        _discovery?.Dispose();
        _multicast?.Dispose();
        _discovery = null;
        _multicast = null;
        _profile = null;
    }

    #endregion
}
#endregion
