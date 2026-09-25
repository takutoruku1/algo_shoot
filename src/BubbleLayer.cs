using Godot;

// BubbleLayer : 戦闘中の会話の吹き出しを、弾と自機・敵より**奥**に描く世界側の層（2026-09-26）。
//
//   作者指摘：中ボス戦・ボス戦で、ボスの一行字幕とキャラの会話枠が自機と敵／味方の弾を隠す。
//   Hud は CanvasLayer＝常に最前面なので、そこに描く限り文字枠は弾の上に乗る（FuryDial の冒頭と同じ理由）。
//   → 吹き出しの**描画だけ**を、盤面の世界座標（384×216）に置いたこの Node2D へ移す。
//     状態（行・話者・顔・タイマー）は従来どおり Hud が持ち、ここは毎フレーム Hud.DrawBubbles を呼ぶだけ
//     ＝状態を二重に持たない。会話送り・BubblePaused・既読スキップの挙動は一切変えない。
//   移すもの：会話バー／ナレ箱（DrawDialog）・ボスと中ボスの一行字幕（DrawBossLine）・スペル宣告カード（DrawSpellCard）。
//   カットシーン（Hud.CinematicMode）は弾が無く、映像側の層（-10..2）が世界に居るので、従来どおり Hud が前面で描く。
//
//   ── レイヤー ─────────────────────────────────────────────────
//   盤面の Z は 背景層 -95..-88 / ScrollFx -70..-55 / StageImagery -50 / WorldGrade -48,-47 / MurkVignette -45 /
//   FuryDial -44 / QuoteStorm -14 / 投稿チップ -12 / 浄化の飛散物・スコア花びら -6 / AreaStrike -2 / 敵の体 -1 /
//   弾 0 / 自機 10。
//   → この層は **ZIndex=-7**：文字の背景（引用・投稿チップ）より手前、ゲームに関わるもの（花びら・演出粒・
//     床マーカー・敵・弾・自機）はすべて手前に残す。-6 と同値にすると花びらとの前後が木の順で決まるので 1 つ下げた。
//
//   ── 「奥に回った」以外の変化を出さないために ─────────────────────
//   ・座標：Hud と同じ UiKit.BeginDesign（設計 1280×720 → ×0.3）で描く＝位置・大きさ・フォントは Hud に居た時のまま。
//   ・カメラ：世界は GameCamera のシェイク（Offset）で動くので、毎フレーム canvas transform の逆を自分の
//     Transform に置いて画面に固定する（被弾で吹き出しが揺れない）。GameCamera の _Process より後に走らせる。
//   ・色味：各 Root は世界の CanvasModulate（冷→暖の Tint）で盤面全体を染めるが、CanvasLayer の Hud は
//     染まらない。ここに移した文字が青く沈まないよう、Tint の逆数を SelfModulate に置いて打ち消す。
public partial class BubbleLayer : Node2D
{
    public Hud Hud = null!;
    private CanvasModulate? _tint;   // 同じ Root に居る世界の色味（無ければ打ち消し不要）

    public override void _Ready()
    {
        ZIndex = -7;                  // 花びら・演出粒(-6) の1つ奥・投稿チップ(-12) の手前
        ZAsRelative = false;
        ProcessPriority = 100;        // GameCamera（Offset 更新）より後＝同じフレームの揺れを打ち消す
        foreach (var n in GetParent().GetChildren())
            if (n is CanvasModulate cm) { _tint = cm; break; }
        Hud.Bubbles = this;
    }

    public override void _ExitTree()
    {
        if (Hud != null && Hud.Bubbles == this) Hud.Bubbles = null;
    }

    public override void _Process(double delta)
    {
        // 世界がカメラで揺れても、この層は画面に固定する。
        Transform = GetViewport().CanvasTransform.AffineInverse();
        // 世界の色味（Tint）を打ち消す。乗算は float のまま出力まで届くので逆数で元の色に戻る。
        if (_tint != null && _tint.Visible)
        {
            var c = _tint.Color;
            SelfModulate = new Color(1f / Mathf.Max(c.R, 0.05f), 1f / Mathf.Max(c.G, 0.05f), 1f / Mathf.Max(c.B, 0.05f), 1f);
        }
        QueueRedraw();
    }

    public override void _Draw() => Hud?.DrawBubbles(this);
}
