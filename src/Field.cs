using Godot;

// Field : プレイフィールド（弾と自機が動ける矩形）の座標を1か所に集めた定数群。
//   内部解像度 384×216 の中で「盤面はどこか」を定義する唯一の場所。盤面を動かしたい時は
//   ここの Left だけを直す（画面レイアウト刷新の第2段: docs/20260906/HUD整理_案.md §10）。
//   Player の可動域・弾の画面外破棄・ボスの立ち位置・エリアスペルの候補配置・前のめり進行の
//   正規化Xは、すべてこのクラスを見る。384/216 の直書きを盤面の意味で使わないこと。
//   （背景・カットシーン・メニューは画面全体を使う＝Field ではなく 384×216 のまま。）
public static class Field
{
    // 内部座標(384×216)のプレイフィールド矩形。ここだけを直せば盤面が動く。
    public const float Left = 120f; // サイドパネル(0..112)＋額縁の隙間(112..120) の右端＝盤面の左端
    public const float Top = 0f, Right = 384f, Bottom = 216f;
    public const float Width = Right - Left;
    public const float Height = Bottom - Top;
    public const float CenterX = (Left + Right) * 0.5f;
    public const float CenterY = (Top + Bottom) * 0.5f;
    public static Rect2 Rect => new Rect2(Left, Top, Width, Height);

    // 設計座標(1280×720)版（UiKit.BeginDesign 後の HUD 描画用）。
    //   HUD は BeginDesign で 1280×720 に伸ばして描くので、盤面に揃えたい UI はこちらを見る。
    //   DLeft=400 / DCenterX=840 / DWidth=880。中央寄せは 640 ではなく DCenterX を使うこと。
    public const float DLeft = Left / UiKit.Scale;
    public const float DCenterX = CenterX / UiKit.Scale;
    public const float DWidth = Width / UiKit.Scale;
    public const float DRight = DLeft + DWidth;
    public const float PanelW = 373f;   // 設計座標のサイドパネル幅（額縁の隙間 373..400 の左）

    // ── ボスの徘徊ゾーン（BossMover.Configure に渡す既定値）──
    //   従来は「中心X=200 / 半幅=90」の直書きだった。盤面が動いても同じ“やや右寄り・盤面の1/4幅”に
    //   なるよう、中心からのずれと半幅を Field 基準の係数で持つ（Left=0 の今は 200 / 90 に一致する）。
    public const float BossOffsX = 8f;                  // 中心からの右寄せ量（192+8=200）
    // 徘徊半幅は盤面幅に対する比で持つ。2026-09-07 に 90/384(=0.2344) → 0.34 へ広げた。
    //   旧値は盤面が 384px 幅だった頃の「半幅 90px」をそのまま比にしたもので、盤面が 264px に
    //   狭まったあと半幅が 62px＝可動域 124px しか無く、実測でもレイの本体 x が 225..300 の
    //   75px しか動かなかった（ユーザー実機指摘「本戦のボスが動いていない」の可動域側の原因）。
    //   0.34 なら半幅 90px＝可動域 180px で、盤面 264px に対して昔と同じ「盤面の約 2/3」に戻る。
    //   2026-09-08 に 0.34 → 0.42（半幅 111px＝可動域 222px。盤面 264px の 84%）。ユーザー実機指摘
    //   「移動範囲増やして／移動距離が短すぎて固定の場所で撃つだけで倒せる」への横側の手当て。
    public const float BossZoneHalfWK = 0.42f;
    public const float BossZoneHalfW = Width * BossZoneHalfWK;
    public const float BossCenterX = CenterX + BossOffsX;

    // ── ボスの徘徊ゾーン（縦）──
    //   2026-09-08 まで、三ボスとも Configure に「中心 y=70 / 半高 28」を直書きしていた。可動域は
    //   y 42..98 の 56px＝盤面 216px の 26% しかなく（上から4分の1の帯）、実機で「ボスが同じ高さ
    //   しか移動しない＝固定の場所で撃ってるだけで倒せてしまう」と指摘された。横（BossZoneHalfW）と
    //   同じく盤面高さに対する比で持ち、盤面が動いても腐らないようにする。
    //
    //   中心 0.46（y=99.4）／半高 0.225（48.6px）＝ y 50.8..148。上下は物理的な制約で決めた:
    //     上限 y=51  … ボスの絵は表示高 56px の中央揃え＝頭が y=23。HUD のボスバーが設計座標
    //                  y=8..52（内部座標 2.4..15.6）なので、頭とバーの間に 7px 残る。
    //                  （最初 0.44/0.235＝上限 y=44 で組んだが、実測スクショで頭が
    //                   スペル宣告カードへ食い込んだ。上限を 7px 下げて余白を作った。）
    //     下限 y=148 … 盤面高さの 69%。自機は盤面のどこへでも行けるが実戦では下側に居るため、
    //                  これ以上下げるとボス本体（縦半径 23px）と自機が重なる理不尽が増える。
    public const float BossZoneCenterYK = 0.46f;
    public const float BossZoneHalfHK = 0.225f;
    public const float BossZoneCenterY = Top + Height * BossZoneCenterYK;   // = 99.36
    public const float BossZoneHalfH = Height * BossZoneHalfHK;             // = 48.6
}
