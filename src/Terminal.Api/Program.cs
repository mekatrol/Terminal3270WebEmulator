using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Terminal.Api.Admin;
using Terminal.Api.Logging;
using Terminal.Api.Options;
using Terminal.Api.WebSockets;
using Terminal.Common.Extensions;
using Terminal.Data.Extensions;

public partial class Program
{
    private const string _spaCorsPolicyName = "TerminalSpa";
    private const string _terminalUserPolicyName = "TerminalUser";
    private const string _terminalAdminPolicyName = "TerminalAdmin";

    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var allowedSpaOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();
        var authenticationOptions = builder.Configuration
            .GetSection(OidcAuthenticationOptions.SectionName)
            .Get<OidcAuthenticationOptions>()
            ?? new OidcAuthenticationOptions();
        var configuredWebSocketPath = builder.Configuration["TerminalProxy:WebSocketPath"] ?? "/ws/terminal";

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
        builder.Services.Configure<OidcAuthenticationOptions>(
            builder.Configuration.GetSection(OidcAuthenticationOptions.SectionName));
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
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtBearerOptions =>
            {
                jwtBearerOptions.Authority = authenticationOptions.Authority;
                jwtBearerOptions.Audience = authenticationOptions.Audience;
                jwtBearerOptions.RequireHttpsMetadata = authenticationOptions.RequireHttpsMetadata;
                jwtBearerOptions.MapInboundClaims = false;
                jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles",
                    ValidateIssuer = true,
                    ValidateAudience = true,
                };
                jwtBearerOptions.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (!string.IsNullOrWhiteSpace(context.Token))
                        {
                            return Task.CompletedTask;
                        }

                        if (context.HttpContext.WebSockets.IsWebSocketRequest &&
                            context.Request.Path.StartsWithSegments(configuredWebSocketPath) &&
                            context.Request.Query.TryGetValue("access_token", out var accessToken))
                        {
                            context.Token = accessToken.ToString();
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(_terminalUserPolicyName, policyBuilder =>
            {
                policyBuilder.RequireAuthenticatedUser();
                policyBuilder.RequireAssertion(context =>
                    context.User.IsInRole(authenticationOptions.TerminalUserRole) ||
                    context.User.IsInRole(authenticationOptions.TerminalAdminRole));
            })
            .AddPolicy(_terminalAdminPolicyName, policyBuilder =>
            {
                policyBuilder.RequireAuthenticatedUser();
                policyBuilder.RequireRole(authenticationOptions.ServerAdminRole);
            });

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
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/api/app-config", () => Results.Json(new
        {
            terminalEndpointDisplayName = terminalProxyOptions.TerminalEndpointDisplayName,
        }));
        app.MapAdminSessionEndpoints().RequireAuthorization(_terminalAdminPolicyName);

        app.Map(terminalProxyOptions.WebSocketPath, static async context =>
        {
            var handler = context.RequestServices.GetRequiredService<TerminalWebSocketSessionHandler>();
            await handler.HandleAsync(context);
        }).RequireAuthorization(_terminalUserPolicyName);

        app.Run();
    }
}
