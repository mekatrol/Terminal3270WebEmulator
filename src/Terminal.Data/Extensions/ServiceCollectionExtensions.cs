using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Terminal.Data.Context;
using Terminal.Data.Options;

namespace Terminal.Data.Extensions;

/// <summary>
/// Registers the terminal data layer with the application's dependency
/// injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the terminal <see cref="TerminalDataContext"/> and binds the
    /// associated in-memory database options from configuration.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">
    /// The host configuration root used to bind the <c>TerminalData</c>
    /// settings section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// This mirrors the repository's extension-based registration pattern so the
    /// API host depends on the data layer through a single composition entry
    /// point rather than duplicating persistence setup in <c>Program.cs</c>.
    /// </remarks>
    public static IServiceCollection AddTerminalData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<TerminalDataOptions>(
            configuration.GetSection(TerminalDataOptions.SectionName));

        services.AddDbContext<TerminalDataContext>((serviceProvider, options) =>
        {
            var terminalDataOptions = serviceProvider
                .GetRequiredService<IOptions<TerminalDataOptions>>()
                .Value;

            var databaseName = string.IsNullOrWhiteSpace(terminalDataOptions.DatabaseName)
                ? "TerminalSessions"
                : terminalDataOptions.DatabaseName;

            options.UseInMemoryDatabase(databaseName);
        });

        return services;
    }
}
