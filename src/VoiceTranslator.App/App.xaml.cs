using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VoiceTranslator.App.ViewModels;
using VoiceTranslator.App.Views;
using VoiceTranslator.App.Services;

namespace VoiceTranslator.App;

public partial class App : System.Windows.Application
{
    private IHost? host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(e.Args);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<VoiceProfileStore>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<DesktopRuntimeService>();
        builder.Services.AddHostedService(
            services => services.GetRequiredService<DesktopRuntimeService>());

        host = builder.Build();
        host.Services.GetRequiredService<MainWindow>().Show();
        await host.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync();
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }

        base.OnExit(e);
    }
}
