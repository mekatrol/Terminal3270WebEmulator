namespace Terminal.Api.WebSockets;

internal sealed record TerminalSessionReadyMessage(
    string Type,
    string Host,
    int Port,
    string TerminalType,
    string? DeviceName,
    TimeSpan SessionLifetime);

internal sealed record TerminalSessionErrorMessage(string Type, string Message);

internal sealed record TerminalSessionEndedMessage(
    string Type,
    string Reason,
    string? TerminalEndpointDisplayName);
