using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Terminal.Common.Extensions;

namespace Terminal.Console;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddTerminalServices(context.Configuration);
                services.AddHostedService<TerminalClientService>();
            })
            .Build();

        await host.RunAsync();
    }
}
