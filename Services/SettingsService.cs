using System.IO;
using System.Text.Json;
using SshTunnelManager.Models;

namespace SshTunnelManager.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SshTunnelManager", "settings.json");

    public ConnectionSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<ConnectionSettings>(json);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // Keep the app usable if the settings file is malformed or inaccessible.
        }
        return new ConnectionSettings();
    }

    public void Save(ConnectionSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
