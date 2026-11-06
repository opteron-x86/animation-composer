using System.Diagnostics;
using System.Text.Json;

namespace MovesetForge.Desktop;

internal sealed record TableColumnLayout(double Name, double Actors, double Priority, bool FitName, double TableWidth)
{
    public bool IsValid() => LayoutPreferences.IsDimension(Name) && LayoutPreferences.IsDimension(Actors)
        && LayoutPreferences.IsDimension(Priority) && LayoutPreferences.IsDimension(TableWidth);
}

internal sealed record LayoutPreferences
{
    public int Version { get; init; } = 1;
    public double WindowWidth { get; init; }
    public double WindowHeight { get; init; }
    public bool Maximized { get; init; }
    public double[]? PaneWidths { get; init; }
    public double[]? PreviewRowHeights { get; init; }
    public double[]? ProjectRowHeights { get; init; }
    public TableColumnLayout? AddedSetColumns { get; init; }

    internal static bool IsDimension(double value) => value is > 0 and <= 1_000_000;

    private static string? SettingsFile()
    {
        var folder = OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData;
        var root = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        return Path.IsPathFullyQualified(root) ? Path.Combine(root, "AnimationComposer", "layout.json") : null;
    }

    public static LayoutPreferences? Load()
    {
        try
        {
            var file = SettingsFile();
            if (file == null || !File.Exists(file)) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var layout = JsonSerializer.Deserialize<LayoutPreferences>(stream);
            return layout is { Version: 1 } ? layout : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning($"Could not read the window layout: {ex.Message}");
            return null;
        }
    }

    public void Save()
    {
        try
        {
            var file = SettingsFile();
            if (file == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    JsonSerializer.Serialize(stream, this, new JsonSerializerOptions { WriteIndented = true });
                File.Move(temporary, file, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not save the window layout: {ex.Message}");
        }
    }
}
