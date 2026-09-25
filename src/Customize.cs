using Godot;
using System;
using System.Collections.Generic;

public partial class Customize : Node2D
{
    private static readonly Color Bg = new("161b20"), Raised = new("242c33"), Ink = new("edf3f5"), Muted = new("a8b5bc");
    private static readonly Rect2 HomeRect = new(40, 25, 44, 44);
    private static readonly Rect2 ActionRect = new(658, 606, 574, 48);
    private static readonly Rect2 CancelRect = new(460, 417, 172, 46), ConfirmRect = new(648, 417, 172, 46);
    private GameManager _game = null!;
    private readonly Dictionary<string, Texture2D> _textures = new();
    private JobTuning[] _characters = Array.Empty<JobTuning>();
    private CosmeticItem[] _items = Array.Empty<CosmeticItem>();
    private int _tab, _character, _selected, _pose, _focus = 3, _hover = -1;
    private bool _acceptHeld, _backHeld, _navHeld, _leaving, _confirmYes;
    private string? _pendingPurchase;
    private string _notice = "";
    private double _time, _noticeTime;
    private CosmeticItem Selected => _items[_selected];
    private static Rect2 TabRect(int i) => new(48 + i * 216, 96, 216, 40);
    private static Rect2 CharacterRect(int i) => new(658 + i * 142, 151, 142, 40);
    private static Rect2 PoseRect(int i) => new(110 + i * 126, 614, 116, 34);
    private Rect2 ItemRect(int i) => new(658 + i * (582f / _items.Length), 326, 582f / _items.Length - 8, 196);

    public override void _Ready()
    {
        _game = GetNode<GameManager>("/root/Game");
        _characters = Array.FindAll(Jobs.All, j => _game.IsJobUnlocked(j.Id));
        _character = Math.Max(0, Array.FindIndex(_characters, j => j.Id == _game.SelectedJob));
        _acceptHeld = Pad.AdvanceHeld();
        TextureFilter = TextureFilterEnum.Linear;
        RefreshItems();
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmShop);
    }

    private Texture2D Texture(string path)
    {
        if (!_textures.TryGetValue(path, out var texture)) _textures[path] = texture = GD.Load<Texture2D>(path);
        return texture;
    }

    private void RefreshItems()
    {
        _items = Array.FindAll(Cosmetics.All, item => _tab == 0 ? item.Kind == CosmeticKind.Cursor
            : item.Kind == CosmeticKind.Costume && item.Character == _characters[_character].Id);
        _selected = Math.Max(0, Array.FindIndex(_items, item => _game.CosmeticEquipped(item.Id)));
        _noticeTime = 0;
    }

    private void RegisterHotspots()
    {
        UiKit.BeginHotspots(Pad.MousePos());
        if (_pendingPurchase != null)
        {
            UiKit.Hotspot(CancelRect, 500);
            UiKit.Hotspot(ConfirmRect, 501);
        }
        else
        {
            UiKit.Hotspot(HomeRect, 100);
            UiKit.Hotspot(ActionRect, 101);
            for (int i = 0; i < 2; i++) UiKit.Hotspot(TabRect(i), 200 + i);
            if (_tab == 1)
            {
                for (int i = 0; i < _characters.Length; i++) UiKit.Hotspot(CharacterRect(i), 300 + i);
                for (int i = 0; i < 3; i++) UiKit.Hotspot(PoseRect(i), 400 + i);
            }
            for (int i = 0; i < _items.Length; i++) UiKit.Hotspot(ItemRect(i), i);
        }
        _hover = UiKit.HoveredId();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _noticeTime = Math.Max(0, _noticeTime - delta);
        QueueRedraw();
        if (_leaving) return;
        if (Pad.UiBlocked(this)) { _acceptHeld = _backHeld = _navHeld = true; return; }
        RegisterHotspots();
        bool accept = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        // もどる＝X／Esc／パッドB（Esc は 2026-09-26 に「一つ前の画面へ」として復帰。メニューは M）。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        bool nav = left || right || up || down;
        bool acceptEdge = accept && !_acceptHeld, backEdge = back && !_backHeld, navEdge = nav && !_navHeld;
        _acceptHeld = accept; _backHeld = back; _navHeld = nav;
        if (_time < 0.25) return;
        if (backEdge)
        {
            if (_pendingPurchase != null) { _pendingPurchase = null; Audio.Instance?.PlayUiCancel(); }
            else Leave();
            return;
        }
        if (_pendingPurchase != null)
        {
            if (navEdge) _confirmYes = !_confirmYes;
            if (Pad.MouseClick() && _hover == 500) _pendingPurchase = null;
            else if ((Pad.MouseClick() && _hover == 501) || (acceptEdge && _confirmYes)) BuyConfirmed();
            else if (acceptEdge) _pendingPurchase = null;
            return;
        }
        if (Pad.MouseClick())
        {
            if (_hover == 100) { Leave(); return; }
            if (_hover == 101) ActivateSelected();
            else if (_hover >= 200 && _hover < 202) { _tab = _hover - 200; _focus = 0; RefreshItems(); }
            else if (_hover >= 300 && _hover < 300 + _characters.Length) { _character = _hover - 300; _focus = 1; RefreshItems(); }
            else if (_hover >= 400 && _hover < 403) { _pose = _hover - 400; _focus = 2; }
            else if (_hover >= 0 && _hover < _items.Length) { _selected = _hover; _focus = 3; _noticeTime = 0; }
        }
        if (navEdge)
        {
            if (up || down)
            {
                _focus = Mathf.Clamp(_focus + (up ? -1 : 1), 0, 4);
                if (_tab == 0 && _focus is 1 or 2) _focus = up ? 0 : 3;
            }
            else
            {
                int step = left ? -1 : 1;
                if (_focus == 0) { _tab = (_tab + 1) % 2; RefreshItems(); }
                else if (_focus == 1) { _character = (_character + step + _characters.Length) % _characters.Length; RefreshItems(); }
                else if (_focus == 2) _pose = (_pose + step + 3) % 3;
                else { _selected = (_selected + step + _items.Length) % _items.Length; _focus = 3; _noticeTime = 0; }
            }
            Audio.Instance?.PlayUiMove();
        }
        if (acceptEdge && _focus >= 3) ActivateSelected();
    }

    private void ActivateSelected()
    {
        if (_game.CosmeticEquipped(Selected.Id)) return;
        if (_game.OwnsCosmetic(Selected.Id))
        {
            if (_game.EquipCosmetic(Selected.Id)) Notice("装備を変更しました", false);
            return;
        }
        if (_game.Impression < Selected.Price) { Notice($"あと {Selected.Price - _game.Impression:N0} インプレ", true); return; }
        _pendingPurchase = Selected.Id;
        _confirmYes = false;
        Audio.Instance?.PlayUiConfirm();
    }

    private void BuyConfirmed()
    {
        string id = _pendingPurchase!;
        _pendingPurchase = null;
        if (!_game.TryPurchaseCosmetic(id)) { Notice("購入できませんでした", true); return; }
        _game.EquipCosmetic(id);
        Audio.Instance?.PlayUiBuy();
        Notice("購入して装備しました", false);
    }

    private void Notice(string text, bool error)
    {
        _notice = text; _noticeTime = 2.5;
        if (error) Audio.Instance?.PlayUiDeny();
        else Audio.Instance?.PlayUiConfirm();
    }

    private void Leave()
    {
        _leaving = true;
        Audio.Instance?.PlayUiCancel();
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    private void Art(string path, Rect2 area, bool flip = false)
    {
        var texture = Texture(path);
        float scale = Mathf.Min(area.Size.X / texture.GetWidth(), area.Size.Y / texture.GetHeight());
        var size = texture.GetSize() * scale;
        var rect = new Rect2(area.GetCenter() - size / 2f, size);
        if (flip) { rect.Position += new Vector2(rect.Size.X, 0); rect.Size = new Vector2(-rect.Size.X, rect.Size.Y); }
        DrawTextureRect(texture, rect, false);
    }

    private void Label(Rect2 rect, string text, bool active, bool focused, int size = 16)
    {
        if (active) UiKit.Box(this, rect, Raised, 6);
        if (focused) UiKit.Box(this, rect, Colors.Transparent, 6, new Color("a6dcec"), 1.5f);
        UiKit.Text(this, UiKit.ZenBold, rect.Position + new Vector2(0, (rect.Size.Y - size * 1.5f) / 2), text,
            size, active ? Ink : Muted, HorizontalAlignment.Center, rect.Size.X);
    }

    public override void _Draw()
    {
        if (_items.Length == 0) return;
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 720), Bg);
        var backdrop = Texture("res://char/bg2/title/L1_far.png");
        Vector2 sourceSize = new(backdrop.GetHeight() * 610f / 516, backdrop.GetHeight());
        DrawTextureRectRegion(backdrop, new Rect2(0, 144, 610, 516),
            new Rect2((backdrop.GetSize() - sourceSize) / 2, sourceSize), new Color(0.34f, 0.4f, 0.44f));
        Vector2 arrow = HomeRect.GetCenter();
        DrawLine(arrow + new Vector2(10, 0), arrow - new Vector2(10, 0), Ink, 2, true);
        DrawPolyline(new[] { arrow + new Vector2(-2, -8), arrow + new Vector2(-10, 0), arrow + new Vector2(-2, 8) }, Ink, 2, true);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(102, 32), "カスタマイズ", 23, Ink);
        UiKit.Text(this, UiKit.Mono, new Vector2(980, 38), $"{UiKit.Abbrev(_game.Impression)} Imp", 18, new Color("edcf82"), HorizontalAlignment.Right, 252);
        if (_hover == 100) UiKit.Text(this, UiKit.Zen, new Vector2(44, 72), "ホーム", 12, Muted);
        for (int i = 0; i < 2; i++)
            Label(TabRect(i), i == 0 ? "カーソル" : "コスチューム", _tab == i, !Pad.UsingMouse && _focus == 0 && _tab == i || _hover == 200 + i);
        if (_tab == 1)
        {
            for (int i = 0; i < _characters.Length; i++)
                Label(CharacterRect(i), _characters[i].CharacterName, i == _character,
                    !Pad.UsingMouse && _focus == 1 && i == _character || _hover == 300 + i, 15);
            int frame = (int)(_time * 6) % 8;
            int[] spin = { 0, 1, 2, 3, 4, 3, 2, 1 };
            string pose = _pose == 0 ? "idle" : _pose == 1 ? "aim_ur" : $"spin_{spin[frame]:00}";
            Art(Selected.PosePath(pose), new Rect2(134, 178, 342, 400), _pose == 2 && frame >= 5);
            for (int i = 0; i < 3; i++)
                Label(PoseRect(i), new[] { "通常", "照準", "回避" }[i], _pose == i,
                    !Pad.UsingMouse && _focus == 2 && _pose == i || _hover == 400 + i, 13);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(658, 165), "マウスアイコン", 16, Muted);
            Art(Selected.PreviewPath, new Rect2(190, 226, 188, 238));
            DrawRect(new Rect2(464, 464, 70, 76), new Color("ecf0ef"));
            Vector2 nativeSize = Texture(Selected.Art).GetSize() / (GetViewport().GetScreenTransform().Scale * UiKit.Scale);
            DrawTextureRect(Texture(Selected.Art), new Rect2(new Vector2(499, 502) - nativeSize / 2f, nativeSize), false);
            UiKit.Text(this, UiKit.Zen, new Vector2(464, 551), "実寸", 13, Muted, HorizontalAlignment.Center, 70);
        }
        Color accent = new(Selected.Accent);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(658, 224), Selected.Name, 28, Ink);
        string state = _game.CosmeticEquipped(Selected.Id) ? "装備中" : _game.OwnsCosmetic(Selected.Id) ? "購入済み" : $"{Selected.Price:N0} Imp";
        UiKit.Text(this, UiKit.ZenBold, new Vector2(658, 274), state, 16, accent);
        for (int i = 0; i < _items.Length; i++)
        {
            Rect2 rect = ItemRect(i);
            bool selected = i == _selected;
            UiKit.Box(this, rect, selected ? Raised : Bg, 6, selected ? accent : new Color("343e45"), selected ? 2 : 1);
            Art(_items[i].PosePath("idle"), new Rect2(rect.Position + new Vector2((rect.Size.X - 70) / 2, 16), new Vector2(70, 100)));
            UiKit.Multi(this, UiKit.ZenBold, rect.Position + new Vector2(14, 134), _items[i].Name, 17,
                selected ? Ink : Muted, rect.Size.X - 28, 2);
            if (_game.CosmeticEquipped(_items[i].Id))
                DrawCircle(rect.Position + new Vector2(rect.Size.X - 10, rect.Size.Y - 10), 3, accent);
        }
        if (_noticeTime > 0) UiKit.Text(this, UiKit.Zen, new Vector2(658, 577), _notice, 14, accent, HorizontalAlignment.Center, 574);
        bool equipped = _game.CosmeticEquipped(Selected.Id), owned = _game.OwnsCosmetic(Selected.Id);
        bool affordable = owned || _game.Impression >= Selected.Price;
        string action = equipped ? "装備中" : owned ? "装備する" : affordable ? $"{Selected.Price:N0} Imp で購入" : $"あと {Selected.Price - _game.Impression:N0} Imp";
        UiKit.Box(this, ActionRect, equipped || !affordable ? Raised : accent, 6);
        if (!Pad.UsingMouse && _focus == 4 || _hover == 101)
            UiKit.Box(this, ActionRect.Grow(3), Colors.Transparent, 7, Ink, 1);
        UiKit.Text(this, UiKit.ZenBold, ActionRect.Position + new Vector2(0, 11), action, 18,
            equipped || !affordable ? Muted : Bg, HorizontalAlignment.Center, ActionRect.Size.X);
        UiKit.Text(this, UiKit.Zen, new Vector2(48, 680), _tab == 1 ? "戦闘用コスチューム  /  能力補正なし" : "マウスカーソル", 12, Muted);
        if (_pendingPurchase != null) DrawConfirmation();
        UiKit.EndDesign(this);
    }

    private void DrawConfirmation()
    {
        var item = Cosmetics.Find(_pendingPurchase!)!;
        DrawRect(new Rect2(0, 0, 1280, 720), new Color(0, 0, 0, 0.78f));
        UiKit.Box(this, new Rect2(440, 243, 400, 243), Raised, 8, new Color("53636b"), 1);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(460, 264), "購入の確認", 22, Ink);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(460, 307), item.Name, 19, new Color(item.Accent));
        UiKit.Text(this, UiKit.Zen, new Vector2(460, 349), $"{item.Price:N0} Imp", 18, Ink);
        UiKit.Text(this, UiKit.Zen, new Vector2(460, 380), $"購入後の残高  {UiKit.Abbrev(_game.Impression - item.Price)} Imp", 14, Muted);
        Label(CancelRect, "キャンセル", false, Pad.UsingMouse ? _hover == 500 : !_confirmYes);
        Label(ConfirmRect, "購入して装備", true, Pad.UsingMouse ? _hover == 501 : _confirmYes);
    }
}
