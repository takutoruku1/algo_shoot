using Godot;

// GlyphMote : 「アンチくん」— SNSで悪魔化した直進ザコの人。
// 黒い吹き出しパネル3枚を旋回させながら左へ進む（弾は撃たない＝パネルは盾専念）。
// 全パネルを剥がすと改心して笑顔の味方になる（Enemy基底）。
public partial class GlyphMote : Enemy
{
    private const float MoveSpeed = 50f;
    private float _campX;   // この位置まで来たら居座る（倒さない限り画面外に出ない）
    private float _vy;      // 居座り中の上下往復

    // かつては「弾を撃つ本体を無害化する」ためのスイッチだったが、GlyphMote自体は
    // 発射ロジックを持たず常に非発火（下のPanelsFire=falseの通り）。現状は挙動に差はないが、
    // 呼び出し側（StageZero のチュートリアル演出意図）を残すためフィールドのみ存置する。
    public bool Harmless;

    protected override void OnEnemyReady()
    {
        Points = 100;
        BodyRadius = 3.4f;   // 当たり判定小さめ（一回り小さく）
        PanelCount = 3;
        PanelInk = 2;
        OrbitRadius = 11.5f;
        PanelDisplayScale = 0.82f; // 一回り小さく
        SpinSpeed = 1.4f;
        PanelsFire = false; // GlyphMoteは発射ループを持たないため常に非発火（Panel側のfires引数は既に無視される＝盾専念）。

        // 生成済みドット絵素材
        PreTexPath = "res://char/enemy_anti_pre.png";
        PostTexPath = "res://char/enemy_anti_post.png";
        PanelTexPath = "res://char/panel_anti.png";
        BodyDisplayH = 23f;             // 一回り小さく

        _campX = GD.Randf() * 150f + 120f;                 // 120〜270 のどこかに陣取る
        _vy = (GD.Randf() < 0.5f ? -1f : 1f) * 16f;
    }

    // 左へ進入 → _campX に着いたら居座る（上下にゆっくり往復・画面外へ出ない）。倒すまで去らない。
    protected override void UpdateMovement(double delta)
    {
        float dt = (float)delta;
        if (GlobalPosition.X > _campX)
        {
            GlobalPosition += new Vector2(-MoveSpeed * dt, 0f);
            return;
        }
        float ny = GlobalPosition.Y + _vy * dt;
        if (ny < 28f || ny > 188f) { _vy = -_vy; ny = Mathf.Clamp(ny, 28f, 188f); }
        GlobalPosition = new Vector2(_campX, ny);
    }
}
