using Godot;

// TintLift : 各ステージ Root の CanvasModulate（夜の冷色 Tint）を、盤面のうち「暗いと読めなくて困るもの」
//   だけ打ち消すための共通道具。BossGauge／BubbleLayer が個別に持っていた逆数補正を、自機・敵にも
//   使えるよう一箇所へまとめた。
//
//   なぜ要るか：Tint は CanvasModulate＝その世界のキャンバス全体に掛かる乗算なので、盤面のスプライトは
//   例外なく沈む。あかりの Cold=(0.60, 0.68, 0.92) は赤を 4 割・緑を 3 割落とす＝自機の当たり判定の芯が
//   青く濁って、夜の背景に溶ける。ノード側に 1/Tint を掛けておけば乗算が相殺され、昼の素材そのままの
//   色で出る（乗算は float のまま出力まで届くので逆数で元の色に戻る＝BossGauge と同じ理屈）。
//
//   2026-09-27 作者指摘とその改訂：
//     「ステージにもよるんだけど、自機や敵が暗い時がある／少なくとも自機の当たり判定は分かるよう明るい方がいい」
//     → 追加指示「自キャラ／敵もどのステージでも明るくしてほしい」
//   で、自機本体・敵本体・当たり判定の芯・シールドの泡はいずれも **完全に（強さ 1.0）** 打ち消すと決まった。
//   いったん検討した中間案（自機は Tint と白の中間 0.5／敵は 0.3 だけ持ち上げる）は却下＝
//   「キャラと敵だけが背景から明るく浮く」見え方を正とする。夜の空気は背景・ScrollFx・WorldGrade・
//   MurkVignette が担い、投稿チップ（Panel）・演出粒・吹き出しは従来どおり Tint を受けたまま＝触らない。
public static class TintLift
{
    // 打ち消しの強さ（1=Tint を完全に打ち消す／0=素のまま沈む）。呼び出し側の意図をこの3つに集約する。
    // 今はすべて 1.0（上記の作者指示）。将来「敵だけ少し夜に残す」等を試すならここだけ動かせば全ステージに効く。
    public const float PlayerBody = 1f;   // 自機の本体スプライト（Player の "Sprite"）
    public const float PlayerCore = 1f;   // 当たり判定の芯＋シールドの泡（PlayerHitDot）
    public const float EnemyBody = 1f;    // 敵の本体スプライト（ザコ・中ボス・ボス共通。Enemy の "Body"）

    // 祖先をたどって「その世界の Tint」を探す（各 Root の "Tint" / Main の "WorldTint"）。
    //   BossGauge._Ready と同じ探し方＝自分の親から上へ、各階層の子に居る CanvasModulate を拾う。
    //   見つからない場面（MinaBattle＝FINAL は Tint を置かない、カットシーン等）は null＝補正不要。
    public static CanvasModulate? Find(Node from)
    {
        for (Node? n = from.GetParent(); n != null; n = n.GetParent())
            foreach (var c in n.GetChildren())
                if (c is CanvasModulate cm) return cm;
        return null;
    }

    // Tint を strength ぶん打ち消す乗算色。SelfModulate / Modulate にそのまま入れる。
    //   α は常に 1＝被弾点滅（Player.Modulate のα）や退場フェード（Enemy.Modulate のα）、
    //   差し替えクロスフェード（Enemy の SelfModulate のα）には一切触らない。
    //   1/c は c が 0 に近いと発散するので 0.05 で床を張る（BossGauge と同じ）。
    //   Tint が既に白い（浄化しきった終盤・現実面の暴露後・Tint 無しの面）なら (1,1,1)＝素通り。
    public static Color Of(CanvasModulate? tint, float strength)
    {
        if (tint == null || !GodotObject.IsInstanceValid(tint) || !tint.Visible) return Colors.White;
        var c = tint.Color;
        return new Color(Ch(c.R, strength), Ch(c.G, strength), Ch(c.B, strength), 1f);
    }

    // 1チャンネルぶん。strength=1 で 1/c（完全打ち消し）、0 で 1（素のまま）。
    //   間は線形補間で、これは「見た目の Tint を Tint と白の中間まで持ち上げる」と同義
    //   （Lerp(1, 1/c, k) == Lerp(c, 1, k) / c）。強さの意味が両方の言い方で一致する。
    private static float Ch(float c, float k) => Mathf.Lerp(1f, 1f / Mathf.Max(c, 0.05f), k);
}
