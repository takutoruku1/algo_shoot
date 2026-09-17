using Godot;
using System.Reflection;
using System.Threading.Tasks;

// BossCardShot : ボスHPカード（Hud.DrawBossCard）のアバター＝Xアイコン表示の見た目確認用スクショ取り（2026-09-17）。
//   実戦に到達させると各ボスまで数分かかるので、ステージを1つ立てて Hud だけを直接叩き、
//   ・4本ボス（あかり／こはる／レイ／ミナ=FINAL）＋中ボス（カメオ）の handle でカードを出す
//   ・改心（HideBossBar）後の「穢れが晴れる」経過も撮る
//   を狙い撃ちで撮る。
//   実行: Godot --headless --path . res://tools/qa_boss_card.tscn -- --qa --card-out <絶対パス>
public partial class BossCardShot : Node
{
    private const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;

    private string _outDir = "user://shots";
    private Hud _hud = null!;

    public override async void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
            if (user[i] == "--card-out" && i + 1 < user.Length) _outDir = user[i + 1];
        DirAccess.MakeDirRecursiveAbsolute(_outDir);

        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent(); game.AutoSaveEnabled = false;
        await Frames(2);

        var scene = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(scene);
        GetTree().CurrentScene = scene;
        await Frames(30);

        _hud = FindNode<Hud>(scene)!;
        (scene.GetType().GetProperty("Stage")!.GetValue(scene) as Node)?.SetProcess(false);
        _hud.HoldBubble = false; _hud.HideBubble();
        await Frames(4);

        // 本ボス4種＋中ボス3種。名前/ハンドルは各 ShowBossBar 呼び出し元の実値をそのまま使う。
        (string name, string handle, string file)[] cases =
        {
            ("あふれるわたし",  BossHandles.AkariBar,   "boss_akari"),
            ("我に返るわたし",  BossHandles.KoharuMain, "boss_koharu"),
            ("星逢レイ",        BossHandles.ReiMain,    "boss_rei"),
            ("穢れたわたし",    BossHandles.MinaBattle, "boss_mina_final"),
            ("レイ",            BossHandles.ReiCameo,   "cameo_rei"),
            ("あかり",          BossHandles.AkariBar,   "cameo_akari"),
            ("こはる",          BossHandles.KoharuCameo,"cameo_koharu"),
        };

        foreach (var (name, handle, file) in cases)
        {
            _hud.ShowBossBar(name, handle);
            _hud.UpdateBossBar(2, 4, 0.62f);
            await Frames(10);
            Capture(file);
        }

        // 改心の見送り（穢れ→浄化）を時系列で撮る。HideBossBar から 0.75s で穢れが剥がれ、
        //   さらに 0.45s でカードが引く。0.0 / 0.35 / 0.75 / 1.0 秒あたりを拾う。
        _hud.ShowBossBar("星逢レイ", BossHandles.ReiMain);
        _hud.UpdateBossBar(0, 4, 0.04f);
        await Frames(10);
        Capture("purify_00_kegare");
        _hud.HideBossBar();
        await Frames(20); Capture("purify_01_mid");    // 約0.33s
        await Frames(25); Capture("purify_02_clear");  // 約0.75s（剥がれきり）
        await Frames(15); Capture("purify_03_fade");   // カードが引く途中

        GD.Print("[bosscard] done.");
        GetTree().Quit();
    }

    private void Capture(string name)
    {
        var img = GetTree().Root.GetTexture().GetImage();
        string path = $"{_outDir}/{name}.png";
        img.SavePng(path);
        GD.Print($"[bosscard] saved {path}");
    }

    private static T? FindNode<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) { var r = FindNode<T>(c); if (r != null) return r; }
        return null;
    }

    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
