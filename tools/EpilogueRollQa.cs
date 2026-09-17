using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// E7 スタッフロールの「うっかりスキップ」回帰テスト。
//   旧実装は開始1秒後の単押し(Z)で END へ飛び、末尾の締めくくり三要素
//   （「そして、ご主人様へ。」／送った言葉の大きい表示／「Thank you for playing.」）が
//   通常プレイでほぼ確実に失われていた。ここを固定する:
//     1. 単押しを何度繰り返してもロールは飛ばない
//     2. 長押し(RetryHold.HoldTime)なら飛ばせる
//     3. 最後まで流せば末尾三要素が実際に画面へ出る（スクショで確認）
public partial class EpilogueRollQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _out = "";
    private static T Read<T>(object obj, string field)
        => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value)
        => obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[RollQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            _out = ProjectSettings.GlobalizePath("res://build/qa_story/roll/shots");
            DirAccess.MakeDirRecursiveAbsolute(_out);
            GetNode<GameManager>("/root/Game").LastSentWord = "おかえり";
            await Frames(2);

            await VerifyTapDoesNotSkip();
            await VerifyHoldSkips();
            await VerifyTailRendersToTheEnd();

            GD.Print("[RollQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[RollQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    // ロール(PhRoll)状態の Epilogue を作る（前半の会話と EndingFilm は飛ばして直接入る）。
    private async Task<Epilogue> EnterRoll()
    {
        var ep = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
        GetTree().Root.AddChild(ep);
        GetTree().CurrentScene = ep;
        await Frames(4);
        Write(ep, "_phase", 2);   // PhRoll
        Write(ep, "_t", 0.0);
        Write(ep, "_rollSkipArmed", false);
        await Frames(2);
        return ep;
    }

    private static float RollSpeed
        => (float)typeof(Epilogue).GetField("RollSpeed", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static float RollLineH
        => (float)typeof(Epilogue).GetField("RollLineH", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static float RollEnd(Epilogue ep)
        => (216f + Read<string[]>(ep, "_roll").Length * RollLineH + 24f) / RollSpeed;

    // 1. 単押し（1フレームだけ押して離す）を繰り返してもロールは飛ばない。
    private async Task VerifyTapDoesNotSkip()
    {
        var ep = await EnterRoll();
        Check(Mathf.Abs(RollSpeed - 20f) < 0.001f, $"roll scrolls at 20px/sec (was 24)");
        float rollEnd = RollEnd(ep);
        // 職種を8セクションへ割った拡充版（2026-09-17）で約35秒→約72秒。尺は行数から自動で伸びるので、
        //   ここは「締めくくりが読める長さがあり、かつ延々と続かない」上下の歯止めだけを見る。
        Check(rollEnd > 60 && rollEnd < 90, $"roll runs about 72s ({rollEnd:0.00}s)");

        for (int i = 0; i < 40; i++)
        {
            KeyDown(Key.Z, true);
            await Frames(2);            // 約0.033秒＝長押ししきい(0.45s)に遠く届かない単押し
            KeyDown(Key.Z, false);
            await Frames(3);
        }
        Check(Read<int>(ep, "_phase") == 2, "repeated taps never skip the staff roll");
        Check(Read<double>(ep, "_t") < rollEnd, "roll is still playing after the taps");
        GD.Print($"[RollQA] after 40 taps: t={Read<double>(ep, "_t"):0.00}s phase={Read<int>(ep, "_phase")}");
        ep.QueueFree();
        await Frames(3);
    }

    // 2. 押し続ければ（RetryHold.HoldTime=0.45s）意図どおり飛ばせる。
    private async Task VerifyHoldSkips()
    {
        var ep = await EnterRoll();
        KeyDown(Key.Z, false);
        await Frames(3);                // 一度離して武装させる
        KeyDown(Key.Z, true);
        await Frames(12);               // 0.2秒＝しきい未満。まだ飛ばない
        Check(Read<int>(ep, "_phase") == 2, "a short hold under the threshold does not skip yet");
        Check(Read<RetryHold>(ep, "_rollSkip").Progress > 0, "the hold shows fill progress while pressed");
        await Frames(30);               // 合わせて0.7秒＝しきい超え
        Check(Read<int>(ep, "_phase") == 3, "holding the advance button does skip the roll");
        KeyDown(Key.Z, false);
        ep.QueueFree();
        await Frames(3);
    }

    // 3. 最後まで流し、末尾三要素が実際に描画されることをスクショで確認する。
    //    各行が画面中央に来る時刻へ _t を進めて撮る（描画は _t からの一意関数なので再現する）。
    private async Task VerifyTailRendersToTheEnd()
    {
        var ep = await EnterRoll();
        var roll = Read<string[]>(ep, "_roll");
        string sent = GetNode<GameManager>("/root/Game").LastSentWord;
        int iThanks = Array.IndexOf(roll, "Thank you for playing.");
        int iMaster = Array.IndexOf(roll, "そして、ご主人様へ。");
        int iSent = Array.IndexOf(roll, sent);
        Check(iMaster >= 0 && iSent > iMaster && iThanks > iSent, "the roll ends with master line, sent word, thanks");
        Check(Read<string>(ep, "_rollLast") == sent, "the sent word is the climax-sized line");

        // 「行 i が画面中央(y=108)に来る」時刻。DrawStaffroll の y = H + i*RollLineH - t*RollSpeed。
        double Center(int i) => (216 + i * RollLineH - 108) / RollSpeed;
        foreach (var (index, name) in new[] { (iMaster, "tail1_master"), (iSent, "tail2_sentword"), (iThanks, "tail3_thanks") })
        {
            Write(ep, "_t", Center(index));
            ep.QueueRedraw();
            await Frames(2);
            using var image = await Shot(name);
            Check(HasInk(image), $"{name} draws visible text at screen centre (t={Center(index):0.0}s)");
        }

        // 末尾の行が中央を過ぎてもロールはまだ終わっていない＝「Thank you」が読める尺がある。
        Check(Center(iThanks) < RollEnd(ep), "the final line is readable before the roll ends");

        // 締めくくりに入ったらスキップのヒントは消えている（最後の一行と重ならない）。
        Write(ep, "_t", Center(iThanks));
        ep.QueueRedraw();
        await Frames(2);
        using (var clean = await Shot("tail3_thanks_no_hint"))
            Check(!HasInkInBand(clean, 690, 716), "the skip hint is gone once the closing lines play");
        GD.Print($"[RollQA] tail times: master={Center(iMaster):0.0}s sent={Center(iSent):0.0}s "
            + $"thanks={Center(iThanks):0.0}s rollEnd={RollEnd(ep):0.0}s");

        // 放っておけば（入力なしで）自動的に END へ落ちる。
        Write(ep, "_t", RollEnd(ep) - 0.05);
        await Frames(10);
        Check(Read<int>(ep, "_phase") == 3, "the roll finishes on its own and reaches the ending scene");
        ep.QueueFree();
        await Frames(3);
    }

    // 画面中央の帯に「背景より明るい文字」が載っているか（ロール本文の存在確認）。
    private static bool HasInk(Image image) => HasInkInBand(image, 320, 400);

    private static bool HasInkInBand(Image image, int top, int bottom)
    {
        int bright = 0;
        for (int y = top; y < bottom; y++)
            for (int x = 120; x < 1160; x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.R + c.G + c.B > 1.8f) bright++;
            }
        GD.Print($"[RollQA]   bright pixels in band y={top}..{bottom}: {bright}");
        return bright > 200;
    }

    private async Task<Image> Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_out}/{name}.png") == Error.Ok, $"screenshot {name}");
        return image;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void KeyDown(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });
}
