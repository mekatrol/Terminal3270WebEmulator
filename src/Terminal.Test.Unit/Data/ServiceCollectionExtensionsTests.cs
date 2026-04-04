using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Terminal.Data.Context;
using Terminal.Data.Extensions;
using Terminal.Data.Models;

namespace Terminal.Test.Unit.Data;

/// <summary>
/// Verifies the terminal data layer registration and schema shape expected by
/// the API host.
/// </summary>
/// <remarks>
/// These tests focus on the composition boundary because the immediate risk is
/// wiring the in-memory database incorrectly and discovering too late that the
/// API cannot resolve the context or persist linked session records.
/// </remarks>
[TestClass]
public sealed class ServiceCollectionExtensionsTests
{
    [TestMethod]
    public async Task AddTerminalData_RegistersInMemoryContextAndPersistsUserSessions()
    {
        var databaseName = $"terminal-data-tests-{Guid.NewGuid():N}";
        var configuration = new ConfigurationManager();
        configuration["TerminalData:DatabaseName"] = databaseName;

        var services = new ServiceCollection();
        services.AddTerminalData(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        await using var context = scope.ServiceProvider.GetRequiredService<TerminalDataContext>();

        var user = new User
        {
            UserId = "00000000-0000-0000-0000-000000000123",
            UserName = "user@example.com",
        };

        var session = new TerminalSession
        {
            TerminalSessionId = Guid.NewGuid(),
            CreatedDateTimeUtc = DateTimeOffset.UtcNow,
            IsActive = true,
            UserId = user.UserId,
        };

        context.Users.Add(user);
        context.TerminalSessions.Add(session);
        await context.SaveChangesAsync();

        var storedSession = await context.TerminalSessions
            .Include(terminalSession => terminalSession.User)
            .SingleAsync();

        Assert.AreEqual("Microsoft.EntityFrameworkCore.InMemory", context.Database.ProviderName);
        Assert.AreEqual("TerminalSessions", context.Model.FindEntityType(typeof(TerminalSession))?.GetTableName());
        Assert.AreEqual("Users", context.Model.FindEntityType(typeof(User))?.GetTableName());
        Assert.IsTrue(storedSession.IsActive);
        Assert.AreEqual(user.UserId, storedSession.UserId);
        Assert.AreEqual(user.UserName, storedSession.User.UserName);
        Assert.IsNull(storedSession.ClosedDateTimeUtc);
    }
}
