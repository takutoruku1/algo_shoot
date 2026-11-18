using Godot;
using System;
using System.Collections.Generic;

public partial class Customize : Node2D
{
    private static readonly Color Bg = new("f3ebdf"), Raised = new("fffaf2"), Ink = new("42343a"), Muted = new("786761");
    private static readonly Color Rose = new("955466"), Brass = new("a9824e"), Seam = new("d8c6b3");
    private static readonly Rect2 HomeRect = new(40, 25, 44, 44);
    private static readonly Rect2 ActionRect = new(658, 606, 574, 48);
    private static readonly Rect2 CancelRect = new(460, 417, 172, 46), ConfirmRect = new(648, 417, 172, 46);
    private GameManager _game = null!;
    private readonly Dictionary<string, Texture2D> _textures = new();
    private readonly Dictionary<int, int> _catalogSelections = new();
    private JobTuning[] _characters = Array.Empty<JobTuning>();
    private CosmeticItem[] _items = Array.Empty<CosmeticItem>();
    private int _tab, _character, _selected, _pose, _focus = 3, _hover = -1;
    private bool _acceptHeld, _backHeld, _navHeld, _leaving, _confirmYes;
    private int _navDirection, _catalog = -2;
    private double _navRepeat;
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
        if (_items.Length > 0) _catalogSelections[_catalog] = _selected;
        _catalog = _tab == 0 ? (int)_characters[_character].Id : -1;
        _items = Array.FindAll(Cosmetics.All, item => _tab == 0
            ? item.Kind == CosmeticKind.Costume && item.Character == _characters[_character].Id
            : item.Kind == CosmeticKind.Cursor);
        _selected = _catalogSelections.TryGetValue(_catalog, out int selection) ? selection
            : Math.Max(0, Array.FindIndex(_items, item => _game.CosmeticEquipped(item.Id)));
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
            if (_tab == 0)
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
        int direction = up != down ? (up ? -2 : 2) : left != right ? (left ? -1 : 1) : 0;
        bool nav = direction != 0;
        bool acceptEdge = accept && !_acceptHeld, backEdge = back && !_backHeld;
        bool navEdge = nav && (!_navHeld || direction != _navDirection);
        _navDirection = direction;
        _navRepeat = navEdge || !nav ? 0.35 : _navRepeat - delta;
        bool navMove = navEdge || nav && _navRepeat <= 0;
        if (navMove && !navEdge) _navRepeat = 0.12;
        _acceptHeld = accept; _backHeld = back; _navHeld = nav;
        if (_time < 0.25) return;
        string backLabel = _pendingPurchase != null ? "キャンセル" : "もどる";
        if (backEdge || Pad.MouseRightClick() || UiKit.HintItemClicked(UiKit.HintAnchor, HintItems(), backLabel))
        {
            if (_pendingPurchase != null) { _pendingPurchase = null; Audio.Instance?.PlayUiCancel(); }
            else Leave();
            return;
        }
        // 右下のヒント帯の「メニュー」は左クリックでも開く（PauseMenu の右下チップは廃止＝帯に統合。Hub と同じ作法）。
        if (UiKit.HintItemClicked(UiKit.HintAnchor, HintItems(), "メニュー"))
        {
            GetNodeOrNull<PauseMenu>("/root/PauseMenu")?.Open();
            return;
        }
        if (_pendingPurchase != null)
        {
            if (navEdge && Math.Abs(direction) == 1)
            {
                _confirmYes = direction > 0;
                Pad.ReleaseMouseFollow();
                Audio.Instance?.PlayUiMove();
            }
            if (Pad.UsingMouse && _hover is 500 or 501) _confirmYes = _hover == 501;
            if (Pad.MouseClick() && _hover == 500) { _pendingPurchase = null; Audio.Instance?.PlayUiCancel(); }
            else if ((Pad.MouseClick() && _hover == 501) || (acceptEdge && _confirmYes)) BuyConfirmed();
            else if (acceptEdge) { _pendingPurchase = null; Audio.Instance?.PlayUiCancel(); }
            return;
        }
        if (Pad.MouseClick())
        {
            if (_hover == 100) { Leave(); return; }
            if (_hover == 101) { _focus = 4; ActivateSelected(); }
            else if (_hover >= 200 && _hover < 202)
            {
                if (_tab != _hover - 200) { _tab = _hover - 200; RefreshItems(); Audio.Instance?.PlayUiMove(); }
                _focus = 0;
            }
            else if (_hover >= 300 && _hover < 300 + _characters.Length)
            {
                if (_character != _hover - 300) { _character = _hover - 300; RefreshItems(); Audio.Instance?.PlayUiMove(); }
                _focus = 1;
            }
            else if (_hover >= 400 && _hover < 403)
            {
                if (_pose != _hover - 400) Audio.Instance?.PlayUiMove();
                _pose = _hover - 400; _focus = 2;
            }
            else if (_hover >= 0 && _hover < _items.Length)
            {
                if (_selected != _hover) Audio.Instance?.PlayUiMove();
                _selected = _hover; _focus = 3; _noticeTime = 0;
            }
            return;
        }
        if (navMove)
        {
            Pad.ReleaseMouseFollow();
            if (Math.Abs(direction) == 2)
            {
                _focus = Mathf.Clamp(_focus + Math.Sign(direction), 0, 4);
                if (_tab == 1 && _focus is 1 or 2) _focus = direction < 0 ? 0 : 3;
            }
            else
            {
                int step = direction;
                if (_focus == 0) { _tab = (_tab + 1) % 2; RefreshItems(); }
                else if (_focus == 1) { _character = (_character + step + _characters.Length) % _characters.Length; RefreshItems(); }
                else if (_focus == 2) _pose = (_pose + step + 3) % 3;
                else { _selected = (_selected + step + _items.Length) % _items.Length; _focus = 3; _noticeTime = 0; }
            }
            Audio.Instance?.PlayUiMove();
        }
        if (acceptEdge)
        {
            Pad.ReleaseMouseFollow();
            if (_focus == 0) { _tab = (_tab + 1) % 2; RefreshItems(); Audio.Instance?.PlayUiMove(); }
            else if (_focus == 1) { _character = (_character + 1) % _characters.Length; RefreshItems(); Audio.Instance?.PlayUiMove(); }
            else if (_focus == 2) { _pose = (_pose + 1) % 3; Audio.Instance?.PlayUiMove(); }
            else ActivateSelected();
        }
    }

    // 右下のヒント帯。キーボード：↑↓ えらぶ／←→ きりかえ／Z けってい／Esc もどる／M メニュー。
    //   購入確認中は ←→ えらぶ／Z けってい／Esc キャンセル。パッドは 十字／A／B／≡。
    private (string token, string label)[] HintItems() =>
        _leaving ? System.Array.Empty<(string, string)>()
        : _pendingPurchase != null ? UiKit.PhoneHints("←→", "けってい", "キャンセル")
        : UiKit.PhoneHints("↑↓", "けってい", "もどる", ("←→", "きりかえ"));

    // ヒント帯の外接矩形（QA 用。出していなければ大きさ 0）。
    public Rect2 HintBarRect => UiKit.HintBarBounds(UiKit.HintAnchor, HintItems());

    private void ActivateSelected()
    {
        if (_game.CosmeticEquipped(Selected.Id)) { Notice("装備中です", false); return; }
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
        GameManager.FadeToScene(this, "res://Hub.tscn");
    }

    // カーソルのアイコン（余白の無い小さな絵）はテクスチャ全体を枠へフィットさせる。立ち絵は Portrait を使う。
    private void Art(string path, Rect2 area)
    {
        var texture = Texture(path);
        float scale = Mathf.Min(area.Size.X / texture.GetWidth(), area.Size.Y / texture.GetHeight());
        var size = texture.GetSize() * scale;
        DrawTextureRect(texture, new Rect2(area.GetCenter() - size / 2f, size), false);
    }

    private static readonly Rect2 PreviewFrame = new(134, 194, 342, 400);
    private const float PreviewContentH = 270f, ThumbContentH = 86f;
    private static readonly Vector2 PreviewCenter = PreviewFrame.GetCenter();
    private static Rect2 ThumbBox(Rect2 card) => new(card.Position.X + (card.Size.X - 110) / 2, card.Position.Y + 16, 110, 100);

    private void Portrait(CosmeticItem item, string pose, Vector2 center, float contentHeight)
        => PlayerArt.Draw(this, Texture(item.PosePath(pose)),
            center, contentHeight);

    private void Label(Rect2 rect, string text, bool active, bool focused, int size = 16)
    {
        UiKit.Box(this, rect, active ? Rose : Raised, 4, active ? Rose : Seam, 1);
        if (focused) UiKit.Box(this, rect.Grow(3), Colors.Transparent, 6, Brass, 2);
        UiKit.Text(this, UiKit.ZenBold, rect.Position + new Vector2(0, (rect.Size.Y - size * 1.5f) / 2), text,
            size, active ? Raised : Ink, HorizontalAlignment.Center, rect.Size.X);
    }

    private void DrawAtelier()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), Bg);
        for (int x = 16; x < 1280; x += 32)
            DrawLine(new Vector2(x, 0), new Vector2(x, 662), new Color(Brass, .045f), 1);
        DrawRect(new Rect2(0, 0, 1280, 84), Raised);
        DrawLine(new Vector2(32, 83), new Vector2(1248, 83), Seam, 1);
        DrawRect(new Rect2(0, 664, 1280, 56), new Color("e5d6c5"));
        DrawLine(new Vector2(0, 664), new Vector2(1280, 664), Brass, 2);
        UiKit.Box(this, new Rect2(24, 145, 578, 513), new Color("e4d1c7"), 8, Seam, 1);
        for (int x = 40; x < 600; x += 30)
            DrawLine(new Vector2(x, 160), new Vector2(x, 561), new Color(Rose, .07f), 1);
        DrawRect(new Rect2(25, 560, 576, 97), new Color("ceb59d"));
        for (int x = -80; x < 750; x += 100)
            DrawLine(new Vector2(Mathf.Clamp(x, 26, 600), 657), new Vector2(305 + (x - 305) * .62f, 561), new Color(Brass, .2f), 1);

        var mirror = new Vector2[27];
        for (int i = 0; i <= 24; i++)
            mirror[i] = new Vector2(305, 337) + Vector2.FromAngle(Mathf.Pi + Mathf.Pi * i / 24) * 164;
        mirror[25] = new Vector2(469, 578);
        mirror[26] = new Vector2(141, 578);
        DrawColoredPolygon(mirror, new Color("c5cfcb"));
        var outline = new Vector2[28];
        Array.Copy(mirror, outline, mirror.Length);
        outline[27] = mirror[0];
        DrawPolyline(outline, Brass, 7, true);
        DrawColoredPolygon(new[] { new Vector2(184, 235), new Vector2(216, 210),
            new Vector2(432, 546), new Vector2(395, 546) }, new Color(1, 1, 1, .14f));
        var rug = new Vector2[48];
        for (int i = 0; i < rug.Length; i++)
            rug[i] = new Vector2(305, 556) + Vector2.FromAngle(i * Mathf.Tau / rug.Length) * new Vector2(147, 22);
        DrawColoredPolygon(rug, new Color("ad7980"));
        UiKit.Text(this, UiKit.Mono, new Vector2(185, 184), _tab == 0 ? "FITTING ROOM" : "ACCESSORIES", 11,
            new Color("546962"), HorizontalAlignment.Center, 240);
        for (int side = 0; side < 2; side++)
        {
            float P(float x) => side == 0 ? x : 610 - x;
            DrawColoredPolygon(new[] { new Vector2(P(43), 161), new Vector2(P(128), 161),
                new Vector2(P(104), 330), new Vector2(P(84), 401), new Vector2(P(119), 568),
                new Vector2(P(43), 584) }, new Color("ad737e"));
            for (int fold = 0; fold < 4; fold++)
                DrawPolyline(new[] { new Vector2(P(51 + fold * 17), 170),
                    new Vector2(P(51 + fold * 8), 395), new Vector2(P(51 + fold * 18), 568) },
                    new Color("8e5965"), 3, true);
            DrawLine(new Vector2(P(44), 399), new Vector2(P(86), 393), new Color("e7c384"), 6, true);
        }
        DrawLine(new Vector2(40, 157), new Vector2(570, 157), Brass, 5, true);
        DrawCircle(new Vector2(38, 157), 6, Brass);
        DrawCircle(new Vector2(572, 157), 6, Brass);
        UiKit.Box(this, new Rect2(201, 582, 208, 24), Raised, 2, Seam, 1);
        UiKit.Text(this, UiKit.Zen, new Vector2(201, 584), _tab == 0 ? "試着はご自由に" : "大きな見本と実寸で確認", 12, Muted, HorizontalAlignment.Center, 208);
    }

    private void DrawCatalog()
    {
        DrawLine(new Vector2(644, 300), new Vector2(1246, 300), Brass, 4, true);
        for (int i = 0; i < _items.Length; i++)
        {
            Rect2 rect = ItemRect(i);
            bool selected = i == _selected;
            bool equipped = _game.CosmeticEquipped(_items[i].Id), owned = _game.OwnsCosmetic(_items[i].Id);
            float hangerX = rect.GetCenter().X;
            DrawArc(new Vector2(hangerX, 305), 5, Mathf.Pi, Mathf.Tau * 1.25f, 24, Brass, 2, true);
            DrawLine(new Vector2(hangerX, 310), new Vector2(hangerX, 316), Brass, 2, true);
            DrawPolyline(new[] { new Vector2(hangerX, 316), new Vector2(hangerX - 17, 324),
                new Vector2(hangerX + 17, 324), new Vector2(hangerX, 316) }, Brass, 2, true);
            UiKit.Box(this, new Rect2(rect.Position + new Vector2(3, 5), rect.Size), new Color(Ink, .09f), 5);
            UiKit.Box(this, rect, selected ? new Color("f4e1dc") : Raised, 4, selected ? Rose : Seam, selected ? 2 : 1);
            if (Pad.UsingMouse ? _hover == i : selected && _focus == 3)
                UiKit.Box(this, rect.Grow(4), Colors.Transparent, 7, Brass, 2);
            DrawCircle(new Vector2(hangerX, rect.Position.Y + 7), 2, Brass);
            Rect2 thumb = ThumbBox(rect);
            if (_items[i].Kind == CosmeticKind.Costume)
                Portrait(_items[i], "idle", thumb.GetCenter(), ThumbContentH);
            else Art(_items[i].PosePath("idle"), thumb);
            for (float x = rect.Position.X + 12; x < rect.End.X - 14; x += 9)
                DrawLine(new Vector2(x, rect.Position.Y + 125), new Vector2(x + 4, rect.Position.Y + 125), Seam, 1);
            UiKit.Text(this, UiKit.ZenBold, rect.Position + new Vector2(14, 135), _items[i].Name, 17, Ink);
            UiKit.Text(this, UiKit.Mono, rect.Position + new Vector2(14, 168),
                _items[i].Price == 0 ? "STANDARD" : $"{_items[i].Price:N0} Imp", 13, Muted);
            if (owned)
            {
                var badge = new Rect2(rect.End.X - 77, rect.Position.Y + 166, 65, 22);
                UiKit.Box(this, badge, equipped ? Rose : new Color("e4d8c9"), 3);
                UiKit.Text(this, UiKit.ZenBold, badge.Position + new Vector2(0, 2), equipped ? "装備中" : "購入済み",
                    11, equipped ? Raised : Ink, HorizontalAlignment.Center, badge.Size.X);
            }
        }
    }

    public override void _Draw()
    {
        if (_items.Length == 0) return;
        UiKit.BeginDesign(this);
        DrawAtelier();
        Vector2 arrow = HomeRect.GetCenter();
        UiKit.Box(this, HomeRect, Raised, 22, Seam, 1);
        DrawLine(arrow + new Vector2(10, 0), arrow - new Vector2(10, 0), Ink, 2, true);
        DrawPolyline(new[] { arrow + new Vector2(-2, -8), arrow + new Vector2(-10, 0), arrow + new Vector2(-2, 8) }, Ink, 2, true);
        UiKit.Tracked(this, UiKit.Mono, new Vector2(104, 13), "REFRAIN / ATELIER", 12, Rose, 1.5f);
        UiKit.Text(this, UiKit.KaiseiDecol, new Vector2(102, 32), "コスチュームショップ", 27, Ink);
        UiKit.Text(this, UiKit.KleeOne, new Vector2(643, 37), "お気に入りを、試着して。", 18, Muted);
        UiKit.Box(this, new Rect2(1000, 18, 232, 48), new Color("e9ddcc"), 3, Seam, 1);
        UiKit.Text(this, UiKit.Zen, new Vector2(1014, 22), "お財布", 11, Muted);
        UiKit.Text(this, UiKit.Mono, new Vector2(1014, 37), $"{UiKit.Abbrev(_game.Impression)} Imp", 18, Ink, HorizontalAlignment.Right, 202);
        for (int i = 0; i < 2; i++)
            Label(TabRect(i), i == 0 ? "コスチューム" : "カーソル", _tab == i, Pad.UsingMouse ? _hover == 200 + i : _focus == 0 && _tab == i);
        if (_tab == 0)
        {
            for (int i = 0; i < _characters.Length; i++)
                Label(CharacterRect(i), _characters[i].CharacterName, i == _character,
                    Pad.UsingMouse ? _hover == 300 + i : _focus == 1 && i == _character, 15);
            int frame = (int)(_time * 6) % PlayerArt.SpinFrames;
            string pose = _pose == 0 ? "idle" : _pose == 1 ? "aim_ur" : $"spin_{frame:00}";
            Portrait(Selected, pose, PreviewCenter, PreviewContentH);
            for (int i = 0; i < 3; i++)
                Label(PoseRect(i), new[] { "通常", "照準", "回避" }[i], _pose == i,
                    Pad.UsingMouse ? _hover == 400 + i : _focus == 2 && _pose == i, 13);
        }
        else
        {
            UiKit.Text(this, UiKit.KaiseiDecol, new Vector2(658, 160), "小さなアクセサリー", 21, Ink);
            Art(Selected.PreviewPath, new Rect2(211, 261, 188, 238));
            UiKit.Box(this, new Rect2(432, 514, 102, 64), Raised, 4, Brass, 1);
            Vector2 nativeSize = Texture(Selected.Art).GetSize() / (GetViewport().GetScreenTransform().Scale * UiKit.Scale);
            DrawTextureRect(Texture(Selected.Art), new Rect2(new Vector2(483, 535) - nativeSize / 2f, nativeSize), false);
            UiKit.Text(this, UiKit.Zen, new Vector2(432, 559), "実寸", 11, Muted, HorizontalAlignment.Center, 102);
        }
        UiKit.Text(this, UiKit.KaiseiDecol, new Vector2(658, 218), Selected.Name, 28, Ink);
        string state = _game.CosmeticEquipped(Selected.Id) ? "ただいま装備中" : _game.OwnsCosmetic(Selected.Id) ? "購入済み / いつでもお着替え" : _tab == 0 ? "試着中 / 購入前に動きも確認できます" : "選択中 / 左のサンプルで実寸を確認";
        UiKit.Text(this, UiKit.Zen, new Vector2(658, 269), state, 14, Rose);
        DrawCatalog();
        DrawLine(new Vector2(658, 545), new Vector2(1232, 545), Seam, 1);
        UiKit.Text(this, UiKit.Mono, new Vector2(658, 553), "CHECKOUT", 11, Brass);
        if (_noticeTime > 0) UiKit.Text(this, UiKit.Zen, new Vector2(658, 579), _notice, 14, Rose, HorizontalAlignment.Center, 574);
        bool equipped = _game.CosmeticEquipped(Selected.Id), owned = _game.OwnsCosmetic(Selected.Id);
        bool affordable = owned || _game.Impression >= Selected.Price;
        string action = equipped ? "装備中" : owned ? "装備する" : affordable ? $"{Selected.Price:N0} Imp で購入" : $"あと {Selected.Price - _game.Impression:N0} Imp";
        UiKit.Box(this, new Rect2(ActionRect.Position + new Vector2(0, 3), ActionRect.Size), new Color("754451"), 4);
        UiKit.Box(this, ActionRect, equipped || !affordable ? new Color("e2d7c9") : Rose, 4);
        if (Pad.UsingMouse ? _hover == 101 : _focus == 4)
            UiKit.Box(this, ActionRect.Grow(4), Colors.Transparent, 7, Brass, 2);
        UiKit.Text(this, UiKit.ZenBold, ActionRect.Position + new Vector2(0, 11), action, 18,
            equipped || !affordable ? Muted : Raised, HorizontalAlignment.Center, ActionRect.Size.X);
        UiKit.Text(this, UiKit.Zen, new Vector2(48, 680), _tab == 0 ? "戦闘用コスチューム / 能力補正なし" : "マウスカーソル / 選んだ絵柄を装備", 12, Muted);
        if (_pendingPurchase != null) DrawConfirmation();
        UiKit.HintBarPlate(this, UiKit.HintAnchor, HintItems(), "メニュー", _pendingPurchase != null ? "キャンセル" : "もどる");
        UiKit.EndDesign(this);
    }

    private void DrawConfirmation()
    {
        var item = Cosmetics.Find(_pendingPurchase!)!;
        DrawRect(new Rect2(0, 0, 1280, 720), new Color(.13f, .09f, .1f, .78f));
        UiKit.Box(this, new Rect2(446, 249, 400, 243), new Color(0, 0, 0, .18f), 3);
        UiKit.Box(this, new Rect2(440, 243, 400, 243), Raised, 3, Brass, 1);
        for (int x = 444; x < 834; x += 12)
            DrawColoredPolygon(new[] { new Vector2(x, 485), new Vector2(x + 6, 492), new Vector2(x + 12, 485) }, Raised);
        UiKit.Text(this, UiKit.KaiseiDecol, new Vector2(460, 264), "お会計の確認", 22, Ink);
        DrawLine(new Vector2(460, 300), new Vector2(820, 300), Seam, 1);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(460, 307), item.Name, 19, Rose);
        UiKit.Text(this, UiKit.Mono, new Vector2(460, 349), $"{item.Price:N0} Imp", 18, Ink);
        UiKit.Text(this, UiKit.Zen, new Vector2(460, 380), $"購入後の残高  {UiKit.Abbrev(_game.Impression - item.Price)} Imp", 14, Muted);
        Label(CancelRect, "キャンセル", false, Pad.UsingMouse ? _hover == 500 : !_confirmYes);
        Label(ConfirmRect, "購入して装備", true, Pad.UsingMouse ? _hover == 501 : _confirmYes);
    }
}
