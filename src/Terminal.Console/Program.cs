using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Terminal.Common.Extensions;
using Terminal.Console.Logging;

namespace Terminal.Console;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));

                if (context.HostingEnvironment.IsDevelopment())
                {
                    // The debugger sink writes to the IDE/debug output window instead of stdout, which means
                    // developers can still inspect startup and protocol diagnostics without corrupting the
                    // interactive 3270 screen rendered in the console window.
                    logging.AddDebug();
                }

                logging.Services.Configure<FileLoggerOptions>(
                    context.Configuration.GetSection(FileLoggerOptions.SectionName));
                logging.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddTerminalServices(context.Configuration);
                services.AddHostedService<TerminalClientService>();
            })
            .Build();

        await host.RunAsync();
    }
}
