using System.Text.Json;
using PKiller.Core.Abstractions;
using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>Remembers per-user process table preferences between launches.</summary>
public sealed class FileSettingsStore : ISettingsStore
{
    private readonly string _settingsPath;

    public FileSettingsStore(string? directory = null)
    {
        var root = directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PKiller");
        Directory.CreateDirectory(root);
        _settingsPath = System.IO.Path.Combine(root, "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return AppSettings.Default;
            }

            var stored = JsonSerializer.Deserialize<StoredSettings>(json);
            if (stored is null)
            {
                return AppSettings.Default;
            }

            return new AppSettings(
                new SortOrder((SortMetric)stored.SortMetric, (SortDirection)stored.SortDirection),
                stored.AutoRefresh,
                GetColumnOrder(stored.ColumnOrder));
        }
        catch (Exception)
        {
            // A corrupt settings file must not stop the app from launching.
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var stored = new StoredSettings(
            (int)settings.Sort.Metric,
            (int)settings.Sort.Direction,
            settings.AutoRefresh,
            settings.ColumnOrder.Select(column => (int)column).ToArray());
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(stored));
        }
        catch (Exception)
        {
            // Preferences are best-effort; the app still runs with the in-memory values.
        }
    }

    private static IReadOnlyList<TableColumn> GetColumnOrder(int[]? values)
    {
        if (values is null
            || values.Length != AppSettings.Default.ColumnOrder.Count
            || values.Any(value => !Enum.IsDefined(typeof(TableColumn), value))
            || values.Distinct().Count() != values.Length)
        {
            return AppSettings.Default.ColumnOrder;
        }

        return values.Select(value => (TableColumn)value).ToArray();
    }

    private sealed record StoredSettings(
        int SortMetric,
        int SortDirection,
        bool AutoRefresh,
        int[]? ColumnOrder = null);
}
