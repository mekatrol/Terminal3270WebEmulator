using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Terminal.Common.Options;
using Terminal.Common.Services;
using Terminal.Common.Services.Implementation;

namespace Terminal.Common.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers TN3270E terminal services and binds configuration from the
    /// <c>Tn3270E</c> section of <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddTerminalServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<Tn3270EOptions>(
            configuration.GetSection(Tn3270EOptions.SectionName));

        services.AddSingleton<INetworkConnectionFactory, TcpNetworkConnectionFactory>();
        services.AddTransient<ITn3270EService, Tn3270EService>();

        return services;
    }
}
