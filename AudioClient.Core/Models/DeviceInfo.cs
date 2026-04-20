namespace AudioClient.Core.Models;

public record DeviceInfo(
    int Index,
    string Name,
    bool IsActive,
    bool IsConnected
);
