namespace AudioClient.Core.Models;

public record WorldInfo(
    string Id,
    string Name,
    string State,
    int UserCount,
    string AccessLevel,
    bool IsFocused,
    bool IsHost,
    bool IsUserspace
);
