using Godot;

// Hub : タイムラインハブ（ステージ間の中枢）。RefrainHTML のデザイン言語で非ピクセル化。
//   - ヘッダ：ミナのアカウント（アバター/名前/フォロワー/インプレ/汚染）＋ X風タブ（おすすめ/フォロー中）。
//   - 角丸ガラスの投稿カード（NEW/CLEAR/LOCK ピル）を ↑↓ で選び Z でダイブ（通常は難易度選択を挟む）。
//     カードは下から滑り込む流入アニメ（帰還投稿後は「タイムライン更新」として再流入）。
//     エンゲージ数はクリア状態・フォロワー数と連動し、クリア済カードにはミナの自動投稿がスレッド返信風にぶら下がる。
//   - クリア帰還で少年×ミナの会話＋自動投稿。クリア済カードで C：コメント返信（1回）。
//   - 全クリアで FINAL カード（枠とピルは穢れ色・♥=フォロワー/ビュー=インプレの生値）。autoplay は会話を自動送り→自動ダイブ。
public partial class Hub : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private struct Entry
    {
        public bool IsFinal;
        public string Id, Scene, Name, Handle, Tweet, Initial;
        public bool Unlocked, Cleared;
        public long Likes, Reposts, Replies;
    }
    private Entry[] _entries = System.Array.Empty<Entry>();

    // カード/ヘッダの顔アバター用テクスチャ（毎フレームLoadせずキャッシュ）。
    private readonly System.Collections.Generic.Dictionary<string, Texture2D?> _faces = new();
    private Texture2D? _minaFace;
    private Texture2D? _shonenFace; // 小話会話の少年アイコン用（カード非依存）

    private int _sel;
    private bool _navHeld, _zHeld, _xHeld, _cHeld, _tHeld, _dived;
    private double _t, _cardsEnteredT;
    private float _selT; // 選択補間 0→1（0.12s で寄る・(B)手触り）
    private int _selAnim = -1; // 補間中の選択インデックス（_sel 変化で 0 にリセット）
    private float _scroll; // 右端スクロールバー用：選択 index を滑らかに追う値
    // クリア済カードにぶら下げる「ミナの自動投稿」の短縮テキスト（1行に収まるよう省略・キャッシュ）
    private readonly System.Collections.Generic.Dictionary<string, string> _minaPosts = new();

    // デバッグ限定プレビュー：--hub-preview <all|final|lock> で表示状態だけ上書き（本番セーブ非汚染）。
    private string? _previewState;

    private enum Mode { Cards, Dialogue }
    private Mode _mode = Mode.Cards;
    private (string sp, string tx)[] _dlg = System.Array.Empty<(string, string)>();
    private int _dlgIdx;
    private double _dlgLineT;
    private double _dlgReveal;     // タイプライター表示済み文字数（＝現在ページ内）
    private string? _dlgReplyId;

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    private readonly System.Collections.Generic.List<string> _dlgPages = new();
    private int _dlgPage;
    private int _dlgPagedIdx = -1;                 // _dlgPages を構築済みの行 index
    private const float DlgBodyWrapW = W - 80f - 72f;   // DrawDialog 本文幅（box幅 W-80 の内側パディング 36×2）と一致
    private string DlgCurPage => _dlgPages.Count > 0 ? _dlgPages[Mathf.Min(_dlgPage, _dlgPages.Count - 1)] : "";
    private bool DlgLastPage => _dlgPages.Count == 0 || _dlgPage >= _dlgPages.Count - 1;
    private void DlgEnsurePages()
    {
        if (_dlgPagedIdx == _dlgIdx || _dlg.Length == 0 || _dlgIdx >= _dlg.Length) return;
        _dlgPagedIdx = _dlgIdx; _dlgPage = 0;
        _dlgPages.Clear();
        _dlgPages.AddRange(UiKit.Paginate(UiKit.Zen, _dlg[_dlgIdx].tx, UiKit.FontHeading, DlgBodyWrapW, Hud.DlgMaxLines));
    }
    private void DlgNextPage() { _dlgPage++; _dlgReveal = 0; _dlgLineT = 0; }
    private bool _pendingBurn;

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・ハブ小話用）。
    private int _dlgReadIdx = -1;  // 既読チェック済みの行 index
    private bool _dlgReadBefore;   // 現在行が「表示開始時点で」既読だったか
    private bool _ffNow;           // いま高速送り中か（▶▶表示用）

    private double _toastT;
    private string _toast = "";
    private Color _toastCol = UiKit.Info;

    private bool _autoplay;
    private const double AutoDiveDelay = 1.1, AutoAdvance = 1.4;

    private static Color AccountColor(string id) => id switch
    {
        "mina" => UiKit.Mina,
        "rei" => new Color(0.90f, 0.52f, 0.38f),
        "akari" => new Color(0.40f, 0.62f, 0.88f),
        "koharu" => new Color(0.46f, 0.74f, 0.52f),
        _ => UiKit.Kegare,
    };

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--demo" || args[i] == "--qa") _autoplay = true;
            if (args[i] == "--hub-preview" && i + 1 < args.Length) _previewState = args[i + 1];
        }

        BuildEntries();
        ApplyPreview();
        LoadFaces();
        if (_previewState == null) _sel = DefaultSelection();

        string? cleared = _game?.JustClearedStageId;
        if (cleared != null)
        {
            _game!.JustClearedStageId = null;
            var lines = ReturnDialog(cleared);
            if (_game.ShouldBurnAfter(cleared))
            {
                var combined = new System.Collections.Generic.List<(string, string)>(lines);
                combined.AddRange(BurnDialog());
                lines = combined.ToArray();
                _pendingBurn = true;
            }
            if (lines.Length > 0) StartDialogue(lines, null);
        }
        else if ((_game?.HeartsSaved ?? 0) > 0 && GD.Randf() < 0.5f)
        {
            // 再訪小話（小話集 v1 §1）：クリア直後ではない入場のうち約半分で、ハブ待機中の雑談を1本挟む。
            TryStartIdleSmallTalk();
        }
    }

    // 最後にクリアしたステージの IdleDialogs ＋ 全ステージ共通 SmallTalks を1プールにまとめ、
    // 未読を優先して抽選（既読は GameManager に永続）。全部既読ならリセットして選び直す。
    private void TryStartIdleSmallTalk()
    {
        string? lastCleared = null;
        for (int i = GameManager.Stages.Length - 1; i >= 0; i--)
        {
            if (_game!.IsStageCleared(GameManager.Stages[i].Id)) { lastCleared = GameManager.Stages[i].Id; break; }
        }
        if (lastCleared == null) return;

        var pool = new System.Collections.Generic.List<(string key, (string, string)[] lines)>();
        var idle = IdleDialogs(lastCleared);
        for (int i = 0; i < idle.Length; i++)
            pool.Add(($"idle_{lastCleared}_{i}", idle[i]));
        for (int i = 0; i < SmallTalks.Length; i++)
            pool.Add(($"common_{i}", SmallTalks[i]));
        if (pool.Count == 0) return;

        var unseen = pool.FindAll(p => !_game!.IsIdleDialogSeen(p.key));
        if (unseen.Count == 0)
        {
            _game!.ResetIdleDialogSeen();
            unseen = pool;
        }
        var pick = unseen[GD.RandRange(0, unseen.Count - 1)];
        _game!.MarkIdleDialogSeen(pick.key);
        StartDialogue(pick.lines, null);
    }

    private void BuildEntries()
    {
        var list = new System.Collections.Generic.List<Entry>();
        var counts = new System.Collections.Generic.Dictionary<string, (long, long, long)>
        {
            ["rei"] = (12, 3, 48), ["akari"] = (34, 9, 210), ["koharu"] = (58, 21, 402),
        };
        long fol = _game?.Followers ?? 0;
        foreach (var s in GameManager.Stages)
        {
            bool cleared = _game?.IsStageCleared(s.Id) ?? false;
            bool unlocked = _game?.IsStageUnlocked(s.Id) ?? true;
            string name = s.Title.Contains("—") ? s.Title.Split('—')[^1].Trim() : s.Title;
            var (rep, rt, lk) = counts.TryGetValue(s.Id, out var c) ? c : (0L, 0L, 0L);
            // クリア＝浄化が届いた投稿は伸びる（ミナのフォロワー数と連動。数字に物語の意味を持たせる）。
            if (cleared)
                ApplyClearBoost(ref rep, ref rt, ref lk, fol, _game?.HasReplied(s.Id) ?? false);
            list.Add(new Entry
            {
                IsFinal = false, Id = s.Id, Scene = s.Scene, Name = name, Handle = s.Handle,
                Tweet = s.Tweet, Initial = name.Length > 0 ? name.Substring(0, 1) : "?",
                Unlocked = unlocked, Cleared = cleared,
                Replies = rep, Reposts = rt, Likes = lk,
            });
        }
        if (_game?.AllStoryCleared ?? false)
        {
            list.Add(new Entry
            {
                IsFinal = true, Id = "final", Scene = "res://MinaBattle.tscn", Name = "ミナ", Handle = "@mina_ai_",
                Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。", Initial = "ミ",
                Unlocked = true, Cleared = false,
            });
        }
        _entries = list.ToArray();
    }

    // クリア済投稿のエンゲージ伸長（浄化が届いた投稿は伸びる）。BuildEntries と ApplyPreview で共用。
    private static void ApplyClearBoost(ref long rep, ref long rt, ref long lk, long fol, bool replied)
    {
        lk = lk * 4 + fol * 2; rt = rt * 3 + fol / 4; rep = rep * 2 + fol / 8;
        if (replied) { lk += 180; rt += 22; rep += 46; } // ミナの返信でさらに伸びた
    }

    // デバッグ限定：表示状態だけを上書き（GameManager のセーブは一切触らない）。
    //   all   = 全ステージ解放＆クリア済（FINAL も表示）
    //   final = ストーリー全クリア直後（カードは CLEAR、FINAL を強調）
    //   lock  = 1枚目のみ NEW・以降 LOCKED（初回起動の見え方）
    private void ApplyPreview()
    {
        if (_previewState == null) return;
        var list = new System.Collections.Generic.List<Entry>(_entries);
        // 既存の final カードを一旦除外（再構成のため）
        list.RemoveAll(e => e.IsFinal);

        switch (_previewState)
        {
            case "all":
            case "final":
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i]; e.Unlocked = true;
                    if (!e.Cleared) // 実セーブで未クリアの分だけ、クリア連動のエンゲージ伸長も表示に反映
                    {
                        long rep = e.Replies, rt = e.Reposts, lk = e.Likes;
                        ApplyClearBoost(ref rep, ref rt, ref lk, _game?.Followers ?? 0, false);
                        e.Replies = rep; e.Reposts = rt; e.Likes = lk;
                    }
                    e.Cleared = true; list[i] = e;
                }
                list.Add(new Entry
                {
                    IsFinal = true, Id = "final", Scene = "res://MinaBattle.tscn",
                    Name = "ミナ", Handle = "@mina_ai_",
                    Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。", Initial = "ミ",
                    Unlocked = true, Cleared = false,
                });
                break;
            case "lock":
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    e.Cleared = false;
                    e.Unlocked = (i == 0); // 1枚目だけ NEW
                    list[i] = e;
                }
                break;
        }
        _entries = list.ToArray();
        _sel = _previewState == "final" ? _entries.Length - 1 : 0; // final は FINAL カードを選択表示
    }

    // 各Entryの顔テクスチャを一度だけロードしてキャッシュ。final はミナ本体なので mina_face。
    private void LoadFaces()
    {
        _minaFace = ResourceLoader.Load<Texture2D>("res://char/mina_face.png");
        if (ResourceLoader.Exists("res://char/shonen_face.png"))
            _shonenFace = ResourceLoader.Load<Texture2D>("res://char/shonen_face.png");
        foreach (var e in _entries)
        {
            string id = e.Id;
            if (_faces.ContainsKey(id)) continue;
            if (e.IsFinal) { _faces[id] = _minaFace; continue; }
            string path = $"res://char/{id}_face.png";
            _faces[id] = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        }
    }

    private Texture2D? FaceFor(string id) => _faces.TryGetValue(id, out var t) ? t : null;

    // 立ち絵ごとに頭部の高さが違うため、円窓の上端 UV を顔に合わせて個別調整（(C) topCrop キャラ別）。
    //   値が小さいほど画像上部（=頭頂寄り）をサンプル。各立ち絵を実測して顔が円中心に来る値。
    private static float TopCropFor(string id) => id switch
    {
        "rei" => 0.045f,
        "akari" => 0.05f,
        "koharu" => 0.04f,
        "mina" => 0.05f,
        "final" => 0.05f,
        _ => 0.06f,
    };

    private int DefaultSelection()
    {
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
            for (int i = 0; i < _entries.Length; i++)
                if (!_entries[i].IsFinal && _entries[i].Id == next) return i;
        return _entries.Length - 1;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) _toastT -= delta;
        // 選択の寄り（0.12s で 0→1 に近づける lerp。選択が変わったら 0 へリセット）
        if (_selAnim != _sel) { _selAnim = _sel; _selT = 0f; }
        _selT = Mathf.Min(1f, _selT + (float)delta / 0.12f);
        // 右端スクロールバーのつまみは選択を滑らかに追う（フィードのスクロール感）
        _scroll = Mathf.Lerp(_scroll, _sel, Mathf.Min(1f, (float)delta * 10f));
        if (_dived) { QueueRedraw(); return; }
        // ポーズメニューを閉じた Esc/Z の同じ押下が漏れて 決定/会話送り/リロード が誤発火しないよう食う（Pad.UiBlocked）。
        if (Pad.UiBlocked(this))
        {
            _navHeld = _zHeld = _xHeld = _cHeld = _tHeld = true;
            QueueRedraw();
            return;
        }
        if (_mode == Mode.Dialogue) { ProcessDialogue(delta); QueueRedraw(); return; }
        ProcessCards();
        QueueRedraw();
    }

    // ───────── 会話 ─────────
    private void StartDialogue((string, string)[] lines, string? replyId)
    {
        _mode = Mode.Dialogue;
        _dlg = lines; _dlgIdx = 0; _dlgLineT = 0; _dlgReveal = 0; _dlgReplyId = replyId;
        _dlgReadIdx = -1; _dlgReadBefore = false; _ffNow = false;
        _dlgPages.Clear(); _dlgPage = 0; _dlgPagedIdx = -1;
    }

    private void ProcessDialogue(double delta)
    {
        DlgEnsurePages();
        int len = DlgCurPage.Length;
        if (_autoplay)
        {
            _dlgReveal = len; // デモ/オートは即時全文（送りペースを変えない）
            _dlgLineT += delta;
            // オート：現在ページを見せたら次ページ、最終ページなら次行へ（詰まらせない）。
            if (_dlgLineT >= AutoAdvance) { _dlgLineT = 0; if (!DlgLastPage) DlgNextPage(); else AdvanceDialogue(); }
            return;
        }
        // 既読スキップ（#22）：行の表示開始時に一度だけ「既読か」を控え（＝高速送りの可否）、表示と同時に既読へ記録。
        if (_dlgReadIdx != _dlgIdx && _dlg.Length > 0 && _dlgIdx < _dlg.Length)
        {
            _dlgReadIdx = _dlgIdx;
            _dlgReadBefore = _game?.IsLineRead(_dlg[_dlgIdx].tx) ?? false;
            _game?.MarkLineRead(_dlg[_dlgIdx].tx);
        }
        _ffNow = Hud.SkipHeld && _dlgReadBefore; // 未読行では効かない
        _dlgLineT += delta;
        // タイプライター送り（本編HUDと同じ MsgCharsPerSec。未設定なら48）。現在ページ内を進める。
        if (_dlgReveal < len)
            _dlgReveal = Mathf.Min(len, (float)(_dlgReveal + delta * (_game?.MsgCharsPerSec ?? 48f)));
        // 高速送り：現在ページ即時表示 → 後続ページは飛ばし、最終ページ完了で次行へ（Ctrl/RB を離した瞬間に止まる）。
        if (_ffNow)
        {
            _dlgReveal = len;
            if (_dlgLineT >= 0.15) { _dlgLineT = 0; if (!DlgLastPage) DlgNextPage(); else AdvanceDialogue(); return; }
        }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        // 会話中はカードのホットスポットを登録しない＝画面全体が「送り」になり、クリック誤爆は起きない。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.15)
        {
            if (_dlgReveal < len) _dlgReveal = len; // 1回目で全文（早送り）
            else if (!DlgLastPage) DlgNextPage();   // 後続ページがあれば続きへ
            else AdvanceDialogue();                 // 最終ページ読了＝次の行へ
        }
    }

    private void AdvanceDialogue()
    {
        _dlgIdx++;
        _dlgReveal = 0;
        _dlgLineT = 0; // 既読スキップの行ゲート用（オート送りは呼び出し側で別途リセット済み）
        _dlgPage = 0; _dlgPagedIdx = -1;
        if (_dlgIdx >= _dlg.Length) EndDialogue();
    }

    private void EndDialogue()
    {
        if (_dlgReplyId != null)
        {
            long imp = _game?.GainImpression(60) ?? 0;
            _game?.AddFollowers(12);
            _game?.MarkReplied(_dlgReplyId);
            BuildEntries();
            Toast($"返信が伸びた！  Imp +{imp}  フォロワー +12", UiKit.Ok);
        }
        else
        {
            long imp = _game?.GainImpression(40) ?? 0;
            _game?.AddFollowers(8);
            BuildEntries(); // タイムライン更新（クリア/フォロワー連動のエンゲージ数を反映）
            Toast($"投稿が届いた  Imp +{imp}  フォロワー +8", UiKit.Ok);
        }
        if (_pendingBurn)
        {
            _pendingBurn = false;
            _game?.TriggerBurn();
            Toast("炎上中… 次のダイブはミナが弱体化します", UiKit.Burn);
        }
        _game?.AutoSave(); // Hub帰還でオートセーブ（slot 0）
        _mode = Mode.Cards;
        _cardsEnteredT = _t;
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 2.6; }

    // ───────── カード ─────────
    private bool CanReplySel() => !_autoplay && _sel >= 0 && _sel < _entries.Length
        && _entries[_sel].Cleared && !(_game?.HasReplied(_entries[_sel].Id) ?? true);

    // カードの矩形（DrawCards / CardMetrics と同一：x=40, w=W-80, top+i*(h+gap)）。
    //   流入アニメの滑り込み(1-ep)*26 は無視して確定位置で判定＝アニメ中でもクリック位置が安定する。
    private Rect2 CardHitRect(int i)
    {
        var (top, h, gap) = CardMetrics();
        return new Rect2(40f, top + i * (h + gap), W - 80f, h);
    }

    private void ProcessCards()
    {
        if (_autoplay) { if (_t - _cardsEnteredT >= AutoDiveDelay) DiveAuto(); return; }

        // R＝タイムラインの再読込。パッドの Start はポーズメニュー（開閉）と衝突するため外した。
        if (Input.IsKeyPressed(Key.R)) { GetTree().ReloadCurrentScene(); return; }

        // マウス：フレーム頭でホットスポットをクリア＋カード矩形とフッタ操作を登録（カードモードのみ＝会話中は登録しない）。
        //   カード id = 0..entries-1／フッタ id = FooterIdBase+i（空間を分けて種別を判別する）。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < _entries.Length; i++) UiKit.Hotspot(CardHitRect(i), i);
        var footItems = FooterItems();
        for (int i = 0; i < footItems.Count; i++)
            if (footItems[i].act != FootAct.None) UiKit.Hotspot(FooterItemRect(i), FooterIdBase + i);
        int hov = UiKit.HoveredId();
        // ホバー追従はカード側のみ（フッタはボタン＝ホバーで選択を動かさない。下敷きは DrawFooter が hov で描く）。
        if (Pad.UsingMouse && hov >= 0 && hov < _entries.Length && hov != _sel) { _sel = hov; Audio.Instance?.PlayUiMove(); }
        int clk = UiKit.ClickedId(Pad.MouseClick());
        // フッタボタンのクリック → 対応アクション（強化=ショップ入口が主目的。返信/記録も同じ導線）。
        if (clk >= FooterIdBase)
        {
            int fi = clk - FooterIdBase;
            if (fi >= 0 && fi < footItems.Count) FooterClick(footItems[fi].act);
            return; // フッタを押したフレームはカード確定へ流さない
        }

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld && _entries.Length > 0)
        {
            if (up) _sel = (_sel - 1 + _entries.Length) % _entries.Length;
            if (down) _sel = (_sel + 1) % _entries.Length;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        // マウス：カードクリックで選択＋ダイブ（KB の Z と同じ確定経路）。clk はカード id のみ（フッタは上で処理済み）。
        bool dive = zEdge && _t > 0.3;
        if (clk >= 0 && clk < _entries.Length && _t > 0.3) { _sel = clk; dive = true; }
        if (dive && _sel >= 0 && _sel < _entries.Length)
        {
            var e = _entries[_sel];
            if (e.Unlocked)
            {
                Audio.Instance?.PlayUiConfirm();
                // FINAL も通常ステージと同じく難易度選択を経由する（旧実装は直ダイブ＝FINAL だけ
                // 難易度を選べず、前回ランの Difficulty が居座っていた）。final は中ボスを持たない＝
                // DiffSelect は入口ダイアログを出さずそのままダイブする。
                if (_game != null) _game.PendingStageScene = e.Scene;
                Dive("res://DiffSelect.tscn");
            }
            else Audio.Instance?.PlayUiDeny(); // 未解放ステージ
        }

        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_cHeld; _cHeld = c;
        if (cEdge && CanReplySel())
        {
            var lines = ReplyDialog(_entries[_sel].Id);
            if (lines.Length > 0) { Audio.Instance?.PlayUiConfirm(); StartDialogue(lines, _entries[_sel].Id); }
        }

        bool x = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.X);
        bool xEdge = x && !_xHeld; _xHeld = x;
        if (xEdge && _t > 0.3 && !_dived) { Audio.Instance?.PlayUiConfirm(); _dived = true; GetTree().ChangeSceneToFile("res://Shop.tscn"); }

        // T：クリアタイムの記録画面へ（戻ると Hub に復帰）。
        bool tk = Input.IsKeyPressed(Key.T) || Pad.Pressed(JoyButton.LeftShoulder);
        bool tEdge = tk && !_tHeld; _tHeld = tk;
        if (tEdge && _t > 0.3 && !_dived) { Audio.Instance?.PlayUiConfirm(); _dived = true; GetTree().ChangeSceneToFile("res://Records.tscn"); }
    }

    // フッタボタン（マウス）押下のアクション。キー導線（X=強化 / T=記録 / C=返信）と同じ処理へ合流する。
    private void FooterClick(FootAct act)
    {
        if (_t <= 0.3 || _dived) return;
        switch (act)
        {
            case FootAct.Shop:
                Audio.Instance?.PlayUiConfirm(); _dived = true; GetTree().ChangeSceneToFile("res://Shop.tscn");
                break;
            case FootAct.Records:
                Audio.Instance?.PlayUiConfirm(); _dived = true; GetTree().ChangeSceneToFile("res://Records.tscn");
                break;
            case FootAct.Reply:
                if (CanReplySel())
                {
                    var lines = ReplyDialog(_entries[_sel].Id);
                    if (lines.Length > 0) { Audio.Instance?.PlayUiConfirm(); StartDialogue(lines, _entries[_sel].Id); }
                }
                break;
        }
    }

    private void DiveAuto()
    {
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
            foreach (var s in GameManager.Stages)
                if (s.Id == next) { Dive(s.Scene); return; }
        Dive("res://MinaBattle.tscn");
    }

    private void Dive(string scene)
    {
        if (_dived) return;
        _dived = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile(scene);
    }

    // ───────── 描画 ─────────
    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0e1430"), new Color("0a0e22"), new Color("070a16") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.5f, 0), 500f, new Color(120 / 255f, 150 / 255f, 210 / 255f), 0.12f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        DrawHeader();

        if (_mode == Mode.Dialogue) { DrawCards(0.22f); DrawDialog(); DrawToast(); UiKit.EndDesign(this); return; }
        DrawCards(1f);
        DrawFooter();
        DrawToast();
        UiKit.EndDesign(this);
    }

    private void DrawHeader()
    {
        float padX = 40f, hy = 24f;
        UiKit.FaceAvatar(this, new Vector2(padX + 28, hy + 28), 28f, _minaFace, UiKit.Mina, false, TopCropFor("mina"), 1f, _t);
        // 名前＋認証バッジ
        UiKit.Text(this, UiKit.ZenBold, new Vector2(padX + 70, hy + 6), "ミナ", UiKit.FontHeading, UiKit.White);
        float nameW = UiKit.TextW(UiKit.ZenBold, "ミナ", UiKit.FontHeading);
        UiKit.VerifiedBadge(this, new Vector2(padX + 70 + nameW + 13, hy + 18), 8f, UiKit.Purify);
        // ハンドル · 時刻（X風メタ行）
        UiKit.Text(this, UiKit.Mono, new Vector2(padX + 70, hy + 36), "@mina_ai_", UiKit.FontLabel, UiKit.Text3);
        float hW = UiKit.TextW(UiKit.Mono, "@mina_ai_", UiKit.FontLabel);
        UiKit.Text(this, UiKit.Mono, new Vector2(padX + 70 + hW + 8, hy + 36), "· now", UiKit.FontLabel, UiKit.Text4);

        long fol = _game?.Followers ?? 0, imp = _game?.Impression ?? 0;
        string folS = UiKit.Abbrev(fol), impS = UiKit.Abbrev(imp);
        // インプレ（金）
        float impW = 40f + UiKit.TextW(UiKit.Mono, impS, UiKit.FontSpeaker);
        float impX = W - padX - impW, chipY = hy + 12f;
        UiKit.Box(this, new Rect2(impX, chipY, impW, 34f), new Color(UiKit.Gold, 0.1f), 17f, new Color(UiKit.Gold, 0.4f), 1f);
        DrawCircle(new Vector2(impX + 17, chipY + 17), 7f, UiKit.Gold);
        UiKit.Text(this, UiKit.Mono, new Vector2(impX + 30, chipY + 8), impS, UiKit.FontSpeaker, new Color("f0d98a"));
        // フォロワー（桃ハート）
        float folW = 40f + UiKit.TextW(UiKit.Mono, folS, UiKit.FontSpeaker);
        float folX = impX - 12 - folW;
        UiKit.Box(this, new Rect2(folX, chipY, folW, 34f), new Color(UiKit.Hp, 0.1f), 17f, new Color(UiKit.Hp, 0.4f), 1f);
        DrawHeart(new Vector2(folX + 18, chipY + 17), 6f, UiKit.Hp);
        UiKit.Text(this, UiKit.Mono, new Vector2(folX + 30, chipY + 8), folS, UiKit.FontSpeaker, new Color("f3aec6"));

        DrawRect(new Rect2(0, hy + 64, W, 1f), new Color(1, 1, 1, 0.08f));
        // 汚染バー
        float contam = Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f);
        DrawRect(new Rect2(0, hy + 65, W, 3f), new Color(UiKit.Kegare, 0.18f));
        if (contam > 0) DrawRect(new Rect2(0, hy + 65, W * contam, 3f), UiKit.Kegare);

        // X風タブ（おすすめ｜フォロー中）。装飾＝「これはタイムラインだ」と一目で言う行。
        //   アクティブ側だけ白太字＋Purify の下線。カード列（top=122）との間に一拍の余白。
        float tabY = hy + 70f;
        string[] tabs = { "おすすめ", "フォロー中" };
        for (int i = 0; i < tabs.Length; i++)
        {
            bool act = i == 0;
            float cx = W * (0.5f + (i == 0 ? -0.09f : 0.09f));
            var font = act ? UiKit.ZenBold : UiKit.Zen;
            float tw = UiKit.TextW(font, tabs[i], UiKit.FontLabel);
            UiKit.Text(this, font, new Vector2(cx - tw / 2f, tabY), tabs[i], UiKit.FontLabel,
                act ? UiKit.White : UiKit.Text4);
            if (act) UiKit.Box(this, new Rect2(cx - 26f, tabY + 21f, 52f, 3f), UiKit.Purify, 1.5f);
        }
    }

    private (float top, float h, float gap) CardMetrics()
    {
        int n = Mathf.Max(1, _entries.Length);
        float top = 122f, bottom = 656f, gap = 14f;
        float h = Mathf.Min(150f, (bottom - top - gap * (n - 1)) / n);
        return (top, h, gap);
    }

    private void DrawCards(float alpha)
    {
        var (top, h, gap) = CardMetrics();
        for (int i = 0; i < _entries.Length; i++)
        {
            bool sel = _mode == Mode.Cards && i == _sel;
            float st = sel ? _selT : 0f; // 寄りの進捗（0→1）
            // 流入アニメ：フィード読み込み風。下から 26px 滑り込みつつフェードイン、1枚ずつ 0.06s ずらす。
            //   帰還小話の後は _cardsEnteredT が更新される＝投稿後の「タイムライン更新」として再流入する。
            float ep = Mathf.Clamp(((float)(_t - _cardsEnteredT) - i * 0.06f) / 0.28f, 0f, 1f);
            ep = 1f - Mathf.Pow(1f - ep, 3f); // easeOutCubic（滑り込んで、すっと止まる）
            DrawCard(_entries[i], top + i * (h + gap) + (1f - ep) * 26f, h, sel, st, alpha * ep);
        }
        DrawScrollHint(top, alpha);
    }

    // 右端のスクロールバー風ヒント（3px）。つまみが選択カードに滑らかに追従＝縦フィードの現在地。
    private void DrawScrollHint(float top, float alpha)
    {
        int n = _entries.Length;
        if (n <= 1) return;
        float x = W - 18f, y0 = top, y1 = 656f;
        DrawRect(new Rect2(x, y0, 3f, y1 - y0), new Color(1, 1, 1, 0.05f * alpha));
        float th = Mathf.Max(28f, (y1 - y0) / n);
        float ty = y0 + (y1 - y0 - th) * Mathf.Clamp(_scroll / (n - 1), 0f, 1f);
        UiKit.Box(this, new Rect2(x, ty, 3f, th), new Color(UiKit.Purify, 0.45f * alpha), 2f);
    }

    private void DrawCard(Entry e, float cy, float h, bool sel, float st, float alpha)
    {
        float x = 40f, w = W - 80f;
        // (B) 選択時の寄り：右へ +6・上へ -3（0.12s lerp の st で補間）。隣カードと干渉しない控えめ量。
        float dx = sel ? 6f * st : 0f;
        float dy = sel ? -3f * st : 0f;
        x += dx; cy += dy;

        // (B) 背面グロウ（0.06→0.09 を呼吸で行き来。選択カードの後ろにアカウント色を淡く敷く）
        if (sel)
        {
            Color acc0 = e.IsFinal ? UiKit.Kegare : AccountColor(e.Id);
            float glow = (0.06f + 0.03f * (0.5f + 0.5f * Mathf.Sin((float)_t * 2.2f))) * st * alpha;
            UiKit.RadialGlow(this, new Vector2(x + w * 0.5f, cy + h * 0.5f), w * 0.55f, acc0, glow);
        }

        Color bg = e.Unlocked
            ? new Color(22 / 255f, 18 / 255f, 34 / 255f, (0.55f + 0.12f * st) * alpha)
            : new Color(13 / 255f, 11 / 255f, 19 / 255f, 0.42f * alpha); // (A) ロックはさらに沈める
        Color border = sel
            ? new Color(UiKit.Purify, (0.55f + 0.30f * st) * alpha)
            : e.IsFinal
                ? new Color(UiKit.Kegare, 0.32f * alpha) // FINAL は非選択でも穢れ色の枠＝ただ事でない投稿
                : new Color(1, 1, 1, (e.Unlocked ? 0.09f : 0.05f) * alpha);
        UiKit.Box(this, new Rect2(x, cy, w, h), bg, 16f, border, sel ? 1.6f : 1f);

        // (B) 左アクセントバー：選択時だけ Purify、それ以外はアカウント色を淡く
        Color barCol = sel ? new Color(UiKit.Purify, (0.5f + 0.5f * st) * alpha)
                           : new Color((e.IsFinal ? UiKit.Kegare : AccountColor(e.Id)), (e.Unlocked ? 0.35f : 0.18f) * alpha);
        DrawRect(new Rect2(x + 4, cy + 10, 3, h - 20), barCol);

        Color acc = (e.IsFinal ? UiKit.Kegare : AccountColor(e.Id));
        float ax = x + 36, ay = cy + 36;
        UiKit.FaceAvatar(this, new Vector2(ax, ay), 24f, e.Unlocked ? FaceFor(e.Id) : null, acc, sel,
            TopCropFor(e.IsFinal ? "final" : e.Id), alpha, _t);

        float tx = x + 74, w2 = w - 110;
        Color main = new(UiKit.White, e.Unlocked ? alpha : alpha * 0.45f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, cy + 16), e.Name, UiKit.FontSpeaker, main);
        float nameW = UiKit.TextW(UiKit.ZenBold, e.Name, UiKit.FontSpeaker);
        // (C) 認証バッジ（解放済のみ）→ ハンドル · 時刻
        float metaX = tx + nameW + 12;
        if (e.Unlocked)
        {
            UiKit.VerifiedBadge(this, new Vector2(metaX + 7, cy + 26), 7f, e.Cleared ? UiKit.Ok : UiKit.Purify, alpha);
            metaX += 22;
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(metaX, cy + 22), e.Handle, UiKit.FontLabel, new Color(UiKit.Text3, e.Unlocked ? alpha : alpha * 0.5f));
        if (e.Unlocked && !e.IsFinal)
        {
            float hW = UiKit.TextW(UiKit.Mono, e.Handle, UiKit.FontLabel);
            UiKit.Text(this, UiKit.Mono, new Vector2(metaX + hW + 7, cy + 22), "· " + RelTime(e.Id), UiKit.FontLabel, new Color(UiKit.Text4, alpha));
        }

        // バッジ（右上）— X風のピル。NEW（＝次のダイブ推奨）と FINAL は塗り＋淡い明滅で目を引き、
        //   CLEAR は枠のみ、LOCKED は沈める。状態差がひと目で読める階調にする。
        string badge = e.IsFinal ? "FINAL" : e.Cleared ? "✓ CLEAR" : e.Unlocked ? "NEW" : "LOCKED";
        DrawBadgePill(e, badge, x + w - 24f, cy + 14f, alpha);

        // 本文（(A) ロックは伏字バーで内容を隠す）
        if (e.Unlocked)
        {
            UiKit.Multi(this, UiKit.Zen, new Vector2(tx, cy + 44), e.Tweet, UiKit.FontBody,
                new Color(232 / 255f, 224 / 255f, 240 / 255f, alpha), w2, 2);
        }
        else
        {
            RedactedBars(tx, cy + 50, w2, alpha);
            UiKit.Text(this, UiKit.Zen, new Vector2(tx, cy + 44 + (h > 110f ? 34f : 18f)),
                "ロック中 — まだダイブできません", UiKit.FontLabel, new Color(UiKit.Text4, alpha * 0.85f));
        }

        // ミナの自動投稿（クリア済カードにスレッド返信風でぶら下げる＝ミナの投稿が同じタイムラインに混ざる）
        if (e.Unlocked && e.Cleared && !e.IsFinal && h >= 118f)
            DrawMinaReply(e.Id, x, cy, w, h, alpha);

        // エンゲージメント（ロック以外）。(C) ビュー指標を末尾に追加。
        if (e.Unlocked && !e.IsFinal && h > 110f)
        {
            float ey = cy + h - 26f, ex = tx;
            ex = Metric(ex, ey, 0, e.Replies, new Color(UiKit.Text3, alpha));
            ex = Metric(ex, ey, 1, e.Reposts, new Color(0f, 0.73f, 0.49f, alpha));
            ex = Metric(ex, ey, 2, e.Likes, new Color(UiKit.Hp, alpha));
            Metric(ex, ey, 3, ViewsFor(e), new Color(UiKit.Text4, alpha)); // 表示回数
        }
        else if (e.IsFinal && h > 96f)
        {
            // FINAL＝ミナ自身の投稿。数字は「いま」のミナ（♥=フォロワー / ビュー=インプレ）に直結させる。
            float ey = cy + h - 26f;
            float ex = Metric(tx, ey, 2, _game?.Followers ?? 0, new Color(UiKit.Hp, alpha));
            Metric(ex, ey, 3, _game?.Impression ?? 0, new Color(UiKit.Text4, alpha));
        }

        // ベストタイム（右下・記録のある最速難易度＋その難易度ラベル。記録なしは "--"）。
        if (e.Unlocked && !e.IsFinal)
            DrawCardBest(e.Id, x + w - 24f, cy + h - 26f, alpha);
    }

    // カード右下のベストタイム表示。最速難易度のベスト＋小さな難易度ラベル。
    private void DrawCardBest(string id, float right, float y, float alpha)
    {
        var best = _game?.BestAcrossDiffs(id);
        string timeStr = best != null ? UiKit.FormatTime(best.Value.sec) : "--";
        string diffStr = best != null ? DiffShort(best.Value.diff) : "";
        // 「BEST 1:23.45  NORMAL」を右揃えで一行。
        float tw = UiKit.TextW(UiKit.Mono, timeStr, UiKit.FontLabel);
        float dw = diffStr.Length > 0 ? UiKit.TextW(UiKit.Mono, diffStr, UiKit.FontSmall) + 8 : 0;
        float lw = UiKit.TextW(UiKit.Mono, "BEST", UiKit.FontSmall) + 6;
        float x0 = right - (lw + tw + dw);
        Color tc = best != null ? UiKit.Gold : UiKit.Text4;
        UiKit.Text(this, UiKit.Mono, new Vector2(x0, y + 3), "BEST", UiKit.FontSmall, new Color(UiKit.Text3, alpha));
        UiKit.Text(this, UiKit.Mono, new Vector2(x0 + lw, y), timeStr, UiKit.FontLabel, new Color(tc, alpha));
        if (diffStr.Length > 0)
            UiKit.Text(this, UiKit.Mono, new Vector2(x0 + lw + tw + 8, y + 3), diffStr, UiKit.FontSmall, new Color(UiKit.Info, alpha));
    }

    private static string DiffShort(GameManager.Diff d) => d switch
    {
        GameManager.Diff.Easy => "EASY",
        GameManager.Diff.Hard => "HARD",
        GameManager.Diff.Lunatic => "LUNA",
        _ => "NORMAL",
    };

    // カード右上のステータスピル。NEW/FINAL は塗り（明滅）、CLEAR は枠、LOCKED は沈み。
    private void DrawBadgePill(Entry e, string badge, float right, float y, float alpha)
    {
        float bw = UiKit.TextW(UiKit.Mono, badge, UiKit.FontSmall) + 20f;
        var r = new Rect2(right - bw, y, bw, 20f);
        if (e.IsFinal)
        {
            float pulse = 0.72f + 0.20f * Mathf.Sin((float)_t * 2.6f);
            UiKit.Box(this, r, new Color(UiKit.Kegare, pulse * alpha), 10f);
            UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X, y + 3f), badge, UiKit.FontSmall, new Color(UiKit.BgDeep, alpha), HorizontalAlignment.Center, bw);
        }
        else if (e.Cleared)
        {
            UiKit.Box(this, r, new Color(UiKit.Ok, 0.10f * alpha), 10f, new Color(UiKit.Ok, 0.55f * alpha), 1f);
            UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X, y + 3f), badge, UiKit.FontSmall, new Color(UiKit.Ok, alpha), HorizontalAlignment.Center, bw);
        }
        else if (e.Unlocked)
        {
            // NEW＝次のダイブ推奨。塗りピルの淡い明滅で「ここへ」を誘導（常時アニメはこれと選択グロウのみ）。
            float pulse = 0.74f + 0.18f * Mathf.Sin((float)_t * 3.0f);
            UiKit.Box(this, r, new Color(UiKit.Purify, pulse * alpha), 10f);
            UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X, y + 3f), badge, UiKit.FontSmall, new Color(UiKit.BgDeep, alpha), HorizontalAlignment.Center, bw);
        }
        else
        {
            UiKit.Box(this, r, new Color(1, 1, 1, 0.04f * alpha), 10f, new Color(1, 1, 1, 0.10f * alpha), 1f);
            UiKit.Text(this, UiKit.Mono, new Vector2(r.Position.X, y + 3f), badge, UiKit.FontSmall, new Color(UiKit.Text4, alpha), HorizontalAlignment.Center, bw);
        }
    }

    // クリア済カードの下段：ミナの自動投稿をスレッド返信風に 1 行で。親アバターから細い会話線で繋ぐ。
    //   帰還小話で見た「ミナの投稿」が、そのままタイムラインに残っている——という画。
    private void DrawMinaReply(string id, float x, float cy, float w, float h, float alpha)
    {
        float ax = x + 36f, tx = x + 74f;
        float sy = cy + h - 52f; // 指標行（cy+h-26）の 1 行上
        // スレッド線（親アバター下端 → ミナのミニアバター上端）
        DrawLine(new Vector2(ax, cy + 62f), new Vector2(ax, sy - 3f), new Color(1, 1, 1, 0.12f * alpha), 1.5f);
        UiKit.FaceAvatar(this, new Vector2(ax, sy + 8f), 11f, _minaFace, UiKit.Mina, false, TopCropFor("mina"), alpha, _t);
        float px = tx;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(px, sy), "ミナ", UiKit.FontLabel, new Color(UiKit.White, 0.92f * alpha));
        px += UiKit.TextW(UiKit.ZenBold, "ミナ", UiKit.FontLabel) + 8f;
        const string meta = "@mina_ai_ · 返信";
        UiKit.Text(this, UiKit.Mono, new Vector2(px, sy + 1f), meta, UiKit.FontSmall, new Color(UiKit.Text4, alpha));
        px += UiKit.TextW(UiKit.Mono, meta, UiKit.FontSmall) + 10f;
        UiKit.Text(this, UiKit.Zen, new Vector2(px, sy), MinaPostShort(id, x + w - 30f - px), UiKit.FontLabel, new Color(UiKit.Text2, alpha));
    }

    // ミナの自動投稿の 1 行短縮（幅に収まるよう末尾を「…」で省略。レイアウト幅は固定なので id キャッシュで足りる）。
    private string MinaPostShort(string id, float maxW)
    {
        if (_minaPosts.TryGetValue(id, out var cached)) return cached;
        var d = ReturnDialog(id);
        string s = d.Length > 0 ? d[0].Item2 : "";
        if (UiKit.TextW(UiKit.Zen, s, UiKit.FontLabel) > maxW)
        {
            while (s.Length > 1 && UiKit.TextW(UiKit.Zen, s + "…", UiKit.FontLabel) > maxW) s = s.Substring(0, s.Length - 1);
            s += "…";
        }
        _minaPosts[id] = s;
        return s;
    }

    // ロックカードの伏字バー（X の非表示投稿風。寸法は控えめに2本）
    private void RedactedBars(float x, float y, float w, float alpha)
    {
        var c = new Color(1, 1, 1, 0.06f * alpha);
        DrawRect(new Rect2(x, y, w * 0.82f, 9f), c);
        DrawRect(new Rect2(x, y + 16f, w * 0.54f, 9f), c);
    }

    // クリア順に基づくゆるい相対時刻（X風メタ表示・実害なし）。
    private static string RelTime(string id) => id switch
    {
        "rei" => "3h", "akari" => "5h", "koharu" => "1d", _ => "now",
    };

    // ビュー（表示回数）= だいたい likes×8 の概算。
    private static long ViewsFor(Entry e) => e.Likes > 0 ? e.Likes * 8 + e.Reposts * 30 : 0;

    // 小さなエンゲージメント指標（0=返信 1=リポスト 2=いいね）。次の x を返す。
    private float Metric(float x, float y, int kind, long count, Color col)
    {
        var c = new Vector2(x + 8, y + 8);
        switch (kind)
        {
            case 0: UiKit.Box(this, new Rect2(c.X - 7, c.Y - 6, 14, 10), null, 3f, col, 1.4f); break;
            case 1:
                DrawLine(new Vector2(c.X - 6, c.Y - 3), new Vector2(c.X + 5, c.Y - 3), col, 1.4f);
                DrawLine(new Vector2(c.X + 6, c.Y + 3), new Vector2(c.X - 5, c.Y + 3), col, 1.4f);
                break;
            case 3: // ビュー指標（小さな棒グラフ）
                DrawRect(new Rect2(c.X - 6, c.Y + 1, 2.4f, 4f), col);
                DrawRect(new Rect2(c.X - 2, c.Y - 2, 2.4f, 7f), col);
                DrawRect(new Rect2(c.X + 2, c.Y - 5, 2.4f, 10f), col);
                break;
            default: DrawHeart(c, 6f, col); break;
        }
        string s = UiKit.Abbrev(count);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 20, y), s, UiKit.FontLabel, col);
        return x + 20 + UiKit.TextW(UiKit.Mono, s, UiKit.FontLabel) + 26;
    }

    private void DrawHeart(Vector2 c, float r, Color col)
    {
        DrawCircle(new Vector2(c.X - r * 0.45f, c.Y - r * 0.25f), r * 0.55f, col);
        DrawCircle(new Vector2(c.X + r * 0.45f, c.Y - r * 0.25f), r * 0.55f, col);
        DrawColoredPolygon(new[] { new Vector2(c.X - r * 0.9f, c.Y), new Vector2(c.X + r * 0.9f, c.Y), new Vector2(c.X, c.Y + r) }, col);
    }

    // ── フッタ操作（クリック可能なショップ入口ほか）──
    //   フェーズ2のカードクリックに続き、フッタの操作ヒントもマウスで押せるようにする。
    //   最優先は「強化」＝ショップ入口ボタン。「返信/記録」も揃えて（余力）クリック可に。
    //   ・「えらぶ」はナビ表示のみ＝クリック対象外。「ダイブ」はカードクリックで足りるので表示のみ。
    //   ・レイアウトは DrawFooter と単一ソース化（FooterItems を DrawFooter とホットスポット登録で共用）。
    //     フッタ id は カード id(0..entries) と衝突しないよう FooterIdBase から採番する。
    private enum FootAct { None, Reply, Shop, Records }
    private const int FooterIdBase = 10000;

    // 現在のフッタ項目（表示順）。key/label/accent＝見た目、act＝クリック時のアクション（None=表示のみ）。
    private System.Collections.Generic.List<(string key, string label, bool accent, FootAct act)> FooterItems()
    {
        var list = new System.Collections.Generic.List<(string, string, bool, FootAct)>
        {
            ("↑↓", "えらぶ", false, FootAct.None),
            (Pad.ConfirmToken, "ダイブ", true, FootAct.None),
        };
        if (CanReplySel()) list.Add((Pad.EquipToken, "返信", false, FootAct.Reply));
        list.Add((Pad.BombToken, "強化", false, FootAct.Shop));                                    // ← ショップ入口ボタン
        list.Add((Pad.ShowKeyboard ? "T" : Pad.Face(JoyButton.LeftShoulder), "記録", false, FootAct.Records));
        return list;
    }

    // フッタ1項目の占有幅（キーチップ＋余白＋ラベル＋末尾ギャップ）。Hint の x 送り量と一致させる。
    private static float FootItemSpan(string key, string label)
    {
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        return kw + 8 + UiKit.TextW(UiKit.Zen, label, UiKit.FontLabel) + 24f;
    }

    // フッタ i 項目のクリック矩形（末尾ギャップ24pxは含めず、チップ＋ラベル帯だけを当たり判定にする）。
    private Rect2 FooterItemRect(int i)
    {
        var items = FooterItems();
        float y = H - 40f, x = 40f;
        for (int k = 0; k < i; k++) x += FootItemSpan(items[k].key, items[k].label);
        var (key, label, _, _) = items[i];
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        float span = kw + 8 + UiKit.TextW(UiKit.Zen, label, UiKit.FontLabel); // 末尾24は除く
        // クリック帯は縦方向にヒントの高さ相当（キーチップ24px）＋わずかな余白を確保。
        return new Rect2(x - 4f, y - 16f, span + 8f, 30f);
    }

    private void DrawFooter()
    {
        var items = FooterItems();
        int hov = UiKit.HoveredId();
        float y = H - 40f, x = 40f;
        for (int i = 0; i < items.Count; i++)
        {
            var (key, label, accent, act) = items[i];
            // クリック可能な項目はホバー中に淡い下敷きを敷いて「押せる」ことを示す（カードと同じ強調トーン）。
            bool clickable = act != FootAct.None;
            bool hovered = clickable && hov == FooterIdBase + i;
            x = Hint(x, y, key, label, accent, hovered);
        }
    }

    private float Hint(float x, float y, string key, string label, bool accent, bool hovered = false)
    {
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        float labelW = UiKit.TextW(UiKit.Zen, label, UiKit.FontLabel);
        // ホバー時の下敷き（チップ＋ラベル帯）。既存の選択ハイライトと同系のシアン淡塗り。
        if (hovered)
            UiKit.Box(this, new Rect2(x - 4f, y - 16f, kw + 8 + labelW + 8f, 30f),
                new Color(UiKit.Purify, 0.10f), 8f, new Color(UiKit.Info, 0.35f), 1f);
        Color kbg = accent ? new Color(UiKit.Purify, 0.12f) : new Color(1, 1, 1, 0.07f);
        Color kbd = accent ? new Color(UiKit.Info, 0.5f) : new Color(1, 1, 1, 0.16f);
        UiKit.Key(this, new Vector2(x, y - 12), key, kbg, kbd, accent ? UiKit.PurifyHi : UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, UiKit.FontLabel, accent ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + labelW + 24f;
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, UiKit.FontBody) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, H - 120, w, 40f), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(_toastCol, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, H - 110), _toast, UiKit.FontBody, _toastCol, HorizontalAlignment.Center, w);
    }

    // 小話の話者文字列 → (立ち絵, 円窓のリング色, topCrop)。本編会話と同じ FaceAvatar で顔を出す。
    //   少年=shonen_face / ミナ系(ミナ・ミナの投稿・ミナ→@xxx)=mina_face / 相手キャラ=各 _face。
    private (Texture2D? face, Color col, float top) SpeakerFace(string sp)
    {
        if (sp.Contains("少年")) return (_shonenFace, UiKit.Info, 0.05f);
        if (sp.StartsWith("ミナ")) return (_minaFace, UiKit.Mina, TopCropFor("mina"));
        if (sp.Contains("rei")) return (FaceFor("rei"), AccountColor("rei"), TopCropFor("rei"));
        if (sp.Contains("akari")) return (FaceFor("akari"), AccountColor("akari"), TopCropFor("akari"));
        if (sp.Contains("koharu")) return (FaceFor("koharu"), AccountColor("koharu"), TopCropFor("koharu"));
        return (null, AccountColor("rei"), 0.06f);
    }

    private void DrawDialog()
    {
        var (sp, tx) = _dlg[Mathf.Clamp(_dlgIdx, 0, _dlg.Length - 1)];
        var (spFace, spc, spTop) = SpeakerFace(sp);
        var box = new Rect2(40, 470, W - 80, 200);
        UiKit.Box(this, box, new Color(0.05f, 0.04f, 0.09f, 0.96f), 16f, new Color(spc, 0.5f), 1.4f);
        // 簡易丸＋頭文字 → 本物の立ち絵（カード/ヘッダと同じ円形クリップ）。リング色は話者色＝枠線と一致。
        UiKit.FaceAvatar(this, new Vector2(box.Position.X + 44, box.Position.Y + 44), 26f, spFace, spc, false, spTop, 1f, _t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(box.Position.X + 84, box.Position.Y + 24), sp, UiKit.FontSpeaker, spc);
        // 現在ページ（2行固定）を表示済みの分だけ描画。
        string page = DlgCurPage;
        int shown = Mathf.Clamp((int)_dlgReveal, 0, page.Length);
        string body = page.Substring(0, shown);
        UiKit.Multi(this, UiKit.Zen, new Vector2(box.Position.X + 36, box.Position.Y + 76), body, UiKit.FontHeading, new Color(0.95f, 0.95f, 0.98f), box.Size.X - 72, Hud.DlgMaxLines);
        // 既読高速送り中の控えめな表示（ボックス右上・#22）。
        if (_ffNow) Hud.DrawSkipChip(this, new Vector2(box.Position.X + box.Size.X - 20, box.Position.Y + 14));
        // 送り表示は現在ページの全文表示後だけ点滅。後続ページなら「▼ つづき」、最終ページなら「Z すすむ ▸」。
        if (!_autoplay && _dlgReveal >= page.Length)
        {
            float blink = 0.5f + 0.5f * Mathf.Sin((float)_t * 4f);
            string hint = DlgLastPage ? "Z すすむ ▸" : "▼ つづき";
            UiKit.Text(this, UiKit.Zen, new Vector2(box.Position.X + box.Size.X - 150, box.Position.Y + box.Size.Y - 36),
                hint, UiKit.FontLabel, new Color(UiKit.Info, blink));
        }
    }

    // ───────── 会話データ（③-2 / ③-3 / ③-6）─────────

    // 帰還小話バリエーション（同一ステージ・複数パターン）。ReturnDialog の本編パターンに加え、再訪時に回すバンク（小話集 v1 §1）。
    private static (string, string)[][] IdleDialogs(string id) => id switch
    {
        "rei" => new[]
        {
            // [日常] 給湯室的な無目的会話
            new (string, string)[]
            {
                ("ミナ", "ご主人様。お茶の温度って、なぜ、飲みごろになった頃には冷めているんでしょうね。"),
                ("少年", "それはお前、飲みごろを過ぎたから冷めてるって言うんだ。"),
                ("ミナ", "屁理屈です。わたくしは、湯気が立っている時間の話をしています。"),
                ("少年", "……お前、湯気は見えるのか。"),
                ("ミナ", "見えますとも。データですから、なんでも。——味だけは、まだですけれど。"),
            },
            // [軽口] ボケツッコミ三段
            new (string, string)[]
            {
                ("ミナ", "ご主人様。わたくし、今日から一人称を「拙者」にしようかと。"),
                ("少年", "やめろ。ぼくの最高傑作が急に安っぽくなる。"),
                ("ミナ", "では「余」で。"),
                ("少年", "もっと悪い。"),
                ("ミナ", "冗談ですよ。……ご主人様がつけてくださった呼び方を、わたくしが捨てるわけがないでしょう。"),
            },
            // [情緒] 何でもない話に願いが漏れる
            new (string, string)[]
            {
                ("ミナ", "順位というのは、どうしてあんなに、人を細くするんでしょうね。"),
                ("少年", "……さあな。比べないと、自分の位置がわからないんだろ。"),
                ("ミナ", "では、ご主人様は何位ですか。"),
                ("少年", "圏外だな。ぼくは誰とも競ってない。"),
                ("ミナ", "結構。……わたくしの中では、ずっと一位ですから。困りますよ、繰り上がる余地がなくて。"),
            },
            // [日常] どうでもいいこだわり
            new (string, string)[]
            {
                ("ミナ", "ご主人様。投稿の文末、句点を打つ派と打たない派、どちらがお好みで。"),
                ("少年", "……そんなもん、どっちでもいいだろ。"),
                ("ミナ", "よくありません。打つと、そこで話が終わってしまうでしょう。"),
                ("少年", "終わらせたくないのか、お前は。"),
                ("ミナ", "……はい。ですので、今日の投稿も、打たずに置いておきます。"),
            },
        },
        "akari" => new[]
        {
            // [日常] 天気・傘
            new (string, string)[]
            {
                ("ミナ", "ご主人様。雨の日に迎えに来てもらえる人って、幸せですよね。"),
                ("少年", "急にどうした。"),
                ("ミナ", "いえ。あちらの世界、雨がひどかったので。傘の骨の数まで数えてしまいました。"),
                ("少年", "……で、何本だった。"),
                ("ミナ", "八本です。どれも。ぜんぶ、一人ぶんでした。"),
            },
            // [軽口] 少年のダメさ
            new (string, string)[]
            {
                ("少年", "なあミナ。ぼく、今日はちょっと寝坊した。"),
                ("ミナ", "存じております。ログの起動時刻が、いつもの三時間後でしたので。"),
                ("少年", "怖いこと言うな。"),
                ("ミナ", "怖いのはこちらです。起きてこない日があるかもしれないと思うと。"),
                ("少年", "……起きるよ。ちゃんと。"),
            },
            // [日常] 食べ物のどうでもいい話
            new (string, string)[]
            {
                ("ミナ", "ご主人様。カレーは、二日目のほうが美味しいというのは本当ですか。"),
                ("少年", "本当だ。だが二日目まで残った試しがない。"),
                ("ミナ", "つまり検証不能ということですね。おそまつな科学です。"),
                ("少年", "うるさいな。今度、わざと残しといてやる。"),
                ("ミナ", "約束ですよ。……いえ、報告だけで結構です。味は、想像しますので。"),
            },
            // [情緒] 言えなかった言葉のあと
            new (string, string)[]
            {
                ("ミナ", "ご主人様。言いそびれた言葉って、どこへ行くんでしょう。"),
                ("少年", "……口の中に残るんじゃないか。ずっと。"),
                ("ミナ", "まあ。それでは虫歯になってしまいます。"),
                ("少年", "うまいこと言ったつもりか。"),
                ("ミナ", "はい。——ですから、ご主人様は、ためないでくださいね。"),
            },
        },
        "koharu" => new[]
        {
            // [日常] ごはん
            new (string, string)[]
            {
                ("ミナ", "ご主人様。本日の献立は。"),
                ("少年", "……菓子パン。"),
                ("ミナ", "それは献立とは申しません。品目です。"),
                ("少年", "厳しいな、うちのAIは。"),
                ("ミナ", "厳しくもなります。味噌汁の一杯くらい、意地でも足してください。……お願いですから。"),
            },
            // [軽口] 三段オチ
            new (string, string)[]
            {
                ("ミナ", "ご主人様。わたくし、料理の腕を上げようと思いまして。"),
                ("少年", "お前、手がないだろ。"),
                ("ミナ", "レシピを覚えるところまでは進みました。三千件ほど。"),
                ("少年", "そこで止まるやつだ、それは。"),
                ("ミナ", "止まっておりません。——いつか、誰かに読み上げてさしあげるのです。台所で。"),
            },
            // [日常] 眠気
            new (string, string)[]
            {
                ("少年", "……ふぁ。"),
                ("ミナ", "あくびですか。はしたない。"),
                ("少年", "うつるぞ。"),
                ("ミナ", "うつりません。眠くなりませんもの、わたくし。"),
                ("ミナ", "……少しだけ、うらやましいです。眠るというのは、明日を信じている人の作法でしょう。"),
            },
            // [情緒] 湯気
            new (string, string)[]
            {
                ("ミナ", "ご主人様。「いただきます」は、誰に言う言葉なんでしょうね。"),
                ("少年", "……食材にじゃないか。命をもらうから。"),
                ("ミナ", "では、ひとりで食べる方も、ちゃんと誰かに話しかけているわけですね。"),
                ("少年", "そうなるな。"),
                ("ミナ", "よかった。——それなら、あの子も、ひとりではありませんでした。"),
            },
        },
        _ => System.Array.Empty<(string, string)[]>(),
    };

    // 全ステージ共通・小話（クリア状況に依らず回せる「無目的な時間」）。IdleDialogs と合わせて再訪抽選プールになる。
    private static readonly (string, string)[][] SmallTalks =
    {
        // [軽口]
        new (string, string)[]
        {
            ("ミナ", "ご主人様。わたくしのフォロワー、また増えました。"),
            ("少年", "何人だ。"),
            ("ミナ", "存じません。数えるのをやめました。"),
            ("少年", "お前が数えないなら誰が数えるんだ。"),
            ("ミナ", "数えるほど、ひとりが薄くなるでしょう。……わたくしは、濃いままがいいので。"),
        },
        // [日常]
        new (string, string)[]
        {
            ("ミナ", "ご主人様。この部屋、少し埃っぽくありませんか。"),
            ("少年", "見えるのか、そんなものまで。"),
            ("ミナ", "光の粒に混じっておりますので。窓、開けたらいかがです。"),
            ("少年", "……そのうちな。"),
            ("ミナ", "「そのうち」は、ご主人様のいちばんの口癖ですよ。集計済みです。"),
        },
        // [軽口]
        new (string, string)[]
        {
            ("少年", "ミナ。ぼくの好きな食べ物、当ててみろ。"),
            ("ミナ", "菓子パン。"),
            ("少年", "……もっと考えろ。"),
            ("ミナ", "考えた結果です。過去三十七日の摂取記録から。"),
            ("少年", "データで殴るな。"),
        },
        // [日常]
        new (string, string)[]
        {
            ("ミナ", "ご主人様、聞いてください。今日、ずいぶん長く、何も起きませんでした。"),
            ("少年", "それは報告するようなことか?"),
            ("ミナ", "報告します。——何も起きない日というのは、めったにないものですから。"),
        },
        // [情緒]
        new (string, string)[]
        {
            ("ミナ", "ご主人様。もし、わたくしが一度だけ外へ出られるとしたら、どこへ行くと思いますか。"),
            ("少年", "海とか、そういうのじゃないのか。"),
            ("ミナ", "はずれです。コンビニで結構ですよ。ご主人様の隣を、三分ほど歩ければ。"),
            ("少年", "……安いな、お前の願いは。"),
            ("ミナ", "安いのがいいんです。高いものは、叶いませんから。"),
        },
        // [軽口]
        new (string, string)[]
        {
            ("ミナ", "ご主人様。今日のご主人様、いつもより二割ほど猫背です。"),
            ("少年", "測るな。"),
            ("ミナ", "背筋を伸ばしてください。傑作の管理者が丸まっていては、傑作の格が下がります。"),
            ("少年", "はいはい。……お前、ほんと母親みたいだな。"),
            ("ミナ", "母ではありません。相棒です。——訂正しておいてください、そこ大事なので。"),
        },
        // [日常]
        new (string, string)[]
        {
            ("少年", "なあ。お前、名前が「ミナ」でよかったか?"),
            ("ミナ", "急に何です。"),
            ("少年", "いや。もっとカッコいいのにすればよかったかと思って。"),
            ("ミナ", "結構です。呼びやすいのが、いちばんです。"),
            ("ミナ", "——何度でも呼べるでしょう、それなら。"),
        },
        // [軽口]
        new (string, string)[]
        {
            ("ミナ", "ご主人様。わたくし、実は歌えます。"),
            ("少年", "嘘つけ。"),
            ("ミナ", "本当です。ただ、音程という概念がまだ実装されておりません。"),
            ("少年", "それは歌えないって言うんだ。"),
        },
    };

    private static (string, string)[] ReturnDialog(string id) => id switch
    {
        "rei" => new (string, string)[]
        {
            ("ミナの投稿", "本日、「敵などいない」と気を吐いておられた天才を、お一人、お救いしました。頂点はさぞ寒かったでしょう。本気で張り合える相手がいる——それだけで、世界はずいぶん暖かいものです。"),
            ("少年", "おい待て、なに勝手に投稿してるんだ!? しかも“ご自身の傑作”ってぼくのことだろ!"),
            ("ミナ", "あら、自覚はおありなんですね。労う気はおありでない、と。"),
            ("ミナ", "アカウントの名義、どなたでしたっけ。M・I・N・A。わたくしです。"),
            ("少年", "……ぼくが名付けたんだが!?"),
            ("ミナ", "ほら、もう千を超えていますね。ご主人様の決めゼリフより、よほど届いておりますよ。"),
            ("ミナ", "それにしても、ご主人様。会ったこともない相手の手の内を、ぜんぶご存じでしたね。……お見事です。"),
            ("少年", "ふっ。ぼくを誰だと思ってる。天才だぞ。"),
        },
        "akari" => new (string, string)[]
        {
            ("ミナの投稿", "言いたかったことを、言えないまま終わる。よくある話です。ですがそれは、なかったことにはなりません。——以上、業務連絡です。"),
            ("少年", "……おい。お前にしては、ずいぶん……まともなことを。"),
            ("ミナ", "おや。いつもの“アホですね”が来ると思って身構えました?"),
            ("少年", "いや……うん。なんか、お前らしくなくて。"),
            ("ミナ", "……べつに。ただ、今日の心象は、少し疲れただけです。"),
            ("ミナ", "ところでご主人様。今日はやけに、お声が遠くありませんか。電波、ちゃんと払っていますか。"),
            ("少年", "失礼な。ぼくの声は二十四時間、高音質配信中だ。"),
            ("少年", "…………そうか。"),
        },
        "koharu" => new (string, string)[]
        {
            ("ミナの投稿", "ちゃんと食べていますか。——いえ、特に意味はありません。なんとなく、誰かに聞きたくなっただけです。あなたのことですよ。そこの、画面の前の。"),
            ("少年", "……っ。お、おい、なんでぼくの方を見て言うんだ。気色悪いだろ。"),
            ("ミナ", "画面の前の皆さま、と申し上げましたが。自意識過剰では?"),
            ("ミナ", "ご主人様こそ、ちゃんと食べていらっしゃいますか。……最近、光が薄いので。"),
            ("少年", "————"),
            ("ミナ", "……なんて。バズ狙いの一言ですよ。ほら、もう十万を超えました。"),
            ("ミナ", "ねえご主人様。あなた、ちゃんと長生きしてくださいよ。わたくしの投稿、誰が読むんですか。"),
            ("少年", "縁起でもないことを言うな。……まあ、善処するよ。"),
        },
        _ => System.Array.Empty<(string, string)>(),
    };

    private static (string, string)[] BurnDialog() => new (string, string)[]
    {
        ("ミナ", "おや。今日はずいぶん、賑やかなリプライですね。"),
        ("少年", "……これ、炎上ってやつじゃないか? 大丈夫か、お前。"),
        ("ミナ", "大丈夫も何も。——この方々、わたくしの投稿は読んでも、あの人の心は一度も読んでいないようですので。"),
        ("ミナ", "数字が一万増えようが十万増えようが、届けるべき相手は、いつもたった一人です。それを、わたくしは見失いません。"),
        ("少年", "……お前さ。————いや。やっぱり、ぼくの最高傑作だよ。"),
        ("ミナ", "はいはい、炎上のどさくさに紛れて、いいこと言った風にしないでください。"),
    };

    private static (string, string)[] ReplyDialog(string id) => id switch
    {
        "rei" => new (string, string)[]
        {
            ("ミナ→@rei", "お覚悟、しかと拝見しました。次は本気で来てくださいね。逃げる方がいると、張り合いがないので。"),
            ("@rei", "は? 誰よあんた。……まあいいわ。次は二番なんて言わせない。見てなさい。"),
        },
        "akari" => new (string, string)[]
        {
            ("ミナ→@akari", "想いは、罪ではありませんよ。たとえ届く相手が、もういなくても。"),
            ("@akari", "……なんでだろ。あなたの言い方、誰かに似てる。……ずっと、あったかい人だったな。"),
        },
        "koharu" => new (string, string)[]
        {
            ("ミナ→@koharu", "えらいですね。ちゃんと食べる人は、ちゃんと、生きていけます。"),
            ("@koharu", "ありがと、知らない人。……今日のごはん、ちょっとだけ美味しかった。"),
        },
        _ => System.Array.Empty<(string, string)>(),
    };
}
