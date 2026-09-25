using Godot;
using System;

// CharacterStoryTalk : 他ジョブ潜行時の回想（memory）を、一枚絵を起こさずに**会話枠だけ**で流すドライバ。
//
// ── なぜ要るか ──────────────────────────────────────────────────────────
//   2026-09-23 ユーザー指示「潜ったキャラクターとボスとのストーリーを作れ」「イラストがない場所を
//   のちに作らせるから、吹き出しのやり取りだけにして」。他ジョブ潜行（CharacterStory.DiveActive）では
//   回想を Akari/Koharu/ReiStoryFilm（一枚絵アトラス＋時制字幕）で流さず、CharacterStory.Memory の
//   9通り（潜行キャラ×その面のボス）を立ち絵＋吹き出しで送る。ミナ潜行は従来どおりフィルム。
//
// ── 進行の契約（ここが壊れると戦闘が再開しない）──────────────────────────
//   ・呼び元（各 Boss*._Process の _memoryPending 枝）は、フィルムと**同じ completed（ResumeBattle）**を渡す。
//     送り切りでも、Hud が取れない／台詞ゼロの異常でも、かならず一度だけ completed() を呼ぶ。
//   ・会話中は Hud.HoldBubble＝BubblePaused が立ち、弾・敵・スペルタイマーが止まる（改心の会話と同じ経路）。
//     ボス本体の _Process は回り続けるので、呼び元は毎フレーム Update を叩くだけでよい。
//   ・送りは Pad.AdvanceHeld（Z/Enter/ui_accept/Pad A/左クリック）。既読スキップ（Hud.FastForwarding）と
//     自動送り（Hud.AutoAdvance）にも各 Stage*/Boss* の会話と同じ条件で乗る＝QA の自動走行でも詰まらない。
//   ・戦闘の最中に挟む回想なので、会話中は Hud.BattleMemoryTempo を立てる＝文字送りが速く、初見でも
//     Ctrl／RB 押しっぱなしで早送りできる（2026-09-26 作者指摘「まだ戦っている最中なのに長すぎる」。
//     詳細は Hud 側のコメント。実測は tools/MemoryTempoQa.cs）。
public class CharacterStoryTalk
{
    private readonly (int who, string text, string face)[] _lines;
    private readonly Func<Hud?> _hud;
    private readonly Action<Hud, int, string, string> _show;   // (hud, who, text, face) → 面ごとの ShowDialog
    private readonly Action _completed;
    private int _line;
    private double _lineT;
    private bool _zHeld;
    private bool _done;

    public bool Active => !_done;

    private CharacterStoryTalk((int who, string text, string face)[] lines, Func<Hud?> hud,
        Action<Hud, int, string, string> show, Action completed)
    {
        _lines = lines; _hud = hud; _show = show; _completed = completed;
        _zHeld = Pad.AdvanceHeld();   // 直前のフレームで押されていた Z を1行目の送りに使わせない
    }

    // 回想を開始する。会話を出せない状況（Hud が無い／台詞ゼロ）なら**その場で completed** を呼んで null を返す
    //   ＝呼び元が「終わりを待つ相手」を持たないまま戦闘が止まる、を作らない。
    public static CharacterStoryTalk? Start((int who, string text, string face)[] lines, Func<Hud?> hud,
        Action<Hud, int, string, string> show, Action completed)
    {
        var host = hud();
        if (host == null || lines.Length == 0) { completed(); return null; }
        var talk = new CharacterStoryTalk(lines, hud, show, completed);
        host.HoldBubble = true;
        host.BattleMemoryTempo = true;
        talk.ShowLine(host);
        return talk;
    }

    // 毎フレーム呼ぶ。送り切ったフレームで completed() を呼び、以降は何もしない（Active が false になる）。
    public void Update(double delta)
    {
        if (_done) return;
        var hud = _hud();
        if (hud == null) { Finish(null); return; }   // 会話枠が消えた＝待ち続けずに戦闘へ戻す
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        _lineT += delta;
        if (zEdge && _lineT >= 0.25 && !hud.DialogRevealed)
        {
            hud.RevealDialogNow();   // 1段目：まず全文表示（読み飛ばし防止）
            _lineT = 0;
            return;
        }
        if (_lineT < 0.25 || !hud.DialogRevealed) return;
        if (!(zEdge || hud.FastForwarding || (hud.AutoAdvance && _lineT >= 1.4))) return;
        _lineT = 0;
        _line++;
        if (_line >= _lines.Length) { Finish(hud); return; }
        ShowLine(hud);
    }

    private void ShowLine(Hud hud)
    {
        var (who, text, face) = _lines[_line];
        _show(hud, who, text, face);
    }

    private void Finish(Hud? hud)
    {
        _done = true;
        if (hud != null) { hud.HoldBubble = false; hud.BattleMemoryTempo = false; hud.HideBubble(); }
        _completed();
    }
}
