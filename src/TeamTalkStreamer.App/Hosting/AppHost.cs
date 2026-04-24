#region Usings
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamTalkStreamer.Accessibility.Feedback;
using TeamTalkStreamer.Accessibility.Speech;
using TeamTalkStreamer.App.ViewModels;
using TeamTalkStreamer.App.Views;
using TeamTalkStreamer.Audio.Windows.Wasapi;
using TeamTalkStreamer.Core.Pipeline;
using TeamTalkStreamer.Persistence.Config;
using TeamTalkStreamer.TeamTalk.Client;
using TeamTalkStreamer.TeamTalk.Sink;
#endregion

namespace TeamTalkStreamer.App.Hosting;

#region Class: AppHost
/// <summary>
/// Composition root. Builds the <see cref="IHost"/> with every
/// service the app needs registered in DI. The WPF <c>App</c> class
/// owns the returned host and decides when to start/stop it.
/// </summary>
/// <remarks>
/// Each project contributes its own services here. Keeping the
/// registration in one place (rather than scattered extension methods)
/// makes the dependency graph easy to read — open this file and you
/// see the entire composition at a glance.
/// </remarks>
public static class AppHost
{
    #region Public API

    /// <summary>Create and return a built <see cref="IHost"/>. Caller
    /// is responsible for starting, stopping, and disposing it.</summary>
    public static IHost Create()
    {
        var builder = Host.CreateApplicationBuilder();

        RegisterAccessibilityServices(builder.Services);
        RegisterPersistenceServices(builder.Services);
        RegisterAudioServices(builder.Services);
        RegisterTeamTalkServices(builder.Services);
        // MobileBridge registrations are removed while the iOS
        // companion app is unbuildable without Mac access. See the
        // TeamTalkStreamer.MobileBridge project for the preserved
        // server/protocol code; restore RegisterMobileBridgeServices
        // below plus the MainViewModel.MobileBridge.cs partial when
        // the companion app is ready.
        RegisterViewsAndViewModels(builder.Services);

        return builder.Build();
    }

    #endregion

    #region Registration — Accessibility
    // Tolk speech + OpenAL feedback tones. Singletons so every caller
    // shares one connection to the screen reader / audio device.

    private static void RegisterAccessibilityServices(IServiceCollection services)
    {
        services.AddSingleton<ISpeechService, TolkSpeechService>();
        services.AddSingleton<IAudioFeedbackService, AudioFeedbackService>();
    }

    #endregion

    #region Registration — Persistence
    // JSON settings store. Default ctor points at %APPDATA%\TeamTalkStreamer.

    private static void RegisterPersistenceServices(IServiceCollection services)
    {
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
    }

    #endregion

    #region Registration — Audio pipeline
    // Router is the central piece — one per app. Loopback source is
    // a singleton too (there's only one default render device to capture).

    private static void RegisterAudioServices(IServiceCollection services)
    {
        services.AddSingleton<AudioRouter>();
        services.AddSingleton<WasapiLoopbackSource>();
    }

    #endregion

    #region Registration — TeamTalk
    // Client is a singleton; the sink wraps it and is also a singleton
    // so the router can hold a stable reference.

    private static void RegisterTeamTalkServices(IServiceCollection services)
    {
        services.AddSingleton<TeamTalkClient>();
        services.AddSingleton<TeamTalkSink>();
    }

    #endregion

    #region Registration — MobileBridge (deferred, see class comment)
    // The registration block below is kept commented so restoring
    // mobile support when the iOS app is ready is a copy/paste job:
    //
    //   1. Re-add the ProjectReference to TeamTalkStreamer.MobileBridge
    //      in TeamTalkStreamer.App.csproj.
    //   2. Restore the `using TeamTalkStreamer.MobileBridge.Server;`
    //      line at the top of this file.
    //   3. Uncomment the method below + its call in Create().
    //   4. Restore the MainViewModel.MobileBridge.cs partial from git
    //      history (or re-add its ctor params to MainViewModel.cs).
    //   5. Restore the Mobile bridge GroupBox in MainWindow.xaml.
    //
    //  private static void RegisterMobileBridgeServices(IServiceCollection services)
    //  {
    //      services.AddSingleton<MobileDeviceRegistry>();
    //      services.AddSingleton<MobileBridgeServer>(sp =>
    //      {
    //          var router = sp.GetRequiredService<AudioRouter>();
    //          var registry = sp.GetRequiredService<MobileDeviceRegistry>();
    //          var store = sp.GetRequiredService<IAppSettingsStore>();
    //          var settings = store.LoadAsync().GetAwaiter().GetResult();
    //          return new MobileBridgeServer(
    //              router: router,
    //              registry: registry,
    //              serviceName: settings.MobileBridge.ServiceName,
    //              port: settings.MobileBridge.ListenPort,
    //              pairingPin: settings.MobileBridge.PairingPin);
    //      });
    //  }

    #endregion

    #region Registration — Views & ViewModels
    // Main window is singleton (only one instance per app); its view
    // model has the same lifetime. The server-settings dialog is
    // transient — each click on "Server settings..." resolves a fresh
    // VM + Window pair so the in-memory form state doesn't leak
    // between opens. A Func<> factory is explicitly registered because
    // Microsoft.Extensions.DependencyInjection does not auto-synthesize
    // factory delegates.

    private static void RegisterViewsAndViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<ServerSettingsViewModel>();
        services.AddTransient<ServerSettingsWindow>();

        services.AddTransient<ExcludedProcessViewModel>();
        services.AddTransient<ExcludedProcessWindow>();

        // Factories: let MainViewModel create a fresh dialog instance
        // on demand without dragging in IServiceProvider (which would
        // be a service-locator anti-pattern).
        services.AddSingleton<Func<ServerSettingsWindow>>(sp =>
            () => sp.GetRequiredService<ServerSettingsWindow>());
        services.AddSingleton<Func<ExcludedProcessWindow>>(sp =>
            () => sp.GetRequiredService<ExcludedProcessWindow>());
    }

    #endregion
}
#endregion
