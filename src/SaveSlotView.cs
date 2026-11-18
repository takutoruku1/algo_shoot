using Godot;

public static class SaveSlotView
{
    public static readonly Rect2 Bounds = new(80, 58, 1120, 604);
    private static readonly Color Paper = new("f5f1eb");
    private static readonly Color Muted = new("aab6c7");
    private static readonly Color SaveAccent = new("b9dcd5");
    private static readonly Color LoadAccent = new("f0baaa");
    private static readonly Color Error = new("efa5ad");
    private static FontFile? _wordmark;

    public static Rect2 RowRect(int index, int count)
    {
        float height = count == 4 ? 94 : 124;
        return new Rect2(476, 174 + index * (height + 12), 688, height);
    }

    public static Rect2 CloseRect => new(1132, 88, 42, 42);

    public static void Draw(CanvasItem canvas, GameManager.SaveSlotInfo[] slots, int firstSlot,
        int selected, bool forSave, bool closeSelected)
    {
        Color accent = forSave ? SaveAccent : LoadAccent;
        canvas.DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color("080d17", 0.80f));
        UiKit.Box(canvas, new Rect2(80, 70, 1120, 604), new Color(0, 0, 0, 0.3f), 14);
        UiKit.Box(canvas, Bounds, new Color("111b29", 0.98f), 14, new Color("3b4a5b"), 1);
        UiKit.HGradient(canvas, new Rect2(94, 73, 344, 536), new Color("1d2b3b"), new Color("111b29"));
        canvas.DrawLine(new Vector2(442, 94), new Vector2(442, 607), new Color("354354"), 1, true);
        UiKit.HGradient(canvas, new Rect2(110, 58, 550, 1), new Color(accent, 0.8f), new Color(accent, 0));

        if (_wordmark == null)
        {
            _wordmark = (FontFile)GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf").Duplicate();
            _wordmark.Oversampling = 2;
        }
        UiKit.Text(canvas, _wordmark, new Vector2(116, 91), "Refrain", 46, Paper);
        UiKit.Tracked(canvas, UiKit.Mono, new Vector2(118, 149), "MEMORY ARCHIVE", 11, Muted, 2);
        UiKit.Text(canvas, UiKit.ZenBold, new Vector2(114, 187), forSave ? "セーブ" : "ロード", 42, Paper);
        UiKit.Multi(canvas, UiKit.Zen, new Vector2(118, 249),
            forSave ? "今の歩みを、ここに残す。\n保存する記録を選んでください。" : "残した記録から、もう一度。\n読み込む記録を選んでください。", 16, Muted, 290);

        canvas.DrawLine(new Vector2(118, 309), new Vector2(412, 309), new Color(accent, 0.24f), 1, true);
        Diamond(canvas, new Vector2(124, 309), 3, accent);
        float glint = (float)(Time.GetTicksMsec() % 4200) / 4200;
        Diamond(canvas, new Vector2(140 + 264 * glint, 309), 2, new Color(accent, Mathf.Sin(glint * Mathf.Pi) * 0.7f));

        if (selected < slots.Length)
            DrawDetails(canvas, slots[selected], firstSlot + selected, forSave, accent);
        else
        {
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2(118, 342), "記録を閉じる", 24, Paper);
            UiKit.Multi(canvas, UiKit.Zen, new Vector2(118, 387), "変更せずに、前の画面へ戻ります。", 17, Muted, 286);
        }

        UiKit.Text(canvas, UiKit.ZenBold, new Vector2(476, 107), forSave ? "保存先を選択" : "記録を選択", 24, Paper);
        UiKit.Tracked(canvas, UiKit.Mono, new Vector2(986, 116), $"{slots.Length:00} SLOTS", 12, Muted, 1);
        DrawClose(canvas, closeSelected, accent);
        for (int i = 0; i < slots.Length; i++)
            DrawRow(canvas, RowRect(i, slots.Length), slots[i], firstSlot + i, selected == i, forSave, accent);

        canvas.DrawLine(new Vector2(116, 612), new Vector2(1164, 612), new Color("354354"), 1, true);
        UiKit.Text(canvas, UiKit.Zen, new Vector2(118, 630), forSave ? "オートセーブとは別に保存します" : "ロード後はハブから再開します", 14, Muted);
        string action = selected >= slots.Length ? "閉じる" : forSave ? "保存" : "ロード";
        UiKit.KeyCapRow(canvas, new Vector2(779, 628), $"↑+↓ 選択 / Z {action} / X 戻る", 24, ink: Paper);
    }

    private static void DrawDetails(CanvasItem canvas, GameManager.SaveSlotInfo info, int slot, bool forSave, Color accent)
    {
        UiKit.Tracked(canvas, UiKit.Mono, new Vector2(118, 330), "SELECTED RECORD", 11, accent, 1.5f);
        UiKit.Text(canvas, UiKit.ZenBold, new Vector2(116, 353), SlotName(slot), 28, Paper);
        if (info.Readable)
        {
            UiKit.Text(canvas, UiKit.Mono, new Vector2(118, 399), info.SavedAt, 17, Muted);
            UiKit.Multi(canvas, UiKit.ZenBold, new Vector2(118, 433), info.Progress, 17, Paper, 294);
            float y = 471;
            y += UiKit.MultiLeading(canvas, UiKit.Zen, new Vector2(118, y), info.PlayTime, 15, Muted, 294, 1);
            foreach (string detail in info.Details.Split('　'))
                y += UiKit.MultiLeading(canvas, UiKit.Zen, new Vector2(118, y + 4), detail, 15, Muted, 294, 1) + 4;
        }
        else
        {
            string heading = info.Exists ? "読み込めないデータ" : "まだ記録がありません";
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2(118, 413), heading, 19, info.Exists ? Error : Muted);
            string note = forSave
                ? info.Exists ? "このスロットを選ぶと、\n現在の進行で上書きします。" : "このスロットを選ぶと、\n新しい記録を保存できます。"
                : info.Exists ? "ほかの記録を選んでください。" : "セーブした記録を選ぶと、\n続きから再開できます。";
            UiKit.Multi(canvas, UiKit.Zen, new Vector2(118, 459), note, 17, Muted, 286);
        }
    }

    private static void DrawRow(CanvasItem canvas, Rect2 row, GameManager.SaveSlotInfo info,
        int slot, bool selected, bool forSave, Color accent)
    {
        bool available = forSave || info.Readable;
        Color border = selected ? accent : new Color("354252");
        Color fill = selected ? new Color("283b4b") : new Color("172332");
        UiKit.Box(canvas, row, fill, 8, new Color(border, selected ? 0.9f : 0.75f), 1);
        if (selected)
        {
            canvas.DrawLine(row.Position + new Vector2(1, 13), row.Position + new Vector2(1, row.Size.Y - 13), accent, 3, true);
            UiKit.HGradient(canvas, new Rect2(row.Position + new Vector2(9, 1), new Vector2(row.Size.X - 18, 1)), new Color(accent, 0.7f), new Color(accent, 0));
        }
        float inset = (row.Size.Y - 94) / 2;
        Vector2 p = row.Position + new Vector2(0, inset);
        string number = slot == 0 ? "A" : slot.ToString("00");
        UiKit.Text(canvas, UiKit.Rajdhani, p + new Vector2(22, 13), number, 52,
            selected ? accent : new Color("71859a"), HorizontalAlignment.Center, 60);
        canvas.DrawLine(p + new Vector2(98, 20), p + new Vector2(98, 74), new Color(border, 0.4f), 1, true);
        UiKit.Text(canvas, UiKit.ZenBold, p + new Vector2(118, 10), SlotName(slot), 22, available ? Paper : Muted);
        string status = info.Readable ? slot == 0 ? "自動保存" : "保存済み" : info.Exists ? "読込不可" : "空きスロット";
        Color statusColor = info.Readable ? accent : info.Exists ? Error : Muted;
        var badge = new Rect2(p + new Vector2(520, 16), new Vector2(112, 25));
        UiKit.Box(canvas, badge, new Color(statusColor, 0.08f), 4, new Color(statusColor, 0.28f), 1);
        UiKit.Text(canvas, UiKit.ZenBold, badge.Position + new Vector2(0, 2), status, 13, statusColor,
            HorizontalAlignment.Center, badge.Size.X);
        if (info.Readable)
        {
            UiKit.Text(canvas, UiKit.Zen, p + new Vector2(118, 43), info.Progress, 16, selected ? Paper : Muted);
            UiKit.Text(canvas, UiKit.Mono, p + new Vector2(118, 68), info.SavedAt, 13, Muted);
            if (selected && forSave)
                UiKit.Text(canvas, UiKit.Zen, p + new Vector2(438, 67), "上書き保存", 13, accent, HorizontalAlignment.Right, 194);
        }
        else
            UiKit.Text(canvas, UiKit.Zen, p + new Vector2(118, 51), forSave ? "ここに現在の進行を保存" : info.Exists ? "ほかの記録を選んでください" : "セーブデータがありません", 16, Muted);
        if (selected && available)
        {
            Vector2 c = row.Position + new Vector2(660, row.Size.Y / 2);
            canvas.DrawLine(c + new Vector2(-8, 0), c + new Vector2(6, 0), accent, 1.5f, true);
            canvas.DrawPolyline(new[] { c + new Vector2(0, -6), c + new Vector2(6, 0), c + new Vector2(0, 6) }, accent, 1.5f, true);
        }
    }

    private static string SlotName(int slot) => slot == 0 ? "オートセーブ" : $"スロット {slot:00}";

    private static void DrawClose(CanvasItem canvas, bool selected, Color accent)
    {
        UiKit.Box(canvas, CloseRect, selected ? new Color(accent, 0.15f) : new Color("1c2939"), 6,
            selected ? accent : new Color("405063"), 1);
        Color color = selected ? Paper : Muted;
        Vector2 center = CloseRect.GetCenter();
        canvas.DrawLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), color, 1.5f, true);
        canvas.DrawLine(center + new Vector2(6, -6), center + new Vector2(-6, 6), color, 1.5f, true);
    }

    private static void Diamond(CanvasItem canvas, Vector2 center, float radius, Color color)
        => canvas.DrawColoredPolygon(new[] { center + new Vector2(0, -radius), center + new Vector2(radius, 0),
            center + new Vector2(0, radius), center + new Vector2(-radius, 0) }, color);
}
