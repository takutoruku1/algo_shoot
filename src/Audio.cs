using Godot;
using System.Collections.Generic;

// Audio : サウンド再生のオートロード (/root/Audio)。
//   FxLayer と同じ「静的 Instance 経由で各所から呼ぶ」流儀。シーンをまたいで常駐する。
//   SE は FxLayer の視覚フック（Muzzle/PlayerHit/PurifyBurst…）の隣で
//     Audio.Instance?.Se(stream) と鳴らし、画と音を同フレームで揃える（mitsuda style §4）。
//   バスは default_bus_layout.tres（Master/Music/SE/Voice/Amb/Alert）。
//   --qa 時は自己ミュート（QaPilot の無音・高速実行を妨げない / pitfalls P9）。
//
// ※この段階では「鳴らす土台」のみ。実音源（.ogg等）は別途調達して各所で差し込む。
public partial class Audio : Node
{
    public static Audio Instance = null!;

    public bool Muted;                 // QA等で全再生を止める

    private const int SePoolSize = 12;     // 同時発音（弾幕で飽和しても破綻しない）
    private const int AlertPoolSize = 3;   // 被弾など最優先音
    private const int VoicePoolSize = 3;   // テキスト送り音（重ならない＝2〜3で十分）
    private const float SilentDb = -60f;   // フェード時の実質無音

    // 実マスタリング音源（道中の実音源 BGM 等）に乗せる固定の音量オフセット（VolumeDb）。
    //   ユーザー制作の実音源はピーク 0dB 近くで焼かれており、コード合成 BGM（全体ゲインを
    //   かなり絞ってある）より明確に大きく鳴る。再生ターゲット音量をこの分だけ下げて粒を揃える。
    //   合成BGM（AudioStreamWav）は従来どおり 0dB のまま＝下げない。実音源だけに適用する。
    //   バス側（Music スライダー／汚染連動 LowPass）は一切触らず、トラックの VolumeDb で吸収する。
    //   微調整しやすいよう定数化（負が深いほど静か）。
    private const float StageBgmRealDb = -10f;

    private readonly List<AudioStreamPlayer> _sePool = new();
    private readonly List<AudioStreamPlayer> _alertPool = new();
    private readonly List<AudioStreamPlayer> _voicePool = new();
    private AudioStreamPlayer _musicA = null!, _musicB = null!;
    private bool _useA = true;

    // 適応演出：再生中の音楽の PitchScale を滑らかに上げ下げする（レイ戦 HP20%以下で加速）。
    //   PitchScale を直に動かすと跳ねる（ポップ）ので Tween で 0.5〜0.8秒かけて寄せる。
    //   別曲へクロスフェードしたら 1.0 に戻す（Music() でリセット）＝ボス戦を抜けたら通常速度。
    //   _musicSpeed は「次に Play する音楽プレイヤーへ適用する目標速度」。通常 1.0。
    private Tween? _musicSpeedTween;
    private float _musicSpeed = 1f;
    private readonly RandomNumberGenerator _rng = new();

    // ───────── アダプティブ（汚染ゲージ連動。設計 §3-3 / 1-5「連続=フィルタ」）─────────
    //   ステージ中だけ、汚染が進むほど BGM の高域を削り（こもらせ）、心象の「濁りパッド」を増す。
    //   浄化(Warmth)が進むと晴れる方向へ戻す。すべて毎フレーム指数補間＝ジッパーノイズ回避。
    //   メニュー等ステージ外では cutoff 開放（透過）・パッド無音へ戻す（こもったまま残さない）。
    private AudioStreamPlayer _ambPad = null!;          // Amb バスに常駐する濁りパッド
    public AudioStreamWav MurkPad = null!;              // その音源（シームレスループ）
    private AudioStream? _currentMusic;                 // 今鳴っている BGM（ステージ判定に使う）
    // 今プレイ中のステージ id（BeginStageRun が設定）。中ボス撃破後の道中復帰で正しい道中曲を引くのに使う。
    public string CurrentStageId { get; private set; } = "";
    private float _murkCutoff = MurkCutoffOpen;         // 現在の LowPass カットオフ（平滑後）
    private float _ambDb = SilentDb;                    // 現在の Amb 音量dB（平滑後）
    private const float MurkCutoffOpen = 16000f;        // 汚染0＝ほぼ透過（高域を通す）
    private const float MurkCutoffMurk = 800f;          // 汚染1＝こもる（設計 §3-3 の下限）
    private const float AmbDbMax = -12f;                // 汚染1で聞こえる程度（控えめ上限）
    private const float MurkSmooth = 4.0f;              // 平滑の速さ（1/秒。大きいほど速く追従）

    // コアSE（コードで合成したプレースホルダ。実音源が来たら差し替える）。
    public AudioStreamWav SfxShot = null!, SfxGraze = null!, SfxHit = null!, SfxPurify = null!;

    // 拡張SE（設計書 ③④⑥⑦⑧⑩）。同じくプレースホルダ。
    public AudioStreamWav SfxBomb = null!, SfxOverload = null!, SfxCalm = null!,
                          SfxSpell = null!, SfxStrip = null!;
    // ボス無防備窓の本体ヒット専用（通常／大威力）。剥離(SfxStrip)・自機被弾(SfxHit)と音域を分ける。
    public AudioStreamWav SfxBossHit = null!, SfxBossHitHeavy = null!;
    // UI操作音（カーソル/決定/キャンセル/購入成功/失敗）。全画面で共通＝一貫性。
    public AudioStreamWav SfxUiMove = null!, SfxUiConfirm = null!, SfxUiCancel = null!,
                          SfxUiBuy = null!, SfxUiDeny = null!;

    // 会話タイプライター送り音（Voiceバス。設計 1-4「話者で音色を変える」）。
    //   少年=温かい木質 / ミナ=澄んだガラス / ボス（穢れ）=低くくぐもり。ナレ=無音。
    //   ごく短い（10〜25ms）ブリップ。1〜2文字に1回だけ・ピッチ微ゆらぎで機械反復を避ける。
    public AudioStreamWav TypBoy = null!, TypMina = null!, TypBoss = null!;

    // BGM。BgmStage/BgmBoss はコード合成のループ（実音源が来たら差し替える）。
    //   全曲で M.I.N.A. モチーフ（ド ミ レ ソ＝523/659/587/784Hz）を共有し「一つの主題の変奏」に。
    //   BgmMenu=タイトル/ハブ等の温かい曲 / BgmStage=道中 / BgmBoss=短調寄り・密度高め・緊張（汎用＝Mina/Hikage 用）。
    //   BgmMenu はユーザー制作の実音源（res://audio/bgm_menu_mina.ogg）を読む。型は実ファイル/合成の
    //   どちらでも受けられるよう AudioStream に広げる（参照側は Music(BgmMenu) で渡すだけなので不変）。
    //   ロード失敗時は従来のコード合成 BuildBgmMenu() にフォールバックする（事故防止）。
    public AudioStream BgmMenu = null!;
    public AudioStreamWav BgmStage = null!, BgmBoss = null!;

    // ステージ1（レイ）の道中＝ユーザー制作の実音源（res://BGM/The_Watcher_in_the_Hall.mp3）。
    //   BgmMenu と同じ作法：実ファイルを読み、失敗時は従来の合成 BgmStage にフォールバック（型を
    //   AudioStream に広げる）。汚染連動の高域カットに乗せるため _Process の inStage 判定にも含める。
    //   mp3 は import 設定 loop=true で焼いてあるが、念のため AudioStreamMP3.Loop も明示する
    //   （Music() は曲尾で止めず鳴らしっぱなしにするため）。
    //   将来 akari/koharu の道中実音源が来たら StageBgm() のマップに足すだけにできる。
    public AudioStream BgmStageRei = null!;

    // ステージ2（あかり）／ステージ3（こはる）の道中＝ユーザー制作の実音源。
    //   akari ＝res://audio/bgm_stage_akari.ogg（原曲 Empty_Desks_at_Four 65秒の末尾フェードを
    //           トリムした 0..61.0秒・-3dB・頭40ms/尻120ms フェードでループ継ぎ目をなじませた ogg）。
    //   koharu＝res://audio/bgm_stage_koharu.ogg（原曲 The_Kettle_Stays_Warm 61秒の末尾フェードを
    //           トリムした 0..57.5秒・-3dB・同様の極小フェード）。いずれも import 設定 loop=true。
    //   BgmStageRei と同じ作法：ogg は loop を焼いてあるが念のため AudioStreamOggVorbis.Loop も明示し、
    //   ロード失敗時は従来の合成 BgmStage にフォールバック（事故防止）。StageBgm() のマップに足す。
    //   汚染連動の高域カット（_Process の inStage 判定）にも含める＝レイ道中と同じ「ステージ中」扱い。
    public AudioStream BgmStageAkari = null!;
    public AudioStream BgmStageKoharu = null!;

    // Final の「音楽的解決」（設計 §1-3「感情の節目で帰ってくる」/ §7「無音→解決音」）。
    //   Final は濁った BgmBoss を全編流し、感情が音楽的に解決しないまま終わっていた。
    //   ミナの反転号令の直前で BgmBoss を切って完全無音にし、「返事は、ありませんでした。」の
    //   一行と同時にこの曲を ppp で立ち上げる。沈黙→解決音の落差が決定打。
    //   主題 M.I.N.A.（ド ミ レ ソ＝C E D G）を、戦闘で「未完／濁り」だった姿から
    //   主音 C へ解決させる温かい長調の変奏（BgmMenu と同じ C-Am-F-G の和声圏）。
    //   Epilogue が BgmMenu へクロスフェードするので、同じ主題・同じ和声圏で自然に橋渡しされる。
    //   ※本物のボーカル挿入歌が来たら、この BgmFinalResolve を実音源に差し替えれば成立する。
    public AudioStreamWav BgmFinalResolve = null!;

    // ボス別の戦闘BGM（設計 §1-2「各ボスの固有モチーフ＝未完→完」）。
    //   いずれも M.I.N.A. 構成音ベース。戦闘中はモチーフが「未完」で、改心で PlayRedeem が「完」を返す。
    //   Rei  ＝主音直前で落ちる（半音で届かない／順位＝あと一歩で一番になれない）。
    //   Akari＝フレーズが途中で切れる（"す——"／言いかけて言えない好き）。
    //   Koharu＝温かい旋律が冷えて減衰する（台所の灯が消えていく／祈りが冷える）。
    //   Rei は戦闘BGMをユーザー制作の実音源（res://audio/bgm_boss_rei.ogg）に差し替え（型を
    //   AudioStream に広げる。BgmMenu/BgmStageRei と同じ作法でロード失敗時は合成へフォールバック）。
    //   Akari もユーザー制作の実音源（res://BGM/Akari_s_Last_Corridor.mp3, loop=true）に差し替え。
    //   Koharu もユーザー制作の実音源（res://BGM/The_Leaking_Tap.mp3, loop=true）に差し替え。
    public AudioStream BgmBossRei = null!;
    public AudioStream BgmBossAkari = null!;
    public AudioStream BgmBossKoharu = null!;

    // ラスボス（ミナ本体）の戦闘BGM＝ユーザー制作の実音源（res://audio/bgm_boss_mina.ogg）。
    //   原曲 The_Weight_Of_Absolution（149秒）は頭フェードイン・尻フェードアウトの劇伴構造で、
    //   そのままだとループで無音の谷ができる。フル強度で鳴り続ける本体区間 51.0..84.5秒（33.5秒）を
    //   切り出し、尻 0.6秒を頭 0.6秒へクロスフェード（acrossfade tri/tri）で折り込んでシームレス
    //   ループ化した（-3dB で他BGMと粒を揃え、import 設定 loop=true）。
    //   ※汎用 BgmBoss（Final/ヒカゲ）は変えず、BossMina.cs のミナ戦本体だけこの専用曲に差し替える。
    //   BgmBossRei と同じ作法：ロード失敗時は汎用合成 BgmBoss にフォールバック。
    //   _Process の inStage 判定にも含める＝「ステージ戦闘曲」として汚染LowPassの対象。
    public AudioStream BgmBossMina = null!;

    // 改心の「解決音（完）」。OnCryStart で戦闘BGMから温かくクロスフェードして鳴らす一節。
    //   3ボスとも主題 M.I.N.A.（C/E/D/G）の構成音に解決する＝Epilogue で主題に溶ける布石。
    public AudioStreamWav RedeemRei = null!, RedeemAkari = null!, RedeemKoharu = null!;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // ポーズ中も音は鳴らせる
        _rng.Randomize();

        // --qa は無音・高速で回す。autoload 初期化順に依存しないよう自分で判定する。
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--qa") { Muted = true; break; }

        for (int i = 0; i < SePoolSize; i++) _sePool.Add(MakePlayer("SE"));
        for (int i = 0; i < AlertPoolSize; i++) _alertPool.Add(MakePlayer("Alert"));
        for (int i = 0; i < VoicePoolSize; i++) _voicePool.Add(MakePlayer("Voice"));
        _musicA = MakePlayer("Music");
        _musicB = MakePlayer("Music");
        // 濁りパッドは Amb バスでステージ中ずっとループ。初期は実質無音から立ち上げる。
        _ambPad = MakePlayer("Amb");
        _ambPad.VolumeDb = SilentDb;

        SfxShot   = SynthShot();
        SfxGraze  = SynthGraze();
        SfxHit    = SynthHit();
        SfxPurify = SynthPurify();
        SfxBomb     = SynthBomb();
        SfxOverload = SynthOverload();
        SfxCalm     = SynthCalm();
        SfxSpell    = SynthSpell();
        SfxStrip    = SynthStrip();
        SfxBossHit      = SynthBossHit();
        SfxBossHitHeavy = SynthBossHitHeavy();
        SfxUiMove    = SynthUiMove();
        SfxUiConfirm = SynthUiConfirm();
        SfxUiCancel  = SynthUiCancel();
        SfxUiBuy     = SynthUiBuy();
        SfxUiDeny    = SynthUiDeny();
        TypBoy  = SynthTypeBoy();
        TypMina = SynthTypeMina();
        TypBoss = SynthTypeBoss();
        BgmMenu   = LoadBgmMenu();
        BgmStage  = BuildBgm();
        BgmStageRei    = LoadBgmStageRei();
        BgmStageAkari  = LoadBgmStageAkari();
        BgmStageKoharu = LoadBgmStageKoharu();
        BgmBoss   = BuildBgmBoss();
        BgmFinalResolve = BuildBgmFinalResolve();
        BgmBossRei    = LoadBgmBossRei();
        BgmBossAkari  = LoadBgmBossAkari();
        BgmBossKoharu = LoadBgmBossKoharu();
        BgmBossMina   = LoadBgmBossMina();
        RedeemRei    = BuildRedeem(0);
        RedeemAkari  = BuildRedeem(1);
        RedeemKoharu = BuildRedeem(2);
        MurkPad   = BuildMurkPad();

        // 濁りパッドはステージ判定で音量を抜き差しするだけ＝常時ループ再生で位相を保つ。
        // --qa（Muted）では負荷/無音前提を壊さないよう再生しない。
        if (!Muted)
        {
            _ambPad.Stream = MurkPad;
            _ambPad.Play();
        }

        // 保存済みの音量設定をバスへ適用（設定画面を開かなくても効く／再起動でも保たれる）。
        AudioConfig.ApplySaved();
        // 保存済みのコントローラ表記スタイル（Xbox/PS）を復元（HUD の操作ガイドへ即反映）。
        Pad.ApplySaved();
    }

    private AudioStreamPlayer MakePlayer(string bus)
    {
        var p = new AudioStreamPlayer { Bus = bus, VolumeDb = 0f };
        AddChild(p);
        return p;
    }

    // ───────── アダプティブ更新（汚染→こもり＋濁りパッド。設計 §3-3 / 1-5）─────────
    //   毎フレーム、目標値（汚染/Warmth から算出）へ指数補間で滑らかに寄せる＝ジッパー回避。
    //   ステージ外（メニュー/カットシーン）では cutoff 開放・パッド無音へ戻す。
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // 補間係数：1 - e^(-k·dt)。フレームレートに依らず一定の追従感。
        float a = 1f - Mathf.Exp(-MurkSmooth * dt);

        // ステージ中だけ濁す。それ以外（メニュー等）は開放・無音を目標にする。
        // ボス別テーマ（Rei/Akari/Koharu）も「ステージ戦闘曲」＝汚染LowPassの対象に含める。
        bool inStage = !Muted && (_currentMusic == BgmStage || _currentMusic == BgmStageRei
                                  || _currentMusic == BgmStageAkari || _currentMusic == BgmStageKoharu
                                  || _currentMusic == BgmBoss
                                  || _currentMusic == BgmBossRei || _currentMusic == BgmBossAkari
                                  || _currentMusic == BgmBossKoharu || _currentMusic == BgmBossMina);

        float targetCutoff = MurkCutoffOpen;
        float targetAmbDb  = SilentDb;

        if (inStage)
        {
            var gm = GetNodeOrNull<GameManager>("/root/Game");
            if (gm != null)
            {
                // 実効汚染：浄化(Warmth)が進むほど晴らす方向へ割り引く（設計の「晴れる」）。
                float murk = Mathf.Clamp(gm.Contamination - 0.3f * gm.Warmth, 0f, 1f);
                // 汚染0で透過(高め)／汚染1でこもる(800Hz)。聴感に合わせ指数寄りで下げる。
                targetCutoff = Mathf.Lerp(MurkCutoffOpen, MurkCutoffMurk, murk * murk * 0.5f + murk * 0.5f);
                // 濁りパッド：汚染0かつ晴れていればほぼ無音、汚染1で控えめに聞こえる。
                targetAmbDb  = murk <= 0.001f ? SilentDb : Mathf.Lerp(SilentDb, AmbDbMax, murk);
            }
        }

        _murkCutoff = Mathf.Lerp(_murkCutoff, targetCutoff, a);
        _ambDb      = Mathf.Lerp(_ambDb, targetAmbDb, a);

        // Music バスの LowPass（effect slot 0）を駆動。null/型不一致は安全に握りつぶす。
        int mb = AudioServer.GetBusIndex("Music");
        if (mb >= 0 && AudioServer.GetBusEffect(mb, 0) is AudioEffectLowPassFilter lp)
            lp.CutoffHz = _murkCutoff;

        if (_ambPad != null) _ambPad.VolumeDb = _ambDb;
    }

    // ───────── SE（効果音）─────────
    // FxLayer の視覚フックと同フレームで鳴らす。プール枯渇時は最古を奪う。
    public void Se(AudioStream stream, float volDb = 0f, float pitch = 1f)
        => Play(_sePool, stream, volDb, pitch);

    // ───────── Alert（被弾・残機・段階移行など最優先音）─────────
    public void AlertSe(AudioStream stream, float volDb = 0f, float pitch = 1f)
        => Play(_alertPool, stream, volDb, pitch);

    // ───────── Voice（テキスト送り音・ボイス）─────────
    // 「ボイス」スライダー（Settings key="voice"）が効く専用バスで鳴らす。
    public void VoiceSe(AudioStream stream, float volDb = 0f, float pitch = 1f)
        => Play(_voicePool, stream, volDb, pitch);

    private void Play(List<AudioStreamPlayer> pool, AudioStream stream, float volDb, float pitch)
    {
        if (Muted || stream == null || pool.Count == 0) return;
        AudioStreamPlayer? p = null;
        for (int i = 0; i < pool.Count; i++)
            if (!pool[i].Playing) { p = pool[i]; break; }
        p ??= pool[0]; // 空きが無ければ最古を奪う＝飽和しても破綻しない
        p.Stream = stream;
        p.VolumeDb = volDb;
        p.PitchScale = pitch;
        p.Play();
    }

    // ───────── BGM ─────────
    // 実マスタリング音源かどうかで「再生ターゲット音量」を出し分ける。
    //   合成BGM（AudioStreamWav）＝従来どおり 0dB。
    //   実音源（mp3/ogg をロードした AudioStreamMP3 / AudioStreamOggVorbis）＝StageBgmRealDb 下げる。
    //   ※ロード失敗時のフォールバックは AudioStreamWav なので自動的に「合成扱い＝下げない」。
    //   BgmMenu（実音源 ogg）はユーザー指摘外（道中）なので対象外に除外し、やり過ぎない。
    //   将来 akari/koharu の道中実音源が来ても mp3/ogg なら自動でこのオフセットに乗る。
    private float MusicTargetDb(AudioStream? stream)
    {
        if (stream == null) return 0f;
        if (stream == BgmMenu) return 0f; // メニュー実音源は今回対象外
        bool isReal = stream is AudioStreamMP3 || stream is AudioStreamOggVorbis;
        return isReal ? StageBgmRealDb : 0f;
    }

    // クロスフェードでループ曲を差し替える。同じ曲なら何もしない。stream=null で停止。
    public void Music(AudioStream? stream, float fade = 1.0f)
    {
        if (Muted) return;
        var cur = _useA ? _musicA : _musicB;
        var nxt = _useA ? _musicB : _musicA;
        if (cur.Stream == stream && cur.Playing) return;

        _currentMusic = stream; // ステージ判定（BgmStage/BgmBoss なら濁し有効）に使う

        // 別曲へ切り替わるので再生速度を通常（1.0）へリセット＝ボス戦を抜けたら加速も解除。
        _musicSpeedTween?.Kill();
        _musicSpeedTween = null;
        _musicSpeed = 1f;
        cur.PitchScale = 1f;

        if (stream != null)
        {
            nxt.Stream = stream;
            nxt.VolumeDb = SilentDb;
            nxt.PitchScale = 1f;
            nxt.Play();
        }
        var t = CreateTween().SetParallel();
        // 実音源は StageBgmRealDb 下げたターゲットへフェードイン（合成は 0dB）。
        // クロスフェード／ResumeStageMusic／PlayRedeem 等もこの Music() 経由なので一律で効く。
        if (stream != null) t.TweenProperty(nxt, "volume_db", MusicTargetDb(stream), fade);
        if (cur.Playing)
        {
            t.TweenProperty(cur, "volume_db", SilentDb, fade);
            t.Chain().TweenCallback(Callable.From(() => { if (cur.Playing) cur.Stop(); }));
        }
        _useA = !_useA;
    }

    public void StopMusic(float fade = 0.5f) => Music(null, fade);

    // ───────── 適応演出：再生中の音楽の再生速度（PitchScale）を滑らかに変える ─────────
    //   レイ戦で HP20%以下になった瞬間に SetMusicSpeed(1.15f) を1回呼ぶ＝曲が加速する（緊迫）。
    //   即値で跳ねさせず ramp 秒（既定0.6s）かけて寄せる＝ポップ感を避ける。ピッチも少し上がる
    //   （ユーザー合意済み）。クロスフェードで別曲に切り替わると Music() が 1.0 に戻す。
    //   この曲（Music バス）の高域カット（汚染連動 LowPass, _Process）とは独立に効く＝両立する。
    public void SetMusicSpeed(float scale, float ramp = 0.6f)
    {
        if (Muted) return;
        _musicSpeed = scale;
        var cur = _useA ? _musicA : _musicB;   // 直近に立ち上げた“今鳴っている”音楽プレイヤー
        _musicSpeedTween?.Kill();
        _musicSpeedTween = CreateTween();
        _musicSpeedTween.TweenProperty(cur, "pitch_scale", scale, ramp)
                        .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    // ───────── 改心の解決音（完）。設計 §1-2「未完→完」─────────
    //   改心が始まる確実な瞬間（各ボス OnCryStart）から boss=0:レイ/1:あかり/2:こはる で呼ぶ。
    //   戦闘BGM（未完モチーフ）を温かい解決アレンジへ素早くクロスフェードし、
    //   そのループに主題 M.I.N.A. 構成音へ解決する一節を内包させる。破壊でなく「届いた」。
    //   退場後（OnCryEnd→次シーン）は通常の Music()/StopMusic() がそのまま上書きするので破綻しない。
    public void PlayRedeem(int boss)
    {
        if (Muted) return;
        AudioStream? redeem = boss switch
        {
            0 => RedeemRei,
            1 => RedeemAkari,
            2 => RedeemKoharu,
            _ => null,
        };
        if (redeem == null) return;
        // 温かい解決へ少し速め（0.8s）に橋渡し。汚染LowPassの対象外なので、こもらず晴れて鳴る。
        Music(redeem, fade: 0.8f);
    }

    // ───────── Final の音楽的解決（設計 §7「無音→解決音」一点投入）─────────
    //   Final.cs から「返事は、ありませんでした。」の表示と同時に呼ぶ。直前で StopMusic 済み＝無音から立ち上げる。
    //   ppp で始め、ゆっくり（既定 4.0s）クロスフェードイン。BgmMenu と同じ和声圏なので
    //   Epilogue の BgmMenu へ自然に溶ける（Epilogue 側 Music(BgmMenu) がそのまま橋渡し）。
    //   汚染LowPassの対象外（_currentMusic 判定に含めない）ので、こもらず晴れて鳴る。
    public void PlayFinalResolve(float fade = 4.0f)
    {
        if (Muted) return;
        Music(BgmFinalResolve, fade: fade);
    }

    // ───────── コアSE 再生（呼び出し側はこれだけ叩く）─────────
    // 発射：短く・減衰速く・ピッチ微ゆらぎ。全開中は高揚（ピッチ上げ）。
    public void PlayShot(bool overload)
        => Se(SfxShot, volDb: -24f, pitch: _rng.RandfRange(0.97f, 1.03f) + (overload ? 0.18f : 0f));
    // グレイズ：鋭く高い「チッ」。被弾と音域を分け、混同させない。
    public void PlayGraze()
        => Se(SfxGraze, volDb: -22f, pitch: _rng.RandfRange(0.98f, 1.05f));
    // 被弾：低いノイズバースト。最優先（Alertバス）で弾幕に埋もれさせない。
    public void PlayHit()
        => AlertSe(SfxHit, volDb: -8f);
    // 浄化：減衰の長いベル。「壊した」でなく「解けた・届いた」温かい余韻。
    public void PlayPurify()
        => Se(SfxPurify, volDb: -14f, pitch: _rng.RandfRange(0.99f, 1.02f));

    // ───────── 拡張SE（③④⑥⑦⑧⑩）─────────
    // ③ボム：一拍の溜め→開放の二段。破壊でなく「鎮める／光が満ちる」。
    public void PlayBomb()
        => Se(SfxBomb, volDb: -10f);
    // ⑥全開発動：上昇音＋光が満ちるジングル。ピークの告知。
    public void PlayOverload()
        => Se(SfxOverload, volDb: -12f);
    // ⑦会話開始の弾消去：「鎮まる音」。沈黙を転換演出に変える。
    public void PlayCalm()
        => Se(SfxCalm, volDb: -16f);
    // ⑩スペル宣言・段階移行：短い宣言音。弾幕変化を耳で予告。被弾の下・グレイズの上＝Alert。
    public void PlaySpell()
        => AlertSe(SfxSpell, volDb: -14f);
    // ④パネル剥がし：軽い「コツッ」。浄化成立（PlayPurify）より一段軽い剥離音。
    public void PlayStrip()
        => Se(SfxStrip, volDb: -22f, pitch: _rng.RandfRange(0.97f, 1.04f));
    // ⑪本体ヒット（無防備窓の撃ち込み）：中低域の「刺さる／ドスッ」。
    //   dmg>=3 は低音を重ねた重い版＝GameCamera.Shake と同期して決定打を映す。
    //   連射で毎フレーム鳴りうるので小音量・ピッチ微ゆらぎ。SEバス（剥離・被弾と音域を分ける）。
    public void PlayBossHit(int dmg)
    {
        if (dmg >= 3)
            Se(SfxBossHitHeavy, volDb: -12f, pitch: _rng.RandfRange(0.97f, 1.02f));
        else
            Se(SfxBossHit, volDb: -16f, pitch: _rng.RandfRange(0.96f, 1.04f));
    }

    // ───────── 会話タイプライター送り音（Voiceバス。設計 1-4「話者で音色」）─────────
    //   少年=温かい木 / ミナ=澄んだガラス / ボス=くぐもり / ナレ=無音。
    //   Relay（少年＝ミナの声）は少年寄り、Post（X投稿）はごく控えめ。
    //   Hud のタイプライターから 1〜2文字に1回だけ呼ぶ（毎文字は鳴らしすぎ／Hud側で間引く）。
    //   ピッチを毎回 ±数% 微ゆらぎさせ、機械的な反復を避ける。
    public void PlayType(Hud.LineKind kind)
    {
        float p = _rng.RandfRange(0.97f, 1.03f);
        switch (kind)
        {
            case Hud.LineKind.Boy:   VoiceSe(TypBoy,  volDb: -18f, pitch: p); break;
            case Hud.LineKind.Mina:  VoiceSe(TypMina, volDb: -19f, pitch: p); break;
            case Hud.LineKind.Other: VoiceSe(TypBoss, volDb: -18f, pitch: p); break;
            case Hud.LineKind.Relay: VoiceSe(TypBoy,  volDb: -19f, pitch: p * 1.04f); break; // 少年寄り・やや高く
            case Hud.LineKind.Post:  VoiceSe(TypMina, volDb: -28f, pitch: p * 1.08f); break; // ごく控えめ・素っ気ない
            default: return; // Narration＝無音（語りとセリフを耳で区別）
        }
    }

    // ───────── UI操作音（⑧。全画面共通＝一貫性）─────────
    public void PlayUiMove()    => Se(SfxUiMove,    volDb: -20f, pitch: _rng.RandfRange(0.99f, 1.02f));
    public void PlayUiConfirm() => Se(SfxUiConfirm, volDb: -14f);
    public void PlayUiCancel()  => Se(SfxUiCancel,  volDb: -16f);
    public void PlayUiBuy()     => Se(SfxUiBuy,     volDb: -12f);
    public void PlayUiDeny()    => Se(SfxUiDeny,    volDb: -14f);

    // ───────── 波形合成（16bit PCM の AudioStreamWav を生成）─────────
    private const int Rate = 44100;

    private static AudioStreamWav MakeWav(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = bytes,
        };
    }

    // 発射：820→440Hz に落ちる短い矩形寄りブリップ（~55ms）。
    private AudioStreamWav SynthShot()
    {
        float dur = 0.04f; int n = (int)(Rate * dur);
        var s = new float[n]; float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float freq = Mathf.Lerp(760f, 420f, t / dur);
            phase += freq / Rate;
            float v = Mathf.Sin(phase * Mathf.Tau);
            float sq = v >= 0f ? 1f : -1f;
            float env = Mathf.Exp(-t / 0.012f);
            // ほぼ正弦（刺さる倍音を最小化）＋短く＋小さく
            s[i] = (0.12f * sq + 0.88f * v) * env * 0.3f;
        }
        return MakeWav(s);
    }

    // グレイズ：2600Hz の鋭い高音＋倍音、極短（~38ms）。
    private AudioStreamWav SynthGraze()
    {
        float dur = 0.03f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.008f);
            float a = Mathf.Sin(Mathf.Tau * 2100f * t);       // 少し低く（刺さりを抑える）
            float b = Mathf.Sin(Mathf.Tau * 4200f * t) * 0.12f; // 倍音は控えめ
            s[i] = (a + b) * env * 0.32f;
        }
        return MakeWav(s);
    }

    // 被弾：低いトーン（160→90Hz）＋ノイズの鈍いバースト（~200ms）。
    private AudioStreamWav SynthHit()
    {
        float dur = 0.18f; int n = (int)(Rate * dur);
        var s = new float[n]; float phase = 0f; float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float freq = Mathf.Lerp(150f, 80f, t / dur);
            phase += freq / Rate;
            float tone = Mathf.Sin(phase * Mathf.Tau) * Mathf.Exp(-t / 0.08f);
            float white = _rng.Randf() * 2f - 1f;
            lp += (white - lp) * 0.12f; // 一極ローパス：高域を削り「耳に刺さる」成分を除去
            float noise = lp * Mathf.Exp(-t / 0.04f);
            s[i] = (0.7f * tone + 0.3f * noise) * 0.6f; // 低い「ドゥッ」。鋭さより重み
        }
        return MakeWav(s);
    }

    // 浄化：柔らかい立ち上がり＋長い減衰のベル（基音523Hz＋倍音、~600ms）。
    private AudioStreamWav SynthPurify()
    {
        float dur = 0.6f; int n = (int)(Rate * dur);
        var s = new float[n];
        float f0 = 523f; // C5
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float attack = t < 0.004f ? t / 0.004f : 1f;
            float env = attack * Mathf.Exp(-t / 0.2f);
            float bell = Mathf.Sin(Mathf.Tau * f0 * t)
                       + 0.4f * Mathf.Sin(Mathf.Tau * f0 * 2f * t)
                       + 0.18f * Mathf.Sin(Mathf.Tau * f0 * 3f * t);
            float third = 0.12f * Mathf.Sin(Mathf.Tau * f0 * 1.26f * t); // 長3度のきらめき（控えめ）
            s[i] = (bell + third) * env * 0.22f;
        }
        return MakeWav(s);
    }

    // ③ボム：低い溜め（~0.18s スウェル）→ 開放（C-G-C の和音ブルーム＋やわらかい衝撃）。~0.9s。
    private AudioStreamWav SynthBomb()
    {
        float dur = 0.9f; int n = (int)(Rate * dur);
        var s = new float[n]; float lp = 0f;
        const float hit = 0.18f; // 溜め→開放の境
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            // 溜め：上昇するうなり（70→150Hz）が境で頂点。
            float ramp = Mathf.Min(1f, t / hit);
            float swell = Mathf.Sin(Mathf.Tau * Mathf.Lerp(70f, 150f, ramp) * t)
                        * ramp * ramp * (t < hit ? 1f : 0f) * 0.5f;
            // 開放：C5/G5/C6 の和音ブルーム（柔らかい立ち上がり＋長い減衰）。
            float bt = Mathf.Max(0f, t - hit);
            float env = (bt < 0.02f ? bt / 0.02f : 1f) * Mathf.Exp(-bt / 0.32f);
            float bloom = (Mathf.Sin(Mathf.Tau * 523.25f * bt)
                         + 0.7f * Mathf.Sin(Mathf.Tau * 783.99f * bt)
                         + 0.5f * Mathf.Sin(Mathf.Tau * 1046.5f * bt)) * env * 0.18f;
            // やわらかい光のノイズ（ローパスで刺さりを除去）。
            float white = _rng.Randf() * 2f - 1f;
            lp += (white - lp) * 0.05f;
            float air = lp * env * 0.12f;
            s[i] = swell + bloom + air;
        }
        FadeEnds(s, (int)(0.004f * Rate));
        return MakeWav(s);
    }

    // ⑥全開：C→E→G→C を駆け上がる上昇アルペジオ＋光が満ちるベル残響。~0.5s。
    private AudioStreamWav SynthOverload()
    {
        float dur = 0.5f; int n = (int)(Rate * dur);
        var s = new float[n];
        float[] steps = { 523.25f, 659.25f, 783.99f, 1046.5f }; // C5 E5 G5 C6
        const float step = 0.07f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            int k = Mathf.Min(steps.Length - 1, (int)(t / step));
            float lt = t - k * step;
            float env = (lt < 0.004f ? lt / 0.004f : 1f) * Mathf.Exp(-lt / 0.12f);
            float pluck = Mathf.Sin(Mathf.Tau * steps[k] * lt) * env * 0.22f;
            // 最後の音に重なる長い残響（光が満ちる）。
            float tailT = Mathf.Max(0f, t - 3 * step);
            float tail = Mathf.Sin(Mathf.Tau * 1046.5f * tailT)
                       * Mathf.Exp(-tailT / 0.2f) * 0.1f;
            s[i] = pluck + tail;
        }
        FadeEnds(s, (int)(0.004f * Rate));
        return MakeWav(s);
    }

    // ⑦鎮まる音：下降する柔らかいパッド（G5→C5）＋ふわっと消える。会話転換の「すっ」。~0.45s。
    private AudioStreamWav SynthCalm()
    {
        float dur = 0.45f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Sin(Mathf.Min(1f, t / dur) * Mathf.Pi) * 0.9f; // 山なり（両端0）
            float f = Mathf.Lerp(783.99f, 523.25f, t / dur); // 下降＝鎮まる
            float pad = (Mathf.Sin(Mathf.Tau * f * t)
                       + 0.5f * Mathf.Sin(Mathf.Tau * f * 1.5f * t)) * env * 0.14f;
            s[i] = pad;
        }
        FadeEnds(s, (int)(0.006f * Rate));
        return MakeWav(s);
    }

    // ⑩宣言：短い二音の警告（A5→E5）＋わずかな歪み。弾幕変化の予告。~0.22s。
    private AudioStreamWav SynthSpell()
    {
        float dur = 0.22f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = t < 0.09f ? 880f : 659.25f; // A5 → E5 の二音
            float lt = t < 0.09f ? t : t - 0.09f;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.06f);
            float v = Mathf.Sin(Mathf.Tau * f * t);
            float sq = v >= 0f ? 1f : -1f; // 矩形を少し混ぜて緊張感
            s[i] = (0.85f * v + 0.15f * sq) * env * 0.28f;
        }
        return MakeWav(s);
    }

    // ④パネル剥がし：高めの短い「コツッ」（2系統の減衰トーン）。浄化より軽い。~0.06s。
    private AudioStreamWav SynthStrip()
    {
        float dur = 0.06f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.016f);
            float a = Mathf.Sin(Mathf.Tau * 1400f * t);
            float b = Mathf.Sin(Mathf.Tau * 2100f * t) * 0.3f;
            s[i] = (a + b) * env * 0.3f;
        }
        return MakeWav(s);
    }

    // ───────── 本体ヒット（無防備窓の撃ち込み専用。設計 §5「手応え＝刺さる」）─────────
    //   ボスEXPOSED中、本体に自機弾が通った一発ごとの「刺さる／ドスッ」。
    //   音域を意図的に PlayStrip（高い1400-2100Hzの軽い「コツッ」）と PlayHit（自機被弾＝
    //   150-80Hzのくぐもったノイズ）の“間”＝中低域に置き、3者を耳で取り違えない。
    //   ・打撃トランジェント（短いノイズのクリック）＋胴鳴り（220→120Hzのピッチ落ちトーン）。
    //   ・短く（~90ms）・速い減衰・小音量＝無防備窓で連射しても飽和しても破綻しない。
    private AudioStreamWav SynthBossHit()
    {
        float dur = 0.09f; int n = (int)(Rate * dur);
        var s = new float[n]; float phase = 0f; float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            // 胴鳴り：中低域のピッチ落ちトーン（刺さりの芯）。
            float freq = Mathf.Lerp(220f, 120f, t / dur);
            phase += freq / Rate;
            float body = Mathf.Sin(phase * Mathf.Tau) * Mathf.Exp(-t / 0.035f);
            // 打撃トランジェント：ごく短いローパス済みノイズのクリック（“ドスッ”のアタック）。
            float white = _rng.Randf() * 2f - 1f;
            lp += (white - lp) * 0.30f; // 一極ローパス：耳に刺さる高域を削る
            float click = lp * Mathf.Exp(-t / 0.006f);
            s[i] = (0.72f * body + 0.28f * click) * 0.5f;
        }
        FadeEnds(s, (int)(0.003f * Rate));
        return MakeWav(s);
    }

    // 大威力（dmg>=3）版：上の芯に低音“ドスッ”を一枚重ねて重く（GameCamera.Shake と同期）。
    //   サブ（70→45Hz）の胴を足し、少し長く（~150ms）伸ばす＝決定打の重み。
    private AudioStreamWav SynthBossHitHeavy()
    {
        float dur = 0.15f; int n = (int)(Rate * dur);
        var s = new float[n]; float phase = 0f, subPhase = 0f; float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            // 胴鳴り（通常版よりやや低く・長く）。
            float freq = Mathf.Lerp(190f, 95f, t / dur);
            phase += freq / Rate;
            float body = Mathf.Sin(phase * Mathf.Tau) * Mathf.Exp(-t / 0.05f);
            // 低音“ドスッ”レイヤー：サブ正弦（70→45Hz）。アタックを丸く・長く伸ばす重み。
            float subF = Mathf.Lerp(70f, 45f, t / dur);
            subPhase += subF / Rate;
            float sub = Mathf.Sin(subPhase * Mathf.Tau)
                      * (t < 0.004f ? t / 0.004f : 1f) * Mathf.Exp(-t / 0.09f);
            // 打撃トランジェント。
            float white = _rng.Randf() * 2f - 1f;
            lp += (white - lp) * 0.30f;
            float click = lp * Mathf.Exp(-t / 0.007f);
            s[i] = (0.5f * body + 0.45f * sub + 0.22f * click) * 0.55f;
        }
        FadeEnds(s, (int)(0.004f * Rate));
        return MakeWav(s);
    }

    // ───────── UI操作音（⑧）─────────
    // カーソル移動：ごく短い高い「ティッ」。
    private AudioStreamWav SynthUiMove()
    {
        float dur = 0.025f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.007f);
            s[i] = Mathf.Sin(Mathf.Tau * 1760f * t) * env * 0.28f;
        }
        return MakeWav(s);
    }

    // 決定：芯のある二音（E5→A5 上昇）。
    private AudioStreamWav SynthUiConfirm()
    {
        float dur = 0.16f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = t < 0.06f ? 659.25f : 880f; // E5 → A5
            float lt = t < 0.06f ? t : t - 0.06f;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.07f);
            s[i] = Mathf.Sin(Mathf.Tau * f * lt) * env * 0.3f;
        }
        return MakeWav(s);
    }

    // キャンセル：下降する二音（A4→E4）。
    private AudioStreamWav SynthUiCancel()
    {
        float dur = 0.16f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = t < 0.06f ? 440f : 329.63f; // A4 → E4
            float lt = t < 0.06f ? t : t - 0.06f;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.08f);
            s[i] = Mathf.Sin(Mathf.Tau * f * lt) * env * 0.28f;
        }
        return MakeWav(s);
    }

    // 購入成功：明るい三音の達成（C5-E5-G5）。
    private AudioStreamWav SynthUiBuy()
    {
        float dur = 0.34f; int n = (int)(Rate * dur);
        var s = new float[n];
        float[] notes = { 523.25f, 659.25f, 783.99f }; // C5 E5 G5
        const float step = 0.08f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            int k = Mathf.Min(notes.Length - 1, (int)(t / step));
            float lt = t - k * step;
            float env = (lt < 0.003f ? lt / 0.003f : 1f) * Mathf.Exp(-lt / 0.13f);
            float tailT = Mathf.Max(0f, t - 2 * step);
            float tail = Mathf.Sin(Mathf.Tau * 783.99f * tailT) * Mathf.Exp(-tailT / 0.18f) * 0.1f;
            s[i] = Mathf.Sin(Mathf.Tau * notes[k] * lt) * env * 0.24f + tail;
        }
        FadeEnds(s, (int)(0.004f * Rate));
        return MakeWav(s);
    }

    // 購入失敗・拒否：低い鈍い「ブッ」（矩形寄り、減衰速い）。
    private AudioStreamWav SynthUiDeny()
    {
        float dur = 0.14f; int n = (int)(Rate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Exp(-t / 0.05f);
            float v = Mathf.Sin(Mathf.Tau * 155f * t);
            float sq = v >= 0f ? 1f : -1f;
            s[i] = (0.5f * v + 0.5f * sq) * env * 0.26f;
        }
        return MakeWav(s);
    }

    // ───────── 会話タイプライター送り音（話者で音色を変える。設計 1-4）─────────
    // 少年：中域の柔らかい正弦（木質＝倍音少なめ・角を丸く）。~16ms。
    private AudioStreamWav SynthTypeBoy()
    {
        float dur = 0.016f; int n = (int)(Rate * dur);
        var s = new float[n];
        const float f0 = 320f; // 中域・温かい
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            // 立ち上がりをわずかに鈍らせ「角を丸く」。短い指数減衰。
            float atk = t < 0.0018f ? t / 0.0018f : 1f;
            float env = atk * Mathf.Exp(-t / 0.006f);
            float v = Mathf.Sin(Mathf.Tau * f0 * t)
                    + 0.10f * Mathf.Sin(Mathf.Tau * f0 * 2f * t); // 倍音はごく薄く＝木質
            s[i] = v * env * 0.5f;
        }
        FadeEnds(s, (int)(0.0008f * Rate));
        return MakeWav(s);
    }

    // ミナ：高域の澄んだガラス質（基音高め＋わずかな上倍音、速くクリアに減衰）。~14ms。
    private AudioStreamWav SynthTypeMina()
    {
        float dur = 0.014f; int n = (int)(Rate * dur);
        var s = new float[n];
        const float f0 = 920f; // 高め・澄んだ
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float atk = t < 0.0006f ? t / 0.0006f : 1f; // 鋭く澄んだ立ち上がり
            float env = atk * Mathf.Exp(-t / 0.0045f);
            float v = Mathf.Sin(Mathf.Tau * f0 * t)
                    + 0.22f * Mathf.Sin(Mathf.Tau * f0 * 2.76f * t); // 非整数の上倍音＝ガラスのきらめき
            s[i] = v * env * 0.42f;
        }
        FadeEnds(s, (int)(0.0008f * Rate));
        return MakeWav(s);
    }

    // ボス（穢れ）：低めでくぐもった音（ローパスで倍音を削る）。~22ms。
    private AudioStreamWav SynthTypeBoss()
    {
        float dur = 0.022f; int n = (int)(Rate * dur);
        var s = new float[n]; float lp = 0f;
        const float f0 = 165f; // 低め
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float atk = t < 0.0025f ? t / 0.0025f : 1f;
            float env = atk * Mathf.Exp(-t / 0.008f);
            float raw = Mathf.Sin(Mathf.Tau * f0 * t)
                      + 0.18f * Mathf.Sin(Mathf.Tau * f0 * 2f * t);
            lp += (raw - lp) * 0.10f; // 一極ローパス＝高域を削り「くぐもり」
            s[i] = lp * env * 0.55f;
        }
        FadeEnds(s, (int)(0.0008f * Rate));
        return MakeWav(s);
    }

    // ───────── BGM 合成（M.I.N.A. モチーフを核にした 8 秒シームレスループ）─────────
    // 進行 Am - F - C - G（vi-IV-I-V＝“泣ける”カノン系）。各小節2秒。
    // パッド/ベース/メロディは小節端で振幅0へ戻すので、ループ継ぎ目でクリックしない。
    private AudioStreamWav BuildBgm()
    {
        const float bar = 2.0f; const int bars = 4;
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];

        // [bass, 三和音1, 三和音2, 三和音3]（Hz）
        float[][] chords =
        {
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
            new[] {  87.31f, 174.61f, 220.00f, 261.63f }, // F
            new[] { 130.81f, 261.63f, 329.63f, 392.00f }, // C
            new[] {  98.00f, 196.00f, 246.94f, 293.66f }, // G
        };
        // M.I.N.A. モチーフ（ド ミ レ ソ）を1小節1音、上声で。
        float[] motif = { 523.25f, 659.25f, 587.33f, 783.99f };

        for (int b = 0; b < bars; b++)
        {
            float[] ch = chords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate; // 小節内の経過秒
                // パッド：三和音の柔らかいスウェル（両端0）
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.25f, 0.45f) * 0.09f;
                // ベース：根音
                float bass = Mathf.Sin(Mathf.Tau * ch[0] * t) * Swell(t, bar, 0.06f, 0.35f) * 0.16f;
                // アルペジオ：四分で三和音を巡る柔らかいプラック（動き）
                float arp = Arp(t, ch) * 0.06f;
                // メロディ：モチーフ1音（柔らかいフルート）
                float mel = Mathf.Sin(Mathf.Tau * motif[b] * t) * Swell(t, bar, 0.10f, 0.7f) * 0.08f;
                // マスターゲイン：SE（控えめ）に対して BGM が大きすぎないよう全体を下げる
                s[baseI + i] = (pad + bass + arp + mel) * 0.4f;
            }
        }
        FadeEnds(s, (int)(0.006f * Rate)); // 端を数msフェード＝継ぎ目を完全に無音化

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // ───────── BgmMenu のロード（ユーザー制作の実音源 → 失敗時は合成へフォールバック）─────────
    //   タイトル/ハブ/ショップ/設定/難易度選択/Prologue/Epilogue/Records で Music(BgmMenu) が鳴らす曲。
    //   res://audio/bgm_menu_mina.ogg（28.6秒の原曲から末尾フェードをトリムした26秒・-3dB・ループ点を
    //   なじませた ogg）を読む。ogg は import 設定 loop=true でループを焼いてあるが、念のため
    //   AudioStreamOggVorbis.Loop も明示する（Music() は曲尾で止めず鳴らしっぱなしにするため）。
    //   ロード失敗（インポート未済・差し替えミス等）の場合は従来のコード合成 BuildBgmMenu() に戻す。
    private AudioStream LoadBgmMenu()
    {
        var s = ResourceLoader.Load<AudioStream>("res://audio/bgm_menu_mina.ogg");
        if (s is AudioStreamOggVorbis ogg) { ogg.Loop = true; return ogg; }
        if (s != null) return s; // 念のため（mp3 等に差し替えても受ける）
        GD.PushWarning("BgmMenu: res://audio/bgm_menu_mina.ogg をロードできず、合成BGMにフォールバック");
        return BuildBgmMenu();
    }

    // ───────── BgmStageRei のロード（レイ道中の実音源 → 失敗時は合成 BgmStage へフォールバック）─────────
    //   res://BGM/The_Watcher_in_the_Hall.mp3（loop=true で再インポート済み）を読む。
    //   ロード失敗（インポート未済・差し替えミス等）の場合は従来の合成 BgmStage に戻す（事故防止）。
    private AudioStream LoadBgmStageRei()
    {
        var s = ResourceLoader.Load<AudioStream>("res://BGM/The_Watcher_in_the_Hall.mp3");
        if (s is AudioStreamMP3 mp3) { mp3.Loop = true; return mp3; }
        if (s != null) return s; // 念のため（ogg 等に差し替えても受ける）
        GD.PushWarning("BgmStageRei: res://BGM/The_Watcher_in_the_Hall.mp3 をロードできず、合成BgmStageにフォールバック");
        return BgmStage;
    }

    // ───────── BgmStageAkari のロード（あかり道中の実音源 → 失敗時は合成 BgmStage へフォールバック）─────────
    //   res://audio/bgm_stage_akari.ogg（末尾フェードをトリムした 0..61.0秒・-3dB・loop=true）を読む。
    //   ogg は loop を焼いてあるが、念のため AudioStreamOggVorbis.Loop も明示する（Music() は鳴らしっぱなし）。
    //   実音源なので再生時に MusicTargetDb() の StageBgmRealDb(-10dB) が自動で乗る（突出しない）。
    private AudioStream LoadBgmStageAkari()
    {
        var s = ResourceLoader.Load<AudioStream>("res://audio/bgm_stage_akari.ogg");
        if (s is AudioStreamOggVorbis ogg) { ogg.Loop = true; return ogg; }
        if (s != null) return s; // 念のため（mp3 等に差し替えても受ける）
        GD.PushWarning("BgmStageAkari: res://audio/bgm_stage_akari.ogg をロードできず、合成BgmStageにフォールバック");
        return BgmStage;
    }

    // ───────── BgmStageKoharu のロード（こはる道中の実音源 → 失敗時は合成 BgmStage へフォールバック）─────────
    //   res://audio/bgm_stage_koharu.ogg（末尾フェードをトリムした 0..57.5秒・-3dB・loop=true）を読む。
    private AudioStream LoadBgmStageKoharu()
    {
        var s = ResourceLoader.Load<AudioStream>("res://audio/bgm_stage_koharu.ogg");
        if (s is AudioStreamOggVorbis ogg) { ogg.Loop = true; return ogg; }
        if (s != null) return s; // 念のため（mp3 等に差し替えても受ける）
        GD.PushWarning("BgmStageKoharu: res://audio/bgm_stage_koharu.ogg をロードできず、合成BgmStageにフォールバック");
        return BgmStage;
    }

    // ───────── BgmBossRei のロード（レイ戦闘の実音源 → 失敗時は合成 BuildBgmBossRei へフォールバック）─────────
    //   res://audio/bgm_boss_rei.ogg（原曲30.8秒・ピーク-0.6dBを -3dB で他BGMと粒を揃え、頭40ms/尻120ms に
    //   極小フェードを掛けてループ継ぎ目をなじませた ogg。import 設定 loop=true）を読む。
    //   ogg は loop を焼いてあるが、念のため AudioStreamOggVorbis.Loop も明示する（Music() は鳴らしっぱなし）。
    //   ロード失敗（インポート未済・差し替えミス等）の場合は従来の合成 BuildBgmBossRei() に戻す（事故防止）。
    private AudioStream LoadBgmBossRei()
    {
        var s = ResourceLoader.Load<AudioStream>("res://audio/bgm_boss_rei.ogg");
        if (s is AudioStreamOggVorbis ogg) { ogg.Loop = true; return ogg; }
        if (s != null) return s; // 念のため（mp3 等に差し替えても受ける）
        GD.PushWarning("BgmBossRei: res://audio/bgm_boss_rei.ogg をロードできず、合成 BuildBgmBossRei にフォールバック");
        return BuildBgmBossRei();
    }

    // ───────── BgmBossAkari のロード（あかり戦闘の実音源 → 失敗時は合成 BuildBgmBossAkari へフォールバック）─────────
    //   res://BGM/Akari_s_Last_Corridor.mp3（loop=true で再インポート済み）を読む。BgmStageRei と同じ作法。
    //   mp3 は import 設定 loop=true で焼いてあるが、念のため AudioStreamMP3.Loop も明示する（Music() は鳴らしっぱなし）。
    //   実音源なので再生時に MusicTargetDb() の StageBgmRealDb(-10dB) が自動で乗る（突出しない）。
    //   ロード失敗（インポート未済・差し替えミス等）の場合は従来の合成 BuildBgmBossAkari() に戻す（事故防止）。
    private AudioStream LoadBgmBossAkari()
    {
        var s = ResourceLoader.Load<AudioStream>("res://BGM/Akari_s_Last_Corridor.mp3");
        if (s is AudioStreamMP3 mp3) { mp3.Loop = true; return mp3; }
        if (s != null) return s; // 念のため（ogg 等に差し替えても受ける）
        GD.PushWarning("BgmBossAkari: res://BGM/Akari_s_Last_Corridor.mp3 をロードできず、合成 BuildBgmBossAkari にフォールバック");
        return BuildBgmBossAkari();
    }

    // ───────── BgmBossKoharu のロード（こはる戦闘の実音源 → 失敗時は合成 BuildBgmBossKoharu へフォールバック）─────────
    //   res://BGM/The_Leaking_Tap.mp3（loop=true で再インポート済み）を読む。BgmBossAkari と同じ作法。
    //   mp3 は import 設定 loop=true で焼いてあるが、念のため AudioStreamMP3.Loop も明示する（Music() は鳴らしっぱなし）。
    //   実音源なので再生時に MusicTargetDb() の StageBgmRealDb(-10dB) が自動で乗る（突出しない）。
    //   ロード失敗（インポート未済・差し替えミス等）の場合は従来の合成 BuildBgmBossKoharu() に戻す（事故防止）。
    private AudioStream LoadBgmBossKoharu()
    {
        var s = ResourceLoader.Load<AudioStream>("res://BGM/The_Leaking_Tap.mp3");
        if (s is AudioStreamMP3 mp3) { mp3.Loop = true; return mp3; }
        if (s != null) return s; // 念のため（ogg 等に差し替えても受ける）
        GD.PushWarning("BgmBossKoharu: res://BGM/The_Leaking_Tap.mp3 をロードできず、合成 BuildBgmBossKoharu にフォールバック");
        return BuildBgmBossKoharu();
    }

    // ───────── BgmBossMina のロード（ミナ戦本体の実音源 → 失敗時は汎用合成 BuildBgmBoss へフォールバック）─────────
    //   res://audio/bgm_boss_mina.ogg（原曲 The_Weight_Of_Absolution の本体 51.0..84.5秒を切り出し、
    //   尻0.6秒を頭へクロスフェードしてシームレスループ化した 33.5秒・-3dB・loop=true）を読む。
    //   ロード失敗時は汎用ボス曲 BuildBgmBoss() に戻す（ミナ戦が無音にならない事故防止）。
    private AudioStream LoadBgmBossMina()
    {
        var s = ResourceLoader.Load<AudioStream>("res://audio/bgm_boss_mina.ogg");
        if (s is AudioStreamOggVorbis ogg) { ogg.Loop = true; return ogg; }
        if (s != null) return s; // 念のため（mp3 等に差し替えても受ける）
        GD.PushWarning("BgmBossMina: res://audio/bgm_boss_mina.ogg をロードできず、汎用合成 BuildBgmBoss にフォールバック");
        return BgmBoss;
    }

    // ───────── ステージ別の道中BGMを引く（将来 akari/koharu の実音源はここに足すだけ）─────────
    //   BeginStageRun(id) と、中ボス撃破後の道中復帰（CameoBoss）から共通で使う。
    //   rei＝実音源 BgmStageRei。それ以外は従来の合成 BgmStage。
    public AudioStream StageBgm(string stageId) => stageId switch
    {
        "rei" => BgmStageRei,
        "akari" => BgmStageAkari,
        "koharu" => BgmStageKoharu,
        _ => BgmStage,
    };

    // ステージ開始時（BeginStageRun）に呼ぶ：id を控えて、そのステージの道中曲を鳴らす。
    //   同じ曲なら Music() 側で何もしない＝リトライで道中BGMが途切れない。
    public void SetStageMusic(string stageId)
    {
        CurrentStageId = stageId;
        Music(StageBgm(stageId));
    }

    // 中ボス（カメオ）撃破後など、道中へ戻るときに「今のステージの道中曲」へ復帰する。
    //   ステージ id を覚えていない（直接シーン起動等）ときは従来の合成 BgmStage に戻す。
    public void ResumeStageMusic()
        => Music(StageBgm(CurrentStageId));

    // ───────── BgmMenu（合成フォールバック。タイトル/ハブ/ショップ/設定/難易度選択）─────────
    // 同じ M.I.N.A. モチーフを、遅いテンポ・薄い編成・長3度寄りの温かい和声で。
    //   進行 C - Am - F - G（I-vi-IV-V＝穏やかなカノン系）。各小節3秒＝ゆったり12秒ループ。
    //   メロディは2小節に1音だけ置き、間（ま）を多く取って耳障りにしない（プレイヤーが長く居る）。
    private AudioStreamWav BuildBgmMenu()
    {
        const float bar = 3.0f; const int bars = 4;
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];

        // [bass, 三和音1, 三和音2, 三和音3]（Hz）。明るめ＝メジャー始まり。
        float[][] chords =
        {
            new[] { 130.81f, 261.63f, 329.63f, 392.00f }, // C
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
            new[] {  87.31f, 174.61f, 220.00f, 261.63f }, // F
            new[] {  98.00f, 196.00f, 246.94f, 293.66f }, // G
        };
        // モチーフ（ド ミ レ ソ）を1オクターブ下げ、偶数小節だけ鳴らす＝薄く、ぽつりと。
        float[] motif = { 261.63f, 329.63f, 293.66f, 392.00f };

        for (int b = 0; b < bars; b++)
        {
            float[] ch = chords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate;
                // パッド：三和音の、ごく緩いスウェル（立ち上がり長め＝呼吸のよう）。
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.6f, 0.8f) * 0.085f;
                // ベース：根音、やわらかく。
                float bass = Mathf.Sin(Mathf.Tau * ch[0] * t) * Swell(t, bar, 0.2f, 0.6f) * 0.13f;
                // メロディ：偶数小節のみ、モチーフ1音をフルートで（間を活かす）。
                float mel = (b % 2 == 0)
                    ? Mathf.Sin(Mathf.Tau * motif[b] * t) * Swell(t, bar, 0.3f, 1.2f) * 0.07f
                    : 0f;
                s[baseI + i] = (pad + bass + mel) * 0.38f;
            }
        }
        FadeEnds(s, (int)(0.008f * Rate));

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // ───────── BgmFinalResolve（Final の音楽的解決。設計 §1-3「節目で帰る」/ §7「無音→解決」）─────────
    //   Final の決定打。沈黙の直後に ppp で立ち上がる、主題 M.I.N.A. の「完」。
    //   和声は BgmMenu と同じ C-Am-F-G（温かい長調圏）＝Epilogue の BgmMenu へ継ぎ目なく溶ける。
    //   各小節4秒のごく緩い16秒ループ＝余韻を長く持続。BgmBoss の不協和な「未完」モチーフが、
    //   ここでは半音テンションを取り去り、最後に主音 C6 へ「届いて」解決する（破壊でなく赦し）。
    //   全体ゲインを BgmMenu より一段下げ ppp とし、沈黙からの立ち上がりの落差を生かす。
    private AudioStreamWav BuildBgmFinalResolve()
    {
        const float bar = 4.0f; const int bars = 4;   // ごく緩く・長い余韻
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];

        // BgmMenu と同じ和声圏（C-Am-F-G）＝主題が「帰る」場所。
        float[][] chords =
        {
            new[] { 130.81f, 261.63f, 329.63f, 392.00f }, // C  （主音＝解決の地盤）
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
            new[] {  87.31f, 174.61f, 220.00f, 261.63f }, // F
            new[] {  98.00f, 196.00f, 246.94f, 293.66f }, // G  （次へ＝主音へ戻す推進）
        };
        // M.I.N.A. モチーフ（ド ミ レ ソ）を1小節1音。最終小節で主音 C へ「届く」よう、
        //   G(784) の次に解決の高音 C6(1046) を内包させる（BgmBoss では半音下に濁って届かなかった音）。
        float[] motif = { 523.25f, 659.25f, 587.33f, 783.99f }; // C5 E5 D5 G5

        for (int b = 0; b < bars; b++)
        {
            float[] ch = chords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate;
                // パッド：三和音の、呼吸のように長いスウェル（両端0＝継ぎ目なし）。
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.9f, 1.2f) * 0.09f;
                // ベース：根音、やわらかく。
                float bass = Mathf.Sin(Mathf.Tau * ch[0] * t) * Swell(t, bar, 0.3f, 0.9f) * 0.12f;
                // メロディ：モチーフ1音を澄んだベルで、間を活かしてぽつりと。
                //   倍音を薄く重ね「届いた」温かさ（破壊でなく赦し＝pitfalls P4）。
                float mhz = motif[b];
                float mel = (Mathf.Sin(Mathf.Tau * mhz * t)
                           + 0.3f * Mathf.Sin(Mathf.Tau * mhz * 2f * t)) // 純正な倍音＝晴れ（半音濁りは無し）
                           * Swell(t, bar, 0.25f, 1.6f) * 0.075f;
                // 最終小節：G の上に解決の主音 C6 を重ね、主題を主音へ「届かせて」締める。
                float resolve = 0f;
                if (b == bars - 1)
                    resolve = Mathf.Sin(Mathf.Tau * 1046.5f * t)
                            * Swell(t, bar, 1.4f, 1.8f) * 0.06f; // 遅れて立ち上がり、長く伸びて余韻
                // 全体を BgmMenu(0.38) より下げ ppp に。
                s[baseI + i] = (pad + bass + mel + resolve) * 0.3f;
            }
        }
        FadeEnds(s, (int)(0.01f * Rate)); // 端を10msフェード＝ループ継ぎ目を無音化

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // ───────── BgmBoss（ボス戦）─────────
    // 同じモチーフを「未完／不協和」に。短調寄り進行・密度高め・強いベース。各小節1.6秒＝速い6.4秒ループ。
    //   進行 Am - Dm - E - Am（i-iv-V-i＝和声的短調＝緊張と解決の反復）。
    //   モチーフは速い八分の刻みで反復し、半音上のテンション音を薄く重ねて濁す。
    private AudioStreamWav BuildBgmBoss()
    {
        const float bar = 1.6f; const int bars = 4;
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];

        float[][] chords =
        {
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
            new[] { 146.83f, 293.66f, 349.23f, 440.00f }, // Dm
            new[] { 164.81f, 329.63f, 415.30f, 493.88f }, // E (長3度 G#=415 で導音の緊張)
            new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
        };
        // モチーフ（ド ミ レ ソ）。短調文脈に置くことで同じ音形が「翳る」。
        float[] motif = { 523.25f, 659.25f, 587.33f, 783.99f };

        for (int b = 0; b < bars; b++)
        {
            float[] ch = chords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate;
                // ベース：根音＋オクターブ下を強く（緊張の地盤）。八分で踏む脈動。
                float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Mathf.Tau * (1f / 0.2f) * t)); // 八分の刻み
                float bass = (Mathf.Sin(Mathf.Tau * ch[0] * t) + 0.5f * Mathf.Sin(Mathf.Tau * ch[0] * 0.5f * t))
                           * Swell(t, bar, 0.03f, 0.2f) * pulse * 0.2f;
                // パッド：三和音、やや硬め（密度を上げる）。
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.1f, 0.3f) * 0.075f;
                // 刻みアルペジオ：八分で三和音を速く巡る（密度＝緊張）。
                float arp = ArpFast(t, ch) * 0.07f;
                // メロディ：モチーフ1音＋半音上のテンションを薄く重ね、不協和で「未完」に。
                float mel = (Mathf.Sin(Mathf.Tau * motif[b] * t)
                           + 0.22f * Mathf.Sin(Mathf.Tau * motif[b] * 1.0595f * t)) // 半音上＝濁り
                           * Swell(t, bar, 0.06f, 0.4f) * 0.07f;
                s[baseI + i] = (bass + pad + arp + mel) * 0.4f;
            }
        }
        FadeEnds(s, (int)(0.006f * Rate));

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // ───────── ボス別テーマ：共通の土台（BuildBgmBoss と同じ枠で、編成だけ差し替える）─────────
    //   設計 §1-2「各ボスの固有モチーフ＝未完→完」。3曲とも M.I.N.A. 構成音（C E D G）を核にし、
    //   それぞれ別の「未完の仕方」でモチーフを翳らせる。改心（PlayRedeem）が同じ音形を「完」に解く。
    //   進行は BuildBgmBoss と同じ Am-Dm-E-Am（緊張と解決の反復）。各小節1.6秒＝6.4秒ループ。
    private static readonly float[][] BossChords =
    {
        new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
        new[] { 146.83f, 293.66f, 349.23f, 440.00f }, // Dm
        new[] { 164.81f, 329.63f, 415.30f, 493.88f }, // E（G#=415 導音の緊張）
        new[] { 110.00f, 220.00f, 261.63f, 329.63f }, // Am
    };

    // 共通の伴奏（ベース脈動＋パッド＋刻みアルペジオ）。melFn が各ボス固有のメロディ（未完の仕方）。
    //   melFn(b, t, n のうちの小節内秒) → メロディ振幅。返り値は概ね ±0.07 スケールで足す。
    private AudioStreamWav BuildBossBase(System.Func<int, float, float> melFn, float density = 1f)
    {
        const float bar = 1.6f; const int bars = 4;
        int barN = (int)(Rate * bar), n = barN * bars;
        var s = new float[n];
        for (int b = 0; b < bars; b++)
        {
            float[] ch = BossChords[b];
            int baseI = b * barN;
            for (int i = 0; i < barN; i++)
            {
                float t = (float)i / Rate;
                float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Mathf.Tau * (1f / 0.2f) * t));
                float bass = (Mathf.Sin(Mathf.Tau * ch[0] * t) + 0.5f * Mathf.Sin(Mathf.Tau * ch[0] * 0.5f * t))
                           * Swell(t, bar, 0.03f, 0.2f) * pulse * 0.2f;
                float pad = (Mathf.Sin(Mathf.Tau * ch[1] * t) + Mathf.Sin(Mathf.Tau * ch[2] * t)
                           + Mathf.Sin(Mathf.Tau * ch[3] * t)) * Swell(t, bar, 0.1f, 0.3f) * 0.075f;
                float arp = ArpFast(t, ch) * 0.07f * density;
                float mel = melFn(b, t);
                s[baseI + i] = (bass + pad + arp + mel) * 0.4f;
            }
        }
        FadeEnds(s, (int)(0.006f * Rate));
        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // M.I.N.A. モチーフ（ド ミ レ ソ）の小節別の主音。
    private static readonly float[] MinaMotif = { 523.25f, 659.25f, 587.33f, 783.99f };

    // Rei：主音の「直前」で半音落ちる＝あと一歩で一番になれない（順位＝届かない）。
    //   各小節、モチーフ音へ向かう途中で目標の半音下に滑り落ちて減衰する。
    private AudioStreamWav BuildBgmBossRei()
    {
        return BuildBossBase((b, t) =>
        {
            float target = MinaMotif[b];
            float reach = target * 0.9438f;                      // 半音下＝届かない音
            // 前半は target を目指して立ち上がるが、後半で reach へずり落ちる（グリッサンド）。
            float k = Mathf.Clamp((t - 0.5f) / 0.6f, 0f, 1f);
            float f = Mathf.Lerp(target, reach, k);
            float env = Swell(t, 1.6f, 0.06f, 0.5f);
            return Mathf.Sin(Mathf.Tau * f * t) * env * 0.07f;
        });
    }

    // Akari：フレーズが途中で切れる＝言いかけて言えない（"す——"）。
    //   モチーフ音を鳴らし始めてすぐ（~0.5s）でぷつりと断ち、残りの小節は沈黙（間＝言えなさ）。
    private AudioStreamWav BuildBgmBossAkari()
    {
        return BuildBossBase((b, t) =>
        {
            const float cut = 0.5f;                              // ここで言葉が切れる
            if (t > cut) return 0f;
            float env = (t < 0.02f ? t / 0.02f : 1f) * Mathf.Min(1f, (cut - t) / 0.06f); // 末端を急に断つ
            return Mathf.Sin(Mathf.Tau * MinaMotif[b] * t) * env * 0.08f;
        });
    }

    // Koharu：温かい旋律が冷えて減衰する＝台所の灯が消えていく／祈りが冷える。
    //   モチーフ音は柔らかく立ち上がるが、ピッチがわずかに沈み、減衰が速まって「冷えて」消える。
    private AudioStreamWav BuildBgmBossKoharu()
    {
        return BuildBossBase((b, t) =>
        {
            float chill = 1f - 0.012f * t;                       // ごくわずかに沈む（冷える）
            float f = MinaMotif[b] * chill;
            float env = (t < 0.04f ? t / 0.04f : 1f) * Mathf.Exp(-t / 0.55f); // 温かい立ち上がり→冷えて減衰
            float warm = Mathf.Sin(Mathf.Tau * f * t)
                       + 0.18f * Mathf.Sin(Mathf.Tau * f * 2f * t); // 薄い倍音＝木質の温もり
            return warm * env * 0.07f;
        }, density: 0.7f); // 刻みを薄め＝温度のある静けさ
    }

    // ───────── 改心の「解決音（完）」（OnCryStart でクロスフェード再生する一節）─────────
    //   戦闘テーマで「未完」だったモチーフ（C E D G）を、ここで主音まで届かせて解く＝赦し・救い。
    //   減衰の長い柔らかいベル＋下支えのパッドで「壊した」でなく「届いた」温かさ（pitfalls P4）。
    //   which: 0=Rei / 1=Akari / 2=Koharu。3者とも同じ M.I.N.A. 解決＝Epilogue で主題に溶ける布石。
    private AudioStreamWav BuildRedeem(int which)
    {
        float dur = 2.6f; int n = (int)(Rate * dur);
        var s = new float[n];
        // モチーフ ド ミ レ ソ を、最後にもう一音「ド（高）」で締めて主音へ解決させる。
        float[] notes = { 523.25f, 659.25f, 587.33f, 783.99f, 1046.5f }; // C5 E5 D5 G5 C6
        float step = 0.42f;                                              // ゆったり歌わせる
        // ボスごとの色付け：Rei=澄んだ高め / Akari=息のある中庸 / Koharu=温かい厚み。
        float warm = which == 2 ? 0.5f : which == 1 ? 0.34f : 0.22f; // 倍音の厚み（温度）
        float padHz = BossChords[3][1];                              // Am の上3度（解決先の支え）
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            // メロディ：1音ずつ立ち上げ、最後の C6 で長く伸ばして「届いた」を残す。
            int k = Mathf.Min(notes.Length - 1, (int)(t / step));
            float lt = t - k * step;
            float rel = k == notes.Length - 1 ? 1.1f : 0.34f;        // 最後の音だけ長い余韻
            float env = (lt < 0.012f ? lt / 0.012f : 1f) * Mathf.Exp(-lt / rel);
            float bell = (Mathf.Sin(Mathf.Tau * notes[k] * lt)
                       + warm * Mathf.Sin(Mathf.Tau * notes[k] * 2f * lt)
                       + 0.12f * Mathf.Sin(Mathf.Tau * notes[k] * 3f * lt)) * env;
            // 下支えパッド：Am→C系の温かい和声を全体に薄く敷き、解決を包む。
            float pad = (Mathf.Sin(Mathf.Tau * padHz * t)
                       + 0.7f * Mathf.Sin(Mathf.Tau * padHz * 1.5f * t))
                       * Swell(t, dur, 0.4f, 0.9f) * 0.06f;
            s[i] = bell * 0.2f + pad;
        }
        FadeEnds(s, (int)(0.01f * Rate));
        return MakeWav(s); // 一度きりの再生（ループしない）
    }

    // ───────── 濁りパッド（Amb バス常駐。設計 1-5「濁り＝想いの重さ」）─────────
    //   低いドローン（根音＋わずかに濁すテンション）＋ごく薄いローパスノイズ。不穏だが小音量。
    //   音量で抜き差しするので素材自体は常に同レベル。汚染ゲージが大きさ＝存在感を決める。
    //   シームレスループ：ループ長(8s)で各正弦が整数周期になる周波数を選び、端を FadeEnds。
    private AudioStreamWav BuildMurkPad()
    {
        const float loop = 8.0f;            // ループ長（s）
        int n = (int)(Rate * loop);
        var s = new float[n];
        float baseHz = 1f / loop;           // この整数倍なら loop 端で位相が0に戻る＝継ぎ目なし

        // ドローン構成音（Aを基調にした低く重い和声）。loop で整数周期になるよう量子化。
        // 65.41=C2 / 98.0=G2 / 110.0=A2、それに半音弱ずらした濁し成分を薄く重ねる。
        float f1 = Mathf.Round(55.00f / baseHz) * baseHz;   // A1（地盤）
        float f2 = Mathf.Round(82.41f / baseHz) * baseHz;   // E2（5度＝安定の芯）
        float f3 = Mathf.Round(110.00f / baseHz) * baseHz;  // A2
        float murk = Mathf.Round(116.54f / baseHz) * baseHz; // 半音上(B♭2弱)＝うなり/不協和の濁り
        // ごく緩い LFO（呼吸のような揺らぎ）も loop で整数周期に。
        float lfo = Mathf.Round(0.5f / baseHz) * baseHz;

        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float breathe = 0.75f + 0.25f * Mathf.Sin(Mathf.Tau * lfo * t); // 0.5..1.0 の緩い脈

            float drone = (Mathf.Sin(Mathf.Tau * f1 * t)
                         + 0.7f * Mathf.Sin(Mathf.Tau * f2 * t)
                         + 0.6f * Mathf.Sin(Mathf.Tau * f3 * t)
                         + 0.25f * Mathf.Sin(Mathf.Tau * murk * t)) // 薄い濁し（うなり）
                        * breathe * 0.16f;

            // ごく薄いローパスノイズ（ざらつき＝不穏。高域は削って刺さらせない）。
            float white = _rng.Randf() * 2f - 1f;
            lp += (white - lp) * 0.012f; // 一極ローパス＝重い低域ノイズ
            float hiss = lp * 0.10f;

            s[i] = drone + hiss;
        }
        FadeEnds(s, (int)(0.02f * Rate)); // 端を20msフェード＝継ぎ目を完全に無音化

        var w = MakeWav(s);
        w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        w.LoopBegin = 0;
        w.LoopEnd = n;
        return w;
    }

    // 八分音符（0.2s）で三和音3音を速く巡る、やや硬めのプラック（ボスの密度用）。
    private static float ArpFast(float t, float[] ch)
    {
        const float step = 0.2f;
        int k = (int)(t / step);
        float lt = t - k * step;
        float f = ch[1 + (k % 3)];
        float env = Mathf.Exp(-lt / 0.08f) * (lt < 0.004f ? lt / 0.004f : 1f);
        float v = Mathf.Sin(Mathf.Tau * f * lt);
        float sq = v >= 0f ? 1f : -1f;
        return (0.8f * v + 0.2f * sq) * env; // 矩形を少し混ぜて硬く
    }

    // attack で 0→1、rel で 1→0（dur で終端0）。両端を0にしてループ/小節境界のクリックを防ぐ。
    private static float Swell(float t, float dur, float atk, float rel)
    {
        if (t < atk) return t / atk;
        if (t > dur - rel) return Mathf.Max(0f, (dur - t) / rel);
        return 1f;
    }

    // 四分音符（0.5s）で三和音3音を巡る柔らかいプラック。
    private static float Arp(float t, float[] ch)
    {
        const float step = 0.5f;
        int k = (int)(t / step);
        float lt = t - k * step;
        float f = ch[1 + (k % 3)];
        float env = Mathf.Exp(-lt / 0.18f) * (lt < 0.005f ? lt / 0.005f : 1f);
        return Mathf.Sin(Mathf.Tau * f * lt) * env;
    }

    private static void FadeEnds(float[] s, int k)
    {
        for (int i = 0; i < k && i < s.Length; i++)
        {
            float g = (float)i / k;
            s[i] *= g;
            s[s.Length - 1 - i] *= g;
        }
    }
}
