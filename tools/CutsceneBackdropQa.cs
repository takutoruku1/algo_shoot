using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// CutsceneBackdropQa : 回想フィルムの**入りと明けの瞬間**に背後のボス背景が透けないことを検証する。
//
// 何を見ているか（2026-09-22 ユーザー実機指摘「BOSSのステージイラストが選択肢の直前やステージ切り替えで出る」）:
//   StoryFilm はノード全体の Modulate を α 0→1 でフェードする＝レターボックスの黒帯も一緒に透け、
//   背後の StageBackground（EnterBoss 後は Mode.Boss のままボス背景を出し続ける）が丸見えになっていた。
//   CutsceneBackdrop（不透明の黒板）を一段奥に敷いて塞いだので、フェードの途中の**どのフレームでも**
//   戦闘画面が見えないことを画素で確かめる。
//
//   検査は必ず**二本立て**にする（片方だけだと片側の事故を見逃す。どちらも実際にやらかした）:
//     ① 透けていないか … 画面に残る彩度（Colorfulness）。戦闘画面はカラー／memory 回想は完全な無彩色。
//     ② 絵が出ているか … 帯の内側の明暗の開き（PictureContrast）。板が絵まで覆うと真っ黒＝0 に落ちる。
public partial class CutsceneBackdropQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(field, Private)!.SetValue(obj, value);
    private static void Call(object obj, string method, Type? type = null)
        => (type ?? obj.GetType()).GetMethod(method, Private)!.Invoke(obj, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[BackdropQA] PASS {message}");
    }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated user data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/backdrop/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);

            var game = GetNode<GameManager>("/root/Game");
            game.SelectedEntry = GameManager.StageEntry.Boss;
            game.MsgCharsPerSec = 300;
            game.AutoAdvanceDialog = false;
            var root = GD.Load<PackedScene>("res://Rei.tscn").Instantiate<Node2D>();
            await Frames(1);
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            var hud = root.GetNode<Hud>("Hud");
            var world = root.GetNode<Node2D>("World");
            var stage = root.GetNode<Node>("StageRei");
            await Frames(15);
            await AdvanceUntil(() => Read<int>(stage, "_step") == 12);

            // ボス背景を確実に立てる（EnterBoss 済みの状態＝ユーザーが見ている状況を再現する）。
            var bg = GetTree().GetFirstNodeInGroup("stagebg") as StageBackground;
            Check(bg != null, "stage background is present behind the film");
            await Frames(30);
            await Shot("00_battle_background");
            // 戦闘中の背景が出ていること（壊していないこと）の基準値。帯の領域はまだ何も被さっていない。
            var battleFrame = GetViewport().GetTexture().GetImage();
            float battleBand = BandBrightness(battleFrame);
            GD.Print($"[BackdropQA] battle band brightness = {battleBand:0.0000}");
            Check(PictureContrast(battleFrame) > 0.15f, "battle background is actually drawn (baseline for the leak test)");
            float battleColor = Colorfulness(battleFrame);
            GD.Print($"[BackdropQA] battle colorfulness = {battleColor:0.0000}");

            // ───── memory 回想の入り ─────
            bool done = false;
            ReiStoryFilm.Play(hud, world, aftermath: false, completed: () => done = true);
            await Frames(1);
            var film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
            Check(film != null, "memory film started");
            // フェード中の各フレームを刻んで撮る。memory の FadeTime=0.45s ＝ 60fps で約27フレーム
            //   （2026-09-26 に 0.65s から詰めた。aftermath は 0.65s のまま）。45 フレーム見れば明けきった後まで含む。
            //   ★透けの判定は画面の彩度で取る（理由は Colorfulness のコメント）。
            float worst = 0;
            string worstAt = "";
            for (int i = 0; i < 45; i++)
            {
                await Frames(1);
                float like = Colorfulness(GetViewport().GetTexture().GetImage());
                if (like > worst) { worst = like; worstAt = $"fade-in frame {i}"; }
                if (i <= 12 || i is 20 or 38) await Shot($"01_fadein_{i:00}");
            }
            GD.Print($"[BackdropQA] worst colorfulness during fade-in = {worst:0.0000} ({worstAt})");
            // ★帯だけを見ていると「画面が真っ黒」でも合格してしまう（2026-09-22 の回帰＝板が
            //   回想の絵まで覆っていたのを、帯の輝度しか見ていなかったこの QA が見逃した）。
            //   フェードが明けた時点で**絵の領域に中身があること**を必ず併せて確かめる。
            await Frames(30);
            var lit = GetViewport().GetTexture().GetImage();
            await Shot("01_fadein_done");
            GD.Print($"[BackdropQA] picture area contrast = {PictureContrast(lit):0.0000}, brightness = {PictureBrightness(lit):0.0000}");
            Check(PictureContrast(lit) > 0.15f, "flashback illustration is actually visible (not a black screen)");
            // 戦闘画面の彩度の 1/3 を超えて色が戻ったら「カラーの戦闘画面が混ざっている」と判定する。
            Check(worst < battleColor / 3f, "battle screen never shows through during the fade-in");

            // ───── memory 回想の明け ─────
            await AdvanceUntil(() => Read<bool>(film!, "_leaving"));
            worst = 0; worstAt = "";
            for (int i = 0; i < 45 && IsInstanceValid(film); i++)
            {
                await Frames(1);
                float like = Colorfulness(GetViewport().GetTexture().GetImage());
                if (like > worst) { worst = like; worstAt = $"fade-out frame {i}"; }
                if (i is 3 or 10 or 20 or 38) await Shot($"02_fadeout_{i:00}");
            }
            GD.Print($"[BackdropQA] worst colorfulness during fade-out = {worst:0.0000} ({worstAt})");
            Check(worst < battleColor / 3f, "battle screen never shows through during the fade-out");

            await WaitUntil(() => done, 600);
            // 黒板は引ききって消えること（残ると以後の画面が暗いままになる＝回帰の芽）。
            await Frames(40);
            Check(hud.GetNodeOrNull("CutsceneBackdrop") == null, "backdrop removes itself after the film");
            await Frames(20);
            await Shot("03_battle_resumed");
            float resumed = BandBrightness(GetViewport().GetTexture().GetImage());
            GD.Print($"[BackdropQA] battle band brightness after film = {resumed:0.0000}");
            // 戦闘へ戻ったら背景はちゃんと元どおり見えていること（黒板が残って暗転しっぱなしでない）。
            Check(Mathf.Abs(resumed - battleBand) < 0.12f, "battle background returns unchanged after the film");

            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GD.Print("[BackdropQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[BackdropQA] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    // レターボックスの帯（上 y<64/720 ・下 y>=516/720 を実画面比で）の平均輝度。
    //   フィルムが正しく塞いでいれば ≒0。背後が透けていれば背景の明るさが出る。
    private static float BandBrightness(Image image)
    {
        int w = image.GetWidth(), h = image.GetHeight();
        int topEnd = h * 64 / 720;
        int botStart = h * 516 / 720;
        double sum = 0;
        int n = 0;
        for (int y = 4; y < topEnd - 4; y += 3)
            for (int x = 8; x < w - 8; x += 7)
            { var c = image.GetPixel(x, y); sum += (c.R + c.G + c.B) / 3.0; n++; }
        for (int y = botStart + 4; y < h - 4; y += 3)
            for (int x = 8; x < w - 8; x += 7)
            { var c = image.GetPixel(x, y); sum += (c.R + c.G + c.B) / 3.0; n++; }
        return n == 0 ? 0 : (float)(sum / n);
    }

    // 「戦闘画面が透けているか」を名指しで測る指標。
    //
    //   ★ここは metric の選び方を二度間違えたので、経緯を残す（2026-09-22）:
    //     ① 帯の輝度だけを見た → 画面が真っ黒でも合格してしまい、板が回想画まで覆う回帰を見逃した。
    //     ② 戦闘フレームとの RGB 差（一致度）を見た → **暗いフレーム同士は勝手に「似ている」と出る**。
    //        回想画が正しく出ている 0〜3 フレーム目でも 0.90 になり、合否の役に立たなかった。
    //     ③ 左の HUD サイドパネルの明るさを見た → 回想画は全画面を覆うので、ここにも回想画の
    //        明るい部分が来る。フェードが明けた 39 フレーム目が最大 0.226 で戦闘時 0.188 を超えた。
    //   → 明るさ系は全部だめ。**色の有無**で見る。
    //
    // 画面にどれだけ「色」が残っているか 0..1（彩度の平均）。
    //   これが memory 回想の透け判定の決め手になる:
    //   ・戦闘画面（レイの部屋・紫の照明・HUD のガラス板）＝**カラー**。実測 0.05 前後。
    //   ・memory 回想＝シェーダで grayscale=true ＝**完全な無彩色**。実測 0.000x。
    //   フェードの途中で背後の戦闘画面が少しでも混ざれば、その割合ぶん彩度が戻る＝素直に効く。
    //   明るさと違い「回想画が明るいから」で誤検知しない（回想画は明るくても彩度ゼロ）。
    private static float Colorfulness(Image frame)
    {
        int h = frame.GetHeight(), w = frame.GetWidth();
        double sum = 0; int n = 0;
        for (int y = 6; y < h - 6; y += 5)
            for (int x = 6; x < w - 6; x += 7)
            {
                var c = frame.GetPixel(x, y);
                float max = Mathf.Max(c.R, Mathf.Max(c.G, c.B));
                float min = Mathf.Min(c.R, Mathf.Min(c.G, c.B));
                sum += max - min;
                n++;
            }
        return n == 0 ? 0 : (float)(sum / n);
    }

    // 絵の領域（帯の内側 y 64..516）の明暗の開き。回想画が出ていれば大きく、真っ黒なら 0 近く。
    private static float PictureContrast(Image image)
    {
        int h = image.GetHeight();
        float min = 1, max = 0;
        foreach (var v in PictureSamples(image, h)) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
        return max - min;
    }

    private static float PictureBrightness(Image image)
    {
        int h = image.GetHeight();
        double sum = 0; int n = 0;
        foreach (var v in PictureSamples(image, h)) { sum += v; n++; }
        return n == 0 ? 0 : (float)(sum / n);
    }

    private static System.Collections.Generic.IEnumerable<float> PictureSamples(Image image, int h)
    {
        int w = image.GetWidth();
        int top = h * 64 / 720, bottom = h * 516 / 720;
        for (int y = top + 6; y < bottom - 6; y += 5)
            for (int x = 10; x < w - 10; x += 9)
            { var c = image.GetPixel(x, y); yield return (c.R + c.G + c.B) / 3f; }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task WaitUntil(Func<bool> condition, int limit)
    {
        for (int i = 0; i < limit && !condition(); i++) await Frames(1);
        if (!condition()) throw new Exception("Timed out waiting for playback");
    }

    private async Task AdvanceUntil(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            KeyEvent(Key.Z, true);
            await Frames(16);
            KeyEvent(Key.Z, false);
            await Frames(2);
        }
        if (!condition()) throw new Exception("Timed out advancing dialogue");
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
    }
}
