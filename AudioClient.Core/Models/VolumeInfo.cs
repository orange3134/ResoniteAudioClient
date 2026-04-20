namespace AudioClient.Core.Models;

public record VolumeInfo(
    float Master,
    float SoundEffect,
    float Multimedia,
    float Voice,
    float UI
);
