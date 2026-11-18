using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// FilmSkipShot : 一度見たムービーのスキップUI（ヒントと充填バー）を撮るだけの小さなハーネス。
//
//   なぜ専用ハーネスが要るか: FilmSkip は --shot のとき**スキップを完全に無効化**する
//   （撮影中に自動操縦の入力で飛ばされないための Epilogue/ChoiceOverlay と同じ作法）。
//   よって素の `--shot` では既読セーブでもヒントが出ない。ここでは Shot オートロードを使わず、
//   自分で既読フラグを立て、ビューポートを直接 PNG に落とす。
//
// 起動: res://tools/qa_film_skip_shot.tscn -- --out <絶対パス>
public partial class FilmSkipShot : Node
{
    private string _out = "";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            var args = OS.GetCmdlineUserArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--out") _out = args[i + 1];
            if (_out.Length == 0) _out = ProjectSettings.GlobalizePath("res://build/qa_film_skip/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));

            var game = GetNode<GameManager>("/root/Game");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            game.SelectedEntry = GameManager.StageEntry.Boss;
            // 「一度見た」状態を作る＝ヒントが出る条件。
            FilmSkip.MarkSeen(game, "akari_memory");
            FilmSkip.MarkSeen(game, $"mina_phase_1_{game.JobDef.CharacterId}");

            var root = GD.Load<PackedScene>("res://Akari.tscn").Instantiate<Node2D>();
            await Frames(1);
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var hud = root.GetNode<Hud>("Hud");
            var world = root.GetNode<Node2D>("World");
            root.GetNode<Node>("StageAkari").SetProcess(false);
            root.SetProcess(false);
            world.ProcessMode = ProcessModeEnum.Inherit;
            await Frames(10);

            // ①回想：ヒントだけ（押していない状態）
            AkariStoryFilm.Play(hud, world, aftermath: false, completed: () => { });
            await Frames(120);
            GetViewport().WarpMouse(FilmSkip.ButtonRect.GetCenter() * UiKit.Scale);
            await Frames(3);
            await Save("story_hint");
            // ②回想：長押しの途中＝充填バーが伸びている状態
            await MouseHoldShot((Node2D)GetTree().GetFirstNodeInGroup("storyfilm"), "story_holding");
            await Frames(3);
            foreach (Node n in GetTree().GetNodesInGroup("storyfilm")) n.QueueFree();
            await Frames(10);

            // ③FINAL のフェーズ間：ヒント
            MinaPhaseScene.Play(hud, world, 1, () => { });
            await Frames(120);
            await Save("phase_hint");
            await MouseHoldShot((Node2D)GetTree().GetFirstNodeInGroup("mina_phase_scene"), "phase_holding");
            await Frames(3);
            foreach (Node n in GetTree().GetNodesInGroup("mina_phase_scene")) n.QueueFree();
            await Frames(10);

            GD.Print($"[FilmSkipShot] done -> {_out}");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[FilmSkipShot] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task Save(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
        GD.Print($"[FilmSkipShot] {name}.png");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task MouseHoldShot(Node2D scene, string name)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        scene.SetProcess(false);
        typeof(Pad).GetField("_usingMouse", flags)!.SetValue(null, true);
        typeof(Pad).GetField("_mousePos", flags)!.SetValue(null, FilmSkip.ButtonRect.GetCenter());
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, false);
        scene._Process(0.01);
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, true);
        scene._Process(0.22);
        await Save(name);
        typeof(Pad).GetField("_mL", flags)!.SetValue(null, false);
        scene._Process(0.01);
        scene.SetProcess(true);
    }
}
