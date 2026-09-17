using Godot;
using System.Reflection;
using System.Threading.Tasks;

// TutorialCardShot : 道中チュートリアルの「操作カード」（ControlCard・2026-09-17）の見た目確認用スクショ取り。
//   STAGE1（Akari.tscn）を立てて初見チュートリアル本文を流し、話題が切り替わる各行で
//   ルートビューポートを PNG 保存する。--shot オートロード（時刻指定）と違い「行」で狙い撃てる。
//   実行: Godot --headless --path . res://tools/qa_tutorial_card.tscn -- --qa \
//           --card-out <絶対パスの出力ディレクトリ>
public partial class TutorialCardShot : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private string _outDir = "user://shots";
    private Hud _hud = null!;
    private Node _stage = null!;

    public override async void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
            if (user[i] == "--card-out" && i + 1 < user.Length) _outDir = user[i + 1];
        DirAccess.MakeDirRecursiveAbsolute(_outDir);

        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent();                       // once_tutorial_route を未読に戻す＝道中チュートリアルが出る
        await Frames(2);

        var scene = GD.Load<PackedScene>("res://Akari.tscn").Instantiate();
        GetTree().Root.AddChild(scene);
        GetTree().CurrentScene = scene;
        await Frames(40);

        _hud = scene.GetNode<Hud>("Hud");
        _stage = scene.GetNode("StageAkari");
        var lines = (System.ValueTuple<int, string, string>[])
            _stage.GetType().GetField("_playerIntro", Private)!.GetValue(_stage)!;
        GD.Print($"[cardshot] intro lines = {lines.Length}");

        // 話題が変わる行だけ撮る。添字は「道中チュートリアル16行の先頭」からの相対位置。
        //   2026-09-17: チュートリアルの後ろにアンチャー紹介（StageTutorial の ③）が繋がったので、
        //   末尾からの決め打ち（lines.Length - 16）では 3 行ぶんズレる。本文の先頭行を実際に探す。
        int tut = System.Array.FindIndex(lines, l => l.Item2.StartsWith("偽りの世界"));
        if (tut < 0) { GD.PushWarning("[cardshot] 道中チュートリアルが見つからない（once 消費済み？）"); GetTree().Quit(1); return; }
        (int line, string name)[] shots =
        {
            (tut + 3,  "00_before"),   // 4行目（コア/LIFE）＝カード無しの素の会話
            (tut + 4,  "01_move"),     // 5行目 移動
            (tut + 5,  "02_shot"),     // 6行目 撃つ
            (tut + 8,  "03_lock"),     // 9行目 ロックオン送り
            (tut + 10, "04_lockclear"),// 11行目 ロックオン解除（2026-09-17 追加）
            (tut + 11, "05_bomb"),     // 12行目 ボム
            (tut + 13, "06_after"),    // 14行目 心の欠片＝カードが畳まれている
        };

        foreach (var (line, name) in shots)
        {
            SetLine(line);
            await Frames(60);          // フェード（0.22s）＋入替（0.16s）が終わるまで待つ
            Capture(name);
        }
        GD.Print("[cardshot] done.");
        GetTree().Quit();
    }

    // 会話を任意の行へ飛ばす（_introLine を書いて ShowLine を呼び直す＝Z連打より速くて確実）。
    private void SetLine(int index)
    {
        var lines = _stage.GetType().GetField("_playerIntro", Private)!.GetValue(_stage)!;
        _stage.GetType().GetField("_introLine", Private)!.SetValue(_stage, index);
        _stage.GetType().GetMethod("ShowLine", Private)!.Invoke(_stage, new[] { lines });
    }

    private void Capture(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.PushWarning("[cardshot] viewport image unavailable"); return; }
        string path = $"{_outDir.TrimEnd('/')}/tutorial_card_{name}.png";
        GD.Print($"[cardshot] saved {path} ({img.GetWidth()}x{img.GetHeight()}) err={img.SavePng(path)}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
