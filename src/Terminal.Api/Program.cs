using Terminal.Api.Logging;
using Terminal.Common.Extensions;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

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
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        builder.Services.AddTerminalServices(builder.Configuration);

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.Run();
    }
}
