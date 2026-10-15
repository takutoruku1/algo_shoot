using Godot;
using System.Linq;

// once キーは小話の既読リセットでも残し、仲間の潜行ではミナの案内を消費しない。
public static class StageTutorial
{
    public const string RouteSeenKey = "once_tutorial_route";
    public const string ShieldSeenKey = "once_tutorial_shield";
    public const string LockSeenKey = "once_tutorial_lock";
    public const string BombSeenKey = "once_tutorial_bomb";
    public const string BossSeenKey = "once_tutorial_boss";
    public const string AnkerAkariSeenKey = "once_ankers_akari";
    public const string AnkerKoharuSeenKey = "once_ankers_koharu";
    public const string AnkerReiSeenKey = "once_ankers_rei";
    public const string ItemsAkariSeenKey = "once_items_akari";
    public const string SkillDodgeSeenKey = "once_skill_dodge";
    public const string SkillChargeSeenKey = "once_skill_charge";
    public const string SkillCharge2SeenKey = "once_skill_charge2";
    private static readonly (int who, string text, string face)[] None = System.Array.Empty<(int, string, string)>();

    private static readonly (int who, string text, string face)[] Route =
    {
        (0, "まず移動から。入力した方向へ進める。攻撃が当たるのは、身体の中心にあるコアだ。そこを守ろう。", ""),
        (1, "こちらですね。服の端をかすめるくらいなら、大丈夫、と。", "res://char/mina_face.png"),
        (0, "うん。光は自動で放たれる。最初は、よけることと狙うことに慣れよう。", ""),
        (1, "余計なボタンを探しておりました。少なくて助かります。", "res://char/mina_face.png"),
    };

    private static readonly (int who, string text, string face)[] Shield =
    {
        (0, "アンチャーの周りを回る板は、アンフォールダー。まず、あれを砕く。浄化が進めば、本人の本音へ近づける。", ""),
        (1, "言葉を消すのではなく、塞いでいるものをほどくのですね。", "res://char/mina_face.png"),
    };

    private static readonly (int who, string text, string face)[] Lock =
    {
        (0, "狙う相手を選ぶときはロックオン。背後にも光が届く。狙う相手は切替操作で選べるけれど、ロック中は少し移動が遅くなるよ。", ""),
        (1, "狙いに夢中で、ぶつからないようにします。", "res://char/mina_face.png"),
        (0, "ロックを解除すれば、元の動きに戻る。ロックした相手を浄化したあとは、次の相手へ狙いが移る。", ""),
    };

    private static readonly (int who, string text, string face)[] Bomb =
    {
        (0, "囲まれたらBOMB。アンチャーをまとめて祓い、短い無敵の間に退避できる。弾は残るから、空いた場所へ。残数も見ておこう。", ""),
        (1, "温存したまま倒れては、元も子もありませんね。", "res://char/mina_face.png"),
    };

    private static readonly ControlCard.Topic[] RouteCues =
    { ControlCard.Topic.Move, ControlCard.Topic.Move, ControlCard.Topic.Shot, ControlCard.Topic.Shot };
    private static readonly ControlCard.Topic[] LockCues =
    { ControlCard.Topic.Lock, ControlCard.Topic.Lock, ControlCard.Topic.LockClear };
    private static readonly ControlCard.Topic[] BombCues =
    { ControlCard.Topic.Bomb, ControlCard.Topic.Bomb };

    // ② 本ボス戦チュートリアル（ブロック2・5行）。ボスの口上（who=2）の直後に続く。
    private static readonly (int who, string text, string face)[] Boss =
    {
        (0, "この先が、フェイクレルムの主だ。周りのアンフォールダーをすべて砕くと、本人へ光が届く時間ができる。", ""),
        (1, "あの方の姿まで、変わっているのですね。", "res://char/mina_face.png"),
        (0, "心を閉じ込めるものが、姿にも重なってる。本人が無防備な間に光を。板が戻ったら、また板をほどこう。", ""),
        (1, "その間に、わたくしがお話しします。", "res://char/mina_face.png"),
        (0, "うん。僕の声は本人には届かない。こちらで危険を見てるから、君の言葉を聞かせてあげて。", ""),
    };

    // ③ アンチャー紹介（2026-09-17）。道中へ入る直前に、その面のアンチャー「全体の性格」だけを掴ませる。
    //   個別の12種は解説しない（画面に敵名が出ないので名指ししても照合できない＝覚える作業になるだけ）。
    //   道中チュートリアル（①）の後ろへ繋ぐ＝「一般（操作と世界のルール）→ 個別（この面の敵の傾向）」の順。
    //   once はステージごと（面ごとに一度きり）＝①の once_tutorial_route（セーブ全体で一度）とは別キー。
    //   本文の正典: docs/20260917/アンチャー紹介_本文_2026-09-17.md（ユーザー確認済み・一字も変えない）。
    private static readonly (int who, string text, string face)[] AnkerAkari =
    {
        (0, "このフロアのアンチャーは、距離を詰めてくる。落ちる途中で速くなるものにも気をつけて。", ""),
        (0, "同じ場所に留まらず、空いた側へ進もう。", ""),
        (1, "はい。道を教えていただけるのは、助かります。", "res://char/mina_face.png"),
    };

    private static readonly (int who, string text, string face)[] AnkerKoharu =
    {
        (0, "ここは速い攻撃と、遅く残る攻撃が混ざる。構えた合図を見てから、一歩ずつよけよう。", ""),
        (1, "全部に反応すると、別のものにぶつかりそうです。", "res://char/mina_face.png"),
        (0, "近いものからでいい。僕も、後ろを見てる。", ""),
        (1, "では、前はわたくしが。お願いします。", "res://char/mina_face.png"),
    };

    private static readonly (int who, string text, string face)[] AnkerRei =
    {
        (0, "この場所では、上からも背後からも来る。光る線が走ったところは、通らないで。", ""),
        (1, "見られる方向が多すぎますね。……落ち着かないです。", "res://char/mina_face.png"),
        (0, "線を引いたアンチャーを先にほどけば、線も消える。右側の相手にしるしをつけた。", ""),
        (1, "見えました。そちらから行きます。", "res://char/mina_face.png"),
    };

    // ④ 強化アイテム説明（2026-09-22・ユーザー要望「最初のステージの一番最初に強化弾の説明を」）。
    //   ザコを浄化すると（Player.KillsPerPowerDrop 体ごとに）心の欠片の1粒に PowerKind（Line/Speed/Life/Shield）
    //   が乗り、色の枠付きで散る（Enemy.Redeem → Player.CountPowerupKill → FxLayer.PurifyBurst → ScoreShards）。
    //   拾い方は欠片と同じ（磁力）。効果は Player.ApplyPowerup、表示は Hud.DrawPowerups（左パネル・LIFE の上）、
    //   被弾で失う（Player.LosePowerupsOnHit／Shield は肩代わりで消費）。本文は数値に依存しない＝WIP の
    //   調整（倍率・間隔）で嘘にならない書き方にしてある。
    //   あかり面で強化を拾い、左の効果欄に実物が出たときに流す。
    //   once はセーブ単位（once_items_akari）。①③と同じく結び手潜行のときだけ・消費も同条件（Take）。
    //   本文の正典: docs/20260922/強化アイテム説明_本文_2026-09-22.md。
    private static readonly (int who, string text, string face)[] ItemsAkari =
    {
        (0, "今の欠片で、力が変わった。色の枠が目印だよ。光が増えたり、速く動けたりする。敵をたくさん倒すと、LIFEを1回復するハートも落ちるよ。", ""),
        (1, "お着替えなしで変われるのは、手軽ですね。危なくないところから、拾います。", "res://char/mina_face.png"),
        (0, "効果は左のパネルへ。被弾すると消えるから気をつけよう。普通の心の欠片は、帰ったあとに力へ変えられる。", ""),
    };

    // ⑤ 習得スキルの説明（2026-09-22・ユーザー要望「回避とチャージショットが追加されたときはステージに
    //   入ったときに使い方を教える。説明の仕方は最初のステージ開始時と同じで」）。
    //   ・回避 … ショップの品目 n_dodge（HasDodge。2026-09-22 ユーザー指示で「1面クリアの物語報酬」から変更）。
    //     発火条件は HasDodge かつ未見＝買ったあと最初に入った面の冒頭。ここは HasDodge だけを見る＝
    //     習得経路がどちらでも動く（本文はステージ名・主の名を出さないので、どの面で流れても嘘にならない）。
    //   ・溜め打ち … 2026-09-25 のユーザー決定で**最初から使える**（ショップの n_charge は「溜め打ち 二段」＝
    //     速さの強化になった）。あかり面では板の敵と戦ってから案内する。
    //     本文（SkillCharge）の1行目も同日に差し替え済み＝「この光に備わっている
    //     使い方」の提示で、習得・購入の含意は無い。
    //   ・溜め打ち2段目 … ショップの品目 n_charge「溜め打ち 二段」（HasChargeTier2）。発火条件は
    //     HasChargeTier2 かつ未見＝買ったあと最初に入った面の冒頭で、回避（SkillDodge）と同じ作法。
    //     既存の SkillCharge（1段目）は一行も変えない＝この4行は「その先がある」ことだけを足す。
    //   ・並ぶときは 回避 → 溜め打ち → 溜め打ち2段目。文面は互いを参照しないので単独でも成立。
    //   ・差し込みは各ステージ _step==1 の Concat 列で 道中チュートリアル①の直後・アンチャー紹介③の前
    //     ＝ステージ1と同じ「操作の説明が先」。FINAL（StageMina）は対象外（ミナが動けない場面で操作説明は
    //     成立しない＝ShowLine もカードを同期しない）。
    //   ・once はセーブ単位（once_skill_dodge / once_skill_charge / once_skill_charge2）。①③④と同じく
    //     結び手潜行のときだけ・消費も同条件（Take）＝他ジョブ潜行中は who=1（ミナ）の発話を出さない流儀に揃える。
    //   ・キー名は書かない＝どのボタンかは操作カード（SkillDodgeCues / SkillChargeCues / SkillCharge2Cues）が担う。
    private static readonly (int who, string text, string face)[] SkillDodge =
    {
        (0, "回避が使えるようになった。向かっている方向へ、一瞬で駆け抜けられる。方向を入れていなければ、その場で避けるよ。", ""),
        (1, "その間だけ、攻撃が当たらないのですね。", "res://char/mina_face.png"),
        (0, "うん。でも、連続では使えない。抜けた先が空いているか、先に見よう。", ""),
        (1, "逃げるために使っても？", "res://char/mina_face.png"),
        (0, "もちろん。無事でいるための力だよ。", ""),
    };

    private static readonly (int who, string text, string face)[] SkillCharge =
    {
        (0, "次はチャージ。押し続けると光が集まる。頭上の弧が満ちて、白く光ったら離して。", ""),
        (0, "板を貫く、重い一発になる。溜めている間は通常の光が止まるよ。溜まる前に離したときは、通常の光へ戻る。", ""),
        (1, "では、安全な場所を選んで試します。必殺技の名前は、帰ってからですね。", "res://char/mina_face.png"),
    };

    // ⑥ 2段階チャージの説明（2026-09-25）。ショップの n_charge（「溜め打ち 二段」1200）を買うと
    //   1段目の先にもう一段が開く（ChargeTier / GameManager.HasChargeTier2）。発火は HasChargeTier2 かつ
    //   未見＝買ったあと最初に入った面の冒頭（SkillDodge と同じ作法）。once は once_skill_charge2。
    //   既存の SkillCharge（1段目）は変更しない＝この4行は「その先がある」ことだけを足す。
    //   キー名は書かない（操作カードが担う）。数値も書かない＝倍率調整で嘘にならない。
    private static readonly (int who, string text, string face)[] SkillCharge2 =
    {
        (0, "チャージの二段目が開いた。白い合図でも離さず、もう少し待つ。同じ輪がもう一周満ちて、暖かな白で光ったら、二段目だ。", ""),
        (1, "強いぶん、溜める時間も長いのですね。", "res://char/mina_face.png"),
        (0, "うん。その間は通常の光が止まる。安全な場所を決めてから使おう。", ""),
        (1, "承知しました。欲張って、帰り道まで失わないようにします。", "res://char/mina_face.png"),
    };

    // ⑤の各行で出す操作カードの話題（本文の [card: ...] 注記どおり。RouteCues と同じ考え方＝
    //   1行目（提示）は畳み、押す・離すを語る 2〜4 行目は同じ話題を出しっぱなしにする）。
    private static readonly ControlCard.Topic[] SkillDodgeCues =
    { ControlCard.Topic.Dodge, ControlCard.Topic.Dodge, ControlCard.Topic.Dodge, ControlCard.Topic.Dodge, ControlCard.Topic.Dodge };
    private static readonly ControlCard.Topic[] SkillChargeCues =
    { ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge };
    // ⑥（2段目）の cue。ControlCard.Topic に Charge2 は無いので既存の Charge を流用する
    //   ＝どのボタンかは 1段目と同じ（押し続ける／離す）ため、カード面を分ける必要が無い。
    private static readonly ControlCard.Topic[] SkillCharge2Cues =
    { ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge };

    // カードを同期する本文ブロックの一覧（本文と cue の対）。SyncCard はここを順に引く。
    private static readonly ((int who, string text, string face)[] lines, ControlCard.Topic[] cues)[] CardBlocks =
    {
        (Route, RouteCues),
        (Lock, LockCues),
        (Bomb, BombCues),
        (SkillDodge, SkillDodgeCues),
        (SkillCharge, SkillChargeCues),
        (SkillCharge2, SkillCharge2Cues),
    };

    // ───────── 操作カード（盤面中央のウィンドウ）の同期 ─────────
    // 各ステージの ShowLine が「いま出した配列と行番号」を渡してくるだけ＝ステージ側はカードの存在を知らない。
    //   lines が道中チュートリアル（Route）や習得スキル説明（⑤）の行でない限り何もしない（＝通常の会話では
    //   一切出ない・他ジョブのキャラ別ストーリーにも混ざらない）。ただしこれらはイントロ末尾へ Concat されて
    //   渡るため、「いま出している行がどのブロックの何行目か」を実体参照（ReferenceEquals）で引き当てて話題を選ぶ。
    //   ※2026-09-17: 以前は「配列の末尾16行が Route」と決め打ちして添字をずらしていたが、Route の
    //     さらに後ろへアンチャー紹介（③）を Concat した途端に末尾が Route でなくなり、カードが一切
    //     出なくなる作りだった。各行は他所に無い固有の文字列リテラル＝参照一致で一意に引ける。
    //   カードは Hud（CanvasLayer）の子として遅延生成し、以後は使い回す。
    public static void SyncCard(Hud? hud, (int who, string text, string face)[] lines, int index)
    {
        if (hud == null) return;
        var card = hud.GetNodeOrNull<ControlCard>("ControlCard");
        var topic = ControlCard.Topic.None;
        bool found = false;
        if (index >= 0 && index < lines.Length)
            foreach (var (block, cues) in CardBlocks)
            {
                for (int i = 0; i < block.Length && i < cues.Length; i++)
                    if (ReferenceEquals(lines[index].text, block[i].text)) { topic = cues[i]; found = true; break; }
                if (found) break;
            }
        if (!found)
        {
            card?.Dismiss();   // チュートリアル以外の行に移った＝畳む（生成前なら何もしない）
            return;
        }
        card ??= ControlCard.Attach(hud);
        card.Show(topic);
    }

    // 道中開始時に一度だけ返す（返した瞬間に once を消費）。出さない条件では None（消費もしない）。
    public static (int who, string text, string face)[] TakeRoute(GameManager? game, bool basicsOnly = false)
    {
        var lines = Take(game, RouteSeenKey, Route);
        return basicsOnly ? lines : lines.Concat(TakeShield(game)).Concat(TakeLock(game)).Concat(TakeBomb(game)).ToArray();
    }

    public static (int who, string text, string face)[] TakeShield(GameManager? game) => Take(game, ShieldSeenKey, Shield);
    public static (int who, string text, string face)[] TakeLock(GameManager? game) => Take(game, LockSeenKey, Lock);
    public static (int who, string text, string face)[] TakeBomb(GameManager? game) => Take(game, BombSeenKey, Bomb);
    public static (int who, string text, string face)[] TakeCharge(GameManager? game) => Take(game, SkillChargeSeenKey, SkillCharge);

    // 本ボス出現時に一度だけ返す。同上。
    public static (int who, string text, string face)[] TakeBoss(GameManager? game) => Take(game, BossSeenKey, Boss);

    // アンチャー紹介（③）。道中開始時に面ごと一度だけ返す。①と同じく結び手潜行のときだけ・消費も同条件。
    public static (int who, string text, string face)[] TakeAnkerAkari(GameManager? game) => Take(game, AnkerAkariSeenKey, AnkerAkari);
    public static (int who, string text, string face)[] TakeAnkerKoharu(GameManager? game) => Take(game, AnkerKoharuSeenKey, AnkerKoharu);
    public static (int who, string text, string face)[] TakeAnkerRei(GameManager? game) => Take(game, AnkerReiSeenKey, AnkerRei);

    public static (int who, string text, string face)[] TakeItemIntroAkari(GameManager? game) => Take(game, ItemsAkariSeenKey, ItemsAkari);

    // 習得スキル説明（⑤⑥）。どのステージでも道中開始時に、未見のものだけを
    //   回避 → 溜め打ち → 溜め打ち2段目 の順で連結して返す。
    //   ・回避は未習得なら出さず once も消費しない＝買ったあと最初の面で必ず見られる。
    //   ・あかり面の溜め打ちは、実物が出たときに TakeCharge で受け取る。
    //   ・2段目は未購入（HasChargeTier2=false）なら出さず once も消費しない＝回避と同じ作法。
    //   結び手潜行のときだけ・消費も同条件（Take）。①の直後・③の前に繋ぐ。
    public static (int who, string text, string face)[] TakeSkillIntros(GameManager? game, bool includeCharge = true)
    {
        if (game == null) return None;
        var dodge = game.HasDodge ? Take(game, SkillDodgeSeenKey, SkillDodge) : None;
        var charge = includeCharge ? TakeCharge(game) : None;
        // 2段目（⑥・2026-09-25）はショップで n_charge を買ったあと最初に入った面の冒頭で一度だけ。
        var charge2 = game.HasChargeTier2 ? Take(game, SkillCharge2SeenKey, SkillCharge2) : None;
        var all = dodge;
        if (charge.Length > 0) all = all.Length == 0 ? charge : all.Concat(charge).ToArray();
        if (charge2.Length > 0) all = all.Length == 0 ? charge2 : all.Concat(charge2).ToArray();
        return all;
    }

    private static (int who, string text, string face)[] Take(
        GameManager? game, string key, (int who, string text, string face)[] lines)
    {
        if (game == null) return None;
        if (CharacterStory.DiveActive(game)) return None;   // 他ジョブ潜行＝出さない・once も消費しない
        if (game.IsIdleDialogSeen(key)) return None;        // セーブ単位で一度きり
        game.MarkIdleDialogSeen(key);                       // 表示が確定した瞬間に消費（次回セーブで永続）
        GD.Print($"[tutorial] {key} fired");                // ヘッドレスQAの発火確認用
        return lines;
    }
}
