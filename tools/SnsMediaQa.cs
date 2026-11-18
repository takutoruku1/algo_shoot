using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class SnsMediaQa : Node
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Read<T>(object target, string name)
    {
        var type = target.GetType();
        var field = type.GetField(name, Members);
        return (T)(field != null ? field.GetValue(target) : type.GetProperty(name, Members)!.GetValue(target))!;
    }
    private static void Write(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static void SetMode(Hub hub, string value)
    {
        var field = typeof(Hub).GetField("_mode", Members)!;
        field.SetValue(hub, Enum.Parse(field.FieldType, value));
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print("[SnsMediaQA] PASS " + message);
    }

    private bool _headless, _movie, _motionDemo;
    private SnsVoices.Post? _returnPost;
    private const string AkariPostId = "akari_warm_meal", AkariPostReadKey = "once_sns_akari_warm_meal";
    private const string KoharuPostId = "koharu_worked_notes", ReiPostId = "rei_book_night", MinaPostId = "mina_shared_sky";
    private string _output = "";
    private Hub _hub = null!;
    private Node _media = null!;
    private VideoStreamPlayer _player = null!;
    private object[] _entries = null!;
    private object? Attachment(string id) => _media.GetType().GetMethod("Find", StaticMembers)!.Invoke(null, new object[] { id });
    private float Height(object entry) => (float)typeof(Hub).GetMethod("CardHeight", StaticMembers)!.Invoke(null, new[] { entry })!;
    private float Top(int index) => (float)Call(_hub, "CardTop", index)!;
    private bool Playing => Read<bool>(_media, "Playing");

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _headless = DisplayServer.GetName() == "headless";
            _movie = Array.Exists(OS.GetCmdlineUserArgs(), argument => argument == "--movie");
            _motionDemo = Array.Exists(OS.GetCmdlineUserArgs(), argument => argument == "--motion-demo");
            _returnPost = Array.Find(SnsVoices.ReturnPosts,
                post => OS.GetCmdlineUserArgs().Contains("--" + post.CharacterId + "-return"));
            _output = ProjectSettings.GlobalizePath(_returnPost != null
                ? $"res://build/qa_story/sns_media/{_returnPost.CharacterId}_return/design" : "res://build/qa_story/sns_media/design");
            DirAccess.MakeDirRecursiveAbsolute(_output);
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            foreach (string seen in new[] { "once_sns_intro", "once_phone_home", "once_account_intro" }) game.MarkIdleDialogSeen(seen);
            game.MarkIdleDialogSeen(GameManager.LunaticUnlockSeenKey);
            foreach (var job in Jobs.All)
            {
                game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
                game.MarkIdleDialogSeen($"companion_hub_{job.CharacterId}");
            }
            game.ShopTutorialSeen = true;
            if (_returnPost == null)
            {
                foreach (var post in SnsVoices.ReturnPosts) game.MarkIdleDialogSeen(SnsVoices.ReturnPostReadKey(post));
                foreach (var stage in GameManager.Stages) Read<HashSet<string>>(game, "_cleared").Add(stage.Id);
                game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 120);
                game.MarkEndingRollSeen();
            }
            if (!_headless)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            await Frames(2);
            _hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(_hub);
            GetTree().CurrentScene = _hub;
            await Frames(30);
            _media = Read<Node>(_hub, "_snsMedia");
            _player = Read<VideoStreamPlayer>(_media, "_player");
            Check(!Playing, "home does not play timeline videos");
            Write(_hub, "_idleTalkPending", false);
            Call(_hub, "BuildEntries");
            _entries = Read<Array>(_hub, "_entries").Cast<object>().ToArray();
            if (_returnPost != null)
            {
                await CheckReturnPost(game, _returnPost);
                await Finish();
                return;
            }
            Check(_entries.Count(entry => Read<object?>(entry, "Social") != null) == SnsVoices.Posts.Length + SnsVoices.ReturnPosts.Length,
                "all chapters and four return posts are present in the layout fixture");
            CheckContent();
            Check(_media.FindChildren("*", "VideoStreamPlayer", true, false).Count == 1,
                "the timeline owns one video decoder");

            Pad.ReleaseMouseFollow();
            Write(_hub, "_homeSel", 0);
            await Keypress(Key.Z);
            for (int frame = 0; frame < 600 && Mode(_hub) == "SnsOpening"; frame++) await Frames(1);
            Check(Mode(_hub) == "Cards", "SNS opens through the normal home action");
            CheckStorySelection();
            if (OS.GetCmdlineUserArgs().Contains("--reactions"))
            {
                await CheckVoiceReplyCounts(game);
                await CheckReactions(game);
                await Finish();
                return;
            }
            if (_motionDemo)
            {
                Check(!_headless, "the motion demonstration uses the rendered game window");
                foreach (string id in new[] { "rain_window", "stream_room" }) await MotionDemo(id);
                await Finish();
                return;
            }
            await Shot("story_first");
            CheckLayout();
            foreach (string id in new[] { "old_blanket", "last_train", "draft_reply", "archive_night", "muted_notifications", "penlight_drawer", "waiting_reply", "unfinished_color", "two_cups", AkariPostId, KoharuPostId, ReiPostId, MinaPostId })
            {
                await ShowPost(id);
                await Shot(id);
                if (_movie && (id is "last_train" or "draft_reply")) await Frames(60);
            }
            foreach (string id in new[] { "rain_window", "stream_room" })
                await CheckVideo(id);

            await ShowPost("stream_room");
            await Keypress(Key.Tab);
            Check(Mode(_hub) == "Job" && !Playing, "account overlay pauses the underlying video");
            await Frames(12);
            await Shot("account_overlay");
            await Keypress(Key.Tab);
            await Frames(8);
            Check(Mode(_hub) == "Cards", "closing the overlay restores the timeline");
            if (!_headless)
            {
                Check(Playing, "closing the overlay resumes the visible video");
                var pause = GetNode<PauseMenu>("/root/PauseMenu");
                pause.Open();
                await Frames(4);
                double pausedAt = _player.StreamPosition;
                await Frames(30);
                Check(Math.Abs(_player.StreamPosition - pausedAt) < .025, "pause menu freezes video playback");
                Call(pause, "Close");
                await Frames(8);
                Check(Playing, "closing pause resumes video playback");
            }
            await Keypress(Key.Escape);
            Check(Mode(_hub) == "Home" && !Playing, "returning home stops the timeline video");
            await Keypress(Key.Z);
            for (int frame = 0; frame < 600 && Mode(_hub) == "SnsOpening"; frame++) await Frames(1);
            Check(Mode(_hub) == "Cards", "SNS can be revisited after browsing media");
            CheckStorySelection();

            foreach (var post in SnsVoices.Posts.Concat(SnsVoices.ReturnPosts))
            {
                Call(_hub, "OpenSocialThread", post);
                await Frames(3);
                string[][] lines = Read<string[][]>(_hub, "_socialLines");
                float[] heights = Read<float[]>(_hub, "_socialHeights");
                Check(lines.Length == post.Replies.Length + 1 && heights.Length == lines.Length
                    && string.Concat(lines[0]) == post.Text
                    && Enumerable.Range(0, post.Replies.Length).All(i => string.Concat(lines[i + 1]) == post.Replies[i].Text),
                    post.Id + " preserves the post and reply order");
                Check(heights.All(height => height > 0) && Mathf.IsEqualApprox(heights.Sum(), Read<float>(_hub, "_socialContentHeight")),
                    post.Id + " thread height covers all replies");
                if (post.Id == "read_only") await Shot("reply_account_icons");
                if (Attachment(post.Id) is { } media)
                {
                    if (!Read<bool>(media, "IsVideo")) await Shot(post.Id + "_thread");
                    Rect2 rect = (Rect2)Call(_hub, "SocialMediaRect")!;
                    float bodyBottom = 78 + 76 + lines[0].Length * (UiKit.Zen.GetHeight(18) + 3);
                    Check(rect.Position.Y >= bodyBottom - .5f && rect.End.Y <= 78 + heights[0] - 26,
                        post.Id + " thread image clears body text and reply count");
                }
            }
            Call(_hub, "OpenSocialThread", SnsVoices.Posts.Single(post => post.Id == "stream_room"));
            await Frames(15);
            if (!_headless) Check(Playing, "thread displays its video");
            await Shot("video_thread");
            await Keypress(Key.Z);
            Check(!Playing && Mode(_hub) == "SocialThread", "thread confirm pauses without leaving the conversation");
            if (!_headless)
            {
                var pausedPosts = Read<HashSet<string>>(_media, "_pausedPosts");
                Check(pausedPosts.Contains("stream_room"), "thread video is explicitly paused before opening the menu");
                double pausedAt = _player.StreamPosition;
                Pad.ReleaseMouseFollow();
                await Keypress(Key.M);
                var pause = GetNode<PauseMenu>("/root/PauseMenu");
                Check(GetTree().Paused && Read<bool>(pause, "_open"), "menu opens over the paused video thread");
                for (int item = 0; item < pause.CloseIndex; item++) await Keypress(Key.Down);
                Check(Read<int>(pause, "_sel") == pause.CloseIndex, "keyboard navigation selects menu close");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, Pressed = true });
                await Frames(12);
                Check(!GetTree().Paused && !Read<bool>(pause, "_open") && Mode(_hub) == "SocialThread",
                    "held confirm closes only the menu");
                Check(pausedPosts.Contains("stream_room") && !Playing && Math.Abs(_player.StreamPosition - pausedAt) < .025,
                    "menu close confirm does not resume the manually paused video while held");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, Pressed = false });
                await Frames(4);
                Check(pausedPosts.Contains("stream_room") && !Playing, "releasing menu close confirm preserves the video pause");
            }
            await Keypress(Key.Z);
            if (!_headless) Check(Playing, "thread confirm resumes video");
            Write(_hub, "_socialScroll", Read<float>(_hub, "_socialContentHeight") - (646f - 78f));
            Write(_hub, "_socialScrollTarget", Read<float>(_hub, "_socialScroll"));
            await Frames(8);
            await Shot("thread_replies");
            await Keypress(Key.Escape);
            Check(Mode(_hub) == "Cards", "thread back returns to the timeline");

            await Finish();
        }
        catch (Exception error)
        {
            GetTree().Paused = false;
            GD.PushError("[SnsMediaQA] FAIL " + error);
            GetTree().Quit(1);
        }
    }

    private int PostCount(string id) => _entries.Count(entry => Read<string>(entry, "Id") == id);

    private string[] RepostRows()
    {
        _entries = Read<Array>(_hub, "_entries").Cast<object>().ToArray();
        var copies = _entries.TakeWhile(entry => Read<bool>(entry, "IsRepost")).ToArray();
        Check(_entries.Skip(copies.Length).All(entry => !Read<bool>(entry, "IsRepost")), "reposts form one section at the top");
        return copies.Select(entry => Read<string>(entry, "Id")).ToArray();
    }

    private async Task CheckVoiceReplyCounts(GameManager game)
    {
        Check(game.SaveToSlot(2), "saved the cleared posts before sending replies");
        foreach (var (id, extraLine) in new[] { ("akari", false), ("akari", true), ("koharu", false), ("rei", false) })
        {
            Check(game.LoadFromSlot(2) && !game.HasReplied(id), id + " has no exchange in the earlier slot");
            if (extraLine) game.RecordChoice("s1_5", "送った言葉", Array.Empty<string>(), 0);
            game.ToggleRepost(id);
            Call(_hub, "BuildEntries");
            RepostRows();
            int index = await ShowPost(id);
            Check(Read<int>(_entries[index], "Replies") == 1, id + " starts with Mina's one return reply");
            Write(_hub, "_cardBtn", 0);
            await Keypress(Key.Right);
            await Keypress(Key.Z);
            Check(Mode(_hub) == "Dialogue" && !game.HasReplied(id), id + " opening the reply does not publish it");
            var exchange = Read<(string speaker, string text)[]>(_hub, "_dlg");
            Check(exchange.Length == (id == "akari" && !extraLine ? 2 : 3), id + " opens the expected dialogue branch");
            for (int key = 0; key < 40 && Mode(_hub) == "Dialogue"; key++) await Keypress(Key.Z);
            Check(Mode(_hub) == "Cards" && game.SentReplies(id).SequenceEqual(exchange), id + " publishes the completed exchange");

            if (extraLine) game.RecordChoice("s1_5", "", Array.Empty<string>(), 0);
            Check(game.SaveToSlot(1) && game.LoadFromSlot(2) && !game.HasReplied(id) && game.LoadFromSlot(1),
                id + " keeps its sent exchange in its own slot");
            Call(_hub, "BuildEntries");
            RepostRows();
            foreach (object entry in _entries.Where(e => Read<string>(e, "Id") == id))
            {
                var rows = Read<Array>(entry, "VoiceReplies").Cast<object>().ToArray();
                Check(rows.Length == 1 + exchange.Length && Read<int>(entry, "Replies") == rows.Length
                    && rows.Skip(1).Select(row => string.Concat(Read<string[]>(row, "Lines"))).SequenceEqual(exchange.Select(line => line.text))
                    && rows.Skip(1).Select(row => Read<string>(row, "AuthorId")).SequenceEqual(exchange.Select(line => line.speaker.StartsWith("ミナ") ? "mina" : id)),
                    id + " shows every sent reply in order on both the original and repost");
            }
            await ShowPost(id);
            if (!_headless)
            {
                while (Read<double>(_hub, "_toastT") > 0) await Frames(1);
                await Shot(extraLine ? "akari_replies_optional" : id + "_replies");
            }
        }
        Check(game.LoadFromSlot(2), "restored the feed before any replies or reactions");
        Call(_hub, "BuildEntries");
        RepostRows();
    }

    private async Task CheckReactions(GameManager game)
    {
        int originalCount = _entries.Length;
        var baseline = _entries.ToDictionary(e => Read<string>(e, "Id"), e => Read<SnsVoices.Reach>(e, "Reach"));
        Check(_entries.All(e => {
            var reach = Read<SnsVoices.Reach>(e, "Reach");
            int replies = Read<int>(e, "Replies");
            return reach.Views > reach.Likes + reach.Reposts + replies && reach.Likes > 0
                && (Read<object?>(e, "Social") is not SnsVoices.Post post || replies == post.Replies.Length);
        }), "all feed cards have readers, credible reactions and the actual thread reply count");
        int followers = game.Followers;
        game.AddFollowers(100000);
        Call(_hub, "BuildEntries");
        Check(Read<Array>(_hub, "_entries").Cast<object>().All(e => Read<SnsVoices.Reach>(e, "Reach") == baseline[Read<string>(e, "Id")]),
            "account growth does not inflate old posts or the pinned report");
        typeof(GameManager).GetProperty(nameof(GameManager.Followers))!.SetValue(game, followers);
        Check(game.SaveToSlot(2), "independent slot saved before any reactions");
        await ShowPost("last_train");
        await Keypress(Key.Left);
        await Keypress(Key.Z);
        Check(game.IsPostLiked("last_train") && Mode(_hub) == "Cards", "confirm likes a social post without opening it");
        await Keypress(Key.Left);
        await Keypress(Key.Z);
        Check(RepostRows().SequenceEqual(new[] { "last_train" }) && PostCount("last_train") == 2
            && Read<float>(_hub, "_feedScroll") == 0, "repost shows a copy above the original and scrolls to it");
        var original = baseline["last_train"];
        Check(_entries.Where(e => Read<string>(e, "Id") == "last_train").All(e =>
            (SnsVoices.Reach)Call(_hub, "DisplayReach", "last_train", Read<SnsVoices.Reach>(e, "Reach"))!
                == (original with { Likes = original.Likes + 1, Reposts = original.Reposts + 1 })),
            "the original and repost each show one player like and repost without multiplying views");
        await Keypress(Key.Right);
        await Keypress(Key.Z);
        Check(!game.IsPostLiked("last_train"), "the copy toggles the original like off");
        await Keypress(Key.Right);
        await Keypress(Key.Z);
        Check(Mode(_hub) == "SocialThread" && Read<SnsVoices.Post>(_hub, "_socialPost").Id == "last_train",
            "the repost opens the original thread");
        await Keypress(Key.Escape);

        int boss = await ShowPost("akari");
        if (_headless)
        {
            await Keypress(Key.Left);
            await Keypress(Key.Z);
            await Keypress(Key.Left);
            await Keypress(Key.Z);
        }
        else
        {
            await Click(((Rect2)Call(_hub, "ReactionRect", boss, -1)!).GetCenter());
            await Click(((Rect2)Call(_hub, "ReactionRect", boss, -2)!).GetCenter());
        }
        Check(game.IsPostLiked("akari") && RepostRows().SequenceEqual(new[] { "akari", "last_train" }) && PostCount("akari") == 2
            && Mode(_hub) == "Cards", "boss reactions work and the newest repost comes first");
        await Shot("reposts_boss_and_post");
        await Keypress(Key.Right);
        await Keypress(Key.Right);
        await Keypress(Key.Z);
        Check(Mode(_hub) == "Detail", "a reposted boss still opens its stage");
        await Keypress(Key.Escape);
        Check(game.SaveToSlot(1), "reactions saved with the slot");
        Check(game.LoadFromSlot(2) && !game.IsPostLiked("akari") && game.RepostedPostIds.Count == 0, "slots keep their own reactions");
        Check(game.LoadFromSlot(1), "reaction slot reloads");
        await ReloadHub(game);
        await OpenSns();
        Check(game.IsPostLiked("akari") && RepostRows().SequenceEqual(new[] { "akari", "last_train" }) && Read<int>(_hub, "_sel") == 0,
            "returning to SNS restores reactions and their order");
        await Keypress(Key.Left);
        await Keypress(Key.Left);
        await Keypress(Key.Z);
        Check(RepostRows().SequenceEqual(new[] { "last_train" }) && PostCount("akari") == 1 && game.IsPostLiked("akari"),
            "undo removes only the repost and keeps the boss and its like");
        await ShowPost("last_train");
        await Keypress(Key.Left);
        await Keypress(Key.Left);
        await Keypress(Key.Z);
        Check(RepostRows().Length == 0 && _entries.Length == originalCount, "removing the final repost restores the original feed");

        var pinned = _entries.Single(entry => Read<object>(entry, "Sort").ToString() == "Pinned");
        string pinnedId = Read<string>(pinned, "Id"), text = Read<string>(pinned, "Tweet");
        await ShowPost(pinnedId);
        await Shot("pinned_reactions");
        await Keypress(Key.Left);
        await Keypress(Key.Z);
        await Shot("pinned_repost_reactions");
        Read<HashSet<string>>(game, "_companionClears").Add("rei");
        Call(_hub, "BuildEntries");
        Check(RepostRows().SequenceEqual(new[] { pinnedId }) && Read<string>(_entries[0], "Tweet") == text && PostCount(pinnedId) == 1,
            "a replaced pinned post retains its original words in the repost section");
        game.ResetPersistent();
        Check(!game.IsPostLiked("akari") && game.RepostedPostIds.Count == 0, "new games start with no reactions");
    }
    private int UnreadPostCount => Read<int>(_hub, "UnreadReturnPostCount");
    private bool Unread(SnsVoices.Post post) => (bool)Call(_hub, "IsUnreadReturnPost", post)!;

    private async Task ReloadHub(GameManager game)
    {
        _hub.QueueFree();
        await Frames(5);
        game.JustClearedStageId = null;
        _hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(_hub);
        GetTree().CurrentScene = _hub;
        await Frames(30);
        Write(_hub, "_idleTalkPending", false);
        _media = Read<Node>(_hub, "_snsMedia");
        _player = Read<VideoStreamPlayer>(_media, "_player");
        _entries = Read<Array>(_hub, "_entries").Cast<object>().ToArray();
        Pad.ReleaseMouseFollow();
    }

    private async Task OpenSns()
    {
        Write(_hub, "_homeSel", 0);
        await Keypress(Key.Z);
        for (int frame = 0; frame < 600 && Mode(_hub) == "SnsOpening"; frame++) await Frames(1);
        Check(Mode(_hub) == "Cards", "home action opens the SNS timeline");
        await Frames(30);
    }

    private async Task CheckReturnPost(GameManager game, SnsVoices.Post post)
    {
        string character = post.CharacterId, readKey = SnsVoices.ReturnPostReadKey(post);
        int order = Array.IndexOf(SnsVoices.ReturnPosts, post);
        var unlockedPosts = SnsVoices.ReturnPosts.Take(order + 1).ToArray();
        Check(PostCount(post.Id) == 0 && UnreadPostCount == 0 && !Unread(post),
            "a fresh game contains no return post or notification");
        Check(SnsVoices.ReturnPostReadKey(SnsVoices.AkariReturnPost) == AkariPostReadKey
            && SnsVoices.ReturnPostReadKey(SnsVoices.KoharuReturnPost) == "once_sns_koharu_worked_notes",
            "the existing Akari and Koharu save read keys are preserved");
        await Shot("before_clear_home");
        for (int prior = 0; prior < order; prior++)
        {
            game.CompleteStage(SnsVoices.ReturnPosts[prior].CharacterId);
            await ReloadHub(game);
            Check(PostCount(post.Id) == 0 && UnreadPostCount == prior + 1 && !Unread(post),
                "earlier chapter clears do not expose " + character + "'s later post");
        }
        if (character == "mina")
        {
            Check(game.AllStoryCleared && !game.IsFinalCleared && !game.EndingRollSeen,
                "three saved voices alone do not unlock Mina's ending post");
            Check(game.SaveToSlot(3), "the pre-final fixture saves independently");
            game.MarkEndingRollSeen();
            await ReloadHub(game);
            Check(PostCount(post.Id) == 0 && !Unread(post), "the ending-seen flag alone cannot unlock Mina's post");
            game.ResetPersistent();
            Check(game.LoadFromSlot(3), "the pre-final fixture restores without an ending flag");
            game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 120);
            await ReloadHub(game);
            Check(game.IsFinalCleared && !game.EndingRollSeen && PostCount(post.Id) == 0 && !Unread(post),
                "FINAL clear alone keeps the ending photograph hidden");
            Check(game.SaveToSlot(0), "the spoiler-gated fixture saves before preview");
            string saveBeforePreview = FileAccess.GetFileAsString("user://save_0.json");
            Write(_hub, "_previewState", "all");
            Call(_hub, "BuildEntries");
            Call(_hub, "LoadFaces");
            game.AutoSaveEnabled = true;
            Call(_hub, "OpenSocialThread", post);
            game.AutoSaveEnabled = false;
            Check(!game.IsIdleDialogSeen(readKey) && Unread(post)
                && FileAccess.GetFileAsString("user://save_0.json") == saveBeforePreview,
                "previewing Mina's ending post neither consumes its unread key nor writes the save");
            await ReloadHub(game);
            Check(PostCount(post.Id) == 0, "leaving preview restores the ending spoiler gate");
            game.MarkEndingRollSeen();
            Check(game.IsFinalCleared && game.EndingRollSeen, "the normal final record and ending completion unlock Mina");
        }
        else
        {
            game.CompleteStage(character);
            Check(game.IsStageCleared(character), "the normal clear operation records " + character + "'s first clear");
        }
        Check(game.SaveToSlot(2), "the first-clear fixture saves into the isolated profile");
        game.ResetPersistent();
        Check(game.LoadFromSlot(2), "the first-clear fixture reloads from disk");
        await ReloadHub(game);
        Check(unlockedPosts.All(item => PostCount(item.Id) == 1 && Unread(item)) && UnreadPostCount == order + 1,
            "the reloaded first clear exposes one copy of each unlocked unread post");
        int index = Array.FindIndex(_entries, entry => Read<string>(entry, "Id") == post.Id);
        object entry = _entries[index];
        string expectedName = character switch { "akari" => "あかり", "koharu" => "こはる", "rei" => "レイ", _ => "ミナ" };
        string expectedHandle = character switch { "akari" => Handles.Akari, "koharu" => Handles.Koharu, "rei" => Handles.Rei, _ => Handles.Mina };
        Check(Read<string>(entry, "Name") == expectedName && Read<string>(entry, "Handle") == expectedHandle
            && Read<object>(entry, "Sort").ToString() == "Personal",
            "the post is attributed to " + character + "'s actual account");
        var faces = Read<Dictionary<string, Texture2D?>>(_hub, "_faces");
        Check(faces[post.Id]?.ResourcePath == CompanionDialogue.AccountIcon(character),
            "the post uses " + character + "'s account portrait");
        var image = Read<Dictionary<string, Texture2D>>(_media, "_images")[post.Id];
        Check(image != null && image.GetWidth() > 0 && image.GetHeight() > 0,
            "the return post's standalone story image loads successfully");
        await Shot("unread_home_badge");
        await OpenSns();
        Check(Read<int>(_hub, "_sel") == index && !(bool)Call(_hub, "CardButtonsShown", index)!,
            "the latest unread post receives focus and has no battle buttons");
        Check((string)Call(_hub, "SideStoryId")! == (character == "mina" ? "final" : character),
            "the return post keeps its character and story context");
        var mediaRect = (Rect2)Call(_hub, "CardMediaRect", index)!;
        Check(new Rect2(400, 128, 480, 518).Encloses(mediaRect),
            "the new post's whole image fits inside the initial timeline view");
        Check(Unread(post), "viewing the card does not silently mark the post read");
        await Shot("unread_card");
        Check(game.SaveToSlot(0), "the autosave baseline contains the unread post");
        game.AutoSaveEnabled = true;
        await Keypress(Key.Z);
        game.AutoSaveEnabled = false;
        Check(Mode(_hub) == "SocialThread" && Read<SnsVoices.Post>(_hub, "_socialPost").Id == post.Id
            && !Read<bool>(_hub, "_dived") && GetTree().CurrentScene == _hub,
            "confirm opens the return post without starting a battle");
        Check(game.IsIdleDialogSeen(readKey) && !Unread(post) && UnreadPostCount == order
            && unlockedPosts.Take(order).All(item => Unread(item) && !game.IsIdleDialogSeen(SnsVoices.ReturnPostReadKey(item))),
            "opening the post consumes only its own unread notification");
        string[][] lines = Read<string[][]>(_hub, "_socialLines");
        Check(lines.Length == 1 && string.Concat(lines[0]) == post.Text,
            "the detail contains the caption and no fabricated reply rows");
        await Frames(30);
        await Shot("post_detail");
        await Keypress(Key.Escape);
        if (post == SnsVoices.AkariReturnPost)
        {
            await Frames(30);
            int selected = Read<int>(_hub, "_sel");
            Check(Mode(_hub) == "Cards" && selected == index + 1 && Read<string>(_entries[selected], "Id") == "koharu",
                "closing Akari's new post focuses the adjacent Koharu story");
            CheckStorySelection();
            await Shot("koharu_after_akari");
            await Keypress(Key.Z);
            Check(Mode(_hub) == "Detail" && !Read<bool>(_hub, "_dived"),
                "Koharu's post opens before choosing whether to dive");
            await Keypress(Key.Escape);
            await Keypress(Key.Up);
            await Keypress(Key.Z);
            Check(Mode(_hub) == "SocialThread" && Read<SnsVoices.Post>(_hub, "_socialPost").Id == post.Id,
                "one step up reopens Akari's meal");
            await Keypress(Key.Escape);
        }
        Check(Mode(_hub) == "Cards" && Read<int>(_hub, "_sel") == index,
            "back returns to the same archived post");
        await Keypress(Key.Escape);
        Check(Mode(_hub) == "Home", "the post can return to the phone home");
        await Shot(order > 0 ? "read_home_remaining_unread" : "read_home_badge_cleared");
        game.ResetIdleDialogSeen();
        Check(game.IsIdleDialogSeen(readKey), "small-talk cycling preserves the post's once-only read key");
        game.ResetPersistent();
        Check(game.LoadFromSlot(0), "the automatic read-state save reloads from disk");
        Check(game.IsIdleDialogSeen(readKey), "opening the post persists its read state through autosave");
        await ReloadHub(game);
        Check(PostCount(post.Id) == 1 && !Unread(post) && UnreadPostCount == order,
            "the read post remains available without a renewed notification after reload");
        for (int remaining = order - 1; remaining >= 0; remaining--)
        {
            var nextPost = unlockedPosts[remaining];
            await OpenSns();
            int nextIndex = Array.FindIndex(_entries, item => Read<string>(item, "Id") == nextPost.Id);
            Check(Read<int>(_hub, "_sel") == nextIndex && Unread(nextPost),
                "SNS next focuses the remaining unread " + nextPost.CharacterId + " post");
            Check((string)Call(_hub, "SideStoryId")! == nextPost.CharacterId,
                "the remaining post uses its own character preview");
            await Shot("remaining_" + nextPost.CharacterId + "_unread");
            game.AutoSaveEnabled = true;
            await Keypress(Key.Z);
            game.AutoSaveEnabled = false;
            Check(Mode(_hub) == "SocialThread" && Read<SnsVoices.Post>(_hub, "_socialPost").Id == nextPost.Id
                && game.IsIdleDialogSeen(SnsVoices.ReturnPostReadKey(nextPost)) && UnreadPostCount == remaining
                && unlockedPosts.Skip(remaining).All(item => !Unread(item)),
                "reading " + nextPost.CharacterId + " changes only its own pending notification");
            await Keypress(Key.Escape);
            await Keypress(Key.Escape);
        }
        await Shot("read_home_badge_cleared");
        game.ResetPersistent();
        Check(game.LoadFromSlot(0), "all consumed read states reload from autosave");
        await ReloadHub(game);
        Check(UnreadPostCount == 0 && unlockedPosts.All(item => PostCount(item.Id) == 1 && !Unread(item)),
            "every read post remains archived exactly once");
        await OpenSns();
        CheckStorySelection();
        string nextStory = character switch { "akari" => "koharu", "koharu" => "rei", _ => "final" };
        Check(Read<string>(_entries[Read<int>(_hub, "_sel")], "Id") == nextStory,
            "after reading new posts, SNS returns to the normal story selection");
        await Shot("next_story_after_read");
        if (character == "mina")
        {
            game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 110);
            game.MarkEndingRollSeen();
        }
        else game.CompleteStage(character);
        Check(game.SaveToSlot(2), "a repeat clear saves normally");
        game.ResetPersistent();
        Check(game.LoadFromSlot(2), "the repeat-clear fixture reloads from disk");
        await ReloadHub(game);
        Check(PostCount(post.Id) == 1 && !Unread(post) && UnreadPostCount == 0,
            "repeat clearing neither duplicates the post nor renews its notification");
        await OpenSns();
        index = Array.FindIndex(_entries, item => Read<string>(item, "Id") == post.Id);
        for (int step = 0; step < _entries.Length && Read<int>(_hub, "_sel") != index; step++)
            await Keypress(Read<int>(_hub, "_sel") < index ? Key.Down : Key.Up);
        Check(Read<int>(_hub, "_sel") == index, "keyboard navigation reaches the archived post");
        await Keypress(Key.Z);
        Check(Mode(_hub) == "SocialThread" && Read<SnsVoices.Post>(_hub, "_socialPost").Id == post.Id,
            "the archived post can be reread after clearing again");
        await Frames(30);
        await Shot("reread_post");
    }

    private async Task Finish()
    {
        _hub.QueueFree();
        await Frames(5);
        Check(!IsInstanceValid(_media) && !IsInstanceValid(_player), "leaving Hub releases its media renderer and decoder");
        Audio.Instance?.StopMusic(0);
        foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
            if (node is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Task.Delay(250);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print("[SnsMediaQA] ALL PASS; scripted UI input, not a perceptual usability judgment."
            + (_returnPost != null ? " " + _returnPost.CharacterId + " return post unlock, independent unread notifications and save/reload checks included."
                : _motionDemo ? " Motion demonstration completed." : _headless
                ? " Rendered image and playback checks require the GUI run."
                : " Displayed image movement, pause and resume checks included; inspect the saved image pairs."));
        GetTree().Quit();
    }

    private void CheckContent()
    {
        var avatars = SnsVoices.All.Select(voice => (Texture2D?)Call(_hub, "MobIcon", voice.Icon)).ToArray();
        Check(avatars.All(texture => texture != null) && avatars.Distinct().Count() == avatars.Length,
            "every general account has its own loaded avatar for posts and replies");
        var posts = SnsVoices.Posts;
        Check(posts.Length == 16 && posts.Select(post => post.Id).Distinct().Count() == 16,
            "the timeline has sixteen distinct posts");
        Check(posts.GroupBy(post => post.Author).All(group => group.Count() <= 2),
            "no author occupies more than two posts");
        var bodyAndReplies = posts.SelectMany(post => post.Replies.Select(reply => reply.Text).Prepend(post.Text));
        Check(bodyAndReplies.All(text => !new[] { "植物", "観葉", "新芽", "鉢植え" }.Any(text.Contains)),
            "the former plant topic is absent from posts and replies");
        for (int chapter = 0; chapter < 4; chapter++)
        {
            var chapterPosts = posts.Where(post => post.Chapter == chapter).ToArray();
            Check(chapterPosts.Length == 4 && chapterPosts.Select(post => post.Author).Distinct().Count() == 4,
                $"chapter {chapter} contains four posts from four authors");
            string voiceId = chapter == 3 ? "final" : GameManager.Stages[chapter].Id;
            int voiceIndex = Array.FindIndex(_entries, entry => Read<string>(entry, "Id") == voiceId);
            int[] postIndices = chapterPosts.Select(post => Array.FindIndex(_entries, entry => Read<string>(entry, "Id") == post.Id)).ToArray();
            Check(voiceIndex >= 0 && postIndices.Min() == voiceIndex + 1 + SnsVoices.ReturnPosts.Count(post => SnsVoices.ReturnPostStageId(post) == voiceId)
                && postIndices.All(index => index > voiceIndex),
                voiceId + " appears before its chapter's social posts and images");
        }
        var followups = posts.Where(post => post.PreviousId.Length > 0).ToArray();
        Check(followups.Length == 2
            && followups.Single(post => post.Id == "unfinished_color").PreviousId == "stream_room"
            && followups.Single(post => post.Id == "late_reply").PreviousId == "draft_reply",
            "the two follow-up conversations link to their intended earlier posts");
        foreach (var post in followups)
        {
            var previous = posts.Single(candidate => candidate.Id == post.PreviousId);
            Check(previous.Author == post.Author && previous.Chapter < post.Chapter,
                post.Id + " follows an older post by the same author");
        }
        string[] photoIds = { "last_train", "draft_reply", "archive_night", "penlight_drawer", "unfinished_color", "two_cups", "old_blanket", "muted_notifications", "waiting_reply" };
        for (int photo = 0; photo < photoIds.Length; photo++)
            Check(Attachment(photoIds[photo]) is { } attachment && !Read<bool>(attachment, "IsVideo")
                && Read<int>(attachment, "Photo") == photo, photoIds[photo] + " uses its own photo");
        foreach (var (id, clip) in new[] { ("rain_window", "rain"), ("stream_room", "studio") })
            Check(Attachment(id) is { } attachment && Read<bool>(attachment, "IsVideo") && Read<string>(attachment, "Clip") == clip,
                id + " uses its intended video");
        var media = posts.Select(post => Attachment(post.Id)).Where(attachment => attachment != null).Select(attachment => attachment!).ToArray();
        var attachmentTable = (System.Collections.IDictionary)_media.GetType().GetField("Attachments", StaticMembers)!.GetValue(null)!;
        var photos = media.Where(attachment => !Read<bool>(attachment, "IsVideo")).Select(attachment => Read<int>(attachment, "Photo")).ToArray();
        var clips = media.Where(attachment => Read<bool>(attachment, "IsVideo")).Select(attachment => Read<string>(attachment, "Clip")).ToArray();
        Check(attachmentTable.Count == photoIds.Length + 2 + SnsVoices.ReturnPosts.Length && media.Length == photoIds.Length + 2
            && photos.Length == photoIds.Length && photos.Distinct().Count() == photoIds.Length
            && clips.Length == 2 && clips.Distinct().Count() == 2,
            "the nine general-post photos and two clips are each used by exactly one post");
        Check(Attachment(AkariPostId) is { } akariImage && !Read<bool>(akariImage, "IsVideo")
            && Read<string>(akariImage, "ImagePath") == "res://assets/sns/akari_warm_meal_v2.png",
            "Akari's return post uses its separate story illustration");
        Check(SnsVoices.AkariReturnPost.CharacterId == "akari"
            && SnsVoices.AkariReturnPost.Text == "今日は、冷める前に食べた。"
            && SnsVoices.AkariReturnPost.Replies.Length == 0,
            "Akari's return post has its own author, a short caption and no invented replies");
        Check(Attachment(KoharuPostId) is { } koharuImage && !Read<bool>(koharuImage, "IsVideo")
            && Read<string>(koharuImage, "ImagePath") == "res://assets/sns/koharu_worked_notes_v2.png",
            "Koharu's return post uses its separate worked-notes illustration");
        Check(SnsVoices.KoharuReturnPost.CharacterId == "koharu"
            && SnsVoices.KoharuReturnPost.Text == "ここ、やっと分かった。"
            && SnsVoices.KoharuReturnPost.Replies.Length == 0,
            "Koharu's distinct return post has her own author, a short caption and no invented replies");
        Check(Attachment(ReiPostId) is { } reiImage && !Read<bool>(reiImage, "IsVideo")
            && Read<string>(reiImage, "ImagePath") == "res://assets/sns/rei_book_night_v2.png"
            && SnsVoices.ReiReturnPost.CharacterId == "rei"
            && SnsVoices.ReiReturnPost.Text == "本の好きなところを最後まで話せた。"
            && SnsVoices.ReiReturnPost.Replies.Length == 0,
            "Rei's return post uses her book photograph, caption and own account");
        Check(Attachment(MinaPostId) is { } minaImage && !Read<bool>(minaImage, "IsVideo")
            && Read<string>(minaImage, "ImagePath") == "res://assets/sns/mina_shared_sky_v2.png"
            && SnsVoices.MinaReturnPost.CharacterId == "mina"
            && SnsVoices.MinaReturnPost.Text == "今夜はこの空を選びました。おやすみなさい。"
            && SnsVoices.MinaReturnPost.Replies.Length == 0,
            "Mina's return post uses her shared sky, caption and own account");
        Check(SnsVoices.ReturnPosts.Length == 4 && SnsVoices.ReturnPosts.Select(post => post.Id).Distinct().Count() == 4
            && SnsVoices.ReturnPosts.Select(post => Read<string>(Attachment(post.Id)!, "ImagePath")).Distinct().Count() == 4,
            "all four return posts have distinct identifiers and photographs");
    }

    private void CheckStorySelection()
    {
        int selected = Read<int>(_hub, "_sel");
        Check(selected == (int)Call(_hub, "DefaultSelection")! && Read<object>(_entries[selected], "Sort").ToString() == "Voice",
            "SNS initially focuses a main-story card rather than a social attachment");
        Check((bool)Call(_hub, "CardButtonsShown", selected)!, "the focused story card exposes its dive action");
        Rect2 button = (Rect2)Call(_hub, "CardBtnRect", selected, 0)!;
        Check(new Rect2(400, 128, 480, 518).Encloses(button), "the dive action is visible inside the timeline");
    }

    private void CheckLayout()
    {
        Write(_hub, "_feedScroll", 0f);
        Write(_hub, "_feedScrollTarget", 0f);
        float expectedTop = 0;
        for (int index = 0; index < _entries.Length; index++)
        {
            object entry = _entries[index];
            string id = Read<string>(entry, "Id");
            float height = Height(entry), top = Top(index);
            Check(Mathf.IsEqualApprox(top, expectedTop) && height > 0, id + " is contiguous with the preceding card");
            expectedTop += height;
            if (Attachment(id) == null) continue;
            Rect2 rect = (Rect2)Call(_hub, "CardMediaRect", index)!;
            int lines = Math.Min(3, UiKit.WrapLines(UiKit.Zen, Read<string>(entry, "Tweet"), 17, 432).Count());
            float bodyBottom = 128 + top + 75 + lines * UiKit.Zen.GetHeight(17);
            float bottomSpace = Read<object>(entry, "Sort").ToString() == "Personal" ? 42 : 72;
            Check(rect.Size.X > 0 && rect.Size.Y > 0 && rect.Position.X >= 400 && rect.End.X <= 880
                && rect.Position.Y >= bodyBottom - .5f && rect.End.Y <= 128 + top + height - bottomSpace,
                id + " attachment fits between the body and reply preview");
        }
        Check(Mathf.IsEqualApprox(Top(_entries.Length), expectedTop), "feed end includes every expanded card");
    }

    private async Task<int> ShowPost(string id)
    {
        int index = Array.FindIndex(_entries, entry => Read<string>(entry, "Id") == id);
        Check(index >= 0, id + " is reachable in the feed");
        SetMode(_hub, "Cards");
        Pad.ReleaseMouseFollow();
        Write(_hub, "_sel", index);
        Write(_hub, "_footSel", -1);
        Write(_hub, "_headFocus", false);
        Write(_hub, "_cardsEnteredT", -10.0);
        float scroll = Mathf.Clamp(Top(index), 0, (float)Call(_hub, "FeedMaxScroll")!);
        Write(_hub, "_feedScroll", scroll);
        Write(_hub, "_feedScrollTarget", scroll);
        await Frames(12);
        return index;
    }

    private async Task CheckVideo(string id)
    {
        int index = await ShowPost(id);
        if (!_headless)
        {
            Check(Read<string>(_media, "ActivePostId") == id && Playing, id + " plays only while visible");
            Rect2 rect = (Rect2)Call(_hub, "CardMediaRect", index)!;
            using var before = await CaptureMedia(rect, id + "_playing_before");
            double position = _player.StreamPosition;
            await Frames(60);
            using var after = await CaptureMedia(rect, id + "_playing_after");
            Check(_player.StreamPosition > position + .2, id + " advances its playback time");
            CheckDisplayedMovement(before, after, id + " playing");
            await Shot(id);
            if (_movie)
            {
                await Frames(150);
                double beforeLoop = _player.StreamPosition;
                await Frames(90);
                Check(Playing && _player.StreamPosition < beforeLoop, id + " loops after the full four-second clip");
            }
            await Click(rect.End - new Vector2(60, 24));
            Check(!Playing && Mode(_hub) == "Cards", "clicking " + id + " pauses without opening the thread");
            position = _player.StreamPosition;
            using var pausedBefore = await CaptureMedia(rect, id + "_paused_before");
            await Frames(60);
            using var pausedAfter = await CaptureMedia(rect, id + "_paused_after");
            Check(Math.Abs(_player.StreamPosition - position) < .025, id + " stays paused");
            var frozen = CompareImages(pausedBefore, pausedAfter);
            Check(frozen.ChangedPixels == 0 && frozen.MeanDifference < .05,
                id + " freezes the displayed image, excluding its controls");
            await Click(rect.End - new Vector2(60, 24));
            Check(Playing, "clicking " + id + " resumes");
            using var resumedBefore = await CaptureMedia(rect, id + "_resumed_before");
            await Frames(60);
            using var resumedAfter = await CaptureMedia(rect, id + "_resumed_after");
            CheckDisplayedMovement(resumedBefore, resumedAfter, id + " resumed");
            Pad.ReleaseMouseFollow();
            Write(_hub, "_feedScroll", Top(index) + 190);
            Write(_hub, "_feedScrollTarget", Top(index) + 190);
            await Frames(8);
            await Shot(id + "_clipped_top");
            Write(_hub, "_feedScroll", Top(index) - 262);
            Write(_hub, "_feedScrollTarget", Top(index) - 262);
            await Frames(8);
            Check(Read<string>(_media, "ActivePostId") != id, id + " yields playback when only a small strip remains visible");
            await Shot(id + "_clipped_bottom");
            await ShowPost(id);
        }
        await Keypress(Key.Z);
        Check(Mode(_hub) == "SocialThread" && Read<SnsVoices.Post>(_hub, "_socialPost").Id == id,
            "card confirm still opens the " + id + " conversation");
        await Keypress(Key.Escape);
        await ShowPost("last_train");
        Check(!Playing, id + " stops after scrolling out of view");
    }

    private async Task<Image> CaptureMedia(Rect2 rect, string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        Vector2 scale = new(frame.GetWidth() / UiKit.DesignW, frame.GetHeight() / UiKit.DesignH);
        using var media = frame.GetRegion(new Rect2I((Vector2I)(rect.Position * scale), (Vector2I)(rect.Size * scale)));
        media.Resize(432, 216, Image.Interpolation.Bilinear);
        media.Convert(Image.Format.Rgb8);
        Check(media.SavePng($"{_output}/{name}.png") == Error.Ok, "displayed media capture " + name);
        // Exclude the badge, buttons, progress bar and rounded edges from motion evidence.
        return media.GetRegion(new Rect2I(8, 38, 416, 133));
    }

    private static (int ChangedPixels, int LargestRegion, double MeanDifference) CompareImages(Image first, Image second)
    {
        byte[] a = first.GetData(), b = second.GetData();
        int width = first.GetWidth(), height = first.GetHeight();
        var changed = new bool[width * height];
        long totalDifference = 0;
        int changedPixels = 0;
        for (int pixel = 0; pixel < changed.Length; pixel++)
        {
            int maximum = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                int difference = Math.Abs(a[pixel * 3 + channel] - b[pixel * 3 + channel]);
                totalDifference += difference;
                maximum = Math.Max(maximum, difference);
            }
            if (maximum >= 18) { changed[pixel] = true; changedPixels++; }
        }
        int largest = 0;
        var pending = new Queue<int>();
        for (int pixel = 0; pixel < changed.Length; pixel++)
        {
            if (!changed[pixel]) continue;
            changed[pixel] = false;
            pending.Enqueue(pixel);
            int area = 0;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                area++;
                int x = current % width, y = current / width;
                // Thin rain outlines can connect diagonally at the displayed resolution.
                foreach (int next in new[] { x > 0 ? current - 1 : -1, x < width - 1 ? current + 1 : -1,
                    y > 0 ? current - width : -1, y < height - 1 ? current + width : -1,
                    x > 0 && y > 0 ? current - width - 1 : -1,
                    x < width - 1 && y > 0 ? current - width + 1 : -1,
                    x > 0 && y < height - 1 ? current + width - 1 : -1,
                    x < width - 1 && y < height - 1 ? current + width + 1 : -1 })
                {
                    if (next < 0 || !changed[next]) continue;
                    changed[next] = false;
                    pending.Enqueue(next);
                }
            }
            largest = Math.Max(largest, area);
        }
        return (changedPixels, largest, totalDifference / (double)a.Length);
    }

    private static void CheckDisplayedMovement(Image before, Image after, string label)
    {
        var motion = CompareImages(before, after);
        GD.Print($"[SnsMediaQA] MOTION {label}: changed_pixels={motion.ChangedPixels}/55328, "
            + $"largest_region={motion.LargestRegion}, mean_channel_difference={motion.MeanDifference:F3}");
        Check(motion.ChangedPixels >= 256 && motion.LargestRegion >= 64,
            label + " changes a visible region of the displayed video, excluding playback UI");
    }

    private async Task MotionDemo(string id)
    {
        int index = await ShowPost(id);
        Rect2 rect = (Rect2)Call(_hub, "CardMediaRect", index)!;
        Check(Playing, id + " demo begins playing");
        await Frames(180);
        await Shot(id + "_demo_playing");
        await Click(rect.End - new Vector2(60, 24));
        Check(!Playing, id + " demo pauses through its visible button");
        await Frames(90);
        await Shot(id + "_demo_paused");
        await Click(rect.End - new Vector2(60, 24));
        Check(Playing, id + " demo resumes through its visible button");
        await Frames(180);
        await Shot(id + "_demo_resumed");
    }

    private async Task Click(Vector2 designPoint)
    {
        Vector2 point = designPoint * UiKit.Scale;
        GetViewport().WarpMouse(point);
        await Frames(3);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
        await Frames(3);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point });
        await Frames(3);
    }

    private async Task Keypress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(4);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(4);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (_headless) return;
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_output}/{name}.png") == Error.Ok, "screenshot " + name);
    }
}
