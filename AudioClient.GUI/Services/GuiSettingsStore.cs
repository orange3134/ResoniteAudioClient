using System;
using System.IO;
using System.Text.Json;

namespace AudioClient.GUI.Services;

public static class GuiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioClient");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "gui-settings.json");

    public static GuiSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new GuiSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<GuiSettings>(json) ?? new GuiSettings();
        }
        catch
        {
            return new GuiSettings();
        }
    }

    public static void Save(GuiSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}

public sealed class GuiSettings
{
    public string? ResoniteInstallPath { get; set; }
    public bool AutoEquipAudioClientAvatar { get; set; } = true;
}
