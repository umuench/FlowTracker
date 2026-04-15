using FlowTracker.Domain;
using System.IO;
using System.Text.Json;

namespace FlowTracker.Services;

public sealed class RadialMenuService(string menuFilePath)
{
    private readonly string _menuFilePath = menuFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<RadialMenuItemDefinition> LoadOrCreate()
    {
        try
        {
            var directory = Path.GetDirectoryName(_menuFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_menuFilePath))
            {
                var json = File.ReadAllText(_menuFilePath);
                var loaded = JsonSerializer.Deserialize<List<RadialMenuItemDefinition>>(json, JsonOptions);
                if (loaded is { Count: > 0 })
                {
                    return loaded;
                }
            }

            var defaults = BuildDefaults();
            File.WriteAllText(_menuFilePath, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }
        catch
        {
            return BuildDefaults();
        }
    }

    private static List<RadialMenuItemDefinition> BuildDefaults() =>
    [
        new() { Label = "Arbeitsbeginn", ActionKey = "start_work", ColorHex = "#1F8A4C" },
        new() { Label = "Arbeitsende", ActionKey = "end_work", ColorHex = "#B42318" },
        new()
        {
            Label = "Pause",
            ActionKey = "pause",
            ColorHex = "#B54708",
            Children =
            [
                new() { Label = "Kurzpause", ActionKey = "break_flexible", ColorHex = "#D97706" },
                new() { Label = "Mittag", ActionKey = "break_lunch", ColorHex = "#CA8A04" },
                new() { Label = "Weiterarbeiten", ActionKey = "resume_work", ColorHex = "#0369A1" }
            ]
        },
        new()
        {
            Label = "Projekt",
            ActionKey = "project",
            ColorHex = "#1D4ED8",
            Children =
            [
                new() { Label = "Projekt A", ActionKey = "project:Projekt A", ColorHex = "#2563EB" },
                new() { Label = "Projekt B", ActionKey = "project:Projekt B", ColorHex = "#3B82F6" },
                new() { Label = "Projekt C", ActionKey = "project:Projekt C", ColorHex = "#60A5FA" }
            ]
        },
        new()
        {
            Label = "Grund",
            ActionKey = "reason",
            ColorHex = "#6D28D9",
            Children =
            [
                new() { Label = "Admin", ActionKey = "reason:Admin", ColorHex = "#7C3AED" },
                new() { Label = "Meeting", ActionKey = "reason:Meeting", ColorHex = "#8B5CF6" },
                new() { Label = "Support", ActionKey = "reason:Support", ColorHex = "#A78BFA" }
            ]
        }
    ];
}
