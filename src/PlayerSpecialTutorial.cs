using Godot;

public partial class PlayerSpecialTutorial : Node
{
    private GameManager _game = null!;
    private Hud _hud = null!;
    private Node? _stage;
    private Job _character;
    private ChoiceOverlay? _choice;
    private ControlCard? _card;
    private string[] _lines = System.Array.Empty<string>();
    private int _line;
    private bool _advanceHeld;
    private double _lineHold;

    internal static string SeenKey(Job character) => $"once_skill_special_{Jobs.Get(character).CharacterId}";

    public static void Start(Node parent, Hud hud, Node? stage = null)
    {
        var game = GameManager.Instance!;
        if (!game.HasSpecial || game.IsIdleDialogSeen(SeenKey(game.SelectedJob)))
        {
            if (stage != null) parent.AddChild(stage);
            return;
        }
        parent.AddChild(new PlayerSpecialTutorial
        {
            Name = "PlayerSpecialTutorial", _game = game, _hud = hud,
            _stage = stage, _character = game.SelectedJob,
        });
    }

    public override void _Ready()
    {
        _hud.HoldBubble = true;
        _hud.ShowMessage($"{Jobs.Get(_character).CharacterName}の必殺技\n「{PlayerSpecial.NameFor(_character)}」の説明を聞きますか？");
        _hud.RevealDialogNow();
        _choice = ChoiceOverlay.Show(_hud, new[] { "はい", "いいえ" }, defaultSel: 0, onBoard: true);
        Pad.ConsumeUi(_choice);
    }

    public override void _Process(double delta)
    {
        object inputOwner = (object?)_choice ?? this;
        if (Pad.UiBlocked(inputOwner)) { _advanceHeld = true; return; }
        Pad.ConsumeUi(inputOwner);
        bool advance = Pad.AdvanceHeld();
        bool edge = advance && !_advanceHeld;
        _advanceHeld = advance;
        if (_choice != null)
        {
            if (!_choice.Decided) return;
            bool explain = _choice.Selected == 0;
            _choice.QueueFree();
            _choice = null;
            if (!explain) { Finish(); return; }
            _lines = LinesFor(_character);
            _card = ControlCard.Attach(_hud);
            _card.Show(ControlCard.Topic.Special);
            ShowLine();
            return;
        }
        _lineHold += delta;
        if (edge && !_hud.DialogRevealed)
        {
            _hud.RevealDialogNow();
            _lineHold = 0;
        }
        else if (_hud.DialogRevealed && (edge || (_hud.FastForwarding && _lineHold >= .15)
                     || (_hud.AutoAdvanceReady && _lineHold >= 1.4)))
        {
            if (++_line == _lines.Length) Finish();
            else ShowLine();
        }
    }

    internal static string[] LinesFor(Job character)
    {
        string detail = character switch
        {
            Job.Melee => "移動方向を入れて発動すると、その方向へ駆け抜ける。マウスならカーソルへ。方向を入れなければ、光を撃つ方向へ進むよ。",
            Job.Heal => "輪は置いた場所に残る。外へ出てもいいけれど、4秒後の回復を受けるには中へ戻ろう。LIFEの上限を超えては回復しないよ。",
            Job.Magic => "ロックした相手から始まり、ロックしていなければ近くの相手を選ぶ。相手を一度ずつ結び、最後に星座で追撃する。次の相手が遠すぎると、そこまでで星座を解放するよ。",
            _ => "受け止める向きは、発動したときに光を撃つ方向で決まる。弾を多く受け止めるほど、撃ち返す光が強く、太くなるよ。",
        };
        string caution = character switch
        {
            Job.Melee => "突進の通り道にいる敵とアンフォールダーを斬る。抜けた先が安全か、発動前に見ておこう。",
            Job.Heal => "輪が消すのは敵の弾。敵本体との接触などには気をつけて。輪の中でも、無敵にはならないよ。",
            Job.Magic => "星座が光る瞬間は、星と線に触れた敵の弾も消える。連鎖中は通常どおり動けるので、次の敵を見ながら避け続けよう。",
            _ => "受け止められるのは正面の弾。後ろや横からの攻撃、敵本体との接触には気をつけて。無敵になる技ではないよ。",
        };
        return new[]
        {
            $"{Jobs.Get(character).CharacterName}の必殺技は「{PlayerSpecial.NameFor(character)}」。{PlayerSpecial.DescriptionFor(character)}",
            detail,
            caution,
            $"技が終わると、再使用まで{PlayerSpecial.Cooldown:0}秒。左の必殺技ゲージが満ちたら、もう一度使える。操作はポーズメニューの「あそびかた」でも確認できるよ。",
        };
    }

    private void ShowLine()
    {
        _lineHold = 0;
        _hud.ShowDialog(Hud.LineKind.Boy, _lines[_line]);
    }

    private void Finish()
    {
        _game.MarkIdleDialogSeen(SeenKey(_character));
        _game.AutoSave();
        _hud.HoldBubble = false;
        _hud.HideBubble();
        _card?.Dismiss();
        Pad.ConsumeWheelTurn();
        Pad.ReleaseMouseFollow();
        Pad.ConsumeUi(this);
        if (_stage != null)
        {
            var stage = _stage;
            _stage = null;
            GetParent().AddChild(stage);
        }
        QueueFree();
    }

    // The stage has not entered the tree while the player is reading or choosing.
    public override void _ExitTree() => _stage?.Free();
}
