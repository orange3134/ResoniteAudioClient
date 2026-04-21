namespace AudioClient.Core.Models;

public record WorldInfo(
    string Id,
    string Name,
    string State,
    int UserCount,
    int MaxUserCount,
    string AccessLevel,
    bool IsFocused,
    bool IsHost,
    bool IsUserspace
);
