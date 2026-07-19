using Godot;

// BossTuning : ボスのバランス値を INI（config/boss_stats.ini）から読む静的ヘルパ。
//   セクション＝ボスごと（[rei] [akari] [koharu] [mina] [hikage] [cameo]）。Godot 標準の ConfigFile で読む。
//   使い方（各ボスの OnEnemyReady / AreaSpellCaster.Configure）:
//     PanelCount = BossTuning.I("rei", "panel_count", 5);   // 第3引数＝現行ハードコード値（フォールバック）
//
// 読み込みチェーン（後勝ち＝上書き。起動後の初回参照で一度だけ読む）:
//   1) res://config/boss_stats.ini …… 既定（リポジトリ管理・エクスポートにも同梱）
//   2) プロジェクトルート直下の boss_stats.ini …… エディタ実行時のみ（試し書き用・任意）
//   3) exe 隣接の boss_stats.ini …… 配布ビルドで開発メンバーが「置くだけ」で上書き調整できる
//
// フォールバック必須の設計：ファイル欠落・キー欠落・型不正でも呼び出し側の既定値で動く。
//   ConfigFile.Load は失敗しても Error を返すだけ（例外なし）。値の型が合わない時もキー単位で
//   既定値へ落とす＝起動不能・実行時例外は絶対に出さない。
public static class BossTuning
{
    private static ConfigFile? _cfg;
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _cfg = new ConfigFile();
        // 1) 既定（res://）。無ければそのまま＝全キーがフォールバックで動く。
        Merge("res://config/boss_stats.ini");
        // 2) エディタ実行時のみ：プロジェクトルート直下（素の ini を直接編集して試す用）。
        if (OS.HasFeature("editor"))
            Merge(ProjectSettings.GlobalizePath("res://boss_stats.ini"));
        // 3) exe 隣接（配布ビルドの上書き。メンバーが ini を置くだけで調整できる）。
        string exeDir = OS.GetExecutablePath().GetBaseDir();
        if (!string.IsNullOrEmpty(exeDir))
            Merge(exeDir.PathJoin("boss_stats.ini"));
    }

    // path の ini を読み、読めたキーだけ上書きマージする（読めなければ何もしない＝前段の値が残る）。
    private static void Merge(string path)
    {
        var f = new ConfigFile();
        if (f.Load(path) != Error.Ok) return;
        foreach (string sec in f.GetSections())
            foreach (string key in f.GetSectionKeys(sec))
                _cfg!.SetValue(sec, key, f.GetValue(sec, key));
    }

    // float 取得。int 表記（8 等）も受ける。キー欠落・型不正は fallback。
    public static float F(string section, string key, float fallback)
    {
        EnsureLoaded();
        Variant v = _cfg!.GetValue(section, key, fallback);
        return v.VariantType switch
        {
            Variant.Type.Float => (float)v.AsDouble(),
            Variant.Type.Int => v.AsInt32(),
            _ => fallback,
        };
    }

    // int 取得。float 表記（5.0 等）は丸めて受ける。キー欠落・型不正は fallback。
    public static int I(string section, string key, int fallback)
    {
        EnsureLoaded();
        Variant v = _cfg!.GetValue(section, key, fallback);
        return v.VariantType switch
        {
            Variant.Type.Int => v.AsInt32(),
            Variant.Type.Float => Mathf.RoundToInt((float)v.AsDouble()),
            _ => fallback,
        };
    }

    // bool 取得。true/false のほか 0/1 の int 表記も受ける。キー欠落・型不正は fallback。
    public static bool B(string section, string key, bool fallback)
    {
        EnsureLoaded();
        Variant v = _cfg!.GetValue(section, key, fallback);
        return v.VariantType switch
        {
            Variant.Type.Bool => v.AsBool(),
            Variant.Type.Int => v.AsInt32() != 0,
            _ => fallback,
        };
    }
}
