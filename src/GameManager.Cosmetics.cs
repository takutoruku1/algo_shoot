using Godot;
using System.Collections.Generic;

public partial class GameManager
{
    private readonly HashSet<string> _ownedCosmetics = new();
    private readonly Dictionary<Job, string> _costumes = new();
    public string SelectedCursor { get; private set; } = Cosmetics.DefaultCursor;
    private ImageTexture? _cursorTexture;
    internal Vector2I CursorPixelDimensions { get; private set; }

    public CosmeticItem CostumeFor(Job job) => _costumes.TryGetValue(job, out string? id)
        ? Cosmetics.Find(id)! : Cosmetics.DefaultCostume(job);

    public bool OwnsCosmetic(string id) => Cosmetics.Find(id) is { } item
        && (item.Price == 0 || _ownedCosmetics.Contains(id));

    public bool CosmeticEquipped(string id) => Cosmetics.Find(id) is { } item &&
        (item.Kind == CosmeticKind.Cursor ? SelectedCursor == id : CostumeFor(item.Character!.Value).Id == id);

    public bool TryPurchaseCosmetic(string id)
    {
        var item = Cosmetics.Find(id);
        if (item == null || OwnsCosmetic(id) || Impression < item.Price
            || (item.Character.HasValue && !IsJobUnlocked(item.Character.Value))) return false;
        Impression -= item.Price;
        _ownedCosmetics.Add(id);
        AutoSave();
        return true;
    }

    public bool EquipCosmetic(string id)
    {
        var item = Cosmetics.Find(id);
        if (item == null || !OwnsCosmetic(id)
            || (item.Character.HasValue && !IsJobUnlocked(item.Character.Value))) return false;
        if (item.Kind == CosmeticKind.Cursor)
        {
            SelectedCursor = id;
            ApplyCursor();
        }
        else _costumes[item.Character!.Value] = id;
        AutoSave();
        return true;
    }

    private void ApplyCursor()
    {
        using var image = GD.Load<Texture2D>(Cosmetics.Find(SelectedCursor)!.Art).GetImage();
        Vector2I window = GetWindow().Size;
        float scale = Mathf.Min(window.X / UiKit.DesignW, window.Y / UiKit.DesignH);
        int height = Mathf.Clamp(Mathf.RoundToInt(20 * scale), 12, 64);
        int width = Mathf.RoundToInt(height * image.GetWidth() / (float)image.GetHeight());
        image.Resize(width, height, Image.Interpolation.Lanczos);
        var texture = ImageTexture.CreateFromImage(image);
        Input.SetCustomMouseCursor(texture, Input.CursorShape.Arrow, Vector2.Zero);
        _cursorTexture?.Dispose();
        _cursorTexture = texture;
        CursorPixelDimensions = new Vector2I(width, height);
    }

    private void SaveCosmetics(Godot.Collections.Dictionary data)
    {
        var owned = new Godot.Collections.Array();
        foreach (string id in _ownedCosmetics) owned.Add(id);
        var equipped = new Godot.Collections.Dictionary();
        foreach (var pair in _costumes) equipped[Jobs.Get(pair.Key).CharacterId] = pair.Value;
        data["cosmetics"] = new Godot.Collections.Dictionary
        {
            ["owned"] = owned, ["cursor"] = SelectedCursor, ["costumes"] = equipped,
        };
    }

    private void LoadCosmetics(Godot.Collections.Dictionary data)
    {
        _ownedCosmetics.Clear();
        _costumes.Clear();
        SelectedCursor = Cosmetics.DefaultCursor;
        if (data.TryGetValue("cosmetics", out var saved) && saved.VariantType == Variant.Type.Dictionary)
        {
            var cosmetics = saved.AsGodotDictionary();
            if (cosmetics.TryGetValue("owned", out var owned) && owned.VariantType == Variant.Type.Array)
                foreach (var value in owned.AsGodotArray())
                    if (value.VariantType == Variant.Type.String && Cosmetics.Find(value.AsString()) is { Price: > 0 } item)
                        _ownedCosmetics.Add(item.Id);
            if (cosmetics.TryGetValue("cursor", out var cursor) && cursor.VariantType == Variant.Type.String
                && Cosmetics.Find(cursor.AsString()) is { Kind: CosmeticKind.Cursor } c && OwnsCosmetic(c.Id))
                SelectedCursor = c.Id;
            if (cosmetics.TryGetValue("costumes", out var outfits) && outfits.VariantType == Variant.Type.Dictionary)
                foreach (var pair in outfits.AsGodotDictionary())
                    if (pair.Key.VariantType == Variant.Type.String && pair.Value.VariantType == Variant.Type.String
                        && Cosmetics.Find(pair.Value.AsString()) is { Kind: CosmeticKind.Costume } item
                        && Jobs.Get(item.Character!.Value).CharacterId == pair.Key.AsString() && OwnsCosmetic(item.Id))
                        _costumes[item.Character.Value] = item.Id;
        }
        ApplyCursor();
    }

    private void ResetCosmetics()
    {
        _ownedCosmetics.Clear();
        _costumes.Clear();
        SelectedCursor = Cosmetics.DefaultCursor;
        ApplyCursor();
    }
}
