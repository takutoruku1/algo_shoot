using Godot;
using System;
using System.Text.Json;

public static class MusicLibrary
{
    public sealed class Cue
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class Track
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Character { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Source { get; set; } = "";
        public string Category { get; set; } = "";
        public string Audio { get; set; } = "";
        public string Cover { get; set; } = "";
        public string Video { get; set; } = "";
        public string Lyrics { get; set; } = "";
        public Cue[] Cues { get; set; } = Array.Empty<Cue>();
        public double Duration { get; set; }
        public bool IsVocal => Category == "vocal";
        public string Edition => Category switch { "vocal" => "歌詞PV", "theme" => "テーマ BGM", "stage" => "道中 BGM", "remix" => "REMIX BGM", _ => "INSTRUMENTAL" };
        public Cue? CueAt(double time)
        {
            foreach (var cue in Cues)
                if (time >= cue.Start && time < cue.End) return cue;
            return null;
        }
    }

    public static Track[] Load()
    {
        using var file = FileAccess.Open("res://assets/music/library.json", FileAccess.ModeFlags.Read);
        var tracks = JsonSerializer.Deserialize<Track[]>(file.GetAsText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var track in tracks)
            if (track.Provider != "Suno" || !Guid.TryParse(track.Id, out _)
                || track.Source != "https://suno.com/song/" + track.Id)
                throw new InvalidOperationException("The music library accepts verified Suno tracks only.");
        return tracks;
    }
}
