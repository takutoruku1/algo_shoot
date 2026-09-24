using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// CharacterStoryPhotoQa : 写真アプリ（Hub の Mode.Photos）で、他ジョブ潜行の「もう一度／帰還」6枚が
//   **新しい解禁キーで開く**ことを確かめ、一覧の画を残す（2026-09-23）。
//
// ── なぜ要るか ────────────────────────────────────────────────────────
//   6枚の解禁キーはもともと `{id}_playable_ch{n}_{memory|aftermath}`＝CharacterStoryFilm の FilmId だった。
//   回想／アフターが一枚絵をやめて吹き出しだけになった（CharacterStory.Memory / Aftermath）ため、
//   あの FilmId はもう誰も立てない＝6枠が永久に開かない状態になっていた。解禁キーを
//   CharacterStory.SeenKey（キャラ単位・面は問わない）へ付け替えたので、その付け替えを機械検査する。
//
//   起動: tools/qa_charstory_photo.tscn（APPDATA は build/qa_story/<name>_appdata へ隔離すること）。
public partial class CharacterStoryPhotoQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object?[] args)
        => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        GD.Print($"[PhotoQA] PASS {text}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            await Frames(2);

            // 写真エントリの表（PhotoEntries）を反射で引く。6枚のIDと、そこに載っているキーを見る。
            var entryType = typeof(Hub).GetNestedType("PhotoEntry", BindingFlags.NonPublic)!;
            var entries = (Array)typeof(Hub).GetField("PhotoEntries", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var idProp = entryType.GetProperty("Id")!;
            var keysProp = entryType.GetProperty("Keys")!;
            string[] playable =
            {
                "akari_playable_memory", "akari_playable_after",
                "koharu_playable_memory", "koharu_playable_after",
                "rei_playable_memory", "rei_playable_after",
            };
            foreach (object entry in entries)
            {
                string id = (string)idProp.GetValue(entry)!;
                if (!playable.Contains(id)) continue;
                var keys = (string[])keysProp.GetValue(entry)!;
                Check(keys.All(k => k.StartsWith("charstory_")),
                    $"{id} unlocks through the new character-story key ({string.Join(",", keys)})");
            }

            // どの面で見たかは問わない＝1回の潜行（memory/aftermath 各1本）で、そのキャラの2枚が開く。
            foreach (var job in new[] { Job.Melee, Job.Heal, Job.Magic })
            {
                FilmSkip.MarkSeen(game, CharacterStory.SeenKey(job, aftermath: false));
                FilmSkip.MarkSeen(game, CharacterStory.SeenKey(job, aftermath: true));
            }

            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            // 写真アプリを開いた状態で入る（--hub-photos と同じ入口。_Ready が見るので追加前に立てる）。
            hub.GetType().GetField("_openPhotos", Private)!.SetValue(hub, true);
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(40);

            int open = 0, total = entries.Length;
            foreach (object entry in entries)
            {
                bool acquired = (bool)Call(hub, "PhotoAcquired", entry)!;
                string id = (string)idProp.GetValue(entry)!;
                if (acquired) open++;
                if (playable.Contains(id)) Check(acquired, $"{id} is unlocked after one dive as that character");
            }
            // 他の枚（ミナ本編のフィルム等）は未見のまま＝6枚だけが開いていること。
            Check(open == 6, $"exactly the six playable photos are open ({open}/{total})");
            Check(Read<object>(hub, "_mode").ToString() == "Photos", "photo app is on screen");

            // 解禁した6枚（index 9〜14）までグリッドを送る＝「開いている枚」が画に入る。
            int first = Array.FindIndex(entries.Cast<object>().ToArray(), e => (string)idProp.GetValue(e)! == playable[0]);
            hub.GetType().GetField("_photoSel", Private)!.SetValue(hub, first + 3);
            await Frames(60);

            string outDir = ProjectSettings.GlobalizePath("res://build/qa_story/photos");
            DirAccess.MakeDirRecursiveAbsolute(outDir);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (var image = GetViewport().GetTexture().GetImage()) image.SavePng($"{outDir}/photos_playable.png");
            GD.Print($"[PhotoQA] shot {outDir}/photos_playable.png");

            GD.Print("[PhotoQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[PhotoQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
