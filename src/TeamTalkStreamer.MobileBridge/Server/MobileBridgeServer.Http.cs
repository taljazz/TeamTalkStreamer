#region Usings
using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#endregion

namespace TeamTalkStreamer.MobileBridge.Server;

#region Class: MobileBridgeServer (partial — Kestrel host)
/// <summary>
/// Builds and starts the Kestrel <see cref="WebApplication"/> that
/// hosts the WebSocket endpoint. Kept separate from the main partial
/// so the Kestrel-specific using directives and wiring don't crowd the
/// lifetime management code.
/// </summary>
public sealed partial class MobileBridgeServer
{
    #region Host construction

    /// <summary>Spin up Kestrel, bind to the requested port (0 = pick
    /// ephemeral), map the WebSocket endpoint, and begin serving.</summary>
    private void BuildAndStartHost()
    {
        var builder = WebApplication.CreateSlimBuilder();

        #region Kestrel configuration
        // Bind the user-supplied port on all LAN interfaces. Port 0
        // lets the OS pick a free ephemeral port; we read back the
        // chosen port after start so mDNS can announce it.
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(_requestedPort, listen =>
            {
                // HTTP/1.1 is sufficient for WebSockets; no HTTP/2 or
                // HTTPS required for LAN use. (Pairing PIN + HMAC
                // protects the handshake.)
                listen.Protocols = HttpProtocols.Http1;
            });
        });
        #endregion

        #region Logging
        // Minimal console logging by default. The App can inject a
        // richer logger via ServiceProviderOptions if it wants to.
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        #endregion

        var app = builder.Build();

        #region Middleware
        // WebSocket upgrade support + the /stream endpoint. Anything
        // that isn't a WS request at /stream gets a 404.
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.Map("/stream", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(
                    "This endpoint accepts WebSocket connections only.");
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await HandleWebSocketSessionAsync(context, socket, _cts.Token)
                .ConfigureAwait(false);
        });

        // Lightweight health probe so mobile clients can confirm the
        // host is alive before trying to upgrade.
        app.MapGet("/health", () => Results.Ok(new
        {
            service = _serviceName,
            port = _listeningPort,
            version = 1,
        }));
        #endregion

        _app = app;
        _runTask = app.RunAsync(_cts.Token);

        // Resolve the actual bound port. If the user asked for 0, this
        // reads back the OS-assigned ephemeral port.
        _listeningPort = ResolveListeningPort(app);
    }

    #endregion

    #region Helpers

    /// <summary>After Kestrel binds, the <c>IServerAddressesFeature</c>
    /// knows what port we actually got. Parse and return it.</summary>
    private static int ResolveListeningPort(WebApplication app)
    {
        var feature = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        if (feature is null) return 0;

        foreach (string address in feature.Addresses)
        {
            // Addresses look like "http://[::]:51234"; parse out the port.
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                return uri.Port;
        }
        return 0;
    }

    #endregion
}
#endregion
