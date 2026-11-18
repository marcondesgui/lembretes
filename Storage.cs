using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lembretes;

public class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 440;
    public bool Topmost { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public bool Compact { get; set; }
    public bool HideHintShown { get; set; }
}

/// <summary>Dados ficam em %APPDATA%\Lembretes.</summary>
public static class Storage
{
    public static readonly string Dir =
        Environment.GetEnvironmentVariable("LEMBRETES_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lembretes");

    private static readonly string ItemsFile = Path.Combine(Dir, "lembretes.json");
    private static readonly string SettingsFile = Path.Combine(Dir, "config.json");
    private static readonly string LogFile = Path.Combine(Dir, "erros.log");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static List<Reminder> LoadReminders() => Load<List<Reminder>>(ItemsFile) ?? new List<Reminder>();
    public static AppSettings LoadSettings() => Load<AppSettings>(SettingsFile) ?? new AppSettings();
    public static void SaveReminders(IEnumerable<Reminder> items) => Save(ItemsFile, items.ToList());
    public static void SaveSettings(AppSettings s) => Save(SettingsFile, s);

    private static T? Load<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opts);
        }
        catch (Exception ex)
        {
            Log($"Falha ao ler {path}: {ex.Message}");
            try { File.Copy(path, path + ".corrompido.bak", true); } catch { /* ignora */ }
            return default;
        }
    }

    private static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Opts));
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            Log($"Falha ao salvar {path}: {ex.Message}");
        }
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { /* ignora */ }
    }
}
