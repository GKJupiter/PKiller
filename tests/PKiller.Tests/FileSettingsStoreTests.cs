using PKiller.Core.Abstractions;
using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class FileSettingsStoreTests
{
    [Fact]
    public void Load_RestoresCustomColumnOrder()
    {
        string directory = CreateSettingsDirectory();
        try
        {
            var store = new FileSettingsStore(directory);
            var expectedOrder = new[]
            {
                TableColumn.Name,
                TableColumn.Memory,
                TableColumn.Cpu,
                TableColumn.Read,
                TableColumn.Write,
                TableColumn.Actions,
            };
            store.Save(new AppSettings(SortOrder.Default, true, expectedOrder));

            AppSettings loaded = store.Load();

            Assert.Equal(expectedOrder, loaded.ColumnOrder);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_UsesDefaultColumnOrderForLegacySettings()
    {
        string directory = CreateSettingsDirectory();
        try
        {
            var store = new FileSettingsStore(directory);
            File.WriteAllText(
                store.SettingsPath,
                """{"SortMetric":2,"SortDirection":1,"AutoRefresh":false}""");

            AppSettings loaded = store.Load();

            Assert.Equal(AppSettings.Default.ColumnOrder, loaded.ColumnOrder);
            Assert.False(loaded.AutoRefresh);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateSettingsDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PKillerSettingsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
