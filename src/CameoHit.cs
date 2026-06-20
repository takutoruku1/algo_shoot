using Godot;

// CameoHit : ボスの“チラ見せ”(カメオ)スプライトに被せる軽量な被弾判定。
// 本戦の Enemy 基底はフェーズ管理(無防備窓/パネル)を持ち重いので、カメオには使わない。
// ここでは自機弾だけを拾う Area2D を 1 つ持ち、当たったら弾を消してダメージを累積するだけ。
// 累積が総HP(=CameoBarHp×CameoBars)に達すると Escaped=true を立て、ステージ側が捨て台詞→逃走へジャンプする。
//
// HUD連携：カメオは本戦ボスと同じ複数ゲージ式ボスバー(Hud.ShowBossBar/UpdateBossBar/HideBossBar)で見せる。
// バーの意味は Enemy.cs（本戦ボス）と完全に揃える：
//   TotalBars      = ゲージ本数（CameoBars=2）
//   BarIndex       = 残ゲージの先頭(0始まり・削れるほど減る)
//   BarFrac        = 現ゲージ(先頭の残ってるバー)の残量 0〜1
// ステージは Tick の直後に UpdateBossBar(BarIndex, TotalBars, BarFrac) を呼べばよい。
//
// 衝突レイヤー設計（Bullet.cs / Enemy.cs に合わせる）：
//   自機弾 PlayerBullet: layer=2。これを拾うので mask に bit2(=値2) を立てる。
//   自分は当たり判定の“受け手”なので CollisionLayer は持たない（他から拾われない）。
//
// 使い方：カメオ表示開始時に new して親(World)へ追加し、毎フレーム SyncTo(sprite) で位置を追従、
// 退場時に必ず QueueFree() する（リーク・残留判定を残さない）。ボスバーの Hide はステージ側で行う。
public partial class CameoHit : Area2D
{
    // ── カメオの体力（ゲージ）定義（3ステージ共通・ここに集約して増減しやすく）──
    // 1ゲージ分のHP。従来の単一閾値(60)を踏襲＝撃ち込み続ければ1本数秒で削れる量。
    public const int CameoBarHp = 60;
    // ゲージ本数。本戦ボス前の演出なので控えめに2本（総HP=CameoBarHp×CameoBars=120相当）。
    public const int CameoBars = 2;
    // 逃走に追い込む総ダメージ（＝全ゲージ削り切り）。
    public const int CameoEscapeHp = CameoBarHp * CameoBars;

    // ── ミニボス戦の共通タイミング（3ステージ共通・ここに集約）──
    // 戦闘ループの保険タイマー（秒）。普通に撃てば先にHPが尽きる長さ。撃たない/与ダメ0でも
    // これを超えたら捨て台詞→逃走に移り、本ボスまで進行不能にならない（QAボットはカメオを撃たない）。
    public const double SafetySec = 30.0;
    // 捨て台詞の一行オーバーレイ1行あたりの表示秒。
    public const double PostLineDur = 2.4;

    // 会話配列(who, text, face)[] から「カメオ本人(who=2)の最初の発話」を1行返す。
    // 戦闘中の挑発オーバーレイ用（手動送りで止めず、世界観のある一言だけ拾う）。
    public static string FirstBossLine((int who, string text, string face)[] lines)
    {
        foreach (var (who, text, _) in lines)
            if (who == 2) return text;
        return lines.Length > 0 ? lines[0].text : "";
    }

    // 捨て台詞配列から who=2 の行を idx 番目以降で次に探して返す。無ければ null（＝逃走へ）。
    // idx は呼び出し側が ++ してから渡す前提（見つかった位置で更新して返す）。
    public static string? NextBossLine((int who, string text, string face)[] lines, ref int idx)
    {
        for (; idx < lines.Length; idx++)
            if (lines[idx].who == 2) return lines[idx].text;
        return null;
    }

    // 追従先スプライトの当たり半径（見た目に対して少し小さめ＝シビアすぎない）。
    private const float HitRadius = 14f;

    // 被弾フラッシュの長さ（秒）。当たるたびにこの値へリセットして「効いている」手応えを出す。
    private const double FlashDur = 0.08;

    private CollisionShape2D _shape = null!;
    private Sprite2D? _target;   // フラッシュ対象＆位置追従元のカメオスプライト
    private double _flashT;

    public int DamageTaken { get; private set; }
    public bool Escaped => DamageTaken >= CameoEscapeHp;

    // ── HUDボスバー連携（本戦ボス Enemy.cs と同じ規約）──
    // 残HP（総HP − 累積ダメージ）。0未満にはしない。
    private int RemainingHp => Mathf.Max(0, CameoEscapeHp - DamageTaken);
    // ゲージ本数。
    public int TotalBars => CameoBars;
    // 残ゲージの先頭(0始まり)。Enemy.CurrentBarIndex と同式（残HPが1以上のとき (残-1)/1ゲージ）。
    public int BarIndex => RemainingHp <= 0 ? 0 : Mathf.Clamp((RemainingHp - 1) / CameoBarHp, 0, CameoBars - 1);
    // 現ゲージ（先頭の残ってるバー）の残量 0〜1。Enemy.CurrentBarFrac と同式。
    public float BarFrac => RemainingHp <= 0 ? 0f : (float)(((RemainingHp - 1) % CameoBarHp) + 1) / CameoBarHp;

    public override void _Ready()
    {
        // 自機弾(layer=2)だけを拾う受け手。自分は誰にも拾われない（Layer=0）。
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionMaskValue(2, true); // 自機弾 PlayerBullet layer=2
        Monitoring = true;
        Monitorable = false;
        ZIndex = 5;

        _shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = HitRadius } };
        AddChild(_shape);

        AreaEntered += OnHit;
    }

    // 追従元スプライトを登録（生成直後に一度呼ぶ）。
    public void Bind(Sprite2D target)
    {
        _target = target;
        if (IsInstanceValid(_target))
            GlobalPosition = _target.GlobalPosition;
    }

    // 毎フレーム、カメオスプライトの現在位置へ判定を合わせる。被弾フラッシュも減衰させる。
    public void Tick(double delta)
    {
        if (IsInstanceValid(_target))
            GlobalPosition = _target!.GlobalPosition;

        if (_flashT > 0)
        {
            _flashT -= delta;
            if (_flashT <= 0 && IsInstanceValid(_target))
                _target!.SelfModulate = new Color(1f, 1f, 1f, _target.SelfModulate.A); // 元の明るさへ（αは保持）
        }
    }

    private void OnHit(Area2D area)
    {
        if (Escaped) return; // 既に逃走確定なら追撃を無視（多重遷移防止）
        if (area is Bullet b && !b.IsEnemy && b.Active)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.Despawn(b);
            DamageTaken += Mathf.Clamp(b.Damage, 1, 4);

            // 被弾フィードバック：一瞬だけ白く明るく（αは退場フェードと両立するよう保持）。
            if (IsInstanceValid(_target))
            {
                float a = _target!.SelfModulate.A;
                _target.SelfModulate = new Color(1.8f, 1.8f, 1.8f, a);
            }
            _flashT = FlashDur;
        }
    }
}
