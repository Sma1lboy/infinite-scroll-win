using System.IO;
using System.Text.Json;
using InfiniteScroll.Models;

namespace InfiniteScroll.Services;

public static class PersistenceManager
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".infinite-scroll");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PersistenceManager: failed to save — {ex.Message}");
        }
    }

    public static AppState? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PersistenceManager: failed to load — {ex.Message}");
            return null;
        }
    }
}
