using System.Collections.Generic;

namespace AudioClient.Core.Models;

public record ActiveSessionInfo(
    string Name,
    string HostUsername,
    int JoinedUsers,
    int MaximumUsers,
    string AccessLevel,
    string PreferredUrl,
    IReadOnlyList<string> SessionUsers
);
