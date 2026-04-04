namespace Terminal.Api.Admin;

internal sealed record AdminSessionSummary(
    Guid TerminalSessionId,
    DateTimeOffset CreatedDateTimeUtc,
    bool IsActive,
    DateTimeOffset? ClosedDateTimeUtc,
    string UserId,
    string UserName);

internal sealed record AdminSessionListResponse(IReadOnlyList<AdminSessionSummary> Sessions);

internal sealed record AdminSessionSelectionRequest(IReadOnlyList<Guid> SessionIds);

internal sealed record AdminSessionActionResponse(
    int SelectedCount,
    int UpdatedCount,
    int RemovedCount,
    int SkippedCount,
    string Message);
