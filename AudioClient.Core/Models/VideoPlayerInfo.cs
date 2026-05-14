namespace AudioClient.Core.Models;

public record VideoPlayerInfo(
    string Id,
    string SlotName,
    string Title,
    string Url,
    string PlaybackUrl,
    bool IsPlaying,
    bool IsLooping,
    float Position,
    double ClipLength,
    float Speed);
