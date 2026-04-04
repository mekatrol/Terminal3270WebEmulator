using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Terminal.Common.Protocol;
using Terminal.Common.Terminal;
using Terminal.MockServer.Screens;
using Terminal.MockServer.Services;

namespace Terminal.MockServer;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        if (TryGetDumpScreenRequest(args, out var requestedScreenId))
        {
            await DumpScreenAsync(requestedScreenId);
            return;
        }

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services
                    .AddOptions<MockServerOptions>()
                    .Bind(context.Configuration.GetSection(MockServerOptions.SectionName))
                    .ValidateOnStart();

                services.AddSingleton<IValidateOptions<MockServerOptions>, MockServerOptionsValidator>();
                services.AddSingleton(static serviceProvider =>
                {
                    var options = serviceProvider
                        .GetRequiredService<IOptions<MockServerOptions>>()
                        .Value;
                    var logger = serviceProvider.GetRequiredService<ILogger<ScreenRegistry>>();
                    var screensDirectory = MockServerPaths.ResolveScreensDirectory(options.ScreensDirectory);

                    var registry = ScreenRegistry.LoadFromDirectory(screensDirectory, logger);

                    if (!registry.TryGet(options.InitialScreen, out _))
                    {
                        throw new InvalidOperationException(
                            $"Initial screen '{options.InitialScreen}' was not found in '{screensDirectory}'.");
                    }

                    return registry;
                });
                services.AddHostedService<MockTn3270Server>();
            })
            .Build();

        await host.RunAsync();
    }

    private static bool TryGetDumpScreenRequest(string[] args, out string? screenId)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--dump-screen", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            screenId = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[index + 1]
                : null;

            return true;
        }

        screenId = null;
        return false;
    }

    private static Task DumpScreenAsync(string? requestedScreenId)
    {
        using var loggerFactory = LoggerFactory.Create(static builder => builder.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger<ScreenRegistry>();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile(
                $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Production}.json",
                optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = configuration
            .GetSection(MockServerOptions.SectionName)
            .Get<MockServerOptions>()
            ?? new MockServerOptions();

        var validator = new MockServerOptionsValidator();
        var validationResult = validator.Validate(name: null, options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                MockServerOptions.SectionName,
                typeof(MockServerOptions),
                validationResult.Failures!);
        }

        var screensDirectory = MockServerPaths.ResolveScreensDirectory(options.ScreensDirectory);
        var registry = ScreenRegistry.LoadFromDirectory(screensDirectory, logger);
        var screenId = string.IsNullOrWhiteSpace(requestedScreenId) ? options.InitialScreen : requestedScreenId;

        if (!registry.TryGet(screenId, out var screen) || screen is null)
        {
            throw new InvalidOperationException($"Screen '{screenId}' was not found.");
        }

        var record = DataStreamEncoder.Encode(screen);
        var parserDescription = Tn3270DataStreamParser.Describe(record);
        var terminalScreen = new Tn3270TerminalScreen(screen.Cols, screen.Rows);
        terminalScreen.ApplyInboundRecord(record);

        Console.WriteLine($"Screen: {screen.Id}");
        Console.WriteLine($"Description: {screen.Description}");
        Console.WriteLine($"Cursor: row={terminalScreen.CursorAddress / screen.Cols + 1}, col={terminalScreen.CursorAddress % screen.Cols + 1}");
        Console.WriteLine($"Command: {parserDescription.CommandName}");
        Console.WriteLine($"WCC: 0x{parserDescription.Wcc ?? 0:X2}");
        Console.WriteLine();

        var rowBuffer = new char[screen.Cols];

        for (var row = 0; row < screen.Rows; row++)
        {
            for (var col = 0; col < screen.Cols; col++)
            {
                var cell = terminalScreen.Cells[(row * screen.Cols) + col];
                rowBuffer[col] = cell.IsFieldAttribute || cell.IsHidden ? ' ' : cell.Character;
            }

            Console.WriteLine(new string(rowBuffer).TrimEnd());
        }

        Console.WriteLine();
        Console.WriteLine("Orders:");
        foreach (var message in parserDescription.Messages)
        {
            Console.WriteLine($"  {message}");
        }

        return Task.CompletedTask;
    }
}
