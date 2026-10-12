using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevReel.Models;

namespace RevReel.Services
{
    /// <summary>Loads and saves settings.json in %AppData%\RevReel.</summary>
    public static class SettingsService
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevReel",
            "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Returns saved settings, or defaults if there are none or the file is unreadable.</summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        return Sanitize(loaded);
                    }
                }
            }
            catch
            {
                // A damaged settings file must never stop the app starting.
            }

            return new AppSettings();
        }

        /// <summary>Saves settings. Returns false if the file could not be written.</summary>
        public static bool Save(AppSettings settings)
        {
            try
            {
                string? folder = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static AppSettings Sanitize(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OutputFolder))
            {
                settings.OutputFolder = AppSettings.DefaultOutputFolder;
            }

            if (settings.Framerate != 30 && settings.Framerate != 60)
            {
                settings.Framerate = 30;
            }

            if (!Enum.IsDefined(settings.OutputSize))
            {
                settings.OutputSize = OutputSizePreset.Automatic;
            }

            return settings;
        }
    }
}
