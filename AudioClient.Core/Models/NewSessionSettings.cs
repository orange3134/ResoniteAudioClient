namespace AudioClient.Core.Models;

public record NewSessionSettings(
    string SessionName,
    int MaxUsers,
    string AccessLevel,
    string Template, // "AudioClientWorld" | "Grid" | "WorldRecord"
    string? WorldRecordUrl
);
