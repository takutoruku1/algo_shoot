using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CameoIntroScene : Node2D
{
    private static (Hud.LineKind who, string text, string face) ApproachLine(Job job, string id, bool boss)
    {
        string signal = (boss, id) switch
        {
            (false, "akari") => "……雨の向こうに、スマホの光。",
            (false, "koharu") => "……奥から、急ぐような足音。",
            (false, "rei") => "……画面の外に、人影。",
            (true, "akari") => "……雨音より近くに、あかりの声。",
            (true, "koharu") => "……光の集まる場所に、こはるの声。",
            (true, "rei") => "……画面の奥から、レイの声。",
            (true, "mina") => "……ミナの声が、すぐそこに。",
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        if (job == Job.Tank && id == "mina")
            return (Hud.LineKind.Boy, "ミナの声だ。……今度は、こちらから会いに行こう。", "");
        string reply = job switch
        {
            Job.Tank => "ご主人様。……ここからは、ゆっくり。",
            Job.Melee => "……一緒に、近づこう。",
            Job.Heal => "……待って。ひと呼吸だけ。",
            _ => "……いたわね。目を離さないで。",
        };
        return (job == Job.Tank ? Hud.LineKind.Mina : Hud.LineKind.Companion,
            signal + "\n" + reply, job == Job.Tank ? "res://char/v3/mina_conversation_v1.png" : CompanionDialogue.Portrait(job));
    }

    private static string Post(string id) => id switch
    {
        "akari" => "今日も終電。待ってる間だけ、誰か話そ。\n……返信は、いつでもいいけど。",
        "koharu" => "今日も最初から最後までいた！\n明日も絶対、全部見る。#星逢レイ",
        _ => "配信おつ！ 今日もいつもどおり笑えた。\n次も笑って会おうね。",
    };

    private static (int who, string text, string face)[] Dialogue(Job job, string id,
        (int who, string text, string face)[] opening)
    {
        if (job == Job.Tank) return opening;
        string reply = (job, id) switch
        {
            (Job.Tank, "akari") => "はい。ただいま、お返事に参りました。",
            (Job.Melee, "akari") => "読んだよ。……あたし、せっかちすぎ！",
            (Job.Heal, "akari") => "よ、読んだ！　いま返すとこ！",
            (Job.Magic, "akari") => "通知より、目の前を見なさいよ。",
            (Job.Tank, "koharu") => "では、ひと息だけでも。",
            (Job.Melee, "koharu") => "その予定、休憩も入れといて！",
            (Job.Heal, "koharu") => "待って、あたしまで置いてかないで！",
            (Job.Magic, "koharu") => "本人の話も聞きなさいって！",
            (Job.Tank, "rei") => "本日は、こちらの席で拝見します。",
            (Job.Melee, "rei") => "その距離なら、声で返せるでしょ。",
            (Job.Heal, "rei") => "あのっ、今日はコメントじゃなくて！",
            (Job.Magic, "rei") => "自分相手にまで、営業しないの。",
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        var (face, challenge) = id switch
        {
            "akari" => ("res://char/v3/akari_mid_conversation_v1.png",
                "じゃあ、既読のまま逃げないでよね！"),
            "koharu" => ("res://char/v3/koharu_mid_conversation_v1.png",
                "だーめ！　次、もう始まっちゃう！"),
            _ => ("res://char/v3/rei_face.png",
                "はい、前置きおしまい。目、離さないで！"),
        };
        int actor = (int)(job == Job.Tank ? Hud.LineKind.Mina : Hud.LineKind.Companion);
        string portrait = job == Job.Tank ? "res://char/v3/mina_conversation_v1.png" : CompanionDialogue.Portrait(job);
        var first = opening[0];
        first.face = face;
        return new[] { first, (actor, reply, portrait), (2, challenge, face) };
    }

    private static string BossPortrait(string id) => id switch
    {
        "akari" => "res://char/v3/akari_mid_conversation_v1.png",
        "koharu" => "res://char/v3/koharu_mid_conversation_v1.png",
        "rei" => CompanionDialogue.ReiAvatarPortrait,
        "mina" => "res://char/mina_worried.png",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static (int who, string text, string face)[] BossDialogue(Job job, string id,
        (int who, string text, string face)[] opening)
    {
        if (job == Job.Tank && id != "mina") return opening;
        opening = opening.Where(line => line.who != (int)Hud.LineKind.Mina).ToArray();
        var (reply, resolve) = (job, id) switch
        {
            (Job.Tank, "akari") => ("見ております。ですが、その方の返事は、わたくしには。", "……消した言葉のほうも、聞かせてください。"),
            (Job.Melee, "akari") => ("その返事を、誰からほしかったか。覚えてる。", "あたしも聞けなかったから。今度は、逃げない。"),
            (Job.Heal, "akari") => ("待ってる人の代わりには、なれないけど……。", "言えなかったほうも、聞きたいんだ。"),
            (Job.Magic, "akari") => ("欲しい返事だけじゃ、埋まらないでしょ。", "あんたが消した言葉を、聞きに来たの。"),
            (Job.Tank, "koharu") => ("席を取るために、ここへ来たのではありません。", "今は、あなたのお返事を。"),
            (Job.Melee, "koharu") => ("……その席、休んだだけでなくなるもの？", "何も。まずは、ひと息ついて話そ。"),
            (Job.Heal, "koharu") => ("あたしも、そう思って離せなかった。", "ちゃんとしてなくても、ここにいてほしい。"),
            (Job.Magic, "koharu") => ("今日は、わたしが会いに来たの。", "何もしなくていい。あんたの話を聞かせて。"),
            (Job.Tank, "rei") => ("……その笑顔の奥にも、声があるのですね。", "では、聞こえた声は、どこへ。"),
            (Job.Melee, "rei") => ("初めてじゃないよ。さっきの声も、聞いてた。", "なら、笑ってないときの声も聞かせて。"),
            (Job.Heal, "rei") => ("今日は、コメント欄じゃなくて、ここから。", "うまく笑えない日も、聞きに来るよ。"),
            (Job.Magic, "rei") => ("その挨拶、わたし相手にも続けるの？", "わたしが消した声なら、わたしが聞く。"),
            (Job.Tank, "mina") => ("届くところまで、近づくよ。", "急がなくていい。ここで聞いている。"),
            (Job.Melee, "mina") => ("ミナ。今度は、あたしがそっちへ行く。", "それでも、ここにいる。ひとりにしない。"),
            (Job.Heal, "mina") => ("怖いよね。……あたしも。でも、来たよ。", "あたし、まだ聞いてる。切らないから。"),
            (Job.Magic, "mina") => ("回線、切らないわよ。顔、上げて。", "傷つかない約束より、あんたの声がほしい。"),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        string challenge = id switch
        {
            "akari" => "じゃあ、どうしてここまで来たの。",
            "koharu" => "……じゃあ、あたしに何をしてほしいの？",
            "rei" => "奥なんてないよ。見えてるわたしが、全部。",
            _ => "……返事をしたら、また、傷つけてしまう。",
        };
        string face = BossPortrait(id);
        int actor = (int)(job != Job.Tank ? Hud.LineKind.Companion
            : id == "mina" ? Hud.LineKind.Boy : Hud.LineKind.Mina);
        string portrait = actor == (int)Hud.LineKind.Boy ? ""
            : job == Job.Tank ? "res://char/v3/mina_conversation_v1.png" : CompanionDialogue.Portrait(job);
        var lines = new List<(int who, string text, string face)>(opening.Select(line =>
            (line.who, line.text, line.who == (int)Hud.LineKind.Other ? face : line.face)));
        if (!opening.Any(line => line.who == (int)Hud.LineKind.Other))
            lines.Add((2, id == "mina" ? "……来ないで。今のわたくしは、あなたにも……。"
                : "全部見てないと、あたしの席、なくなっちゃう。", face));
        lines.Add((actor, reply, portrait));
        lines.Add((2, challenge, face));
        lines.Add((actor, resolve, portrait));
        return lines.ToArray();
    }

    private Hud _hud = null!;
    private Node _world = null!;
    private GameManager _game = null!;
    private string _id = "", _name = "";
    private Action _completed = null!;
    private Action _arriving = null!;
    private (int who, string text, string face)[] _opening = null!, _lines = null!;
    private Texture2D _background = null!, _enemyPortrait = null!;
    private Texture2D? _playerPortrait;
    private PostToast? _post;
    private ProcessModeEnum _worldMode, _gameMode;
    private CutsceneBackdrop _backdrop = null!;
    private readonly FilmSkip _skip = new();
    private string _filmId = "";
    private bool _held, _started, _postClosing, _talking, _leaving, _restored, _suppressed, _bossIntro;
    private bool _arrived;
    private const double ApproachHold = 2.2, ApproachFade = 0.45;
    private double _approachTime;
    private int _line;
    private double _time, _postTime, _lineTime, _talkTime, _exitTime;
    private string EnemyName => _id != "mina" && Jobs.Get(_game.SelectedJob).CharacterId == _id ? $"投稿の中の{_name}" : _name;

    public static void Play(Hud hud, Node world, string id,
        (int who, string text, string face)[] opening, Action completed)
        => hud.AddChild(new CameoIntroScene {
            Name = "CameoIntroScene", ZIndex = -10, TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            _hud = hud, _world = world,
            _id = id, _opening = opening, _completed = completed,
            _arriving = () => (hud.GetTree().GetFirstNodeInGroup("stagebg") as StageBackground)?.BeginMidboss(),
        });

    public static void PlayBoss(Hud hud, Node world, string id,
        (int who, string text, string face)[] opening, Action completed, Action arriving)
        => hud.AddChild(new CameoIntroScene {
            Name = "BossIntroScene", ZIndex = -10, TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            _hud = hud, _world = world, _bossIntro = true,
            _id = id, _opening = opening, _completed = completed, _arriving = arriving,
        });

    public override void _Ready()
    {
        string group = _bossIntro ? "boss_intro" : "cameo_intro";
        AddToGroup(group);
        _game = GetNode<GameManager>("/root/Game");
        _name = BossPostStory.Get(_id).Name;
        _lines = _bossIntro ? BossDialogue(_game.SelectedJob, _id, _opening) : Dialogue(_game.SelectedJob, _id, _opening);
        _background = GD.Load<Texture2D>(_bossIntro ? BossPostStory.Get(_id).FakeBackground : $"res://char/bg2/midboss/{_id}_v1.png");
        _enemyPortrait = GD.Load<Texture2D>(_bossIntro ? BossPortrait(_id) : _lines[0].face);
        if (_id != "mina" || _game.SelectedJob != Job.Tank)
            _playerPortrait = GD.Load<Texture2D>(_game.SelectedJob switch {
                Job.Tank => "res://char/v3/mina_conversation_v1.png",
                Job.Magic => "res://char/v3/rei_gawa.png",
                _ => CompanionDialogue.Portrait(_game.SelectedJob),
            });
        _worldMode = _world.ProcessMode;
        _world.ProcessMode = ProcessModeEnum.Disabled;
        _gameMode = _game.ProcessMode;
        _game.ProcessMode = ProcessModeEnum.Disabled;
        _suppressed = _hud.SuppressCallouts;
        // Cinematic mode hides the opening spell; suppression would erase it before combat resumes.
        _hud.SuppressCallouts = !_bossIntro;
        _hud.HoldBubble = true;
        _hud.HideBubble();
        if (!_bossIntro) _hud.HideSpellCard();
        _hud.SetCinematicMode(false);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        _held = Pad.AdvanceHeld();
        var cue = ApproachLine(_game.SelectedJob, _id, _bossIntro);
        _hud.ShowDialog(cue.who, cue.text, cue.face);
        _hud.RevealDialogNow();
        _filmId = $"{group}_{_id}_{Jobs.Get(_game.SelectedJob).CharacterId}";
        _skip.Begin(_lines.All(line => _game.IsLineRead(line.text)) ? _game : null, _filmId);
    }

    public override void _Process(double delta)
    {
        if (Pad.UiBlocked(this)) { _held = true; return; }
        bool held = Pad.AdvanceHeld();
        bool edge = held && !_held;
        _held = held;
        QueueRedraw();
        if (!_leaving && _skip.Update(delta))
        {
            if (!_arrived) Arrive();
            Leave();
        }
        if (_leaving)
        {
            _exitTime += delta;
            // Keep dialogue/skip presses out of the first combat frame.
            if (_exitTime < 0.45 || held || Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B)) return;
            Restore();
            _completed();
            QueueFree();
            return;
        }
        if (!_arrived)
        {
            _approachTime += delta;
            if (_approachTime >= ApproachHold) _hud.HideBubble();
            if (_approachTime >= ApproachHold + ApproachFade) Arrive();
            return;
        }
        _time += delta;
        if (!_started)
        {
            if (_time < 0.45) return;
            _started = true;
            var story = BossPostStory.Get(_id);
            string post = _bossIntro ? story.Posts[0].Replace("\n", "") : Post(_id);
            _post = PostToast.Show(this, _id == "rei" ? "星逢レイ" : _name, story.Handle, "· 少し前", post,
                likes: _id == "rei" ? 12 : 2, views: _id == "rei" ? 203 : 47,
                dwell: -1, portrait: GD.Load<Texture2D>(CompanionDialogue.AccountIcon(_id)));
            return;
        }
        if (!_talking)
        {
            _postTime += delta;
            if (!_postClosing && (_postTime >= 2.2 || (edge && _postTime >= 0.6)))
            {
                _postClosing = true;
                _post!.Dismiss();
            }
            if (!_post!.Gone) return;
            _post.QueueFree();
            _post = null;
            _talking = true;
            ShowLine();
            return;
        }
        _talkTime += delta;
        _lineTime += delta;
        double hold = _bossIntro ? Math.Clamp(1.15 + _lines[_line].text.Length * 0.055, 2.0, 4.8)
            : Math.Clamp(1.15 + _lines[_line].text.Length * 0.035, 1.6, 2.2);
        if (_lineTime >= hold || (_lineTime >= 0.4 && (edge || _hud.FastForwarding)))
        {
            _line++;
            if (_line == _lines.Length) Leave();
            else ShowLine();
        }
    }

    private void Arrive()
    {
        _arrived = true;
        _hud.HideBubble();
        _hud.SetCinematicMode(true);
        _backdrop = CutsceneBackdrop.Attach(_hud, ZIndex);
        _arriving();
    }

    private void ShowLine()
    {
        _lineTime = 0;
        var (who, text, face) = _lines[_line];
        if (who == (int)Hud.LineKind.Other) _enemyPortrait = GD.Load<Texture2D>(face);
        _hud.SetCinematicMode(true, accent: who == (int)Hud.LineKind.Other
            ? BossPostStory.Get(_id).Accent : CompanionDialogue.Accent(_game.SelectedJob));
        _hud.ShowDialog((Hud.LineKind)who, text, face, otherName: EnemyName);
        _hud.RevealDialogNow();
    }

    private void Leave()
    {
        _leaving = true;
        _hud.HideBubble();
        _post?.Dismiss();
        FilmSkip.MarkSeen(_game, _filmId);
    }

    public override void _Draw()
    {
        if (!_arrived)
        {
            float fade = Mathf.SmoothStep(0, 1, Mathf.Clamp((float)((_approachTime - ApproachHold) / ApproachFade), 0, 1));
            DrawRect(new Rect2(0, 0, 384, 216), new Color(0, 0, 0, fade));
            UiKit.BeginDesign(this);
            _skip.Draw(this);
            UiKit.EndDesign(this);
            return;
        }
        float alpha = _leaving ? 1 - Mathf.Clamp((float)(_exitTime / 0.45), 0, 1) : Mathf.Clamp((float)(_time / 0.45), 0, 1);
        float scale = Mathf.Max(384f / _background.GetWidth(), 216f / _background.GetHeight());
        var size = _background.GetSize() * scale;
        DrawTextureRect(_background, new Rect2(new Vector2(192, 108) - size * 0.5f, size), false, new Color(1, 1, 1, alpha));
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 720), new Color(0.02f, 0.025f, 0.035f, alpha * 0.26f));
        DrawRect(new Rect2(0, 0, 1280, 64), new Color(0.02f, 0.025f, 0.035f, alpha));
        DrawRect(new Rect2(0, 516, 1280, 204), new Color(0.02f, 0.025f, 0.035f, alpha));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(48, 21), _talking
            ? (_bossIntro ? "届かなかった声" : "投稿の向こう側") : "届いた投稿", 19, new Color(UiKit.Text2, alpha));
        if (_talking)
        {
            float enter = Mathf.SmoothStep(0, 1, Mathf.Clamp((float)(_talkTime / 0.35), 0, 1));
            float a = alpha * enter;
            bool playerSpeaking = !_leaving && _lines[_line].who != 2;
            if (_playerPortrait != null) DrawPortrait(_playerPortrait, 360 - (1 - enter) * 12, playerSpeaking, a);
            DrawPortrait(_enemyPortrait, _playerPortrait == null ? 640 : 920 + (1 - enter) * 12, !playerSpeaking, a);
        }
        _skip.Draw(this);
        UiKit.EndDesign(this);
    }

    private void DrawPortrait(Texture2D portrait, float x, bool speaking, float alpha)
    {
        float brightness = speaking ? 1 : 0.72f;
        var size = portrait.GetSize() * Mathf.Min(420f / portrait.GetHeight(), 370f / portrait.GetWidth());
        DrawTextureRect(portrait, new Rect2(new Vector2(x - size.X * 0.5f, 516 - size.Y), size), false,
            new Color(brightness, brightness, brightness, alpha));
    }

    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        if (IsInstanceValid(_world)) _world.ProcessMode = _worldMode;
        if (IsInstanceValid(_game)) _game.ProcessMode = _gameMode;
        if (IsInstanceValid(_hud))
        {
            _hud.HoldBubble = false;
            _hud.HideBubble();
            _hud.SetCinematicMode(false);
            _hud.SuppressCallouts = _suppressed;
        }
        if (IsInstanceValid(_backdrop)) _backdrop.Dismiss();
    }

    public override void _ExitTree() => Restore();
}
