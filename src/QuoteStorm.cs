using Godot;
using System.Collections.Generic;

// QuoteStorm : S3-5b「引用の嵐」（STAGE3 レイの道中C）。
//
// 正典: wiki/08_仮台本/11_引用ポストの嵐.md（ユーザー承認済み・2026-09-05）。文面は 11 のまま一字も変えない。
//   ひとつの投稿に、顔のない引用が寄ってたかって積もる。遊び手の仕事は説得でも反論でもなく、剥がすこと。
//   剥がし切ると、束の下に本人の送られなかった一行が残っている。
//
// 骨子（11 の「演出の骨子」）:
//   1. ピン留め … 星逢レイの投稿カードが上部中央に留まる（この Node2D が描く）。
//   2. 飛来     … 右端から引用チップ（言葉弾の大型版＝Bullet.SetWord のチップ）が飛んでくる。
//   3. 貼りつき … 到達位置で速度 0。核はここで降ろす（Bullet.MakeHarmless）＝貼りついたあとは当たらない。
//   4. 剥がす   … 撃つと剥がれる（祈り弾と同じ Erasable の経路）。剥がれるときは灰色の紙片（FxLayer.PaperScrap）。
//   5. 声が飽きる … 三段階が終わると飛来がぱたりと止まる（「はい次の話題」で去る）。以後は増えない。
//                 撃ち残しは、ここから一拍おいて勝手に紙片になって落ちる＝剥がし切りを遊び手に強いない
//                 （2026-09-26 作者指示「壊さないと先に進まないコメントを出さない」。剥がすのは自由、進行は待たない）。
//   6. 下書き   … すべて剥がれると、送られなかった一行が残り、ミナが拾う。
//
// 境界（11 の「やらないこと」）:
//   ・剥がし漏らしの罰は無い（ゲージ減算・汚染加算・咎めを置かない）。
//   ・声が止まった後は必ず終わる（新規飛来なし＋撃ち残しは自動で剥がれる＝取り残しで詰まない・待たされない）。
//   ・引用側を裁かない。剥がしても紙片が落ちるだけで、罰や反撃の演出は付けない。
//   ・ホラーにしない。画面は暗くしない。引用が積もって投稿とガワが見えなくなることだけが恐さの源。
public partial class QuoteStorm : Node2D
{
    // ───────── 11 の仮台本（引用17枚・3段階）─────────
    // 本文は 11 のまま。表示名（@ハンドル）は Handles.Garble で 1 文字化かす＝実在アカウントと一致させない（2026-09-23）。顔も動機も与えない（09 の「引用型（加害側）」の枠）。
    // チップに乗るのは本文だけなので、表示名は貼りついたカードの上に添える（Handle）。
    private readonly struct Quote
    {
        public readonly string Handle, Body;
        public Quote(string h, string b) { Handle = h; Body = b; }
    }

    // 段階1（茶化し。0〜12秒・5枚）
    private static readonly Quote[] Stage1 =
    {
        new(Handles.Garble("@tori398"),     "これ本気で言ってる?"),
        new(Handles.Garble("@gaiya_8"),     "それ限定公開でやれ"),
        new(Handles.Garble("@anon_5502"),   "> それだけで十分 ←十分じゃない顔してる"),
        new(Handles.Garble("@rom_only"),    "はいはい感謝芸"),
        new(Handles.Garble("@kansoku_01"),  "誰に向けて言ってんのこれ 3人?"),
    };
    // 段階2（嘲笑。12〜26秒・6枚）
    private static readonly Quote[] Stage2 =
    {
        new(Handles.Garble("@no_name_77"),  "切り抜きで見た 本編行く価値なし"),
        new(Handles.Garble("@mob_4410"),    "ガワだけで中身ない"),
        new(Handles.Garble("@sotogawa_2"),  "一年伸びてない配信者のサンプルとして保存した"),
        new(Handles.Garble("@nichijo_x"),   "企画ゼロで何を見ろと"),
        new(Handles.Garble("@kansoku_01"),  "いいね3 自分と身内でしょ"),
        new(Handles.Garble("@teifujo__"),   "痛い 枠ごと消したら?"),
    };
    // 段階3（存在の否定。26〜40秒・6枚）。最後の一枚「はい次の話題」で飛来が止まる。
    private static readonly Quote[] Stage3 =
    {
        new(Handles.Garble("@nanashi_3942"), "こういう人がいるから界隈が終わる"),
        new(Handles.Garble("@gaiya_8"),      "誰も見てないって何回言えばいい"),
        new(Handles.Garble("@tori398"),      "同接3 まだやってたんだ"),
        new(Handles.Garble("@anon_5502"),    "配信やめても誰も気づかないタイプ"),
        new(Handles.Garble("@rom_only"),     "引退しろまでは言わないけど 察して"),
        new(Handles.Garble("@nichijo_x"),    "はい次の話題"),
    };

    // 段階ごとの飛来間隔（11 の表）。段階が進むほど詰まる＝剥がす速度を飛来が上回る。
    private static readonly double[] StageInterval = { 2.4, 2.0, 1.6 };
    // 段階の切れ目の一拍（本人の返信を読ませる間）。11 の段階の尺（0〜12／12〜26／26〜40秒）と
    // 枚数×間隔（12.0／12.0／9.6秒）の差ぶんを、返信の行が出るこの間に置く。
    private static readonly double[] StageGap = { 2.0, 4.4 };

    // 本人の返信（段階の切れ目ごとに1行・文字数が減っていく。11 の「本人の返信が縮む」）。
    //   4行目は空欄＝返信欄が開いて、閉じる。ガワの笑顔のまま崩れない（表情差分は持たせない）。
    private static readonly string[] Replies = { "見てくれてありがとう", "ごめんなさい", "ごめん", "" };

    // ピン留めの投稿（09 R42）。配信を切った直後の一言で、配信中と同じ明るさ。
    private const string PinHandle = "星逢レイ " + Handles.Rei;
    private const string PinBody = "配信おわり 来てくれてありがとう 人数じゃないから 全部読めた それだけで十分";

    // 剥がし切りで残る下書き（09 R46）。送られていない＝いいね欄も時刻欄も無い。何が「いい」のかは書かない。
    public const string DraftLine = "もう、いいかな";

    // ───────── 状態 ─────────
    // Phase: 段階1〜3 の飛来 → 声が止まる（Silence＝残りが勝手に剥がれ落ちる）→ 下書き（Draft）→ 終了（Done）。
    private enum Phase { Storm, Silence, Draft, Done }
    private Phase _phase = Phase.Storm;
    private int _stage;                 // 0..2（飛来中の段階）
    private int _inStage;               // その段階で出した枚数
    private double _spawnT;             // 次の飛来までの残り
    private double _elapsed;            // 場面全体の経過秒（進行の目安・保険）
    private double _phaseT;             // フェーズ内の経過秒
    private int _replyShown;            // 出した本人の返信の数（段階の切れ目ごとに1行）

    public int QuoteCount { get; private set; }         // 貼りついた総枚数（システム帯「引用: n」）
    public bool Finished => _phase == Phase.Done;       // 剥がし切って下書きまで出し終えたか

    // 貼りついた引用（Bullet と、その文面）。剥がれた（Despawn された）ものは毎フレーム掃除する。
    private readonly List<(Bullet b, Quote q)> _stuck = new();
    // 飛来中の引用（到達位置に着いたら貼りつける）。
    private readonly List<(Bullet b, Quote q, Vector2 dst)> _flying = new();

    // その Bullet が「まだこの引用のまま」か。プールは LIFO なので、剥がれた直後の Bullet が同じ物理フレーム内に
    //   道中弾として再利用されると参照だけが生き残り、Active だけ見ていると他人の弾を引用と取り違える
    //   （＝いつまでも「貼りついている」ことになって場面が終わらない）。文面と Erasable（Activate が毎回落とす）で見分ける。
    private static bool IsChip(Bullet b, Quote q)
        => IsInstanceValid(b) && b.Active && b.Erasable && b.Word == q.Body;

    private readonly RandomNumberGenerator _rng = new();
    private Hud? _hud;

    // 全17枚。11 の「貼りつきの上限12枚」は、超えた分が古い引用の上に重なる＝画面上の枚数は増えるが
    // 位置の抽選帯を狭めて重ねる（消さない＝剥がす枚数は必ず 17 に一致する）。
    private const int TotalQuotes = 5 + 6 + 6;
    private const int StackCap = 12;

    // ピン留めカードの設計位置（プレイ領域＝Field の上部中央）。ガワ（ボスの姿）はまだ出ていないので、
    // 貼りつき先はカードのまわり＝この矩形の下側の帯になる。
    // HUD の上帯（浄化バー）と自機の主戦場を避け、幅は本文が一行に収まる最小に留める。
    private static readonly Rect2 PinRect = new(Field.CenterX - 108f, Field.Top + 20f, 216f, 22f);

    public override void _Ready()
    {
        ZIndex = -14;                    // 投稿チップ(-12)よりさらに奥＝貼りついた引用がカードの上に見える
        ZAsRelative = false;
        _rng.Randomize();
        _hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
        _spawnT = 0.8;                   // 入りの一拍（ピン留めを読む時間）
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (Hud.BubblePaused) return;    // 会話中（バブル）は嵐も止める（他の ambient と同じ流儀）

        // ★集中モード（#10）：嵐（＝敵の攻撃）も敵側の時計で進める。
        delta = GameManager.EnemyDelta(delta);
        _elapsed += delta;
        _phaseT += delta;

        AdvanceFlying(delta);
        SweepStuck();

        switch (_phase)
        {
            case Phase.Storm:   TickStorm(delta); break;
            case Phase.Silence: TickSilence(delta); break;
            case Phase.Draft:   TickDraft(); break;
        }
        QueueRedraw();
    }

    // ───────── 段階1〜3：飛来 ─────────
    private void TickStorm(double delta)
    {
        _spawnT -= delta;
        if (_spawnT > 0) return;

        var src = _stage switch { 0 => Stage1, 1 => Stage2, _ => Stage3 };
        SpawnQuote(src[_inStage]);
        _inStage++;
        _spawnT = StageInterval[_stage];

        if (_inStage < src.Length) return;

        // 段階の切れ目：本人の返信が一行、前より短くなって出る（ガワの笑顔のまま）。
        ShowReply();
        _inStage = 0;
        _stage++;
        if (_stage < 3) { _spawnT = StageGap[_stage - 1]; return; }

        // 段階3 の最後の一枚（「はい次の話題」）を出し切った＝飛来が止まる。以後は増えない。
        _phase = Phase.Silence;
        _phaseT = 0;
        _peelT = PeelLead;
        ShowReply();                     // 4行目＝空欄（返信欄が開いて、閉じる）
        if (QaPilot.Verbose) GD.Print($"[QuoteStorm] silence t={_elapsed:0.0} stuck={_stuck.Count} flying={_flying.Count}");
    }

    // ───────── 声が止まったあと：残りは勝手に剥がれ落ちる ─────────
    // ここに「剥がし切るまで待つ」は置かない（2026-09-26 作者指示：壊さないと先に進まないコメントを出さない）。
    //   飛来が止まったら、撃ち残しは一拍おいて1枚ずつ紙片になって落ちる（声が飽きて散った、の絵）＝
    //   遊び手が一枚も撃たなくても場面は同じ尺で下書きへ出る。撃てば早く剥がれる、それだけ。
    //   旧: 10 秒の猶予（SilenceGrace）のあいだ剥がし切りを待っていた＝道中がここで止まって見えた。
    private const double PeelLead = 0.6;        // 返信欄が閉じてから落ち始めるまでの一拍
    private const double PeelStep = 0.28;       // 自動で剥がれる間隔（ぱらぱら）
    private const double FlyWait = 1.8 + 0.5;   // 最後の一枚の飛行（1.8 秒）を待つ上限。過ぎたら飛行中扱いのまま落とす
    private double _peelT;                      // 次の自動剥がれまでの残り
    private void TickSilence(double delta)
    {
        // 最後の一枚が貼りつくのを待つ（上限つき＝取り違えた参照が残っても場面は終わる）。着いてから一拍。
        if (_flying.Count > 0 && _phaseT < FlyWait) { _peelT = PeelLead; return; }

        if (_stuck.Count == 0 && _flying.Count == 0) { EnterDraft(); return; }

        _peelT -= delta;
        if (_peelT > 0) return;
        _peelT = PeelStep;
        PeelOne();
    }

    // 残りの引用を新しいほうから1枚、紙片にして落とす（罰ではなく「必ず終わる」ための送り）。
    private void PeelOne()
    {
        Bullet b; Quote q;
        if (_stuck.Count > 0) { (b, q) = _stuck[^1]; _stuck.RemoveAt(_stuck.Count - 1); }
        else { (b, q, _) = _flying[^1]; _flying.RemoveAt(_flying.Count - 1); }
        if (!IsChip(b, q)) return;
        FxLayer.Instance?.PaperScrap(b.GlobalPosition);
        GetNodeOrNull<BulletPool>("/root/Pool")?.Despawn(b);
    }

    private void EnterDraft()
    {
        _phase = Phase.Draft;
        _phaseT = 0;
        // 剥がした下には、まだ同じ笑顔のガワ。カードの下に薄い字で一行（この Node2D が描く）。
        _hud?.ShowBossLine("", $"下書き: {DraftLine}", new Color(0.72f, 0.70f, 0.76f), 3.2);
        if (QaPilot.Verbose) GD.Print($"[QuoteStorm] draft t={_elapsed:0.0}");
    }

    // 下書きを読ませる間だけ留まってから終わる（この後の台詞は StageRei が続ける）。
    private void TickDraft()
    {
        if (_phaseT < 3.2) return;
        _phase = Phase.Done;
        if (QaPilot.Verbose) GD.Print($"[QuoteStorm] done t={_elapsed:0.0}");
    }

    // ───────── 引用チップの飛来と貼りつき ─────────
    private void SpawnQuote(Quote q)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;

        // 貼りつき先：ピン留めカードのまわり（下側の帯）。11 の上限12枚を超えた分は同じ帯に重ねる
        //   （消さない＝剥がす枚数は 17 で一致し、投稿とガワが見えなくなっていく）。
        int i = QuoteCount;
        float t = (i % StackCap) / (float)StackCap;
        float dx = PinRect.Position.X + 6f + t * (PinRect.Size.X - 12f);
        float dy = PinRect.Position.Y + PinRect.Size.Y + 4f + (i % 4) * 13f + _rng.RandfRange(-2f, 2f);
        var dst = new Vector2(dx, dy);

        // 飛行 1.8 秒で右端 → 貼りつき位置（11 の「読ませる速度」）。飛行中だけ核を持つ。
        //   ★旧 +30f は Bullet の画面外余白（16px）を超えており初フレームで Despawn＝引用チップが1枚も出なかった
        //   （S3-5b の「剥がす」遊びが丸ごと消えていた。2026-09-23 QA 検出）。飛行中だけ BoundsMargin を広げて
        //   最長チップ（≈282px）の半幅＋縁光ぶん外＝+150 から滑り込ませ、貼りつきで既定へ戻す。
        var from = new Vector2(Field.Right + 150f, dst.Y + _rng.RandfRange(-10f, 10f));
        var vel = (dst - from) / 1.8f;
        var b = pool.Spawn(from, vel, isEnemy: true, 3f, 1);
        if (b == null) return;
        b.BoundsMargin = 260f;            // 飛行中は右端の外に丸ごと居られる余白（貼りつきで既定へ）
        // 引用は「投稿に寄る側」で層が違う（09）。濁色チップにして、面の投稿弾（テーマ色）と見分ける。
        b.SetWord(q.Body, q.Handle, new Color(0.55f, 0.55f, 0.60f), murk: true,
            coreArt: BulletArt.Get("rei_film"));
        b.MakeErasable();                 // 撃つと剥がれる（祈り弾と同じ経路）
        _flying.Add((b, q, dst));
        QuoteCount++;
        ShowCounter();
    }

    // 飛行中の引用が到達位置に着いたら、そこで止めて核を降ろす（＝貼りついたあとは当たらない）。
    private void AdvanceFlying(double delta)
    {
        for (int i = _flying.Count - 1; i >= 0; i--)
        {
            var (b, q, dst) = _flying[i];
            if (!IsChip(b, q)) { _flying.RemoveAt(i); continue; }   // 飛行中に撃たれた（or プールで別の弾になった）
            if (b.GlobalPosition.X > dst.X + 1.5f) continue;

            b.GlobalPosition = dst;
            b.Velocity = Vector2.Zero;
            b.BoundsMargin = Bullet.DefaultBoundsMargin;
            b.MakeHarmless();             // 11：核は飛行中だけ。貼りついたあとは当たらない
            _flying.RemoveAt(i);
            _stuck.Add((b, q));
        }
    }

    // 剥がされた（プールへ戻った）引用をリストから落とす。剥がれた瞬間の紙片は Bullet 側では出せないので
    // （祈り弾の経路は花びらを出す）、ここで消滅を検知して灰色の紙片に置き換える。
    private void SweepStuck()
    {
        for (int i = _stuck.Count - 1; i >= 0; i--)
        {
            var (b, q) = _stuck[i];
            if (IsChip(b, q)) continue;
            if (IsInstanceValid(b) && !b.Active) FxLayer.Instance?.PaperScrap(b.GlobalPosition);   // 別の弾に化けた参照には出さない
            _stuck.RemoveAt(i);
        }
    }

    // 本人の返信（段階の切れ目ごとに1行）。弾を止めない字幕で流す＝手が止まらない。
    private void ShowReply()
    {
        if (_replyShown >= Replies.Length) return;
        string text = Replies[_replyShown++];
        // 4行目は空欄＝「返信欄が開いて、閉じる」。字幕には「……」だけを置き、言葉は出さない。
        _hud?.ShowBossLine("レイ", text.Length > 0 ? text : "……", new Color(0.78f, 0.84f, 0.96f), 2.6);
    }

    // システム帯「引用: n」（11 の骨子5＝数字が止まると、声が飽きたことが分かる）。
    private void ShowCounter() => _hud?.ShowBossLine("", $"引用: {QuoteCount}", new Color(0.62f, 0.60f, 0.68f), 1.1);

    // ───────── 描画：ピン留めの投稿カード ─────────
    // 引用チップ本体は Bullet が描くので、ここが描くのはピン留めカードだけ（貼りついた引用がこの上に重なる）。
    public override void _Draw()
    {
        if (_phase == Phase.Done) return;
        var f = UiKit.Zen;
        if (f == null) return;

        var r = PinRect;
        // 「留まっている」ことが分かる程度の静かなカード。暗くしない・点滅させない（11 の境界）。
        UiKit.Box(this, r, new Color(0.055f, 0.065f, 0.105f, 0.86f), 4f, new Color(0.62f, 0.70f, 0.92f, 0.42f), 1f);
        // ピン留めの印（左上の小さな丸）。
        DrawCircle(new Vector2(r.Position.X + 7f, r.Position.Y + 7f), 2.4f, new Color(0.62f, 0.70f, 0.92f, 0.8f), true, -1f, true);
        DrawString(f, new Vector2(r.Position.X + 13f, r.Position.Y + 8f), PinHandle,
            HorizontalAlignment.Left, -1, 5, new Color(0.62f, 0.70f, 0.92f, 0.72f));
        // 本文は一行に収める（幅に入らなければ自動で詰める＝カードを広げない）。
        DrawString(f, new Vector2(r.Position.X + 6f, r.Position.Y + 17f), PinBody,
            HorizontalAlignment.Left, r.Size.X - 12f, 6, new Color(0.86f, 0.88f, 0.94f, 0.92f));

        // 剥がし切ったあと：カードの下に薄い字で、送られなかった一行（09 R46）。
        if (_phase == Phase.Draft)
            DrawString(f, new Vector2(r.Position.X + 6f, r.Position.Y + r.Size.Y + 12f), $"下書き: {DraftLine}",
                HorizontalAlignment.Left, -1, 7, new Color(0.70f, 0.68f, 0.74f, 0.80f));
    }

    // 場面を畳む（StageRei が step を抜けるとき）。貼りついた引用は残さない。
    public void Dismiss()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        foreach (var (b, q) in _stuck) if (IsChip(b, q)) pool?.Despawn(b);
        foreach (var (b, q, _) in _flying) if (IsChip(b, q)) pool?.Despawn(b);
        _stuck.Clear();
        _flying.Clear();
        QueueFree();
    }
}
