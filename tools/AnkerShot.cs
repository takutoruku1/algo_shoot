using Godot;
using System.Reflection;
using System.Threading.Tasks;

// AnkerShot : アンチャー紹介（StageTutorial の ③）が実際に会話枠へ出ている絵を撮る（2026-09-17）。
//   実行: Godot --path . res://tools/qa_anker_shot.tscn -- --qa --anker-out <絶対パス>
public partial class AnkerShot : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _outDir = "user://shots";

    public override async void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
            if (user[i] == "--anker-out" && i + 1 < user.Length) _outDir = user[i + 1];
        DirAccess.MakeDirRecursiveAbsolute(_outDir);

        var game = GetNode<GameManager>("/root/Game");
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
        await Frames(2);

        var scene = GD.Load<PackedScene>("res://Akari.tscn").Instantiate();
        GetTree().Root.AddChild(scene);
        GetTree().CurrentScene = scene;
        await Frames(40);

        var stage = (Node)scene.GetType().GetProperty("Stage")!.GetValue(scene)!;
        var hud = (Hud)scene.GetType().GetProperty("Hud")!.GetValue(scene)!;
        var lines = (System.ValueTuple<int, string, string>[])
            stage.GetType().GetField("_playerIntro", Private)!.GetValue(stage)!;
        for (int k = 3; k >= 1; k--)   // 末尾3行＝アンチャー紹介
        {
            int idx = lines.Length - k;
            stage.GetType().GetField("_introLine", Private)!.SetValue(stage, idx);
            stage.GetType().GetMethod("ShowLine", Private)!.Invoke(stage, new object[] { lines });
            // 実プレイの Step_Lines は HoldBubble=true で送り待ちにする。ここは ShowLine を直に叩くので
            //   自分で立てないと自動消灯のタイマーで消える（撮影harness側の都合＝ゲーム挙動ではない）。
            hud.HoldBubble = true;
            await Frames(26);
            var img = GetViewport()?.GetTexture()?.GetImage();
            string p = $"{_outDir.TrimEnd('/')}/anker_akari_{4 - k}.png";
            GD.Print($"[ankershot] {p} line='{lines[idx].Item2}' err={img?.SavePng(p)}");
        }
        GetTree().Quit();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
