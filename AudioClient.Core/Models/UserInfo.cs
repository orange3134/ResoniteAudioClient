namespace AudioClient.Core.Models;

public record UserInfo(
    string UserName,
    string? UserId,
    bool IsHost,
    bool IsLocal,
    bool IsPresentInWorld,
    int Ping,
    bool IsContact,
    string? IconUrl = null
);
