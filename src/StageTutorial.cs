using Godot;
using System.Linq;

// StageTutorial : 初見チュートリアル会話（ミナ）の once 管理と本文の一元置き場（2026-09-16）。
//   ①道中 … セーブで最初にステージの道中へ入ったとき（各ステージ _Ready の _step==1 確定後、
//            イントロ会話の末尾へ Concat ＝道中ザコ戦が始まる直前に流れる）。
//   ②本ボス … 言葉の板（パネル）が周回する本ボス戦の初回開始時。Step_BossSpawn で消費し、
//            対面演出のあとに _playerBoss として表示する＝道中で倒れても初ボス到達まで温存。
//   once キーはセーブ単位（GameManager._idleDialogSeen）。"once_" 接頭辞は小話の既読リセット
//   （ResetIdleDialogSeen）でも消えない＝once_phone_home と同じ流儀。
//   ・結び手（ミナ潜行）のときだけ表示する。他ジョブのキャラ別ストーリー中は出さず、
//     once も消費しない＝結び手の初回で必ず見られる。
//   ・本文の正典: docs/20260916/フェイクレルム_チュートリアル本文_2026-09-16.md（ユーザー支給草稿準拠・
//     操作表記は HowToPlay/Player の実装が正典）。差し替えはこの配列だけ＝フック側は無改造。
//   ・2026-09-17: 操作表記（キー名の羅列）をミナのセリフから抜き、盤面中央の操作カード（ControlCard）へ
//     分離した。セリフは「何ができるか・なぜするか」、カードは「どのボタンか」を担う。行ごとの
//     出し分けは下の RouteCues（Route と同じ並び・同じ長さ）が持つ。
public static class StageTutorial
{
    public const string RouteSeenKey = "once_tutorial_route";
    public const string BossSeenKey = "once_tutorial_boss";
    public const string AnkerAkariSeenKey = "once_ankers_akari";
    public const string AnkerKoharuSeenKey = "once_ankers_koharu";
    public const string AnkerReiSeenKey = "once_ankers_rei";
    public const string ItemsAkariSeenKey = "once_items_akari";
    public const string SkillDodgeSeenKey = "once_skill_dodge";
    public const string SkillChargeSeenKey = "once_skill_charge";
    public const string SkillCharge2SeenKey = "once_skill_charge2";

    private const string MFace = "res://char/mina_face.png";
    private const string MWorried = "res://char/mina_worried.png";
    private static readonly (int who, string text, string face)[] None = System.Array.Empty<(int, string, string)>();

    private static readonly (int who, string text, string face)[] Route =
    {
        (0, "まず移動から。入力した方向へ進める。攻撃が当たるのは、身体の中心にあるコアだ。そこを守ろう。", ""),
        (1, "こちらですね。服の端をかすめるくらいなら、大丈夫、と。", "res://char/mina_face.png"),
        (0, "うん。光は自動で放たれる。最初は、よけることと狙うことに慣れよう。", ""),
        (1, "余計なボタンを探しておりました。少なくて助かります。", "res://char/mina_face.png"),
        (0, "アンチャーの周りを回る板は、アンフォールダー。まず、あれを砕く。浄化が進めば、本人の本音へ近づける。", ""),
        (1, "言葉を消すのではなく、塞いでいるものをほどくのですね。", "res://char/mina_face.png"),
        (0, "その通り。背後の相手を狙うときはロックオン。押すたびに近い相手から順に切り替わる。その間は少し移動が遅くなるよ。", ""),
        (1, "狙いに夢中で、ぶつからないようにします。", "res://char/mina_face.png"),
        (0, "解除すれば、すぐ元の動きに戻る。相手を浄化したあとは、次の相手へ狙いが移る。", ""),
        (0, "囲まれたらBOMB。弾とアンチャーをまとめて祓える。残数は見ておこう。", ""),
        (1, "温存したまま倒れては、元も子もありませんね。", "res://char/mina_face.png"),
        (0, "うん。使って帰ってきてくれるほうが、僕はうれしい。心の欠片も、拾える範囲で集めよう。帰ったあとに力へ変えられる。", ""),
        (1, "承知しました。危ないところは、遠慮なく教えてください。ご主人様。", "res://char/mina_face.png"),
    };

    private static readonly ControlCard.Topic[] RouteCues =
    { ControlCard.Topic.Move, ControlCard.Topic.Move, ControlCard.Topic.Shot, ControlCard.Topic.Shot, ControlCard.Topic.None, ControlCard.Topic.None, ControlCard.Topic.Lock, ControlCard.Topic.Lock, ControlCard.Topic.LockClear, ControlCard.Topic.Bomb, ControlCard.Topic.Bomb, ControlCard.Topic.None, ControlCard.Topic.None };

    // ② 本ボス戦チュートリアル（ブロック2・5行）。ボスの口上（who=2）の直後に続く。
    private static readonly (int who, string text, string face)[] Boss =
    {
        (0, "この先が、フェイクレルムの主だ。周りのアンフォールダーをほどけば、本人へ働きかけられる。", ""),
        (1, "あの方の姿まで、変わっているのですね。", "res://char/mina_face.png"),
        (0, "心を閉じ込めるものが、姿にも重なってる。アンフォールダーは時間がたつと戻るから、何度でもほどこう。", ""),
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
        (1, "立てる場所を、少しずつ削られますね。", "res://char/mina_face.png"),
        (0, "同じ場所に留まらず、空いた側へ。次は左が広い。", ""),
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
    //   最初の面（あかり）だけ。アンチャー紹介③の後ろへ繋ぐ＝「一般 → この面の敵 → 拾い物」の順。
    //   once はセーブ単位（once_items_akari）。①③と同じく結び手潜行のときだけ・消費も同条件（Take）。
    //   本文の正典: docs/20260922/強化アイテム説明_本文_2026-09-22.md。
    private static readonly (int who, string text, string face)[] ItemsAkari =
    {
        (0, "色の枠がある欠片は、拾うとその場で力になる。光が増えたり、速く動けたり、LIFEや身を守る力が増えたりする。", ""),
        (1, "お着替えなしで変われるのは、手軽ですね。", "res://char/mina_face.png"),
        (0, "変身中に待ってくれる相手でもないしね。効果は、左のパネルに出るよ。", ""),
        (1, "ご主人様の趣味で、長い変身シーンがついていなくて安心しました。", "res://char/mina_face.png"),
        (0, "……付けなくてよかった。攻撃を受けると効果が消えるから、そこは気をつけよう。", ""),
    };

    // ⑤ 習得スキルの説明（2026-09-22・ユーザー要望「回避とチャージショットが追加されたときはステージに
    //   入ったときに使い方を教える。説明の仕方は最初のステージ開始時と同じで」）。
    //   ・回避 … ショップの品目 n_dodge（HasDodge。2026-09-22 ユーザー指示で「1面クリアの物語報酬」から変更）。
    //     発火条件は HasDodge かつ未見＝買ったあと最初に入った面の冒頭。ここは HasDodge だけを見る＝
    //     習得経路がどちらでも動く（本文はステージ名・主の名を出さないので、どの面で流れても嘘にならない）。
    //   ・溜め打ち … 2026-09-25 のユーザー決定で**最初から使える**（ショップの n_charge は「溜め打ち 二段」＝
    //     速さの強化になった）。発火条件は「未見」だけ＝Route（①）と同じく最初に入った面の冒頭で必ず出る。
    //     本文（SkillCharge）の1行目も同日に差し替え済み＝「この光に備わっている
    //     使い方」の提示で、習得・購入の含意は無い（2〜4行目＝操作の説明は変更なし）。
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
        (1, "……今ですか。", "res://char/mina_face.png"),
        (0, "そう。板を貫く、重い一発になる。ただ、溜めている間は通常の光が止まるよ。", ""),
        (1, "必殺技の名前を考えている間に、囲まれそうですね。", "res://char/mina_face.png"),
        (0, "名前は帰ってから考えよう。溜まる前に離したときは、通常の光へ戻る。", ""),
        (1, "では、今は無言で。……えい。", "res://char/mina_face.png"),
    };

    // ⑥ 2段階チャージの説明（2026-09-25）。ショップの n_charge（「溜め打ち 二段」1200）を買うと
    //   1段目の先にもう一段が開く（ChargeTier / GameManager.HasChargeTier2）。発火は HasChargeTier2 かつ
    //   未見＝買ったあと最初に入った面の冒頭（SkillDodge と同じ作法）。once は once_skill_charge2。
    //   既存の SkillCharge（1段目）は変更しない＝この4行は「その先がある」ことだけを足す。
    //   キー名は書かない（操作カードが担う）。数値も書かない＝倍率調整で嘘にならない。
    private static readonly (int who, string text, string face)[] SkillCharge2 =
    {
        (0, "チャージの二段目が開いた。白い合図でも離さず、もう少し待つ。外側の弧が金色になったら、二段目だ。", ""),
        (1, "強いぶん、溜める時間も長いのですね。", "res://char/mina_face.png"),
        (0, "うん。その間は通常の光が止まる。安全な場所を決めてから使おう。", ""),
        (1, "承知しました。欲張って、帰り道まで失わないようにします。", "res://char/mina_face.png"),
    };

    // ⑤の各行で出す操作カードの話題（本文の [card: ...] 注記どおり。RouteCues と同じ考え方＝
    //   1行目（提示）は畳み、押す・離すを語る 2〜4 行目は同じ話題を出しっぱなしにする）。
    private static readonly ControlCard.Topic[] SkillDodgeCues =
    { ControlCard.Topic.Dodge, ControlCard.Topic.Dodge, ControlCard.Topic.Dodge, ControlCard.Topic.Dodge, ControlCard.Topic.Dodge };
    private static readonly ControlCard.Topic[] SkillChargeCues =
    { ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge };
    // ⑥（2段目）の cue。ControlCard.Topic に Charge2 は無いので既存の Charge を流用する
    //   ＝どのボタンかは 1段目と同じ（押し続ける／離す）ため、カード面を分ける必要が無い。
    private static readonly ControlCard.Topic[] SkillCharge2Cues =
    { ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge, ControlCard.Topic.Charge };

    // カードを同期する本文ブロックの一覧（本文と cue の対）。SyncCard はここを順に引く。
    private static readonly ((int who, string text, string face)[] lines, ControlCard.Topic[] cues)[] CardBlocks =
    {
        (Route, RouteCues),
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
    public static (int who, string text, string face)[] TakeRoute(GameManager? game) => Take(game, RouteSeenKey, Route);

    // 本ボス出現時に一度だけ返す。同上。
    public static (int who, string text, string face)[] TakeBoss(GameManager? game) => Take(game, BossSeenKey, Boss);

    // アンチャー紹介（③）。道中開始時に面ごと一度だけ返す。①と同じく結び手潜行のときだけ・消費も同条件。
    public static (int who, string text, string face)[] TakeAnkerAkari(GameManager? game) => Take(game, AnkerAkariSeenKey, AnkerAkari);
    public static (int who, string text, string face)[] TakeAnkerKoharu(GameManager? game) => Take(game, AnkerKoharuSeenKey, AnkerKoharu);
    public static (int who, string text, string face)[] TakeAnkerRei(GameManager? game) => Take(game, AnkerReiSeenKey, AnkerRei);

    // 強化アイテム説明（④）。あかり面の道中開始時に一度だけ返す（③の直後に繋ぐ）。結び手潜行のときだけ・消費も同条件。
    public static (int who, string text, string face)[] TakeItemIntroAkari(GameManager? game) => Take(game, ItemsAkariSeenKey, ItemsAkari);

    // 習得スキル説明（⑤⑥）。どのステージでも道中開始時に、未見のものだけを
    //   回避 → 溜め打ち → 溜め打ち2段目 の順で連結して返す。
    //   ・回避は未習得なら出さず once も消費しない＝買ったあと最初の面で必ず見られる。
    //   ・溜め打ちは最初から使える（2026-09-25）＝習得を待たず、最初の面の冒頭で必ず出る。
    //   ・2段目は未購入（HasChargeTier2=false）なら出さず once も消費しない＝回避と同じ作法。
    //   結び手潜行のときだけ・消費も同条件（Take）。①の直後・③の前に繋ぐ。
    public static (int who, string text, string face)[] TakeSkillIntros(GameManager? game)
    {
        if (game == null) return None;
        var dodge = game.HasDodge ? Take(game, SkillDodgeSeenKey, SkillDodge) : None;
        // 溜め打ちは最初から使える（2026-09-25）＝習得を待たず、最初に入った面の冒頭で必ず一度出す。
        //   Route（道中チュートリアル①）と同じ扱いになった＝条件は「未見」だけ。
        var charge = Take(game, SkillChargeSeenKey, SkillCharge);
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
