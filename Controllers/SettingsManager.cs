using System;
using System.IO;
using System.Text.Json;
using System.ComponentModel;

namespace Project_FREAK.Controllers;

public class SettingsManager : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? AppliedSettingsChanged;

    // Paths
    private static readonly string SavedSettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Assets", "CONFIG", "saved_settings.json");

    private static readonly string AppliedSettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Assets", "CONFIG", "applied_settings.json");

    // Settings data
    private SettingsData _appliedSettings = new();
    public SettingsData AppliedSettings
    {
        get => _appliedSettings;
        private set
        {
            _appliedSettings = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppliedSettings)));
        }
    }

    public SettingsManager()
    {
        LoadSavedSettings();
        LoadAppliedSettings();
    }

    public void LoadSavedSettings()
    {
        try
        {
            if (File.Exists(SavedSettingsPath))
            {
                var json = File.ReadAllText(SavedSettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                AppliedSettings = settings ?? new SettingsData();
            }
        }
        catch { /* Handle errors */ }
    }

    public void SaveAppliedSettingsToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(AppliedSettings);
            Directory.CreateDirectory(Path.GetDirectoryName(SavedSettingsPath)!);
            File.WriteAllText(SavedSettingsPath, json);
        }
        catch { /* Handle errors */ }
    }

    public void UpdateAppliedSettings(SettingsData newSettings)
    {
        AppliedSettings = newSettings;
        AppliedSettingsChanged?.Invoke(this, EventArgs.Empty);

        // Save applied settings to session file
        try
        {
            var json = JsonSerializer.Serialize(newSettings);
            Directory.CreateDirectory(Path.GetDirectoryName(AppliedSettingsPath)!);
            File.WriteAllText(AppliedSettingsPath, json);
        }
        catch { /* Handle errors */ }
    }

    public void LoadAppliedSettings()
    {
        try
        {
            if (File.Exists(AppliedSettingsPath))
            {
                var json = File.ReadAllText(AppliedSettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                AppliedSettings = settings ?? new SettingsData();
            }
        }
        catch { /* Handle errors */ }
    }

    public class SettingsData
    {
        public bool DemoModeEnabled { get; set; }
    }
}