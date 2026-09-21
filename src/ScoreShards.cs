using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class ScoreShards : Node2D
{
    public const float Lifetime = 5f;
    public const float MagnetRadius = 48f;
    // 散りの慣性が効いている時間。これを過ぎると velocity が exp(-5dt) で急ブレーキし、粒はその場に
    // 留まる＝「散り終わり」。道中ザコは従来どおり 0.24s（すぐ止まって拾いやすい位置に残る）。
    // ボス/中ボス撃破の粒は Shard.Fly に長い値を入れて盤面の端まで飛ばす（下の Add 参照）。
    private const float ScatterTime = 0.24f;
    private const float CollectRadius = 6f;
    // 256→640：ボス撃破で 104〜128 粒が一度に入る（+道中の取り残し）。上限に当たると
    // Add が false を返して「ただのパーティクル＝拾えない花びら」に落ちるため、格の差を
    // 出すには容量が先に要る。描画は 1粒 2〜6 DrawCall の単純ループで、640 でも実測余裕（下の _Draw 参照）。
    private const int Capacity = 640;

    private sealed class Shard
    {
        public FxLayer.P Particle = null!;
        public Vector2 Position, Velocity;
        public float Age, Delay;
        // この粒が慣性で飛び続ける時間（＝散りのフェーズ長）。既定は ScatterTime。
        // ボス/中ボス撃破では 0.5〜0.9s を入れて盤面いっぱいまで広げる（2026-09-22）。
        public float Fly = ScatterTime;
        public int Points;
        // 2026-09-17 経済改修：欠片はスコア(Points)とショップ通貨の基礎額(Imp)を別々に運ぶ。
        //   Imp は GameManager.AddScoreShard 経由で GainImpression の倍率を通る（難易度倍率は経済側の1本だけ）。
        public int Imp;
        public bool Attracted;
        public PowerKind? Power;
    }

    private readonly List<Shard> _shards = new();
    public int PowerCount => _shards.Count(shard => shard.Power.HasValue);
    private GameManager _game = null!;
    private Player? _player;
    private FxLayer _fx = null!;
    private float _soundCooldown, _collectIdle;
    private int _chain;
    // 強制吸引（ラッシュ）の残り秒。ボス/中ボス撃破の瞬間に FxLayer.PurifyBurst から立てる。
    // この間は (a) 距離に関係なく全欠片が吸引状態になり (b) 会話(BubblePaused)中でも動き続ける。
    // 撃破直後は必ず改心会話へ入るので、これが無いと大量に撒いた欠片が凍って寿命で消える＝損した気分になる。
    private float _rush;
    // 回収の見せ場（2026-09-22 ユーザー要望「回収時に派手に」）。ボス/中ボス撃破で立ち、
    // ラッシュ中の拾得を FxLayer.ShardHarvest（連鎖で段が上がる）へ回す。
    //   _harvest      : 見せ場が走っているか
    //   _harvestFinale: 最後の1粒まで拾い切ったときに締めの一拍を出すか（ボス撃破のみ true）。
    //     中ボスは戦闘が続く＝画面を覆う締めのフラッシュ／大リングは弾を隠すので出さない。
    private bool _harvest, _harvestFinale;

    // 撃破の瞬間から duration 秒だけ全欠片を強制回収する。
    public void BeginRush(float duration) => _rush = Mathf.Max(_rush, duration);

    // 回収演出を開始する。finale=true（ボス撃破）だけが拾い切りの締めを出す。
    public void BeginHarvest(bool finale)
    {
        _harvest = true;
        _harvestFinale |= finale;
    }

    public override void _Ready()
    {
        ZIndex = -6;
        ZAsRelative = false;
        _game = GetNode<GameManager>("/root/Game");
        _fx = GetParent<FxLayer>();
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        UpdateDialogueVisibility();
    }

    public void UpdateDialogueVisibility() => Visible = !Hud.BubblePaused || _game.StageCleared || _rush > 0;

    // fly : 慣性で飛び続ける秒数（0以下＝既定の ScatterTime）。吸引が始まるまでの Delay も
    //   これに追従する＝「散り切ってから吸い込まれる」。ボス/中ボスの全方位バーストだけが指定する。
    public bool Add(FxLayer.P particle, int points, int imp = 0, float fly = 0f, PowerKind? power = null)
    {
        if (_shards.Count == Capacity) return false;
        float flyTime = fly > 0f ? fly : ScatterTime;
        _shards.Add(new Shard
        {
            Particle = particle,
            Position = ToLocal(new Vector2(particle.X, particle.Y)),
            Velocity = new Vector2(particle.Vx, particle.Vy),
            Delay = flyTime + particle.Ttl * 0.08f,
            Fly = flyTime,
            Points = points,
            Imp = imp,
            Power = power,
        });
        QueueRedraw();
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_rush > 0) _rush = Mathf.Max(0f, _rush - dt);
        // ラッシュ中は会話が始まっていても回収を走らせる（撃破の一拍を会話で殺さない）。
        bool sweep = _game.StageCleared || _rush > 0;
        UpdateDialogueVisibility();
        if (!Visible) return;
        if (!IsInstanceValid(_player))
            _player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (_player == null) return;
        if (_player.Lives <= 0)
        {
            _shards.Clear();
            _harvest = _harvestFinale = false;
            QueueRedraw();
            return;
        }
        // 拾い切る前にラッシュが切れた（＝寿命で消えた／死んだ）場合も見せ場を畳む。
        // 締めの一拍は「全部拾えた」ときのご褒美なので、取りこぼしたまま出してはいけない。
        if (_harvest && _rush <= 0 && _shards.Count == 0) _harvest = _harvestFinale = false;

        _soundCooldown -= dt;
        _collectIdle += dt;
        if (_collectIdle > 0.3f) _chain = 0;
        Vector2 target = ToLocal(_player.GlobalPosition);
        int collected = 0, collectedImp = 0, collectedCount = 0;
        Color pickupColor = FxLayer.Heart;
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var shard = _shards[i];
            shard.Age += dt;
            shard.Particle.Rot += shard.Particle.Spin * dt;
            Vector2 toPlayer = target - shard.Position;
            float distance = toPlayer.Length();
            // ラッシュ中は散りの一拍（Delay）を詰める＝「咲いて → すぐ吸い込まれる」テンポ。
            // ただしボス級の長い散り(Fly>ScatterTime)は 0.8 までしか詰めない：ここを半分にすると
            // 盤面の端まで届く前に吸引が始まり、「全体に散る」が見えないまま終わる（2026-09-22）。
            float delay = _rush > 0 ? shard.Delay * (shard.Fly > ScatterTime ? 0.8f : 0.5f) : shard.Delay;
            if (shard.Age >= delay && (sweep || distance <= MagnetRadius))
                shard.Attracted = true;

            if (shard.Attracted)
            {
                // ラッシュ中だけ上限を 430→620 に上げる。ボス撃破は 100粒超が最大200px先から
                // 集まるので、据え置きだと尾が長く伸びて会話に食い込む（テンポ §3）。
                float cap = _rush > 0 ? 620f : 430f;
                float speed = Mathf.Min(cap, Mathf.Max(95f, shard.Velocity.Length()) + 1000f * dt);
                if (distance <= CollectRadius + speed * dt)
                {
                    collected += shard.Points;
                    collectedImp += shard.Imp;
                    collectedCount++;
                    pickupColor = shard.Particle.Col;
                    if (shard.Power is { } kind && _player.ApplyPowerup(kind))
                    {
                        _fx.ScorePickup(_player.GlobalPosition, PowerPickupArt.ColorFor(kind));
                        Audio.Instance?.PlayUiConfirm();
                    }
                    _shards.RemoveAt(i);
                    continue;
                }
                shard.Velocity = shard.Velocity.MoveToward(toPlayer / distance * speed, 2200f * dt);
                shard.Position += shard.Velocity * dt;
            }
            else
            {
                if (shard.Age >= Lifetime)
                {
                    _shards.RemoveAt(i);
                    continue;
                }
                if (shard.Age <= shard.Fly)
                {
                    shard.Velocity.Y += shard.Particle.Grav * dt;
                    shard.Velocity *= Mathf.Max(0f, 1f - shard.Particle.Drag * dt);
                }
                else shard.Velocity *= Mathf.Exp(-5f * dt);
                shard.Position += shard.Velocity * dt;
                if (shard.Age > 0.5f) shard.Position.X -= 5f * dt;
                var global = ToGlobal(shard.Position);
                global.X = Mathf.Clamp(global.X, Field.Left + 5f, Field.Right - 5f);
                global.Y = Mathf.Clamp(global.Y, Field.Top + 19f, Field.Bottom - 9f);
                shard.Position = ToLocal(global);
            }
        }
        if (collectedCount > 0)
        {
            _game.AddScoreShard(collected, collectedImp);
            _collectIdle = 0;
            if (_soundCooldown <= 0)
            {
                // 見せ場中は連鎖に応じて段が上がる版へ（音の半音上がりと視覚の段を揃える）。
                if (_harvest) _fx.ShardHarvest(_player.GlobalPosition, pickupColor, _chain);
                else _fx.ScorePickup(_player.GlobalPosition, pickupColor);
                Audio.Instance?.PlayScorePickup(_chain++);
                // ラッシュ（ボス/中ボス撃破）中は間隔を詰めて連鎖を密に鳴らす（0.065→0.045s）。
                // 0.045s＝約22音/秒。これ以上詰めると単音が潰れて「ノイズ」になる境界。
                _soundCooldown = _rush > 0 ? 0.045f : 0.065f;
            }
            // 最後の1粒を拾い切った瞬間だけ締めの一拍。_harvest を降ろすのはここ＝
            // 以降の道中の拾得は従来の控えめな ScorePickup に戻る。
            if (_harvest && _shards.Count == 0)
            {
                if (_harvestFinale) _fx.ShardHarvestFinale(_player.GlobalPosition);
                _harvest = _harvestFinale = false;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 尾を引く線のアンチエイリアスは 1本あたりのコストが高い。ボス撃破ラッシュでは
        // 100本超が同時に尾を引くので、粒が多いフレームだけ AA を落とす（見た目の差はほぼ無い／
        // フレーム落ちのほうが手触りを壊す＝§11 完成度は面白さの前提）。
        bool aa = _shards.Count <= 96;
        foreach (var shard in _shards)
        {
            float alpha = shard.Attracted ? 1f : Mathf.Clamp((Lifetime - shard.Age) / 0.7f, 0, 1);
            var particle = shard.Particle;
            var position = shard.Position;
            if (!shard.Attracted) position.Y += Mathf.Sin(shard.Age * 4f + shard.Delay * 10f) * 1.2f;
            // 尾は「速く飛んでいる」ものにだけ。低速の粒に短い線を足しても見えず、描画だけ増える。
            if (shard.Attracted && shard.Velocity.LengthSquared() > 3600f)
            {
                // 見せ場（ボス/中ボス撃破の回収）中は尾を長く・二本重ねて「光の尾」にする。
                // 12→26px／芯の白を重ねる＝自機へ収束する線が束になって見える。1粒 +1本なので
                // 640粒でも +640 DrawLine＝実測でフレーム落ちは出ない（AA は 96粒超で自動 off）。
                float maxLen = _harvest ? 26f : 12f;
                Vector2 tail = -shard.Velocity.Normalized() * Mathf.Min(maxLen, shard.Velocity.Length() * (_harvest ? 0.06f : 0.03f));
                DrawLine(position, position + tail, new Color(particle.Col, _harvest ? 0.55f : 0.4f), 0.8f, aa);
                if (_harvest) DrawLine(position, position + tail * 0.45f, new Color(1f, 1f, 1f, 0.5f), 0.7f, aa);
            }
            particle.X = position.X;
            particle.Y = position.Y;
            particle.Life = (1f - alpha) * particle.Ttl;
            FxLayer.DrawP(this, particle, null);
            if (shard.Power is { } kind)
            {
                float size = particle.Size * 1.35f;
                PowerPickupArt.Draw(this, new Rect2(position - Vector2.One * size * 0.5f, Vector2.One * size), kind, alpha);
            }
        }
    }
}
