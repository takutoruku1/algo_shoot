using Godot;

// AudioConfig : 音量設定の単一窓口。設定画面(Settings)・起動時(Audio)・ポーズメニュー(PauseMenu)が共有する。
//   user://settings.json のフラットな {key: 0..100} を読み書きし、対応バスへ反映する。
//   これにより「設定画面を開かないと音量が効かない／再起動で戻る」を解消し、
//   ポーズ画面からも同じ値をいじれる（一つの真実）。
//   キー→バス: master→Master, bgm→Music, se→SE, voice→Voice, amb→Amb。
public static class AudioConfig
{
    private const string SettingsPath = "user://settings.json";

    // 音量キー → (バス名, 既定値)。Settings.cs の Apply/BuildDefaults と一致させること。
    public static readonly (string Key, string Bus, float Def)[] Volumes =
    {
        ("master", "Master", 80f),
        ("bgm",    "Music",  70f),
        ("se",     "SE",     85f),
        ("voice",  "Voice",  90f),
        ("amb",    "Amb",    55f),
    };

    // 0..100 をバス音量へ（0.5以下は実質ミュート）。Settings.SetBusDb と同一カーブ。
    public static void SetBus(string bus, float f)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i >= 0) AudioServer.SetBusVolumeDb(i, f <= 0.5f ? -80f : Mathf.LinearToDb(f / 100f));
    }

    // 保存済み音量を全バスへ適用（起動時に1回。Audio._Ready から呼ぶ）。未保存キーは既定値。
    public static void ApplySaved()
    {
        var data = Read();
        foreach (var (key, bus, def) in Volumes)
            SetBus(bus, GetF(data, key, def));
    }

    // 1キーの現在値（保存値 or 既定）。
    public static float Get(string key)
    {
        var data = Read();
        foreach (var (k, _, def) in Volumes)
            if (k == key) return GetF(data, key, def);
        return 0f;
    }

    // 1キーを設定＝即バス反映＋他キー（表示/操作など）を保ったまま保存。
    public static void Set(string key, float f)
    {
        foreach (var (k, bus, _) in Volumes)
            if (k == key) { SetBus(bus, f); break; }
        var data = Read();
        data[key] = f;
        Write(data);
    }

    private static float GetF(Godot.Collections.Dictionary d, string key, float def)
        => d.ContainsKey(key) ? (float)d[key].AsDouble() : def;

    private static Godot.Collections.Dictionary Read()
    {
        if (!FileAccess.FileExists(SettingsPath)) return new Godot.Collections.Dictionary();
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
        if (f == null) return new Godot.Collections.Dictionary();
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return new Godot.Collections.Dictionary();
        return json.Data.AsGodotDictionary();
    }

    private static void Write(Godot.Collections.Dictionary data)
    {
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
        f?.StoreString(Json.Stringify(data));
    }
}
