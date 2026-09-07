using Godot;
using System.Collections.Generic;

// Epilogue : EPILOGUE E5b → E7 → E6（案C・仮台本 wiki/08_仮台本/08。2026-09-07 ユーザー決定で全面改稿）。
// 旧 E1 タイムライン／E2 合言葉／E3 四行／E4 開示／E5 空・DM は**全削除**。エピローグは3場面だけになった：
//   E5b 見上げる（夜。四人が並んで立ち止まっている・10行）→ E5b 歩く（夜→明け方・8行）
//   → E7 スタッフロール → E6 END（最後の下書き選択。作品全体の最後の選択）。
// E6 が最後なので、END を送り切ったらタイトルへ戻る。
// 全編エンジン描画。Zで送り、R/Start 長押しで最初から（スタッフロール以降はタイトルへ）。
public partial class Epilogue : Node2D
{
    private const float W = 384f, H = 216f;

    // ───────── phase ─────────
    //   0: E5b 見上げる（夜）／1: E5b 歩く（夜→明け方）／2: E7 スタッフロール／3: E6 END
    //   旧 E1〜E5 の削除で番号を詰めてある（旧 0/1=E1・2=E2・3=E3・4=E4〜E6・5=E7）。
    private const int PhGaze = 0, PhWalk = 1, PhRoll = 2, PhEnd = 3;

    private FontFile _font = null!;
    private Texture2D? _tears;   // E6 のミナ落涙立ち絵
    private double _t;
    private int _phase = PhGaze;
    private bool _zHeld;
    private readonly RetryHold _retry = new(); // R/Start 長押しで最初から/タイトルへ（即発の誤爆防止）
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（＝現在ページ内）
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有
    private bool _musicStarted;    // E5b のオルゴールを実際に鳴らしたか（未調達なら false＝停止も呼ばない）

    // 撮影モード（--shot）か。スタッフロールの Z 飛ばしを遅らせるためだけに見る（ChoiceOverlay と同じ作法）。
    private static bool ShotHold
    {
        get
        {
            if (_shotHoldChecked) return _shotHold;
            _shotHoldChecked = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--shot") { _shotHold = true; break; }
            return _shotHold;
        }
    }
    private static bool _shotHold, _shotHoldChecked;

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    //   会話フェーズ（PhGaze/PhWalk/PhEnd）が対象。折り返しは DrawLineBox と一致させる。
    private const float BoxWrapW = W - 56f;    // DrawLineBox の本文折り返し幅と一致
    private readonly List<string> _pages = new();
    private int _page;
    private int _pagedKey = -1;                // _pages を構築済みの行キー（phase×1000+line）
    private string CurPage => _pages.Count > 0 ? _pages[Mathf.Min(_page, _pages.Count - 1)] : "";
    private bool LastPage => _pages.Count == 0 || _page >= _pages.Count - 1;
    // 現在行のページを（未構築なら）作る。折り返しは DrawLineBox と同じ書体・同じ幅で引く。
    private void EnsurePages()
    {
        string? t = CurLineText();
        if (t == null) { _pages.Clear(); _page = 0; _pagedKey = -1; return; }
        int key = _phase * 1000 + _line;
        if (_pagedKey == key) return;
        _pagedKey = key; _page = 0;
        _pages.Clear();
        _pages.AddRange(UiKit.Paginate(_font, t, UiKit.CutBody, BoxWrapW, Hud.DlgMaxLines));
    }
    private void NextPage() { _page++; _reveal = 0; _lineT = 0; }

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・独自レンダラ側の実装）。
    // スタッフロール(PhRoll)は対象外（CurLineText が null＝会話行フェーズのみ効く）。
    private int _readKey = -1;     // 既読チェック済みの行キー（phase×1000+line。フェーズ跨ぎの index 重複を区別）
    private bool _lineWasRead;     // 現在行が「表示開始時点で」既読だったか
    private bool _ffNow;           // いま高速送り中か（▶▶表示用）

    // 配色は UiKit のカットシーントークンへ集約（3画面で同値のコピーだったものを参照に置換）。
    private static readonly Color Cool = UiKit.CutMina;   // ミナ
    private static readonly Color Ink  = UiKit.CutInk;

    // E7 スタッフロール（タイムライン式）。三人の「その後のタイムライン」→クレジット→【終】の余韻。
    //   投稿3行は 12 のスタッフロール投稿（順は面の順）。末尾の枠は「そして、ご主人様へ。／【終】」
    //   （＝E6 で送った言葉…だが E6 は E7 の**後**に来るので、ここに載るのは F4 で送った言葉＝【初】）。
    //   実行時に組む（【終】が入るため静的配列にできない）。
    private string[] _roll = System.Array.Empty<string>();
    private void BuildRoll()
    {
        string last = (_game?.LastSentWord ?? "").Trim();
        _roll = new[]
        {
            "", "", "", "",
            "── その後のタイムライン ──",
            "",
            "[あかり @akari.] 向かいの席 中途の人来た 自分から話しかけた 既読とかない 顔見て言った",
            "[こはる @koharu] 今日も来ました って打った あと一行 足した",
            "[星逢レイ @rei_____] 同接7 うち1人はわたし 6人は知らない人 名前覚えた",
            "", "", "",
            "── staff ──",
            "",
            "企画・ディレクション   takutoruku1",
            "シナリオ・サウンド     Claude (AI)",
            "キャラクター・実装     Claude (AI)",
            "", "", "",
            "そして、ご主人様へ。",
            "",
            last.Length > 0 ? last : "ミナ",   // 【終】。全く送っていない異常時だけフォールバック
            "", "", "",
            "Thank you for playing.",
        };
        _rollLast = last.Length > 0 ? last : "ミナ";
    }
    private string _rollLast = "";   // クライマックス級で大きく出す1行（旧 "stay." の枠）
    private const float RollSpeed = 24f, RollLineH = 17f;

    // ───────── E6 END の下書き選択（08 E6）─────────
    //   「また来る／ありがとう／（送らない）」。作品全体の最後の選択。
    //   受けは【迷】＝今回の迷い秒数を P2 と比べて3分岐する
    //   （短ければ P2 の実測秒数をそのまま差し込む対句、長ければ集計の一言、無言なら集計に入れておく）。
    //   （送らない）は【終】を更新しない。END の一行は分岐しない。
    //   沈黙20秒の自動決定は末尾＝（送らない）へ落ちる（ChoiceOverlay の既定挙動が台本と一致）。
    private static readonly string[] E6Choices = { "また来る", "ありがとう", "（送らない）" };
    private int _e6ChoiceLine = -1;   // ここに着いたら選択を出す（-1＝提示済み）
    private ChoiceOverlay? _e6Choice;
    private double _e6ChoiceT;        // 提示からの経過＝迷い秒数（RecordChoice へ渡す）

    private struct DLine { public string Who; public string Text; }   // Who: "地"=語り / "ミナ" / 三人の名 / "あなた"
    private readonly List<DLine> _gaze = new();   // PhGaze（E5b 前半・見上げる10行）
    private readonly List<DLine> _walk = new();   // PhWalk（E5b 後半・歩く8行）
    private readonly List<DLine> _end  = new();   // PhEnd （E6 END）

    public override void _Ready()
    {
        _font = UiKit.Zen; // 非ピクセル（滑らかゴシック）
        // E5b はオルゴール（未調達＝null なら無音のまま）。主題（BgmMenu）が戻るのは E6 の1行目。
        //   MusicOnce＝1周で無音に落ちる再生。台本の「…………。」の行では下で明示的に止める。
        if (Audio.Instance != null && Audio.Instance.BgmEpilogueWalk != null)
        {
            Audio.Instance.MusicOnce(Audio.Instance.BgmEpilogueWalk, 2.0f);
            _musicStarted = true;
        }
        _tears = ResourceLoader.Load<Texture2D>("res://char/mina_tears.png");
        BuildSky();
        BuildWalkers();
        _game = GetNodeOrNull<GameManager>("/root/Game");
        BuildRoll();        // E7 のロール（末尾の一行に【終】が入る）

        // ── E5b 見上げる（夜。四人が並んで立ち止まっている。08 E5b 前半10行）──
        void G(string who, string t) => _gaze.Add(new DLine { Who = who, Text = t });
        G("地", "その夜、タイムラインは、少しだけ静かでした。");
        G("ミナ", "……ご主人様。今夜は、こちらを。——わたくしの目で、見た空です。");
        G("ミナ", "……数えきれません。……数えるのが、仕事なのですが。");
        G("ミナ", "ひとつ、ひとつが、どなたかの画面です。……あれで、ぜんぶではありません。");
        G("ミナ", "……ここからだと、光っているところしか、見えません。");
        G("ミナ", "……光っていないほうは、観測できません。送られていないので。");
        G("ミナ", "この旅で、わたくしが拾えたのは——三件でした。");   // 三人＝ステージ数の固定値
        G("ミナ", "…………。");                                       // ここで曲を完全停止（無音）
        G("ミナ", "膨らんで、壊れてしまう前に。……拾えるところにいたい、と思います。");
        G("ミナ", "——できることは、数えることと、覚えていることだけ、ですが。");

        // ── E5b 歩く（夜 → 明け方。空が白んでいく。08 E5b 後半8行）──
        void K(string who, string t) => _walk.Add(new DLine { Who = who, Text = t });
        K("地", "四人は、歩きはじめました。");
        K("あかり", "——おはよ。");                                   // まだ暗いうちに言う（早すぎる挨拶）
        K("こはる", "……こんにちは。");                               // 時刻が合っていない。直さない
        K("ミナ", "……本日、二件目です。");                            // ミナは受けない。数える
        K("レイ", "……なんか用? ……いえ、別に。ついてくだけ。");
        K("地", "誰も、名前を呼びませんでした。");
        K("ミナ", "……ご主人様。歩数を、数えておりました。");
        K("ミナ", "四人分に、なっていました。");                        // ここで空が明ける → そのまま E6 へ

        // ── E6 END（08 E6）。E7 の後に来る＝作品全体の最後の場面。──
        _end.Add(new DLine { Who = "ミナ", Text = "ご主人様。本日の業務は、以上です。" });
        _e6ChoiceLine = _end.Count;   // ここに着いたら最後の選択を出す
    }

    private void ShowE6Choice()
    {
        _e6ChoiceT = 0;
        // 沈黙20秒の自動決定は末尾へ落ちるので、（送らない）を末尾に置く（台本どおり）。
        // カットシーン＝盤面が無いので onBoard は既定(false)＝画面全体の中心へ。
        _e6Choice = ChoiceOverlay.Show(this, E6Choices, defaultSel: E6Choices.Length - 1);
    }

    // E6 の確定：送った言葉と迷い秒数を記録し、受け（対句）と END の2行を挿し込む。
    private void ApplyE6Choice(int sel)
    {
        bool sent = sel < E6Choices.Length - 1;
        float hesitation = (float)_e6ChoiceT;
        // （送らない）は言葉ではないので送信語にも散る語にも数えない＝表示候補2件が丸ごと散る（P3・S3-7 と同じ流儀）。
        var others = new List<string>();
        for (int i = 0; i < E6Choices.Length - 1; i++) if (i != sel) others.Add(E6Choices[i]);
        _game?.RecordChoice("e6", sent ? E6Choices[sel] : "", others, hesitation);
        _e6ChoiceLine = -1;
        // 【迷】の対句：P2 の迷い秒数と比べて3分岐。P2 の実測秒数をそのまま差し込む（丸めは Prologue と同じ流儀）。
        float p2 = _game?.HesitationAt("p2") ?? 0f;
        int p2Sec = Mathf.Max(1, Mathf.RoundToInt(p2));
        string couplet = !sent
            ? "……無言。ふふ。それも、集計に入れておきます。"
            : hesitation <= p2
                ? $"……ええ。いまの、{p2Sec}秒も、かかりませんでしたね。"
                : "……今日は、長かったですね。……ええ。集計だけ、しています。";
        var after = new List<DLine>();
        if (sent) after.Add(new DLine { Who = "あなた", Text = E6Choices[sel] });
        after.Add(new DLine { Who = "ミナ", Text = couplet });
        after.Add(new DLine { Who = "ミナ", Text = "いってらっしゃいませ、ご主人様。" });     // 送り出す側の反転
        after.Add(new DLine { Who = "ミナ", Text = "——ええ、ご主人様。わたくしは、どこにも行きませんよ。" }); // END
        _end.AddRange(after);
        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1; _readKey = -1;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _lineT += delta;
        // 会話送り／各フェーズの決定：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも進める共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        // R / Start 長押し(0.45s)：スタッフロール以降は「タイトルへ」、それ以前は最初から(Prologue)
        // ＝演出のやり直し（即発は誤爆で読み進みを失いやすい→長押し化。ここはポーズ対象外なので Start 可）。
        if (_retry.Update(delta, Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start)))
        {
            GetTree().ChangeSceneToFile(_phase >= PhRoll ? "res://TitleMenu.tscn" : "res://Prologue.tscn");
            return;
        }

        // 現在行を2行ページに割り、タイプライターは現在ページ内を進める（語り/会話の行フェーズだけ）。
        string? curT = CurLineText();
        EnsurePages();
        int pageLen = curT != null ? CurPage.Length : 0;
        if (curT != null && _reveal < pageLen)
            _reveal = Mathf.Min(pageLen, (float)(_reveal + delta * (_game?.MsgCharsPerSec ?? 48f)));

        // 既読スキップ（#22）：行の表示開始時に一度だけ「既読か」を控え（＝高速送りの可否）、表示と同時に既読へ記録。
        int readKey = _phase * 1000 + _line;
        if (curT != null && _readKey != readKey)
        {
            _readKey = readKey;
            _lineWasRead = _game?.IsLineRead(curT) ?? false;
            _game?.MarkLineRead(curT);
        }
        _ffNow = curT != null && Hud.SkipHeld && _lineWasRead; // 未読行では効かない

        switch (_phase)
        {
            case PhGaze:   // E5b 見上げる（夜）
                if ((zEdge || _ffNow) && _lineT >= 0.25)  // _ffNow=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
                {
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; } // 1回目で現在ページ全文（早送り）
                    else if (!LastPage) { NextPage(); }                          // 後続ページがあれば続きへ
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++; _page = 0; _pagedKey = -1;
                        // 台本の「…………。」を送り切ったところで曲を完全停止（無音）。残り2行は無音のまま。
                        //   曲が未調達で最初から鳴っていないときは呼ばない（Music() が空の Tween を作る）。
                        if (_line == SilenceLine && _musicStarted) { Audio.Instance?.StopMusic(1.2f); _musicStarted = false; }
                        if (_line >= _gaze.Count) { _phase = PhWalk; _t = 0; _line = 0; }
                    }
                }
                break;
            case PhWalk:   // E5b 歩く（夜 → 明け方）
                if ((zEdge || _ffNow) && _lineT >= 0.25)
                {
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; }
                    else if (!LastPage) { NextPage(); }
                    else
                    {
                        _lineT = 0; _reveal = 0; _line++; _page = 0; _pagedKey = -1;
                        if (_line >= _walk.Count) { _phase = PhRoll; _t = 0; _line = 0; }
                    }
                }
                break;
            case PhRoll:   // E7 スタッフロール → E6 END
                float rollEnd = (H + _roll.Length * RollLineH + 24f) / RollSpeed;
                // --shot（撮影）のときだけ Z の飛ばしを遅らせる。自動プレイは Z を常時パルスするので、
                //   従来どおりだと 1 秒でロールを飛ばしてしまい、ロールの実画面が一度も撮れない
                //   （ChoiceOverlay の ShotHoldGate と同じ作法。通常プレイには一切影響しない）。
                double rollSkipGate = ShotHold ? 12.0 : 1.0;
                if (_t >= rollEnd || (_t > rollSkipGate && zEdge))
                {
                    _phase = PhEnd; _t = 0; _line = 0; _lineT = 0; _reveal = 0; _pagedKey = -1;
                    // 主題（温かいメニューBGM）が戻る。終わりの余韻に主題が戻って一周する。
                    if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu, 2.0f);
                }
                break;
            case PhEnd:    // E6 END（最後の下書き選択 → 受け → END → タイトルへ）
                // 選択の提示中は会話送りを止め、決定だけを待つ。
                if (_e6Choice != null)
                {
                    _e6ChoiceT += delta;
                    if (!_e6Choice.Decided) break;
                    ApplyE6Choice(_e6Choice.Selected);
                    _e6Choice.QueueFree();
                    _e6Choice = null;
                    break;
                }
                // 「本日の業務は、以上です。」を送り切って選択点に着いたら提示する。
                if (_e6ChoiceLine >= 0 && _line >= _e6ChoiceLine) { ShowE6Choice(); break; }
                if ((zEdge || _ffNow) && _lineT >= 0.25)
                {
                    if (curT != null && _reveal < pageLen) { _reveal = pageLen; }
                    else if (!LastPage) { NextPage(); }
                    else
                    {
                        _lineT = 0; _reveal = 0; _page = 0; _pagedKey = -1;
                        // 未提示の選択点に着いたら会話の途中＝次フレームの提示に譲る（Final F4 と同じ作法）。
                        if (_line < _end.Count - 1 || _e6ChoiceLine >= 0) _line++;
                        else { GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); return; }
                    }
                }
                break;
        }
        UpdateSky(delta);
        UpdateWalkers(delta);
        QueueRedraw();
    }

    // 台本で「…………。」＝曲を完全停止する行（見上げ8行目）を送り切った直後の index。
    //   行の追加で番号がずれないよう実行時に引く。見つからなければ -1（＝停止しない）。
    private int SilenceLine
    {
        get { int i = _gaze.FindIndex(d => d.Text == "…………。"); return i < 0 ? -1 : i + 1; }
    }

    // ═════════ E5b の空（夜 → 明け方）═════════
    //   **満月と雲のある夜空の絵はまだ発注していない**。当面はコード描画のグラデーション＋月＋星で組む。
    //   絵が来たら res://char/bg2/epilogue_sky/ に置くだけで差し替わる（BuildSky が拾って
    //   _skyNight/_skyDawn に入り、コード描画（DrawProcSky）は自動で止まる）。期待するファイル名は
    //     sky_night.png … 夜（満月と雲）
    //     sky_dawn.png  … 明け方（同じ構図で空だけ白む。無ければ夜の絵を暖色へモジュレートして代用）
    //   いずれも 1280×720 想定（内部解像度 384×216 へ 0.3 倍＝BgLayers と同じ高さフィット）。
    private Sprite2D? _skyNight, _skyDawn;
    private bool _hasSkyTex;
    private float _dawnK;    // 0=夜 1=明け方
    private double _dawnT;
    // 空の色が目標へ追いつく速さの上限（1.0 ぶんに掛かる最短秒数）。行を早送りしても跳ねない。
    private const double DawnFadeSec = 2.5;

    private void BuildSky()
    {
        const string dir = "res://char/bg2/epilogue_sky/";
        const float s = H / 720f;   // 216/720 = 0.3
        Sprite2D? Add(string file, int z, float alpha)
        {
            string path = dir + file;
            if (!ResourceLoader.Exists(path)) return null;
            var tex = ResourceLoader.Load<Texture2D>(path);
            if (tex == null || tex.GetHeight() <= 0) return null;
            var spr = new Sprite2D
            {
                Name = file.Replace(".png", ""), Texture = tex, Centered = false,
                Scale = new Vector2(s, s), Position = Vector2.Zero,
                ZIndex = z, ZAsRelative = false,
                Modulate = new Color(1f, 1f, 1f, alpha),
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            };
            AddChild(spr);
            return spr;
        }
        _skyNight = Add("sky_night.png", -95, 1f);
        if (_skyNight == null) return;                       // 夜が無ければ丸ごとコード描画へ
        _skyDawn = Add("sky_dawn.png", -94, 0f);             // 明け方は α0 で重ねる（無くてもよい）
        _hasSkyTex = true;
        ApplySkyTint();
    }

    // 夜→明け方の進行。歩行フェーズ（PhWalk）で**行の進みに合わせて**明ける。
    //   時間ではなく行数を基準にするのは、読む速さ（と自動プレイの送り速度）に関わらず
    //   最終行「四人分に、なっていました。」で必ず明け切らせるため（台本 E5b の指定）。
    //   実際の追従は DawnFadeSec で緩めるので、行を早送りしても空は跳ねない。
    //   スタッフロールと END は明け方のまま（時間が一本で繋がる＝台本 E5b「繋ぎ目が無い」）。
    private void UpdateSky(double delta)
    {
        double target = _phase switch
        {
            PhGaze => 0.0,                                                  // 見上げ＝夜のまま
            PhWalk => Mathf.Min(1.0, (_line + 1.0) / Mathf.Max(1, _walk.Count)), // 歩行＝行が進むほど白む
            _      => 1.0,                                                  // ロール／END＝明け方
        };
        if (Mathf.IsEqualApprox(_dawnT, target)) return;
        double step = delta / DawnFadeSec;
        _dawnT = target > _dawnT ? Mathf.Min(target, _dawnT + step) : Mathf.Max(target, _dawnT - step);
        float k = (float)_dawnT;
        _dawnK = k * k * (3f - 2f * k);   // smoothstep
        if (_hasSkyTex) ApplySkyTint();
    }

    // 空の絵がある場合の明け具合の反映（夜↔明け方のたすき掛け）。
    //   明け方の絵が無いときは、夜の絵を暖色寄り・明度上げでモジュレートして代用する。
    private void ApplySkyTint()
    {
        if (_skyDawn != null)
        {
            _skyNight!.Modulate = new Color(1f, 1f, 1f, 1f - _dawnK);
            _skyDawn.Modulate = new Color(1f, 1f, 1f, _dawnK);
        }
        else
        {
            // 代用：夜の絵を明け方へ寄せる（青を抑えて赤を足し、全体を持ち上げる）。
            _skyNight!.Modulate = new Color(Mathf.Lerp(1f, 1.45f, _dawnK), Mathf.Lerp(1f, 1.20f, _dawnK),
                                            Mathf.Lerp(1f, 1.05f, _dawnK), 1f);
        }
    }

    // 空の絵が無いあいだの繋ぎ描画。上から下へのグラデーション＋満月＋雲の帯＋星。
    //   絵が来たら BuildSky が拾って _hasSkyTex=true になり、ここは呼ばれなくなる。
    private void DrawProcSky()
    {
        // 天頂と地平の色を夜↔明け方で補間する。
        //   下端はテキストボックス（上端 H-58）に隠れるので、明けの暖色は**地平線（GroundY）で
        //   出し切る**ようグラデーションを GroundY までに収める＝ボックスの上に朝が見える。
        Color topN = new(0.04f, 0.05f, 0.12f), botN = new(0.11f, 0.13f, 0.24f);
        Color topD = new(0.30f, 0.36f, 0.56f), botD = new(0.95f, 0.76f, 0.58f);
        Color top = topN.Lerp(topD, _dawnK), bot = botN.Lerp(botD, _dawnK);
        // 水平の帯で塗る（設計解像度が低いので帯でバンディングは出ない）。
        const int Bands = 30;
        float bh = GroundY / Bands;
        for (int i = 0; i < Bands; i++)
        {
            float u = (i + 0.5f) / Bands;
            DrawRect(new Rect2(0, i * bh, W, bh + 1f), top.Lerp(bot, u * u));
        }
        // 星。明けるにつれて消える。位置は固定シード（毎フレーム同じ空＝ちらつかない）。
        float starA = (1f - _dawnK) * 0.85f;
        if (starA > 0.01f)
        {
            var rng = new RandomNumberGenerator { Seed = 20260907 };
            for (int i = 0; i < 90; i++)
            {
                float x = rng.RandfRange(0, W), y = rng.RandfRange(0, GroundY - 6f);
                float tw = 0.6f + 0.4f * Mathf.Sin((float)_t * 1.7f + i * 2.3f);   // 弱い瞬き
                DrawRect(new Rect2(x, y, 1f, 1f), new Color(1f, 1f, 1f, starA * tw * 0.9f));
            }
        }
        // 満月（台本の指定）。明けても薄く残す。
        var moon = new Vector2(W * 0.24f, H * 0.24f);
        DrawCircle(moon, 16f, new Color(0.95f, 0.95f, 0.86f, 0.10f * (1f - _dawnK * 0.6f)));  // ハロ
        DrawCircle(moon, 9f, new Color(0.98f, 0.98f, 0.92f, Mathf.Lerp(0.95f, 0.35f, _dawnK)));
        // 雲。ゆっくり右から左へ流す（歩いている距離感）。明け方は暖色に染まる。
        //   1つの雲は「潰した円を数個重ねた塊」で作る（矩形だと看板に見える）。
        Color cloudN = new(0.16f, 0.18f, 0.30f, 1f), cloudD = new(0.78f, 0.60f, 0.56f, 1f);
        Color cloud = cloudN.Lerp(cloudD, _dawnK);
        for (int i = 0; i < 5; i++)
        {
            float speed = 1.6f + i * 0.55f;                  // 手前ほど速い
            float y = 26f + i * 20f;
            float r = 7f + i * 2.2f;                          // 塊の大きさ
            float span = r * 5.5f;
            float x = Mathf.PosMod((float)(-_t * speed) + i * 149f, W + span * 2f) - span;
            float a = (0.28f + i * 0.06f) * Mathf.Lerp(1f, 0.85f, _dawnK);
            // 潰した円 5 個を少しずつずらして重ねる（中心が厚く、端が薄い＝雲の形）。
            DrawSetTransform(new Vector2(x, y), 0f, new Vector2(1f, 0.42f));
            for (int k = 0; k < 5; k++)
            {
                float kx = (k - 2f) * r * 0.95f;
                float kr = r * (1f - Mathf.Abs(k - 2f) * 0.22f);
                DrawCircle(new Vector2(kx, Mathf.Sin(k * 1.9f + i) * r * 0.25f), kr, cloud with { A = a });
            }
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        // 地面（四人が立っている／歩いている線）。空が明けるほど手前も持ち上がる。
        Color ground = new Color(0.05f, 0.05f, 0.09f).Lerp(new Color(0.22f, 0.19f, 0.22f), _dawnK);
        DrawRect(new Rect2(0, GroundY, W, H - GroundY), ground);
        DrawRect(new Rect2(0, GroundY, W, 1f), ground.Lightened(0.35f));
    }

    // ═════════ E5b の四人（見上げる／歩く）═════════
    //   素材は char/v3/walk/{name}_walk_{1..4}.png（4コマ・右向き・高さ360px・透過）と
    //   {name}_up.png（見上げる後ろ姿）。
    //   再生は 8ステップ（1 2 3 4 1 2 3 4）／1コマ 0.14秒／1周期 1.12秒。
    //   後半4ステップは**上下オフセットの位相を反転**して「逆脚」に見せる（左右反転は禁止＝
    //   髪とペンライトが逆になり別人に見えるため。素材は右向きのまま使う）。
    //   四人で位相をずらす（台本「揃っていないまま並んで歩ける」を画で出す）。
    private static readonly string[] WalkNames = { "akari", "koharu", "mina", "rei" };
    private static readonly float[] WalkPhase  = { 0.00f, 0.35f, 0.55f, 0.80f };   // あかり/こはる/ミナ/レイ
    private const float StepSec = 0.14f;              // 1コマ
    private const int Steps = 8;                      // 1 2 3 4 1 2 3 4
    private const float CycleSec = StepSec * Steps;   // 1.12秒
    private const float WalkH = 36f;                  // 表示高さ＝自機（36px）に合わせる
    private const float FootY = GroundY + 2f;         // 接地線（地面の上端よりわずかに下＝地に足がつく）
    // 地面の上端。テキストボックス（上端 H-58＝158）に足元が隠れないよう、四人の全身が
    //   ボックスより上に収まる高さに置く（FootY 152 − WalkH 36 ＝ 頭 116）。
    private const float GroundY = H - 66f;
    private readonly Texture2D?[,] _walkTex = new Texture2D?[4, 4];   // [人, コマ]
    private readonly Texture2D?[] _upTex = new Texture2D?[4];
    private bool _hasWalkers;
    private double _walkT;   // 歩行の時間（見上げでは進めない＝止まって立っている）

    private void BuildWalkers()
    {
        const string dir = "res://char/v3/walk/";
        for (int p = 0; p < WalkNames.Length; p++)
        {
            for (int f = 0; f < 4; f++)
            {
                string path = $"{dir}{WalkNames[p]}_walk_{f + 1}.png";
                if (ResourceLoader.Exists(path)) _walkTex[p, f] = ResourceLoader.Load<Texture2D>(path);
            }
            string up = $"{dir}{WalkNames[p]}_up.png";
            if (ResourceLoader.Exists(up)) _upTex[p] = ResourceLoader.Load<Texture2D>(up);
            if (_walkTex[p, 0] != null) _hasWalkers = true;
        }
    }

    private void UpdateWalkers(double delta)
    {
        if (_phase == PhWalk) _walkT += delta;   // 見上げ（PhGaze）は止まっている
    }

    // 四人を横に並べて描く。x は画面下 1/4 に等間隔、y は接地線。
    //   見上げ＝{name}_up.png（後ろ姿）を静止で。歩き＝4コマを 8ステップで回し、
    //   後半4ステップは上下オフセットの位相を反転する（逆脚）。
    private void DrawWalkers(bool walking)
    {
        if (!_hasWalkers) return;
        // 並び順は面の順（あかり→こはる→レイ）＋ミナが四人目。描画配列は WalkNames の順なので
        // 表示順を別に持つ（配列の順を変えると位相の対応もずれるため）。
        int[] order = { 0, 1, 3, 2 };   // あかり・こはる・レイ・ミナ
        float span = 128f, x0 = W * 0.5f - span * 0.5f;
        for (int i = 0; i < order.Length; i++)
        {
            int p = order[i];
            float x = x0 + span * i / (order.Length - 1);
            Texture2D? tex;
            float bob = 0f;
            if (walking)
            {
                // 位相をずらした周期内の位置（0..1）→ 8ステップ
                float u = Mathf.PosMod((float)_walkT / CycleSec + WalkPhase[p], 1f);
                int step = Mathf.Clamp((int)(u * Steps), 0, Steps - 1);
                tex = _walkTex[p, step % 4];
                // 上下 1.5px の正弦揺らし。後半4ステップ（step>=4）は**位相を半周ずらす**＝逆脚に見せる
                //   （左右反転はしない。反転すると髪とペンライトが逆になり別人に見えるため）。
                //   Abs(Sin) は周期が半分になって位相反転が効かないので、素の Sin を 0..1 へ写す。
                float sub = u * Steps - step;                       // コマ内の進み 0..1
                float ph = (step % 4 + sub) / 4f;                   // 4コマぶんの位相 0..1
                if (step >= 4) ph += 0.5f;                          // 逆脚
                bob = -1.5f * (0.5f + 0.5f * Mathf.Sin(ph * Mathf.Pi * 2f));
            }
            else
            {
                tex = _upTex[p] ?? _walkTex[p, 0];
                bob = -0.6f * Mathf.Sin((float)_t * 1.1f + WalkPhase[p] * 6f);   // 呼吸だけ
            }
            if (tex == null) continue;
            float h = WalkH, w = h * tex.GetWidth() / Mathf.Max(1, tex.GetHeight());
            // 足元の楕円影（薄く・明けるほど濃く短く）。地面に置いて見せるための最小限。
            DrawSetTransform(new Vector2(x, FootY), 0f, new Vector2(1f, 0.28f));
            DrawCircle(Vector2.Zero, w * 0.34f, new Color(0f, 0f, 0f, Mathf.Lerp(0.18f, 0.32f, _dawnK)));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            DrawTextureRect(tex, new Rect2(x - w * 0.5f, FootY - h + bob, w, h), false);
        }
    }

    public override void _Draw()
    {
        if (!_hasSkyTex) DrawProcSky();

        switch (_phase)
        {
            case PhGaze:
                DrawWalkers(walking: false);
                DrawNarration(_gaze, _line);
                break;
            case PhWalk:
                DrawWalkers(walking: true);
                DrawNarration(_walk, _line);
                break;
            case PhRoll: DrawStaffroll(); break;
            case PhEnd:
                // END は明け方の空をそのまま残しつつ沈める（最後の選択肢＝ChoiceOverlay の
                //   紫の文字が明るい空に溶けて読めなくなるため。空は「繋ぎ目が無い」まま後ろに残る）。
                DrawRect(new Rect2(0, 0, W, H), new Color(0.02f, 0.03f, 0.06f, 0.78f));
                DrawEnd();
                break;
        }

        // R/Start 長押しリトライの充填チップ（押している間だけ・設計座標で描く）。
        if (_retry.Progress > 0f)
        {
            UiKit.BeginDesign(this);
            Hud.DrawRetryHoldChip(this, _retry.Progress,
                (Pad.ShowKeyboard ? "R" : Pad.Face(JoyButton.Start))
                + (_phase >= PhRoll ? " 長押しでタイトルへ" : " 長押しでさいしょから"));
            UiKit.EndDesign(this);
        }
    }

    // 背景に直乗せする文字（スタッフロール）用のドロップシャドウ付き DrawString。
    //   ボックスの下敷きが無い画面では背景の明部に本文が溶けるため、(0.5,0.5) の黒を先に敷いて浮かせる。
    private void Shadowed(Font f, Vector2 pos, string s, HorizontalAlignment al, float w, int size, Color c)
    {
        DrawString(f, pos + new Vector2(0.5f, 0.5f), s, al, w, size, new Color(0f, 0f, 0f, 0.55f * c.A));
        DrawString(f, pos, s, al, w, size, c);
    }

    private void DrawStaffroll()
    {
        if (_font == null) return;
        // ロール中は空を沈める（文字が最優先）。明け方の空はそのまま後ろに残す。
        DrawRect(new Rect2(0, 0, W, H), new Color(0.02f, 0.03f, 0.06f, 0.55f));
        for (int i = 0; i < _roll.Length; i++)
        {
            float y = H + i * RollLineH - (float)_t * RollSpeed;
            if (y < -RollLineH || y > H) continue;
            string line = _roll[i];
            if (line.Length == 0) continue;
            bool head = line.StartsWith("──");
            bool post = line.StartsWith("[");   // 投稿枠（[名前 @handle] 本文）
            Color c = head ? Cool with { A = 0.9f }
                    : post ? UiKit.CutInk with { A = 0.95f }
                    : Ink;
            // 【終】の一行だけクライマックス級（旧 "stay." の枠）。見出し・投稿とは重ならない。
            int sz = !head && !post && line == _rollLast ? UiKit.CutClimax : UiKit.CutBody;
            Shadowed(_font, new Vector2(0, y), line, HorizontalAlignment.Center, W, sz, c);
        }
        if (((int)(_t * 1.5f) % 2) == 0)
            Shadowed(_font, new Vector2(0, H - 10), "Z：つづける", HorizontalAlignment.Center, W, UiKit.CutNote,
                UiKit.CutInk2 with { A = 0.7f });
    }

    // タイプライターで送る現在行のテキスト（会話フェーズのみ。スタッフロールは対象外）。
    private string? CurLineText()
    {
        if (_phase == PhGaze) return _line < _gaze.Count ? _gaze[_line].Text : null;
        if (_phase == PhWalk) return _line < _walk.Count ? _walk[_line].Text : null;
        if (_phase == PhEnd)  return _line < _end.Count ? _end[_line].Text : null;
        return null;
    }

    private void DrawNarration(List<DLine> lines, int idx)
    {
        if (_font == null || idx >= lines.Count) return;
        DrawLineBox(lines[idx]);
    }

    private void DrawEnd()
    {
        if (_font == null || _line >= _end.Count) return;
        // クライマックス：ミナの台詞行で落涙の立ち絵を差す（画をピークに集める／§8）。
        if (_end[_line].Who == "ミナ" && _tears != null)
        {
            float a = Mathf.Clamp((float)_lineT / 0.5f, 0f, 1f);
            float ph = 116f, pw = ph * _tears.GetWidth() / Mathf.Max(1, _tears.GetHeight());
            DrawTextureRect(_tears, new Rect2(W / 2f - pw / 2f, H - 58f - ph + 6f, pw, ph), false,
                new Color(1f, 1f, 1f, a));
        }
        DrawLineBox(_end[_line]);
        // END は最後の1行だけ。選択がまだ出ていない間（＝末尾が「本日の業務は、以上です。」）は出さない。
        if (_e6ChoiceLine < 0 && _line >= _end.Count - 1)
            Shadowed(_font, new Vector2(0, 40f), "END", HorizontalAlignment.Center, W, UiKit.CutClimax,
                UiKit.CutInk with { A = 0.9f });
    }

    // 話者ごとの縁色。三人（あかり／こはる／レイ）は面の色を借りて、ミナと取り違えないようにする。
    private static Color EdgeFor(string who) => who switch
    {
        "地"      => UiKit.CutNarr,     // 語り＝話者名なし・中央寄せ
        "あなた"  => UiKit.CutWarm,     // 送られた下書き（E6）
        "あかり"  => new Color("ffb0b8"),
        "こはる"  => new Color("ffd28a"),
        "レイ"    => new Color("9fd8ff"),
        _          => Cool,              // ミナ
    };

    // 下部の語り／会話ボックス。Who: "地"=語り / "ミナ" / 三人の名（あかり・こはる・レイ）/ "あなた"。
    //   旧 "UI"（画面テキスト・起動記録の等幅コード緑）は E2〜E5 の削除で使う行が無くなったので落とした。
    private void DrawLineBox(DLine d)
    {
        bool narr = d.Who == "地";        // 語り＝話者名なし・中央寄せでセリフと区別
        var font = _font;
        Color edge = EdgeFor(d.Who);
        // 現在ページ（2行固定・禁則つき）。ボックスは2行分の固定高さ（行数で伸ばさない＝全ボックス統一）。
        string page = CurPage;
        var lines = UiKit.WrapLines(font, page, UiKit.CutBody, W - 56);
        float boxTop = H - 58f;   // 2行固定（下余白12px＝額縁を効かせる）
        // ボックス（Hub/Shop と同じ角丸＋話者色の額縁。UiKit.CutBox で3画面共通）
        UiKit.CutBox(this, new Rect2(14, boxTop, W - 28, H - 10f - boxTop), edge, 0.5f);
        string label = narr ? "" : d.Who;
        if (label != "")
            DrawString(UiKit.ZenBold, new Vector2(24, boxTop + 12), label, HorizontalAlignment.Left, -1, UiKit.CutSpeaker, edge);
        var align = narr ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        // 中央寄せのナレは「中央から左右へ広がる」見え方になるタイプライターをやめ、現在ページ全文をその場でフェードイン表示。
        //   （中央寄せ＋部分文字列だと毎フレーム再センタリングされて左右に展開して見えるため）。
        // セリフ（左寄せ）は従来どおり左→右のタイプライターで送る。
        Color ink = Ink;
        int shown;
        if (narr)
        {
            shown = page.Length;                             // 現在ページ全文をその場で（広がる演出なし）
            float a = Mathf.Clamp((float)_lineT / 0.35f, 0f, 1f); // 短いフェードイン
            ink = new Color(Ink.R, Ink.G, Ink.B, a);
        }
        else
        {
            shown = Mathf.Clamp((int)_reveal, 0, page.Length);
        }
        UiKit.TypewriterLines(this, font, lines, new Vector2(24, boxTop + 27f), W - 56, UiKit.CutBody, ink, shown, align);
        // 既読高速送り中の控えめな表示（ボックス右上・#22）。
        if (_ffNow)
            DrawString(UiKit.ZenBold, new Vector2(W - 42, boxTop + 12), "▶▶", HorizontalAlignment.Left, -1, UiKit.CutSpeaker,
                new Color(Cool, 0.8f));
        // 送り三角は現在ページの全文表示後だけ点滅（本編と同じ作法。後続ページも同じ▼で示す）。
        // ナレは現在ページを即表示するので、フェード完了で点滅（タイプライター完了を待たない）。
        bool ready = narr ? _lineT >= 0.35 : _reveal >= page.Length;
        if (ready && ((int)(_t * 2f) % 2) == 0)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, UiKit.CutNote,
                new Color(1f, 1f, 1f, 0.7f));
    }
}
