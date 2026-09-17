using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

// EndingFilm が「実プレイと同じ条件で（Elapsed を差し替えず・入力を一切与えず）自動終了するか」を見る。
//   EpilogueQa の VerifyFilm は Elapsed を 49 秒へ飛ばして残りだけ見るので、
//   本当に頭から尺どおり流れて終わるかはここで確かめる。
public partial class EndingFilmRealtimeQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public override async void _Ready()
    {
        try
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            GetNode<GameManager>("/root/Game").MsgCharsPerSec = 300;
            await Frames(2);   // _Ready 中は root が子を組み立て中で AddChild が弾かれる
            var ep = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
            GetTree().Root.AddChild(ep);
            GetTree().CurrentScene = ep;
            await Frames(10);

            // E5b の語りは本題ではないので映画から始める（Epilogue 本来の入り口と同じ呼び出し）。
            typeof(Epilogue).GetMethod("StartFilm", Private)!.Invoke(ep, null);
            if (Read<int>(ep, "_phase") != 1)
                throw new Exception($"could not enter the film (phase={Read<int>(ep, "_phase")})");
            var film = Read<EndingFilm>(ep, "_film");
            GD.Print("[FilmRT] film started");

            // ここから一切入力しない＝実プレイで放置したときの挙動。
            ulong start = Time.GetTicksMsec();
            double lastReport = 0;
            while (IsInstanceValid(film) && Time.GetTicksMsec() - start < 90_000)
            {
                await Frames(30);
                double wall = (Time.GetTicksMsec() - start) / 1000.0;
                if (wall - lastReport >= 10)
                {
                    lastReport = wall;
                    GD.Print($"[FilmRT] wall={wall:F1}s elapsed={film.Elapsed:F1}s phase={Read<int>(ep, "_phase")}");
                }
            }
            double total = (Time.GetTicksMsec() - start) / 1000.0;
            if (IsInstanceValid(film))
                throw new Exception($"film never finished (wall={total:F1}s elapsed={film.Elapsed:F1}s)");
            GD.Print($"[FilmRT] film finished on its own after {total:F1}s of wall clock");
            await Frames(5);
            GD.Print($"[FilmRT] phase after the film = {Read<int>(ep, "_phase")} (2 = staff roll)");
            if (Read<int>(ep, "_phase") != 2) throw new Exception("the film did not hand over to the roll");
            GD.Print("[FilmRT] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[FilmRT] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static T Read<T>(object obj, string field)
        => (T)obj.GetType().GetField(field, Private)!.GetValue(obj)!;

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
