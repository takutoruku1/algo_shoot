using Godot;
using System;

// FuryMeterQa : 【激情】メーター（src/FuryMeter.cs・第1段）の数値まわりをヘッドレスで確かめる。
//   画面は撮らない（表示の確認は build/shots_fury/*.png を目視）。
//   (a) 何も選ばずに来たら初期値は中央 0（＝道中を踏んでいないランで勝手に傾かない）
//   (b) 道中を最大限「上げる」側で選ぶと ＋側、最大限「下げる」側で選ぶと −側
//   (c) どう選んでも初期値は ±InitialClamp（=30）を超えない＝道中だけで端（±90）に届かない
//   (d) Begin→Tick で値がプラス方向へ上がり、±100 で止まる
//   (e) End したら以後は動かない（面を抜けた後にメーターが進まない）
//   (f) BandOf の境目が +90 / -90（第2段のエンディング分岐がここを読む前提）
public partial class FuryMeterQa : Node
{
    private int _pass;
    private void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        _pass++;
        GD.Print($"[FuryQA] PASS {message}");
    }

    // 台帳に1件積む（others/迷い秒数は初期値の計算に使わないので空・0でよい）。
    private static void Put(GameManager g, string id, string chosen)
        => g.RecordChoice(id, chosen, Array.Empty<string>(), 0f);

    public override void _Ready()
    {
        try
        {
            var game = GetNodeOrNull<GameManager>("/root/Game");
            Check(game != null, "GameManager autoload is present");

            // ── (a) 何も選んでいないラン ──
            foreach (string id in new[] { "akari", "koharu", "rei" })
                Check(Mathf.IsEqualApprox(Fury.InitialFor(game, id), Fury.Center),
                    $"{id}: no choices -> center ({Fury.Center})");

            // ── (b) 上げ切り ──
            Put(game!, "p4", "だれの声");
            Put(game!, "s1_4", "十二件、ぜんぶ");
            Put(game!, "s1_5", "傘、忘れてる");
            Put(game!, "s1_2", "置き傘、三本目");
            Put(game!, "s2_4", "返してない返信");
            Put(game!, "s3_2", "同接、9");
            Put(game!, "s3_5c", "見ています");
            float up1 = Fury.InitialFor(game, "akari");
            float up2 = Fury.InitialFor(game, "koharu");
            float up3 = Fury.InitialFor(game, "rei");
            Check(up1 > 0 && up2 > 0 && up3 > 0, $"max-up choices lean + (akari={up1} koharu={up2} rei={up3})");

            // ── (c) 上限クランプ ──
            Check(up1 <= Fury.InitialClamp && up2 <= Fury.InitialClamp && up3 <= Fury.InitialClamp,
                $"max-up stays within +{Fury.InitialClamp}");
            Check(up1 < Fury.RageEdge && up3 < Fury.RageEdge, "max-up never reaches the rage edge (+90)");

            // ── (b') 下げ切り（全部「送らない」＝台帳に空文字で積まれる）──
            foreach (string id in new[] { "p4", "s1_4", "s1_5", "s1_2", "s2_2", "s2_4", "s3_2", "s3_5c" })
                Put(game!, id, "");
            Put(game!, "p4", "見なかったことにする");
            float dn1 = Fury.InitialFor(game, "akari");
            float dn2 = Fury.InitialFor(game, "koharu");
            float dn3 = Fury.InitialFor(game, "rei");
            Check(dn1 < 0 && dn2 < 0 && dn3 < 0, $"skip-everything leans − (akari={dn1} koharu={dn2} rei={dn3})");
            Check(dn1 >= -Fury.InitialClamp && dn2 >= -Fury.InitialClamp && dn3 >= -Fury.InitialClamp,
                $"skip-everything stays within −{Fury.InitialClamp}");
            Check(dn1 > Fury.NumbEdge && dn2 > Fury.NumbEdge && dn3 > Fury.NumbEdge,
                "skip-everything never reaches the numb edge (−90)");

            // ── (d) 自然上昇と端での頭打ち ──
            game!.BeginFury("akari");
            float start = game.FuryValue;
            game.AddFury(10f);
            Check(game.FuryValue > start, "AddFury(+) raises the value while active");
            game.AddFury(9999f);
            Check(Mathf.IsEqualApprox(game.FuryValue, Fury.Max), $"clamps at +{Fury.Max}");
            game.AddFury(-9999f);
            Check(Mathf.IsEqualApprox(game.FuryValue, Fury.Min), $"clamps at {Fury.Min}");

            // ── (e) End 後は動かない ──
            game.EndFury();
            float frozen = game.FuryValue;
            game.AddFury(50f);
            Check(Mathf.IsEqualApprox(game.FuryValue, frozen), "AddFury does nothing after EndFury");
            Check(!game.FuryActive, "FuryActive is down after EndFury");

            // ── (f) 帯の境目 ──
            Check(Fury.BandOf(Fury.RageEdge) == Fury.Band.Rage && Fury.BandOf(Fury.RageEdge - 0.1f) == Fury.Band.Calm,
                "BandOf switches to Rage exactly at +90");
            Check(Fury.BandOf(Fury.NumbEdge) == Fury.Band.Numb && Fury.BandOf(Fury.NumbEdge + 0.1f) == Fury.Band.Calm,
                "BandOf switches to Numb exactly at −90");
            Check(Fury.BandOf(Fury.Center) == Fury.Band.Calm, "center (0) is Calm");

            GD.Print($"[FuryQA] ALL PASS ({_pass} checks)");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FuryQA] FAIL {e.Message}");
            GetTree().Quit(1);
            return;
        }
        GetTree().Quit(0);
    }
}
