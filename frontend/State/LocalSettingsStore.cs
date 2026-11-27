using System.IO;
using System.Text.Json;

namespace ClimaPulse.Desktop.State;

public static class LocalSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "ClimaPulse",
        "settings.json");

    public static async Task<int?> LoadLastProfileIdAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(SettingsPath);

            var settings = JsonSerializer.Deserialize<LocalSettings>(json);

            return settings?.LastProfileId;
        }
        catch
        {
            return null;
        }
    }

    public static async Task SaveLastProfileIdAsync(int profileId)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

            var json = JsonSerializer.Serialize(
                new LocalSettings(profileId));

            await File.WriteAllTextAsync(SettingsPath, json);
        }
        catch
        {
            // The profile can still be used when local settings cannot be saved.
        }
    }

    private sealed record LocalSettings(int LastProfileId);
}