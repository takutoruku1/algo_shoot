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
    public const float Left = 0f;   // ← 第3段でサイドパネルぶん右へ寄せる。今は 0 のまま（挙動不変）
    public const float Top = 0f, Right = 384f, Bottom = 216f;
    public const float Width = Right - Left;
    public const float Height = Bottom - Top;
    public const float CenterX = (Left + Right) * 0.5f;
    public const float CenterY = (Top + Bottom) * 0.5f;
    public static Rect2 Rect => new Rect2(Left, Top, Width, Height);

    // 設計座標(1280×720)版（UiKit.BeginDesign 後の HUD 描画用）。
    public const float DLeft = Left / UiKit.Scale;
    public const float DCenterX = CenterX / UiKit.Scale;
    public const float DWidth = Width / UiKit.Scale;
    public const float PanelW = 373f;   // 設計座標のサイドパネル幅（第3段で使う）

    // ── ボスの徘徊ゾーン（BossMover.Configure に渡す既定値）──
    //   従来は「中心X=200 / 半幅=90」の直書きだった。盤面が動いても同じ“やや右寄り・盤面の1/4幅”に
    //   なるよう、中心からのずれと半幅を Field 基準の係数で持つ（Left=0 の今は 200 / 90 に一致する）。
    public const float BossOffsX = 8f;                  // 中心からの右寄せ量（192+8=200）
    public const float BossZoneHalfWK = 90f / 384f;     // 盤面幅に対する徘徊半幅の比（384*0.2344=90）
    public const float BossZoneHalfW = Width * BossZoneHalfWK;
    public const float BossCenterX = CenterX + BossOffsX;
}
