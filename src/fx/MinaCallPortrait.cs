using Godot;
using System;
using System.Collections.Generic;

internal sealed class MinaCallPortrait
{
    // All cels share these source pixels. The collar follows the chest, the waist seam stays planted.
    private static readonly Vector2 Canvas = new(1024, 1536), Neck = new(544, 797), Nape = new(636, 766), Waist = new(544, 1432);
    private const int Columns = 64;
    private readonly Vector2[] _uv, _source, _points;
    private readonly float[] _neckWeight, _eyelidTravel, _irisWeight;
    private readonly TorsoWeights[] _torso;
    private readonly int[] _indices;
    private readonly Dictionary<Texture2D, Cels> _expressions = new();
    private sealed record Cels(Texture2D Talk, Texture2D Blink, Texture2D TalkBlink, Vector2 Lip, bool Smile);
    private readonly record struct TorsoWeights(float Carry, float Shoulder);
    internal readonly record struct BodyPose(float Lean, float Bow, float Breath, float Nod);

    public MinaCallPortrait()
    {
        var rows = new List<int>();
        for (int y = 0; y <= 1536; y += y is >= 448 and < 736 ? 8 : 32) rows.Add(y);
        int stride = Columns + 1;
        _uv = new Vector2[rows.Count * stride];
        _source = new Vector2[_uv.Length];
        _points = new Vector2[_uv.Length];
        _neckWeight = new float[_uv.Length];
        _eyelidTravel = new float[_uv.Length];
        _irisWeight = new float[_uv.Length];
        _torso = new TorsoWeights[_uv.Length];
        _indices = new int[(rows.Count - 1) * Columns * 6];
        int triangle = 0;
        for (int row = 0; row < rows.Count; row++)
        for (int col = 0; col < stride; col++)
        {
            int i = row * stride + col;
            Vector2 p = _source[i] = new(col * 16, rows[row]);
            _uv[i] = p / Canvas;
            float center = 1 - RangeEase(180, 340, Mathf.Abs(p.X - Neck.X));
            _neckWeight[i] = 1 - RangeEase(Mathf.Lerp(625, 756, center), Mathf.Lerp(812, 891, center), p.Y);
            _eyelidTravel[i] = LidTravel(p, new(450, 492), 70) + LidTravel(p, new(655, 521), 65);
            _irisWeight[i] = IrisWeight(p, new(454, 521)) + IrisWeight(p, new(645, 546));
            float side = Mathf.Sign(p.X - Neck.X) * RangeEase(105, 336, Mathf.Abs(p.X - Neck.X));
            float carry = 1 - RangeEase(936, 1460, p.Y);
            float shoulder = side * RangeEase(768, 896, p.Y) * (1 - RangeEase(1008, 1384, p.Y));
            _torso[i] = new(carry, shoulder);
            if (row + 1 == rows.Count || col == Columns) continue;
            _indices[triangle++] = i; _indices[triangle++] = i + 1; _indices[triangle++] = i + stride + 1;
            _indices[triangle++] = i; _indices[triangle++] = i + stride + 1; _indices[triangle++] = i + stride;
        }
    }

    private static float RangeEase(float start, float end, float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp((value - start) / (end - start), 0, 1));

    private static float LidTravel(Vector2 p, Vector2 lash, float radius)
    {
        float dx = p.X - lash.X, dy = p.Y - lash.Y - dx * .15f;
        float side = 1 - RangeEase(radius * .64f, radius, Mathf.Abs(dx));
        return 41 * side * RangeEase(-48, 0, dy) * (1 - RangeEase(0, 59, dy));
    }

    private static float IrisWeight(Vector2 p, Vector2 center)
    {
        float dx = p.X - center.X, dy = p.Y - center.Y - .13f * dx;
        return (1 - RangeEase(24, 48, Mathf.Abs(dx))) * RangeEase(-23, -11, dy) * (1 - RangeEase(13, 25, dy));
    }

    public void Prepare(Texture2D rest)
    {
        if (_expressions.ContainsKey(rest)) return;
        var (name, lip) = rest.ResourcePath switch
        {
            CompanionDialogue.MinaSmilePortrait => ("smile", new Vector2(545, 668)),
            CompanionDialogue.MinaWorriedPortrait => ("worried", new Vector2(545, 673)),
            _ => ("neutral", new Vector2(545, 673))
        };
        string prefix = $"res://char/v3/mina_call/{name}_";
        _expressions[rest] = new(GD.Load<Texture2D>(prefix + "talk.png"), GD.Load<Texture2D>(prefix + "blink.png"),
            GD.Load<Texture2D>(prefix + "talk_blink.png"), lip, name == "smile");
    }

    internal static float BlinkAmount(double elapsed)
    {
        float age = (float)elapsed;
        return RangeEase(0, .082f, age) * (1 - RangeEase(.123f, .233f, age));
    }

    internal readonly record struct Speech(double End, float Mouth, float LipWidth, bool Hesitating)
    {
        public bool Articulating => Mouth > .12f;
    }

    private static bool Voiced(char c) => char.IsLetterOrDigit(c) && c is not ('っ' or 'ッ' or 'ん' or 'ン');

    private static float JoinedOpening(char previous, char next) =>
        Voiced(previous) && Voiced(next) && !"まみむめもばびぶべぼぱぴぷぺぽマミムメモバビブベボパピプペポ".Contains(next) ? .23f : 0;

    internal static Speech ReadSpeech(string page, DialoguePacing.Page? pacing, double time)
    {
        double end = 0;
        float mouth = 0, width = 0;
        bool hesitating = false;
        for (int i = 0; i < page.Length; i++)
        {
            char c = page[i];
            end += pacing?.Before(i) ?? 0;
            if (c is '\n' or '\r') continue;
            double length = c switch
            {
                '、' or '，' or ',' => .22,
                '。' or '！' or '？' or '!' or '?' => .32,
                '…' or '—' => .27,
                >= '\u4e00' and <= '\u9fff' => .24,
                _ => .145
            };
            bool active = time >= end && time < end + length;
            if (active && c is '…' or '—') hesitating = true;
            if (active && Voiced(c))
            {
                float phase = (float)((time - end) / length);
                char kana = c is >= 'ァ' and <= 'ヶ' ? (char)(c - ('ア' - 'あ')) : c;
                bool wide = "いきぎしじちぢにひびぴみりえけげせぜてでねへべぺめれ".Contains(kana);
                bool round = "うくぐすずつづぬふぶぷむゆるゅおこごそぞとどのほぼぽもよろょ".Contains(kana);
                float opening = wide ? .48f : round ? .62f : .86f;
                int before = i - 1, after = i + 1;
                while (before >= 0 && page[before] is '\n' or '\r') before--;
                while (after < page.Length && page[after] is '\n' or '\r') after++;
                float entry = before >= 0 && (pacing?.Before(i) ?? 0) == 0 ? JoinedOpening(page[before], c) : 0;
                float exit = after < page.Length && (pacing?.Before(after) ?? 0) == 0 ? JoinedOpening(c, page[after]) : 0;
                mouth = Mathf.Lerp(entry, opening, RangeEase(0, .43f, phase));
                mouth = Mathf.Lerp(mouth, exit, RangeEase(.5f, 1, phase));
                width = (wide ? .12f : round ? -.26f : -.04f) * RangeEase(0, .45f, phase) * (1 - RangeEase(.53f, 1, phase));
            }
            end += length;
        }
        return new(end + (pacing?.Tail ?? 0), mouth, width, hesitating);
    }

    public void Draw(CanvasItem canvas, Rect2 rect, Texture2D rest, BodyPose body, float angle, Vector2 gaze, float blink, Speech speech, float alpha)
    {
        var cels = _expressions[rest];
        bool closed = blink > .78f;
        Texture2D texture = speech.Articulating ? (closed ? cels.TalkBlink : cels.Talk)
            : (closed ? cels.Blink : rest);
        float lid = closed ? 0 : blink;
        Vector2 irisTravel = closed ? Vector2.Zero : gaze * (1 - blink);
        float closingSmile = cels.Smile ? 1 - RangeEase(.12f, .65f, speech.Mouth) : 0;
        float scale = rect.Size.Y / Canvas.Y;
        var fromWaist = new Transform2D(body.Lean - Mathf.DegToRad(1.1f) * body.Bow, Waist);
        // Rotate the whole face about the nape; only the neck and hanging hair blend into the torso.
        float dip = Mathf.DegToRad(-2.8f) * body.Nod;
        Vector2 dippedNeck = Nape + (Neck - Nape).Rotated(dip);
        var fromNeck = new Transform2D(angle + dip, dippedNeck);
        for (int i = 0; i < _points.Length; i++)
        {
            Vector2 p = _source[i];
            var torso = _torso[i];
            p += irisTravel * _irisWeight[i];
            p.Y += _eyelidTravel[i] * lid;
            if (speech.Articulating && Mathf.Abs(p.Y - cels.Lip.Y) < 40 && Mathf.Abs(p.X - cels.Lip.X) < 58)
            {
                var lip = p - cels.Lip;
                float radiusSquared = (lip / new Vector2(58, 40)).LengthSquared();
                float influence = 1 - RangeEase(.43f, 1, radiusSquared);
                p.Y -= lip.Y * influence * (1 - speech.Mouth) * .9f;
                p.X += lip.X * influence * (speech.LipWidth - .18f * closingSmile);
                // The resting smile's corners sit above its center, slightly higher on the left.
                float side = Mathf.Clamp(lip.X / 34, -1, 1);
                p.Y += (-3 + 3 * side - 7 * side * side) * influence * closingSmile;
            }
            if (_neckWeight[i] > 0)
                p = p.Lerp(fromNeck * (p - Neck), _neckWeight[i]);
            p = p.Lerp(fromWaist * (p - Waist), torso.Carry);
            p.X += 5.5f * body.Breath * torso.Shoulder;
            p.Y -= 1.6f * body.Breath * Mathf.Abs(torso.Shoulder);
            _points[i] = rect.Position + p * scale;
        }
        RenderingServer.CanvasItemAddTriangleArray(canvas.GetCanvasItem(), _indices.AsSpan(), _points.AsSpan(),
            stackalloc Color[] { new(Colors.White, alpha) }, _uv.AsSpan(), ReadOnlySpan<int>.Empty,
            ReadOnlySpan<float>.Empty, texture.GetRid(), -1);
    }
}
