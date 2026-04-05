using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Terminal.Data.Context;

namespace Terminal.Api.Admin;

/// <summary>
/// Maps HTTP endpoints used by the administrative session management surface in
/// the SPA.
/// </summary>
internal static class AdminSessionEndpoints
{
    /// <summary>
    /// Registers the admin session endpoints on the supplied route builder.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The mapped route group for further configuration.</returns>
    public static RouteGroupBuilder MapAdminSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/admin/sessions");

        group.MapGet(string.Empty, GetSessionsAsync);
        group.MapPost("/terminate", TerminateSessionsAsync);
        group.MapPost("/clear", ClearSessionsAsync);

        return group;
    }

    private static async Task<Ok<AdminSessionListResponse>> GetSessionsAsync(
        TerminalDataContext terminalDataContext,
        CancellationToken cancellationToken)
    {
        var sessions = await terminalDataContext.TerminalSessions
            .AsNoTracking()
            .Include(session => session.User)
            .OrderByDescending(session => session.CreatedDateTimeUtc)
            .Select(session => new AdminSessionSummary(
                session.TerminalSessionId,
                session.CreatedDateTimeUtc,
                session.IsActive,
                session.ClosedDateTimeUtc,
                session.UserId,
                session.User.UserName))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AdminSessionListResponse(sessions));
    }

    private static async Task<Ok<AdminSessionActionResponse>> TerminateSessionsAsync(
        AdminSessionSelectionRequest request,
        TerminalDataContext terminalDataContext,
        ActiveTerminalSessionRegistry sessionRegistry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selectedSessionIds = request.SessionIds
            .Distinct()
            .ToArray();

        if (selectedSessionIds.Length == 0)
        {
            return TypedResults.Ok(new AdminSessionActionResponse(
                0,
                0,
                0,
                0,
                "No sessions were selected."));
        }

        var sessions = await terminalDataContext.TerminalSessions
            .Where(session => selectedSessionIds.Contains(session.TerminalSessionId))
            .ToListAsync(cancellationToken);

        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var session in sessions)
        {
            if (!session.IsActive)
            {
                skippedCount++;
                continue;
            }

            if (!await sessionRegistry.TryRequestTerminationAsync(session.TerminalSessionId))
            {
                session.IsActive = false;
                session.ClosedDateTimeUtc = DateTimeOffset.UtcNow;
            }

            updatedCount++;
        }

        await terminalDataContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AdminSessionActionResponse(
            selectedSessionIds.Length,
            updatedCount,
            0,
            skippedCount,
            updatedCount == 0
                ? "No active sessions were terminated."
                : $"Termination requested for {updatedCount} session(s)."));
    }

    private static async Task<Ok<AdminSessionActionResponse>> ClearSessionsAsync(
        AdminSessionSelectionRequest request,
        TerminalDataContext terminalDataContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selectedSessionIds = request.SessionIds
            .Distinct()
            .ToArray();

        if (selectedSessionIds.Length == 0)
        {
            return TypedResults.Ok(new AdminSessionActionResponse(
                0,
                0,
                0,
                0,
                "No sessions were selected."));
        }

        var sessions = await terminalDataContext.TerminalSessions
            .Where(session => selectedSessionIds.Contains(session.TerminalSessionId))
            .ToListAsync(cancellationToken);

        var skippedCount = 0;
        var removedCount = 0;

        foreach (var session in sessions)
        {
            if (session.IsActive)
            {
                skippedCount++;
                continue;
            }

            terminalDataContext.TerminalSessions.Remove(session);
            removedCount++;
        }

        await terminalDataContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AdminSessionActionResponse(
            selectedSessionIds.Length,
            0,
            removedCount,
            skippedCount,
            removedCount == 0
                ? "No inactive session entries were cleared."
                : $"Cleared {removedCount} session entr{(removedCount == 1 ? "y" : "ies")}."));
    }
}
