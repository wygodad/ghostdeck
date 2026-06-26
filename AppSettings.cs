using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MSIProfileSwitcher;

public sealed class HotkeyDef
{
    public uint Mods { get; set; }
    public uint Vk { get; set; }
    public string Display { get; set; } = "";

    [JsonIgnore] public bool IsSet => Vk != 0;

    public HotkeyDef Clone() => new() { Mods = Mods, Vk = Vk, Display = Display };
}

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public Dictionary<string, HotkeyDef> Hotkeys { get; set; } = new();
    public Dictionary<string, string> Colors { get; set; } = new();   // klucz profilu -> hex
    public bool Autostart { get; set; }

    public bool AutoSwitchEnabled { get; set; } = false;              // domyslnie OFF (nie gryzc sie z MSI)
    public string ProfileOnAC { get; set; } = "Balanced";
    public string ProfileOnBattery { get; set; } = "Silent";

    public int ChargeLimit { get; set; } = 0;                          // 0 = nie zmieniaj; inaczej 60/80/100
    public bool StatusOnTop { get; set; } = false;                     // okno Status "zawsze na wierzchu"
    public bool ExperimentalEnabled { get; set; } = false;             // pozwol na zapis dla modeli Experimental

    [JsonIgnore]
    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSIProfileSwitcher");
    [JsonIgnore]
    public static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) { s.EnsureDefaults(); return s; }
            }
        }
        catch { }
        var def = new AppSettings();
        def.EnsureDefaults();
        return def;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { }
    }

    public void EnsureDefaults()
    {
        const uint CA = Hk.MOD_CONTROL | Hk.MOD_ALT;
        void Def(string k, uint vk, string disp)
        {
            if (!Hotkeys.ContainsKey(k))
                Hotkeys[k] = new HotkeyDef { Mods = CA, Vk = vk, Display = disp };
        }
        Def("Silent",       0x70, "Ctrl+Alt+F1");
        Def("Balanced",     0x71, "Ctrl+Alt+F2");
        Def("Extreme",      0x72, "Ctrl+Alt+F3");
        Def("SuperBattery", 0x73, "Ctrl+Alt+F4");
        Def("Cycle",        0x50, "Ctrl+Alt+P");
    }

    public Color ColorFor(ProfileId id)
    {
        var def = Profiles.Get(id);
        if (Colors.TryGetValue(def.Key, out var hex) && !string.IsNullOrWhiteSpace(hex))
        {
            try { return ColorTranslator.FromHtml(hex); } catch { }
        }
        return def.DefaultColor;
    }

    public AppSettings Clone()
    {
        var c = new AppSettings
        {
            Language = Language,
            Autostart = Autostart,
            AutoSwitchEnabled = AutoSwitchEnabled,
            ProfileOnAC = ProfileOnAC,
            ProfileOnBattery = ProfileOnBattery,
            ChargeLimit = ChargeLimit,
            StatusOnTop = StatusOnTop,
            ExperimentalEnabled = ExperimentalEnabled,
        };
        foreach (var (k, v) in Hotkeys) c.Hotkeys[k] = v.Clone();
        foreach (var (k, v) in Colors) c.Colors[k] = v;
        return c;
    }
}
