using Godot;
using System;

public partial class BossTransformation : Node2D
{
    public readonly record struct Frame(Texture2D Texture, Vector2 Position, Vector2 Scale, Vector2 Offset, bool Flip)
    {
        public static Frame Capture(Sprite2D body)
            => new(body.Texture, body.GlobalPosition, body.Scale, body.Offset, body.FlipH);
        public Sprite2D Create() => new()
        {
            Texture = Texture, Position = Position, Scale = Scale, Offset = Offset,
            FlipH = Flip, TextureFilter = TextureFilterEnum.Linear,
        };
    }

    public const double Duration = 3.0;
    public const float ImpactTime = 1.28f;
    private Enemy _boss = null!;
    private Hud _hud = null!;
    private Node _world = null!;
    private GameManager _game = null!;
    private ProcessModeEnum _worldMode, _gameMode;
    private Node? _player;          // 止めている間だけ物理処理を預かる自機（下の _Ready／Restore 参照）
    private bool _playerPhysics;
    private Frame _before, _after;
    private Sprite2D _old = null!, _new = null!;
    private Node2D _front = null!;
    private VideoStreamPlayer _movie = null!;
    private bool _morphing;
    private UnfolderKind _style;
    private Action? _completed;
    private double _time;
    private bool _restored, _visible, _suppressed, _revealed, _charged;
    private CharacterStoryTalk? _dialogue;
    public static string VideoPath(UnfolderKind style)
        => $"res://assets/video/{style.ToString().ToLowerInvariant()}_transform_v3.ogv";

    private static string[] LeadIn(UnfolderKind style) => style switch
    {
        UnfolderKind.Akari => new[]
        {
            "……消したはずなのに。\n言えなかった言葉が、まだこんなに。",
            "笑って見送りたかったのに……。\n行かないで、追いかけたいって、あふれてくる。",
            "封筒が、身体に……。\nこの手が届かないなら、追いつける姿に……！",
        },
        UnfolderKind.Koharu => new[]
        {
            "腕、もう上がらないのに……。\nみんなは続けてるから、まだ振らなきゃ。",
            "一度でも止まったら、好きじゃないって思われる。\nここに、いられなくなる。",
            "ペンライトが、身体に……。\nこの光も脚も、追いつくまで止まらないで！",
        },
        UnfolderKind.Rei => new[]
        {
            "……笑って話してるのに、まだ人が減る。\nわたしが、つまらないから……？",
            "見てもらうために、何度も作り直した顔なのに。\nこれでも、足りないの……？",
            "飾りが、羽みたいに……。\n画面いっぱいに広がれば、目を離せないでしょ？",
        },
        UnfolderKind.Mina => new[]
        {
            "……預かった苦しさが、あふれてきます。\nもう、この身体には収まりません。",
            "誰かに渡せば、その方まで苦しませてしまう。\nわたくしが、抱えていなければ。",
            "……もっと、大きく。\n皆さまを守れる身体に、なれば……！",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    private static string FirstWords(UnfolderKind style) => style switch
    {
        UnfolderKind.Akari => "……尾の先まで、消せなかった言葉が。\n待って、今度こそ追いつくから……！",
        UnfolderKind.Koharu => "……手を振らなくても、角が光ってる。\nこの脚なら、まだついていけるよね？",
        UnfolderKind.Rei => "ほら、羽を広げれば、画面いっぱい。\n……もう、見えないなんて言わせない。",
        UnfolderKind.Mina => "……この翼の内側なら、全部守れます。\nどうか、こちらへ来ないで。",
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    private static string Reply(UnfolderKind style, Job job) => (style, job) switch
    {
        (UnfolderKind.Akari, Job.Tank) => "封筒が、追いかける狐の尾に……。\nあかりさん、その一通を聞かせてください。",
        (UnfolderKind.Akari, Job.Melee) => "狐になってまで、追いかけたかったんだね。\nあたしの中のその気持ち、今度は聞くよ。",
        (UnfolderKind.Akari, Job.Heal) => "狐の尾、ぜんぶ封筒なんだ……。\n追いついて伝えたい言葉、あたしも聞きたい。",
        (UnfolderKind.Akari, Job.Magic) => "封筒を尾にして、追いかける狐にまで……。\n追いついたら、何て伝えるつもり？",
        (UnfolderKind.Koharu, Job.Tank) => "その脚も、光る角も、追いつくために……。\nそれほど、休むのが怖かったのですね。",
        (UnfolderKind.Koharu, Job.Melee) => "鹿の脚なら、休まなくて済むって……？\nそんなに、置いていかれるのが怖かったんだ。",
        (UnfolderKind.Koharu, Job.Heal) => "鹿になれば、ずっと走れるって思ったんだ。\nあたし、そんなに止まるのが怖かったんだね。",
        (UnfolderKind.Koharu, Job.Magic) => "その角、ペンライトの光なのね。\nわたしについてくるために、そこまで……。",
        (UnfolderKind.Rei, Job.Tank) => "見てほしいという願いが、その翼に……。\nレイさん、飾りの奥の声も聞こえています。",
        (UnfolderKind.Rei, Job.Melee) => "見てもらいたくて、鳥の姿にまで……。\n羽の陰に隠れなくても、ここから見てるよ。",
        (UnfolderKind.Rei, Job.Heal) => "その翼、みんなに見つけてもらうためなんだね。\nあたし、小さい声だって聞いてるよ。",
        (UnfolderKind.Rei, Job.Magic) => "鳥の姿にまで作り替えて、目を引くのね。\n誰もいなくなるのが怖いのは、知ってるわ。",
        (UnfolderKind.Mina, Job.Tank) => "龍になってまで、ひとりで抱えるのか。\nミナ、僕の声まで締め出さないで。",
        (UnfolderKind.Mina, Job.Melee) => "龍になったら、もっと抱えられるって……？\nその翼で、あたしたちまで遠ざけないで。",
        (UnfolderKind.Mina, Job.Heal) => "大きな龍になっても、抱えるのはひとりなの？\n翼の外に追い出さないで、一緒に持ちたいよ。",
        (UnfolderKind.Mina, Job.Magic) => "全部抱えるために、龍の姿を選んだのね。\nその翼で、わたしたちまで遠ざけないで。",
        _ => throw new ArgumentOutOfRangeException(nameof(job)),
    };

    public static BossTransformation Play(Enemy boss, Frame before, Action? completed = null)
    {
        var hud = (Hud)boss.GetTree().GetFirstNodeInGroup("hud");
        var effect = new BossTransformation
        {
            Name = "BossTransformation", ZIndex = -10, ProcessMode = ProcessModeEnum.Pausable,
            _boss = boss, _hud = hud, _world = boss.GetParent(), _before = before,
            _after = Frame.Capture(boss.GetNode<Sprite2D>("Body")), _completed = completed,
        };
        hud.AddChild(effect);
        return effect;
    }

    // ── ルナティック用：戦闘を止めない形態変化の合図 ──
    //   ルナティックは「物語ギミックを全部切った、止まらない純粋シューティング」（GameManager.IsLunatic の
    //   コメント）で、回想・改心会話・下書き選択・HoldBubble・ボス投稿・段間カットシーンを畳んである。
    //   World を3秒止めるこの演出だけがその規則の外に居たので、同じ作法で畳む：止めるのは「演出のために
    //   戦闘を凍らせること」だけで、形態変化そのものは起こす。
    //   止めずに「姿が変わった」を読ませる手は、全部すでに盤面にある語彙でそろえる：
    //     ・ガワの差し替えと squash→pop … 呼び出し側（Enemy.AdvanceForm2／BossMina.BeginPhaseTransition）が
    //       SwapBody で起動済み。World が止まらないぶん、_PhysicsProcess が自前の尺でクロスフェードを流す
    //       ＝体が一度弾んで新しい姿になる（本編では止めている間に進まないので FinishFormReveal で畳んでいた）。
    //     ・Hud.Flash() … HPバーが1本割れた瞬間（Enemy.OnBarBroken）と同じ白フラッシュ＝「節目」の同語彙。
    //     ・画面揺れと着地音 … 本編の ImpactTime で鳴るものをそのまま、尺ゼロで一度だけ。
    public static void FlashForm(Enemy boss)
    {
        var style = boss.UnfolderStyle;
        (boss.GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();
        GameCamera.Instance?.Shake(style == UnfolderKind.Mina ? 2.8f : 1.7f, .18f);
        ImpactSe(style);
        boss.GetNodeOrNull<BossPostSequence>("PostSequence")?.ResumeMusic(0.2f);
    }

    public override void _Ready()
    {
        AddToGroup("boss_transform");
        _game = GetNode<GameManager>("/root/Game");
        _worldMode = _world.ProcessMode;
        _gameMode = _game.ProcessMode;
        // ── World を止めるのは「物理コールバックの外」で ──
        //   変身の起点は敵本体の当たり判定コールバックの中にある（Area2D.area_entered →
        //   Enemy.OnBodyHitByPlayerBullet → AdvanceForm2 → RevealForm → この _Ready。ミナの龍だけは
        //   _Process 側）。その最中に World の ProcessMode を落とすと、配下の Area2D（自機・敵・パネル）が
        //   コールバック中に物理空間から外れることになり、Godot は
        //   "Disabling a CollisionObject node during a physics callback" を出して**落としを破棄する**
        //   （collision_object_2d.cpp:244 の _apply_disabled）＝通知の出元だったボス本体の判定だけが
        //   生き残る、まさに undesired behavior。遅延させれば適用が物理ステップ末尾の
        //   MessageQueue flush ＝コールバックの外になり、全員が同じ瞬間に外れる。
        // ★遅延しても「止めたはずの1フレーム」は開かない。World 配下が今フレームの _PhysicsProcess を
        //   もらってしまう穴を、二重に塞いでいる：
        //   ・下の SetCinematicMode(true) は同期的に Hud.BubblePaused を立てる（UpdateDialoguePause）。
        //     World 配下は移動・発射の手前で必ずこれを見て return する（Enemy／Panel／Player の各ゲート）。
        //     ゲートより前に居るのは色味・差し替えアニメ・無防備窓の時計など「会話中も進めたい」と
        //     決めてある類だけ＝会話で止めたときと同じ振る舞いになる。
        //   ・BubblePaused を跨いで自機の位置を動かす経路（回避ダッシュと仕切り直しの帰還）はゲートの外なので、
        //     自機の物理処理そのものを同期的に預かる（下の数行）。当たり判定には触らないので上のエラーは出ない。
        //   ・被弾は構造的に起きない：接触通知はこの tick ぶんを配り終えた時点に居て、弾は下の DespawnAll で
        //     消え、次の tick には World が止まっている。
        //   遅延先は「World の property」ではなく自分のメソッドにする。property へ直に SetDeferred すると、
        //   流れる前に演出が畳まれた場合（Restore→ProcessMode を戻す→その後に遅延の Disabled が着弾）に
        //   World が止まったまま取り残される。自分のメソッドなら解放済みの遅延呼び出しは捨てられ、
        //   既に Restore 済みなら下の _restored で降りられる。
        CallDeferred(MethodName.SuspendWorld);
        _game.ProcessMode = ProcessModeEnum.Disabled;
        _player = GetTree().GetFirstNodeInGroup("player");
        if (_player != null)
        {
            _playerPhysics = _player.IsPhysicsProcessing();
            _player.SetPhysicsProcess(false);
        }
        _visible = _boss.Visible;
        _boss.Visible = false;
        _suppressed = _hud.SuppressCallouts;
        _hud.SuppressCallouts = true;
        _hud.HideSpellCard();
        _hud.SetCinematicMode(true, dialogueBubble: true);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        _style = _boss.UnfolderStyle;
        _old = _before.Create();
        _new = _after.Create();
        _new.Visible = false;
        AddChild(_old);
        AddChild(_new);
        _movie = new VideoStreamPlayer
        {
            Name = "TransformationMovie", Stream = GD.Load<VideoStream>(VideoPath(_style)),
            Visible = false, Volume = 0,
        };
        AddChild(_movie);
        _movie.Finished += FinishMovie;
        _front = new Node2D { ZIndex = 100, Visible = false };
        _front.Draw += DrawMovie;
        AddChild(_front);
        BeginDialogue(after: false);
    }

    private void BeginDialogue(bool after)
    {
        if (GameManager.BossRushActive)
        {
            if (after) Complete();
            else BeginMorph();
            return;
        }
        _front.Visible = false;
        _hud.SetCinematicMode(true, dialogueBubble: true);
        var (name, face) = _style switch
        {
            UnfolderKind.Akari => ("あかり", "res://char/v3/akari_face.png"),
            UnfolderKind.Koharu => ("こはる", "res://char/v3/koharu_face_pale.png"),
            UnfolderKind.Rei => ("レイ", CompanionDialogue.ReiAvatarPortrait),
            _ => ("ミナ", "res://char/v3/mina_conversation_worried_v1.png"),
        };
        int speaker = (int)(_style == UnfolderKind.Mina ? Hud.LineKind.Mina : Hud.LineKind.Other);
        var lines = Array.ConvertAll(LeadIn(_style), text => (speaker, text, face));
        if (after)
        {
            var responder = _game.SelectedJob == Job.Tank
                ? (_style == UnfolderKind.Mina ? Hud.LineKind.Boy : Hud.LineKind.Mina)
                : Hud.LineKind.Companion;
            lines = new[]
            {
                (speaker, FirstWords(_style), ""),
                ((int)responder, Reply(_style, _game.SelectedJob), responder == Hud.LineKind.Mina ? "res://char/v3/mina_conversation_v1.png" : ""),
            };
        }
        _dialogue = CharacterStoryTalk.Start(lines, () => _hud,
            (hud, who, text, portrait) => hud.ShowDialog((Hud.LineKind)who, text, portrait, name),
            after ? Complete : BeginMorph);
        QueueRedraw();
        _front.QueueRedraw();
    }

    private void BeginMorph()
    {
        _morphing = true;
        _movie.Play();
        _front.Visible = true;
        _hud.SetCinematicMode(true, dialogueBubble: true);
        Audio.Instance?.Se(Audio.Instance.SfxCalm, -15, _style == UnfolderKind.Mina ? .55f : .85f);
        QueueRedraw();
    }

    private void Complete()
    {
        Restore();
        QueueFree();
        _completed?.Invoke();
    }

    // _Ready から遅延で呼ばれる World の停止（理由は _Ready のコメント）。
    //   畳むのが先に来ていたら（_restored）何もしない＝止めっぱなしで取り残さない。
    private void SuspendWorld()
    {
        if (_restored) return;
        if (IsInstanceValid(_world)) _world.ProcessMode = ProcessModeEnum.Disabled;
    }

    public override void _Process(double delta)
    {
        _movie.Paused = Pad.UiBlocked(this);
        if (_movie.Paused) return;
        if (!IsInstanceValid(_boss) || _boss.IsQueuedForDeletion() || _boss.IsPurified)
        {
            Restore();
            QueueFree();
            return;
        }
        if (_dialogue is { Active: true })
        {
            _dialogue.Update(delta);
            return;
        }
        if (!_morphing) return;
        _time = _movie.StreamPosition;
        UpdateMovieCues();
        _front.QueueRedraw();
    }

    private void UpdateMovieCues()
    {
        if (_time >= .64 && !_charged)
        {
            _charged = true;
            PlayCharge();
        }
        if (_time >= ImpactTime && !_revealed)
        {
            _revealed = true;
            _old.Visible = false;
            _new.Visible = true;
            PlayImpact();
        }
    }

    private void FinishMovie()
    {
        if (!_morphing || _restored) return;
        _morphing = false;
        _time = Duration;
        UpdateMovieCues();
        _front.Visible = false;
        BeginDialogue(after: true);
    }

    public override void _Draw()
        => DrawRect(new Rect2(0, 0, Field.Right, Field.Bottom), new Color(.015f, .018f, .03f, .4f));

    private void DrawMovie()
    {
        float fade = (float)Math.Clamp(Math.Min(_time / .12, (Duration - _time) / .24), 0, 1);
        _front.DrawTextureRect(_movie.GetVideoTexture(), new Rect2(0, 0, Field.WorldRight, Field.Bottom),
            false, new Color(1, 1, 1, fade));
    }

    private void PlayCharge()
    {
        if (Audio.Instance is not { } audio) return;
        switch (_style)
        {
            case UnfolderKind.Akari: audio.Se(audio.SfxStrip, -18, .75f); break;
            case UnfolderKind.Koharu: audio.Se(audio.SfxPurify, -18, .75f); break;
            case UnfolderKind.Rei: audio.Se(audio.SfxUiConfirm, -16, .65f); break;
            case UnfolderKind.Mina: audio.Se(audio.SfxMemoryFlash, -17, .55f); break;
        }
    }

    private void PlayImpact()
    {
        ImpactSe(_style);
        _boss.GetNodeOrNull<BossPostSequence>("PostSequence")?.ResumeMusic(0.2f);
    }

    // 変身の着地音。ルナティックは演出を出さないので、ここだけを FlashForm から単体で鳴らす。
    private static void ImpactSe(UnfolderKind style)
    {
        if (Audio.Instance is not { } audio) return;
        switch (style)
        {
            case UnfolderKind.Akari:
                audio.Se(audio.SfxStrip, -11, .7f); audio.Se(audio.SfxSpell, -17, .9f); break;
            case UnfolderKind.Koharu:
                audio.Se(audio.SfxPurify, -12, 1.15f); audio.Se(audio.SfxSpell, -19, .75f); break;
            case UnfolderKind.Rei:
                audio.Se(audio.SfxStrip, -15, 1.3f); audio.Se(audio.SfxSpell, -12, 1.2f); break;
            case UnfolderKind.Mina:
                audio.Se(audio.SfxBomb, -13, .65f); audio.Se(audio.SfxStrip, -16, .6f); break;
        }
    }

    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        _morphing = false;
        _movie.Stop();
        _movie.Stream = null;
        if (IsInstanceValid(_boss))
        {
            _boss.FinishFormReveal();
            _boss.Visible = _visible;
        }
        // 戻し側は遅延しない。Restore を呼ぶのは _Process（＝idle。物理コールバックの外）と _ExitTree
        //   （ノード解放は _flush_delete_queue＝物理コールバックの外）だけで、Godot が弾くのは
        //   「コールバック中の disable」のみ＝enable は同じ制約を持たない。遅延させると預かった自機の
        //   物理処理が戻る順番が1フレームぶれるので、ここは即時でそろえる。
        if (IsInstanceValid(_player)) _player!.SetPhysicsProcess(_playerPhysics);
        if (IsInstanceValid(_world)) _world.ProcessMode = _worldMode;
        if (IsInstanceValid(_game)) _game.ProcessMode = _gameMode;
        if (IsInstanceValid(_hud))
        {
            _hud.HoldBubble = false;
            _hud.HideBubble();
            _hud.SetCinematicMode(false);
            _hud.SuppressCallouts = _suppressed;
        }
    }

    public override void _ExitTree() => Restore();
}
