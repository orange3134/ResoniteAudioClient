namespace AudioClient.Core.Models;

public record ContactInfo(
    string Username,
    string UserId,
    string OnlineStatus,
    string? CurrentSessionName,
    string? CurrentSessionHost,
    int CurrentSessionUsers,
    int CurrentSessionMaxUsers
);

public record ContactSessionMeta(
    bool IsHost,
    bool IsHidden,
    string AccessLevel
);
