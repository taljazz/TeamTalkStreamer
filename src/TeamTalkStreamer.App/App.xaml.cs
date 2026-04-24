#nullable enable

#region Usings
using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamTalkStreamer.App.Hosting;
#endregion

namespace TeamTalkStreamer.App;

#region Class: App
/// <summary>
/// WPF application entry point. Responsible for constructing the
/// generic <see cref="IHost"/> (DI + configuration + logging),
/// resolving the main window from the container, and shutting down
/// cleanly when the window closes.
/// </summary>
public partial class App : Application
{
    #region Fields

    #region Host handle
    /// <summary>The composition root. Created in <see cref="OnStartup"/>
    /// and torn down in <see cref="OnExit"/>.</summary>
    private IHost? _host;
    #endregion

    #region Shutdown tuning
    // Upper bound on graceful teardown. If anything hangs past this we
    // fall back to Environment.Exit so the process doesn't linger in
    // the user's task manager as a zombie.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    #endregion

    #endregion

    #region Lifecycle: OnStartup

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        #region Build host
        // AppHost.Create wires every project's services into a single
        // ServiceCollection and returns a built IHost ready to start.
        _host = AppHost.Create();
        await _host.StartAsync().ConfigureAwait(true);
        #endregion

        #region Resolve & show main window
        // Resolve through the container so the window receives its
        // MainViewModel (which itself receives all the pipeline
        // services it needs via constructor injection).
        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
        #endregion
    }

    #endregion

    #region Lifecycle: OnExit
    // IMPORTANT: this method is intentionally synchronous (not async
    // void) so WPF actually waits for teardown before letting the
    // process terminate. Blocking with GetAwaiter().GetResult() is
    // fine here because:
    //   * The dispatcher is already winding down, so we can't deadlock.
    //   * The Task.Run jumps to the thread pool, freeing the UI thread.

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            TearDownHost();
        }
        catch
        {
            // Never let teardown failures block the shutdown. The
            // Environment.Exit call below guarantees the process ends.
        }

        base.OnExit(e);

        #region Belt-and-suspenders force-exit
        // Native libraries in our dependency graph (TeamTalk5.dll, the
        // OpenAL runtime, ACE inside TeamTalk) can spawn non-background
        // threads that keep the Windows process alive even after WPF
        // has finished shutting down. Calling Environment.Exit here is
        // the reliable way to ensure closing the window actually
        // ends the process.
        Environment.Exit(0);
        #endregion
    }

    #endregion

    #region Private: host teardown

    /// <summary>
    /// Synchronously runs <c>StopAsync</c> + async disposal of the
    /// host on a pool thread. Async disposal is critical — several of
    /// our singletons (<c>TeamTalkClient</c>, <c>MobileBridgeServer</c>,
    /// <c>WasapiLoopbackSource</c>, <c>TeamTalkSink</c>) only implement
    /// <see cref="IAsyncDisposable"/>, so the synchronous
    /// <c>IDisposable.Dispose</c> path on the container would either
    /// skip them or throw — in both cases their native resources would
    /// leak and keep the process alive.
    /// </summary>
    private void TearDownHost()
    {
        if (_host is null) return;

        var host = _host;
        _host = null;

        #region Run teardown off the UI thread with a hard time-bound
        // Task.Run avoids the classic sync-over-async deadlock by
        // never capturing the UI SynchronizationContext. Wait() with a
        // timeout means we don't hang here indefinitely if a service
        // misbehaves on dispose.
        var teardownTask = Task.Run(async () =>
        {
            try
            {
                await host.StopAsync(ShutdownTimeout).ConfigureAwait(false);
            }
            catch
            {
                // Continue to dispose even if StopAsync throws.
            }

            // Prefer async disposal so IAsyncDisposable singletons run
            // their DisposeAsync bodies (TeamTalkClient.DisposeAsync
            // disconnects the SDK and deletes the native instance,
            // which is what actually releases the TT5.dll threads).
            if (host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                host.Dispose();
        });

        teardownTask.Wait(ShutdownTimeout);
        #endregion
    }

    #endregion
}
#endregion
