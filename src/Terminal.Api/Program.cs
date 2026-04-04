using Microsoft.Extensions.Options;
using Terminal.Api.Admin;
using Terminal.Api.Logging;
using Terminal.Api.Options;
using Terminal.Api.WebSockets;
using Terminal.Common.Extensions;
using Terminal.Data.Extensions;

public partial class Program
{
    private const string _spaCorsPolicyName = "TerminalSpa";

    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var allowedSpaOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        if (builder.Environment.IsDevelopment())
        {
            // The debugger sink writes to the IDE/debug output window instead of the host console,
            // which keeps HTTP host diagnostics available during development without depending on
            // stdout collection from the ASP.NET Core process.
            builder.Logging.AddDebug();
        }

        builder.Services.Configure<FileLoggerOptions>(
            builder.Configuration.GetSection(FileLoggerOptions.SectionName));
        builder.Services.Configure<TerminalProxyOptions>(
            builder.Configuration.GetSection(TerminalProxyOptions.SectionName));
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        builder.Services.AddSingleton<ActiveTerminalSessionRegistry>();
        builder.Services.AddSingleton<TerminalWebSocketSessionHandler>();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(_spaCorsPolicyName, policyBuilder =>
            {
                if (allowedSpaOrigins is { Length: > 0 })
                {
                    policyBuilder
                        .WithOrigins(allowedSpaOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                    return;
                }

                policyBuilder
                    .WithOrigins(
                        "http://localhost:5173",
                        "https://localhost:5173",
                        "http://127.0.0.1:5173",
                        "https://127.0.0.1:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        builder.Services.AddTerminalServices(builder.Configuration);
        builder.Services.AddTerminalData(builder.Configuration);

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        var terminalProxyOptions = app.Services
            .GetRequiredService<IOptions<TerminalProxyOptions>>()
            .Value;

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = terminalProxyOptions.WebSocketKeepAliveInterval,
        });

        app.UseHttpsRedirection();
        app.UseCors(_spaCorsPolicyName);
        app.MapAdminSessionEndpoints();

        app.Map(terminalProxyOptions.WebSocketPath, static async context =>
        {
            var handler = context.RequestServices.GetRequiredService<TerminalWebSocketSessionHandler>();
            await handler.HandleAsync(context);
        });

        app.Run();
    }
}
