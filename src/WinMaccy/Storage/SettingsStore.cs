using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMaccy.Models;

namespace WinMaccy.Storage;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON next to the history file.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(StoragePaths.SettingsFile))
            {
                var json = File.ReadAllText(StoragePaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null) Current = settings;
            }
        }
        catch
        {
            Current = new AppSettings(); // corrupt settings must never kill the app
        }
        Current.HotKey ??= new HotKeyConfig();
    }

    public void Save()
    {
        lock (_gate)
        {
            StoragePaths.EnsureCreated();
            var tmp = StoragePaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tmp, StoragePaths.SettingsFile, overwrite: true);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        Save();
    }
}
