using Godot;
using System;
using System.Collections.Generic;

public partial class StoryFilm : Node2D
{
    protected readonly record struct Line(int Shot, string Time, string Speaker, string Text, double Hold = 0.2);

    protected Hud _hud = null!;
    protected Node _world = null!;
    protected ProcessModeEnum _worldMode;
    private GameManager _game = null!;
    private ProcessModeEnum _gameMode;
    protected Action _completed = null!;
    protected Line[] _lines = null!;
    protected ShaderMaterial _grade = null!;
    protected bool _aftermath, _held, _leaving, _restored, _suppressed;
    protected int _line, _shot;
    protected double _fadeT, _lineT, _readT, _shotT, _blendT;
    protected bool _started;
    protected const double FadeTime = 0.65;
    // 背後（StageBackground/BgLayers のボスイラスト）を塞ぐ不透明の黒板。
    //   このノードの Modulate フェードはレターボックスの帯にも等しく乗る＝入り／明けの 0.65 秒は
    //   画面全体が半透明になり、背後のボス背景がそのまま透けていた（2026-09-22 実機指摘）。
    //   フェードの影響を受けない**別ノード**として一段奥に敷いて塞ぐ。詳細は CutsceneBackdrop。
    private CutsceneBackdrop _backdrop = null!;

    // ───────── 一度見た回想のスキップ（2026-09-22 ユーザー要望「ムービーは一度みたらスキップ」）─────────
    //   回想は8本（4人×memory/aftermath）あり、これまでスキップ手段が無かった。初回は最後まで見せ、
    //   見終えた時点で FilmSkip.Key(FilmId) を既読に落とす＝2回目以降だけ Esc/B 長押しで飛ばせる。
    //   ・キーは1本ごと（"akari_memory" / "akari_aftermath" …）。まとめると見ていない話まで飛ぶ。
    //   ・スキップしても「最後まで読んだ」と同じ道（_leaving → Restore → _completed()）を通る。
    //     memory の呼び元はボス曲の張り直し、aftermath の呼び元はクリア会話への引き渡しを
    //     completed: に持っているので、ここを迂回させると進行が壊れる。
    private readonly FilmSkip _skip = new();
    protected virtual string FilmId => $"{_storyKey}_{(_aftermath ? "aftermath" : "memory")}";

    // ───────── 時制の見出し（「いつのシーンか」）─────────
    // 以前は左上（64,19）に _lines[_line].Time を**常時**出していた（ユーザー指摘「回想シーンで
    //   左上に出しているいつのシーンか」）。常時表示は絵の隅に札が貼りっぱなしで、場面が変わった
    //   ことを知らせる力が無く、シネスコの額の外に情報が逃げていた。
    //   → 会話欄の位置に、場面が変わった瞬間だけ出す**字幕**へ変える。
    // ・話者名を持たない＝名前欄そのものを描かない。本作のナレはミナの肉声（三人称の語り手は居ない）
    //   ので、これを Hud.ShowMessage（＝LineKind.Narration。会話ログに「ナレーション」として積まれ、
    //   既読記録も付く）に流すと**ミナが時刻を読み上げた**ことになってしまう。
    //   時制は語りではなく画面の見出しなので、Hud を通さずこの _Draw が自前で描く
    //   （バックログを汚さない・送り音が鳴らない・既読スキップの対象にもならない）。
    // ・字幕として読ませる工夫：本文（白・24px）より一段落とした淡色＋小さめの字、字間を開け、
    //   細い罫を添えて「見出しの体裁」にする。セリフの吹き出し様式（枠・話者色）は使わない。
    //   置き場所は会話欄の話者名の行の右端（左詰めの話者名とぶつからない。DrawTimeCard 参照）。
    private string _timeText = "";        // いま出している見出し（空＝出していない）
    private string _timeShown = "";       // 直近に見出しを出した Time。同じ時制が続く行では出し直さない
    private double _timeT;                // 見出しの経過秒
    private double _timeHold;             // セリフを出すまで待つ秒（0＝もう出した）
    private const double TimeLead = 1.05; // 見出しだけを見せる一拍
    private const double TimeLife = 3.2;  // 見出しが消えるまで（セリフと重なって余韻を残す）
    private const double TimeWipe = 0.42; // 罫が伸びて字が出るまで

    protected string _atlasPath = "";
    protected string _storyName = "";
    protected int _atlasRows = 3;
    protected Dictionary<int, string> _shotImages = new();
    private Texture2D _atlas = null!;

    // 回想BGMを引くためのキー（"rei"/"akari"/"koharu"/"mina"）。_storyName の小文字＝
    //   派生側で別に持たせず、ここで一意に導出する（表示名と選曲キーがズレない）。
    private string _storyKey => _storyName.ToLowerInvariant();

    public override void _Ready()
    {
        AddToGroup("storyfilm");
        _worldMode = _world.ProcessMode;
        // BubblePaused alone leaves the boss's BREAK/RECLOSE clock running.
        _world.ProcessMode = ProcessModeEnum.Disabled;
        _game = GetNode<GameManager>("/root/Game");
        _gameMode = _game.ProcessMode;
        _game.ProcessMode = ProcessModeEnum.Disabled;
        _suppressed = _hud.SuppressCallouts;
        _hud.SuppressCallouts = true;
        _hud.HideBubble();
        _hud.HoldBubble = true;
        _hud.SetCinematicMode(true, accent: StoryAccent());
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        foreach (Node hazard in GetTree().GetNodesInGroup("aoe"))
            if (hazard is AreaStrike) hazard.QueueFree();
        // 回想の曲へクロスフェードする（2026-09-14②。ユーザー実機指摘「ボス戦の回想シーンに
        //   BGM入ってないよ」への対応）。ここは以前 StopMusic(0.7f) で**無音に落とすだけ**だった。
        //   ・memory（戦闘中・モノクロ）＝過去の傷。戦闘曲から翳りのある曲へ。
        //   ・aftermath（撃破後・カラー）＝癒えた未来。温度の戻る曲へ。
        //   StoryBgm() が null を返す枠（＝ミナの aftermath）は**意図的な無音**なので、
        //   従来どおり StopMusic して沈黙のまま通す（直後の Final が「無音→挿入歌」を持つため。
        //   詳細は Audio.StoryBgm() のコメントと BGM/candidates.md ㉖）。
        var storyBgm = Audio.Instance?.StoryBgm(_storyKey, _aftermath);
        if (storyBgm != null) Audio.Instance?.Music(storyBgm, 0.7f);
        else Audio.Instance?.StopMusic(0.7f);
        _held = Pad.AdvanceHeld();
        _shot = _lines[0].Shot;
        _atlas = GD.Load<Texture2D>(_atlasPath);
        _grade = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/story_film.gdshader") };
        SetFrame("scene", _shot);
        SetFrame("previous", _shot);
        _grade.SetShaderParameter("grayscale", !_aftermath);
        AddChild(new TextureRect
        {
            Texture = _atlas,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Size = new Vector2(384, 216), MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = _grade, ZIndex = -1,
        });
        Modulate = new Color(1, 1, 1, 0);
        // フィルム本体より先に、その一段奥へ黒板を立ち上げる（ZIndex はこのノードの1つ下）。
        _backdrop = CutsceneBackdrop.Attach(_hud, ZIndex);
        _skip.Begin(_game, FilmId);
        GD.Print($"[{_storyName}Story] {(_aftermath ? "aftermath" : "memory")} start"
                 + (_skip.Available ? " (skippable)" : ""));
    }

    public override void _Process(double delta)
    {
        if (Pad.UiBlocked(this)) { _held = true; return; }
        bool held = Pad.AdvanceHeld();
        bool edge = held && !_held;
        _held = held;
        _fadeT += delta;
        _shotT += delta;
        _blendT += delta;
        _grade.SetShaderParameter("motion_time", (float)_shotT);
        _grade.SetShaderParameter("blend_amount", Mathf.Clamp((float)(_blendT / 0.8), 0, 1));
        float fade = Mathf.Clamp((float)(_fadeT / FadeTime), 0, 1);
        Modulate = new Color(1, 1, 1, _leaving ? 1 - fade : fade);
        QueueRedraw();
        if (_leaving)
        {
            if (_fadeT < FadeTime) return;
            Restore();
            GD.Print($"[{_storyName}Story] {(_aftermath ? "aftermath" : "memory")} complete");
            _completed();
            QueueFree();
            return;
        }
        // 既読フィルムの長押しスキップ。立ち上げの 0.65 秒フェード中も受け付ける（そこで待たせる意味が無い）。
        //   飛ばしたあとは最終行まで読んだときと**同じ畳み方**（_leaving からの明けフェード）へ落とす。
        if (_skip.Update(delta)) { BeginLeave(); return; }
        if (!_started)
        {
            if (_fadeT < FadeTime) return;
            _started = true;
            ShowLine();
            return;
        }
        _lineT += delta;
        if (_timeText.Length > 0) _timeT += delta;
        // 時制の見出しの一拍。ここではまだセリフを出していないので、行送りの判定より先に返す
        //   （_hud は空＝DialogRevealed が true を返すため、素通りさせると _line が飛ぶ）。
        if (_timeHold > 0)
        {
            // オートは一拍を待たせる（そのための自動送り）。押した人と既読スキップだけ即座に明ける。
            //   FastForwarding は「表示中の行が既読」で立つフラグ＝ここでは直前の行を見ているが、
            //   既読スキップ中に見出しで止まらないという意図どおりに働く。
            if (_timeT >= _timeHold || edge || _hud.FastForwarding) ShowLineText();
            return;
        }
        if (_hud.DialogRevealed) _readT += delta;
        if (edge && _lineT >= 0.2 && !_hud.DialogRevealed)
        {
            _hud.RevealDialogNow();
            _readT = 0;
        }
        else if (_lineT >= _lines[_line].Hold && _hud.DialogRevealed
                 && (edge || _hud.FastForwarding || (_hud.AutoAdvance && _readT >= 1.4)))
        {
            _line++;
            if (_line == _lines.Length) BeginLeave();
            else ShowLine();
        }
    }

    // 回想を畳む（最終行を送り切った／既読スキップ）。どちらの道でも同じ明けフェードを通し、
    //   ここで「このフィルムは見た」を記録する＝次回から FilmSkip が開く。
    //   記録はセーブ（GameManager._idleDialogSeen → idleDialogSeen）に載るが、保存そのものは
    //   既存のオートセーブ点（ステージクリア／ハブ帰還／FINAL 記録）に任せる。回想の直後は必ず
    //   戦闘の続きかクリア処理へ戻るので、ここで個別に AutoSave を挟む必要はない。
    private void BeginLeave()
    {
        if (_leaving) return;
        _leaving = true;
        _fadeT = 0;
        FilmSkip.MarkSeen(_game, FilmId);
        _hud.HideBubble();
    }

    private void ShowLine()
    {
        var line = _lines[_line];
        if (line.Shot != _shot)
        {
            SetFrame("previous", _shot);
            _shot = line.Shot;
            SetFrame("scene", _shot);
            _blendT = 0;
            _shotT = 0;
        }
        _lineT = _readT = 0;
        // 時制が変わった行＝場面の切り替わり。見出しを先に一拍だけ見せてからセリフへ渡す。
        //   判定は Shot ではなく Time の変化で取る。4本とも「絵が変わる行は必ず時制も変わる」が、
        //   逆は成り立たない（例 AkariStoryFilm の shot 1 に「数年後」「別の日」「一か月前」の3場面、
        //   shot 4 の aftermath に3場面）。Shot だけを見ると、同じ絵のまま日が飛ぶ場面で
        //   見出しが出ず、常時表示だった頃より情報が減る。
        //   1行目は _timeShown が空なので必ず出る（4本とも Time は全行埋まっていて空文字は無い）。
        if (line.Time.Length > 0 && line.Time != _timeShown)
        {
            _timeShown = _timeText = line.Time;
            _timeT = 0;
            // 一拍のあいだセリフを出さずに待つ。**送りボタンは増やさない**：待ちは _Process が
            //   自動で明け、待っている間に送りを押せばその場で明ける（＝待ちたくない人は素通りできる）。
            _timeHold = TimeLead;
            return;
        }
        ShowLineText();
    }

    // 見出しの一拍が明けた（あるいは最初から不要だった）あとの、実際のセリフ表示。
    private void ShowLineText()
    {
        _timeHold = 0;
        // 一拍ぶん進んでいた行タイマを仕切り直す（待ち時間を「もう読んだ時間」に数えない）。
        _lineT = _readT = 0;
        var line = _lines[_line];
        if (line.Speaker.Length == 0) _hud.ShowMessage(line.Text);
        else _hud.ShowDialog(Hud.LineKind.Other, line.Text, otherName: line.Speaker);
    }

    private void SetFrame(string prefix, int shot)
    {
        bool fullFrame = _shotImages.TryGetValue(shot, out string? path);
        var texture = fullFrame ? GD.Load<Texture2D>(path!) : _atlas;
        var region = fullFrame ? new Vector4(0, 0, 1, 1)
            : new Vector4((shot % 2) / 2f, (shot / 2) / (float)_atlasRows, 0.5f, 1f / _atlasRows);
        _grade.SetShaderParameter(prefix + "_texture", texture);
        _grade.SetShaderParameter(prefix + "_region", region);
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        DrawStoryFrame();
        DrawTimeCard();
        // 既読のときだけ上辺の黒帯にスキップのヒントを出す（初回は存在ごと見せない）。
        //   明けフェード中は畳む処理が走っているので消す（飛ばしたあとにヒントが残って見える）。
        if (!_leaving) _skip.Draw(this);
        UiKit.EndDesign(this);
    }

    private Color StoryAccent() => _storyKey switch
    {
        "akari" => new Color("f0c969"),
        "koharu" => new Color("a6dac8"),
        "rei" => new Color("de91b9"),
        "mina" => new Color("87d7ed"),
        _ => UiKit.Info,
    };

    private Color StoryAccent2() => _storyKey switch
    {
        "akari" => new Color("f5a66a"),
        "koharu" => new Color("7bd79f"),
        "rei" => new Color("9a72d9"),
        "mina" => new Color("9a72d9"),
        _ => UiKit.Mina,
    };

    private string StoryLabel() => _storyKey switch
    {
        "akari" => "あかり",
        "koharu" => "こはる",
        "rei" => "レイ",
        "mina" => "ミナ",
        _ => _storyName,
    };

    private void DrawStoryFrame()
    {
        Color accent = StoryAccent();
        Color accent2 = StoryAccent2();
        Color ink = new(0.025f, 0.025f, 0.04f, 0.96f);
        Color glass = new(0.035f, 0.035f, 0.055f, 0.82f);

        UiKit.VGradient(this, new Rect2(0, 0, 1280, 88),
            new[] { new Color(0.010f, 0.012f, 0.020f, 0.98f), new Color(0.020f, 0.020f, 0.030f, 0.88f), new Color(0, 0, 0, 0f) },
            new[] { 0f, 0.72f, 1f });
        UiKit.VGradient(this, new Rect2(0, 480, 1280, 240),
            new[] { new Color(0, 0, 0, 0f), new Color(0.010f, 0.012f, 0.020f, 0.9f), ink },
            new[] { 0f, 0.24f, 1f });
        DrawRect(new Rect2(0, 0, 1280, 64), new Color(0.018f, 0.020f, 0.030f, 0.72f));
        DrawRect(new Rect2(0, 516, 1280, 204), new Color(0.018f, 0.018f, 0.026f, 0.78f));

        UiKit.HGradient(this, new Rect2(0, 64, 1280, 2), new Color(accent, 0f), new Color(accent, 0.65f));
        UiKit.HGradient(this, new Rect2(0, 516, 1280, 2), new Color(accent2, 0.55f), new Color(accent, 0f));
        DrawRect(new Rect2(18, 82, 2, 410), new Color(accent, 0.18f));
        DrawRect(new Rect2(1260, 82, 2, 410), new Color(accent2, 0.18f));

        const float panelX = 72f, panelY = 526f, panelW = 1136f, panelH = 174f;
        UiKit.Box(this, new Rect2(panelX, panelY, panelW, panelH), glass, 8f, new Color(accent, 0.28f), 1.2f);
        UiKit.VGradient(this, new Rect2(panelX + 1, panelY + 1, panelW - 2, 54),
            new[] { new Color(1, 1, 1, 0.055f), new Color(1, 1, 1, 0f) }, new[] { 0f, 1f });
        DrawRect(new Rect2(panelX + 20, panelY + 18, 3, 42), new Color(accent, 0.78f));
        DrawRect(new Rect2(panelX + panelW - 28, panelY + 18, 3, 42), new Color(accent2, 0.52f));

        string mode = _aftermath ? "AFTER SCENE" : "MEMORY LOG";
        string modeJ = _aftermath ? "アフターシーン" : "回想記録";
        float chipW = Mathf.Max(188f, UiKit.TextW(UiKit.Mono, mode, 18) + 44f);
        UiKit.Box(this, new Rect2(42, 18, chipW, 30), new Color(0.040f, 0.038f, 0.058f, 0.72f), 8f, new Color(accent, 0.45f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(64, 24), mode, 18, new Color(accent, 0.96f));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(64 + chipW, 25), $"  {StoryLabel()} / {modeJ}", 16, new Color(UiKit.Text2, 0.9f));

        float metaRight = _skip.Available ? 990f : 1194f;
        string progress = $"{_line + 1:00}/{_lines.Length:00}";
        float progressW = UiKit.TextW(UiKit.Mono, progress, 18);
        UiKit.Text(this, UiKit.Mono, new Vector2(metaRight - progressW, 25), progress, 18, new Color(UiKit.Text2, 0.86f));
        DrawProgressTicks(accent, accent2, metaRight - 58f);
    }

    private void DrawProgressTicks(Color accent, Color accent2, float right)
    {
        int count = Math.Max(1, _lines.Length);
        float w = 206f, x0 = right - w, y = 37f;
        DrawRect(new Rect2(x0, y, w, 1.4f), new Color(1, 1, 1, 0.11f));
        for (int i = 0; i < count; i++)
        {
            float x = x0 + w * i / Math.Max(1, count - 1);
            float h = i == _line ? 12f : i < _line ? 8f : 5f;
            Color c = i <= _line ? accent.Lerp(accent2, count <= 1 ? 0 : i / (float)(count - 1)) : new Color(UiKit.Text4, 0.32f);
            DrawRect(new Rect2(x - 1.2f, y - h * 0.5f, 2.4f, h), c);
        }
    }

    // 時制の見出し（字幕）。会話欄の話者名の行（Hud のシネマ表示は 112,542 に話者名を描く）に、
    //   その**逆端**から出す。時制が変わった瞬間だけ現れ、TimeLife 秒かけて自分で消える。
    //   ・右端に寄せる理由：見出しの一拍が明けた直後の行に話者が居る場合（例 KoharuStoryFilm の
    //     各場面の頭は同級生／こはるの台詞）、左詰めだと 112,542 の話者名と同じ場所に重なる。
    //     実際に重なったのを確認して右寄せへ直した（build/qa_story/akari/shots/memory_pressure.png の初版）。
    //     右端は Hud の既読スキップ印（1168,546）とページ送りの▼（1136,664）を避けて 1136 で止める。
    //   ・左詰めの話者名＝発話、右端の淡い小さな字＝画面の見出し、と位置と書体で役割が分かれる。
    //   セリフ本文は Hud が下の行（112,588）に描くので、縦にも重ならない。
    private void DrawTimeCard()
    {
        if (_timeText.Length == 0) return;
        if (_timeT >= TimeLife) { _timeText = ""; return; }
        // 出るとき：罫が伸び、字が追って浮く。消えるとき：最後の 0.5 秒で一緒に沈む。
        float wipe = Mathf.Clamp((float)(_timeT / TimeWipe), 0, 1);
        float ease = 1 - (1 - wipe) * (1 - wipe);
        float outA = Mathf.Clamp((float)((TimeLife - _timeT) / 0.5), 0, 1);
        const float right = 1136f, y = 542f;
        const int size = 18;
        const float track = 1.6f;   // 字間。少し開けて「見出し」に寄せる
        // 右端から積むので、先に総幅を測って書き出しを決める。
        float w = 0;
        for (int i = 0; i < _timeText.Length; i++)
            w += UiKit.TextW(UiKit.Zen, _timeText.Substring(i, 1), size) + track;
        float cx = right - w;
        // 字幕であって発話ではない、という体裁を作る唯一の飾り（本文側の縦罫と対になる細い罫）。
        DrawRect(new Rect2(cx - 14f, y + 4, 2.5f, 17f * ease), new Color(UiKit.Text3, 0.8f * outA));
        for (int i = 0; i < _timeText.Length; i++)
        {
            string ch = _timeText.Substring(i, 1);
            // 一文字ずつ、罫の伸びを追いかけて浮き上がる（字送りの演出。送り音は鳴らさない）。
            float t = Mathf.Clamp((float)((_timeT - TimeWipe * 0.5 - i * 0.012) / 0.22), 0, 1);
            if (t > 0)
                UiKit.Text(this, UiKit.Zen, new Vector2(cx, y + (1 - t) * 5f), ch, size,
                    new Color(UiKit.Text3, 0.95f * t * outA));
            cx += UiKit.TextW(UiKit.Zen, ch, size) + track;
        }
    }

    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        // 黒板はフィルムが消えきってから引く＝フィルムの明けフェードを内側に完全に包む。
        if (IsInstanceValid(_backdrop)) _backdrop.Dismiss();
        if (IsInstanceValid(_world)) _world.ProcessMode = _worldMode;
        if (IsInstanceValid(_game)) _game.ProcessMode = _gameMode;
        if (IsInstanceValid(_hud))
        {
            _hud.HoldBubble = false;
            _hud.HideBubble();
            _hud.SetCinematicMode(false);
            _hud.SuppressCallouts = _suppressed;
        }

        // 回想明けの曲の担当（2026-09-14②）。**memory と aftermath で違う**ので、ここでは何もしない:
        //   ・memory（戦闘へ戻る）＝呼び出し側の completed: が各ボス曲を張り直している
        //     （BossRei/BossAkari/BossKoharu/BossMina の4箇所）。ここで触ると同フレーム帯で Music() が
        //     二重に走り、以前直した「ボス曲が消える」事故（_musicFadeTween の Kill 漏れ）と同型の
        //     競合を招く。**触らない**のが正しい。
        //   ・aftermath（撃破後のクリア会話へ戻る）＝**回想曲をそのまま鳴らし続ける**。
        //     クリア会話はミナが独白する感情の後日談（例: StageRei.Clear の6行）で、
        //     回想 aftermath と**同じ「癒えたあと」の一続きの場面**なので、ここで切ると
        //     会話が無音に落ちる（＝ユーザー指摘の無音がクリア会話に残る）。
        //     曲は次のシーン（Hub / Final）の _Ready が張る Music() が自然に上書きする。
    }

    public override void _ExitTree() => Restore();
}
