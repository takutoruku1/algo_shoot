using System;
using System.Collections.Generic;
using System.Linq;

internal static class ReiColorRiddles
{
    internal readonly record struct Question(string Family, string Text, string Explanation, int Answer);

    private static readonly string[] Colors = { "赤", "青", "黄" };
    private static readonly string[] Shapes = { "○", "△" };
    private static readonly int[] Signs = { 0, 1, 2, 3, 4, 5 };
    private static readonly bool[] Choices = { false, true };
    internal static IReadOnlyList<Question> All { get; } = Build();

    private static string Label(int sign) => Colors[sign / 2] + Shapes[sign % 2];
    private static string References(int a, int b) => $"コメント A「{Label(a)}」 B「{Label(b)}」";
    private static string References(int a, int b, int c) => $"{References(a, b)} C「{Label(c)}」";
    private static int Matches(int sign, int reference)
        => (sign / 2 == reference / 2 ? 1 : 0) + (sign % 2 == reference % 2 ? 1 : 0);
    private static string Relation(string reference, int matches, char ending)
        => matches == 0 ? $"{reference}とは、色も形も変えてね{ending}"
            : $"{reference}と合わせるのは、色か形の片方だけ{ending}";
    private static string Branch(char comparedReference, bool same, string comparedAttribute,
        string chosenAttribute, char thenReference, char elseReference, char ending)
        => $"Aと{comparedReference}、{comparedAttribute}が{(same ? "同じ" : "違う")}？ なら{thenReference}の{chosenAttribute}、{(same ? "違えば" : "同じなら")}{elseReference}{ending}";
    private static string CommentPair(int color, int shape, bool notColor, bool notShape)
        => $"①「{Colors[color]}{(notColor ? "じゃないよ" : "だよ")}」 ②「{Shapes[shape]}{(notShape ? "じゃないよ" : "だよ")}」";
    private static bool Gate(int gate, bool first, bool second) => gate switch {
        0 => first && second,
        1 => first || second,
        2 => first != second,
        _ => !first && !second,
    };

    private static IReadOnlyList<Question> Build()
    {
        var questions = new List<Question>();
        var wording = new HashSet<string>(StringComparer.Ordinal);
        void Add(string family, string text, string explanation, Func<int, bool> accepts)
        {
            int[] answers = Signs.Where(accepts).ToArray();
            if (answers.Length == 1 && wording.Add(text))
                questions.Add(new Question(family, text, explanation, answers[0]));
        }

        foreach (int a in Signs)
        foreach (int b in Signs.Where(sign => sign != a))
        {
            int[] pair = { a, b };
            for (int colorReference = 0; colorReference < 2; colorReference++)
            foreach (bool inverse in Choices)
            {
                int shapeReference = 1 - colorReference;
                char colorName = (char)('A' + colorReference), shapeName = (char)('A' + shapeReference);
                int color = pair[colorReference] / 2, shape = pair[shapeReference] % 2 ^ (inverse ? 1 : 0);
                Add("AttributeMix", $"{References(a, b)}\n色は{colorName}から借りるね。\nでも形は、{shapeName}と{(inverse ? "逆にしてね" : "おそろい")}！",
                    $"{colorName}の色と、{shapeName}の{(inverse ? "逆の形" : "形")}。ついてこられた？",
                    sign => sign / 2 == color && sign % 2 == shape);
            }
            for (int firstMatches = 0; firstMatches <= 1; firstMatches++)
            for (int secondMatches = 0; secondMatches <= 1; secondMatches++)
            {
                Func<int, bool> first = sign => Matches(sign, a) == firstMatches;
                Func<int, bool> second = sign => Matches(sign, b) == secondMatches;
                if (Signs.Count(first) < 2 || Signs.Count(second) < 2) continue;
                Add("Relations", $"{References(a, b)}\n{Relation("A", firstMatches, '。')}\n{Relation("B", secondMatches, '！')}",
                    $"Aとは{(firstMatches == 0 ? "両方違う" : "片方だけ同じ")}。Bとは{(secondMatches == 0 ? "両方違う" : "片方だけ同じ")}！",
                    sign => first(sign) && second(sign));
            }
        }

        foreach (int a in Signs)
        foreach (int b in Signs.Where(sign => sign != a))
        foreach (int c in Signs.Where(sign => sign != a && sign != b))
        {
            int[] cards = { a, b, c };
            string header = References(a, b, c);
            for (int shapeReference = 0; shapeReference < 3; shapeReference++)
            foreach (bool inverse in Choices)
            {
                int firstColorReference = (shapeReference + 1) % 3, secondColorReference = (shapeReference + 2) % 3;
                int firstColor = cards[firstColorReference] / 2, secondColor = cards[secondColorReference] / 2;
                if (firstColor == secondColor) continue;
                char firstName = (char)('A' + firstColorReference), secondName = (char)('A' + secondColorReference);
                char shapeName = (char)('A' + shapeReference);
                int shape = cards[shapeReference] % 2 ^ (inverse ? 1 : 0);
                Add("AttributeMix", $"{header}\n{firstName}の色も{secondName}の色も、今日は呼ばないよ。\nでも形は、{shapeName}と{(inverse ? "逆にしてね" : "おそろい")}！",
                    $"残った色に、{shapeName}の{(inverse ? "逆の形" : "形")}。そこだよ！",
                    sign => sign / 2 != firstColor && sign / 2 != secondColor && sign % 2 == shape);
            }

            foreach (bool compareColor in Choices)
            foreach (bool same in Choices)
            foreach (bool swap in Choices)
            {
                if (compareColor ? b % 2 == c % 2 : b / 2 == c / 2) continue;
                bool condition = (compareColor ? a / 2 == b / 2 : a % 2 == b % 2) == same;
                char thenName = swap ? 'C' : 'B', elseName = swap ? 'B' : 'C';
                int selected = condition == swap ? c : b;
                char selectedName = condition ? thenName : elseName;
                for (int inverse = 0; inverse < (compareColor ? 1 : 2); inverse++)
                {
                    int color = compareColor ? a / 2 : selected / 2;
                    int shape = compareColor ? selected % 2 : a % 2 ^ inverse;
                    string fixedLine = compareColor ? "色はAから借りてね！" : $"形はAと{(inverse == 0 ? "おそろい" : "逆にしてね")}！";
                    string branch = Branch('B', same, compareColor ? "色" : "形", compareColor ? "形" : "色", thenName, elseName, '。');
                    Add("Conditional", $"{header}\n{branch}\n{fixedLine}",
                        $"AとB、{(compareColor ? "色" : "形")}は{(condition == same ? "同じだったね" : "違ったでしょ")}。{selectedName}の番！",
                        sign => sign / 2 == color && sign % 2 == shape);
                }
            }

            if (b / 2 != c / 2 && b % 2 != c % 2)
            foreach (bool colorSame in Choices)
            foreach (bool shapeSame in Choices)
            foreach (bool colorSwap in Choices)
            foreach (bool shapeSwap in Choices)
            {
                bool colorCondition = (a % 2 == b % 2) == colorSame;
                bool shapeCondition = (a / 2 == c / 2) == shapeSame;
                char colorThen = colorSwap ? 'C' : 'B', colorElse = colorSwap ? 'B' : 'C';
                char shapeThen = shapeSwap ? 'C' : 'B', shapeElse = shapeSwap ? 'B' : 'C';
                int color = (colorCondition == colorSwap ? c : b) / 2;
                int shape = (shapeCondition == shapeSwap ? c : b) % 2;
                Add("DoubleConditional", $"{header}\n{Branch('B', colorSame, "形", "色", colorThen, colorElse, '。')}\n{Branch('C', shapeSame, "色", "形", shapeThen, shapeElse, '！')}",
                    $"色は{(colorCondition ? colorThen : colorElse)}、形は{(shapeCondition ? shapeThen : shapeElse)}。両方聞いてた？",
                    sign => sign / 2 == color && sign % 2 == shape);
            }

            int[] colorCounts = Enumerable.Range(0, 3).Select(color => cards.Count(sign => sign / 2 == color)).ToArray();
            int[] shapeCounts = Enumerable.Range(0, 2).Select(shape => cards.Count(sign => sign % 2 == shape)).ToArray();
            if (colorCounts.Contains(1) && colorCounts.Contains(2) && shapeCounts.Contains(1) && shapeCounts.Contains(2))
            for (int colorCount = 1; colorCount <= 2; colorCount++)
            for (int shapeCount = 1; shapeCount <= 2; shapeCount++)
                Add("Frequency", $"{header}\nこの3人、色は{(colorCount == 1 ? "ひとりだけ" : "ふたり")}が推してるほう。\n形は、{(shapeCount == 1 ? "ひとりだけ" : "ふたり")}が推してるほう！",
                    $"色は{(colorCount == 1 ? "ひとり" : "ふたり")}、形は{(shapeCount == 1 ? "ひとり" : "ふたり")}。声、届いたね！",
                    sign => colorCounts[sign / 2] == colorCount && shapeCounts[sign % 2] == shapeCount);

            if (b / 2 != c / 2)
            for (int firstShape = 0; firstShape < 2; firstShape++)
            foreach (bool reverse in Choices)
            {
                int whenShape = a % 2 ^ (reverse ? 1 : 0);
                int color = (whenShape == firstShape ? b : c) / 2, shape = 1 - a % 2;
                string change = $"{Shapes[firstShape]}ならBの色、{Shapes[1 - firstShape]}ならCの色へ";
                string operations = reverse ? $"Aからね。先に形を逆にして。\n次は、{change}！" : $"Aからね。{change}。\nそのあと、形を逆にしてね！";
                Add("OrderedRewrite", $"{header}\n{operations}",
                    reverse ? "先に形を変えたから、この色だよ！" : "色を決めてから、形を変えたでしょ？",
                    sign => sign / 2 == color && sign % 2 == shape);
            }
        }

        for (int color = 0; color < 3; color++)
        for (int shape = 0; shape < 2; shape++)
        foreach (int pair in Signs)
        foreach (bool notColor in Choices)
        foreach (bool notShape in Choices)
        {
            bool First(int sign) => (sign / 2 == color) != notColor;
            bool Second(int sign) => (sign % 2 == shape) != notShape;
            foreach (bool notPair in Choices)
            for (int truthCount = 1; truthCount <= 2; truthCount++)
            {
                bool Third(int sign) => (sign == pair) != notPair;
                bool[] Truths(int sign) => new[] { First(sign), Second(sign), Third(sign) };
                int[] answers = Signs.Where(sign => Truths(sign).Count(value => value) == truthCount).ToArray();
                if (answers.Length != 1) continue;
                bool allNeeded = Enumerable.Range(0, 3).All(omitted => Signs.Count(sign => {
                    int known = Truths(sign).Where((value, index) => value && index != omitted).Count();
                    return known <= truthCount && known + 1 >= truthCount;
                }) > 1);
                if (!allNeeded) continue;
                string correct = string.Join("と", Truths(answers[0]).Select((value, index) => value ? new[] { "①", "②", "③" }[index] : null).Where(value => value != null));
                Add("TruthCount", $"{CommentPair(color, shape, notColor, notShape)}\n③「{Label(pair)}{(notPair ? "じゃないよ" : "だよ")}」\nホントなのは{(truthCount == 1 ? "ひとつ" : "ふたつ")}だけ。どこかわかる？",
                    $"ホントだったのは{correct}。信じすぎちゃだめだよ？", sign => Truths(sign).Count(value => value) == truthCount);
            }

            for (int gate = 0; gate < 4; gate++)
            for (int relation = 0; relation <= 1; relation++)
            {
                // A unique OR + one-match solution always flips the quoted shape, regardless of the comments.
                if (gate == 1 && relation == 1) continue;
                bool Main(int sign) => Gate(gate, First(sign), Second(sign));
                bool Extra(int sign) => Matches(sign, pair) == relation;
                int[] answers = Signs.Where(sign => Main(sign) && Extra(sign)).ToArray();
                if (answers.Length != 1 || Signs.Count(Main) < 2 || Signs.Count(Extra) < 2) continue;
                if (Signs.Count(sign => Extra(sign) && (Gate(gate, false, Second(sign)) || Gate(gate, true, Second(sign)))) < 2
                    || Signs.Count(sign => Extra(sign) && (Gate(gate, First(sign), false) || Gate(gate, First(sign), true))) < 2) continue;
                string logic = gate switch {
                    0 => "ね、①も②も信じていいよ。",
                    1 => "①と②、どっちかはホント。両方かもね。",
                    2 => "①と②、ホントは片方だけだよ。",
                    _ => "①も②もウソ。信じちゃった？",
                };
                string relationText = Relation(Label(pair), relation, '！');
                Add("LogicGate", $"{CommentPair(color, shape, notColor, notShape)}\n{logic}\n{relationText}",
                    $"①は{(First(answers[0]) ? "ホント" : "ウソ")}、②は{(Second(answers[0]) ? "ホント" : "ウソ")}。見抜いた？",
                    sign => Main(sign) && Extra(sign));
            }
        }

        for (int red = 0; red < 3; red++)
        for (int blue = 0; blue < 3; blue++)
        {
            if (red == blue) continue;
            int[] map = { red, blue, 3 - red - blue };
            foreach (int start in Signs)
            {
                if (map[start / 2] == start / 2) continue;
                for (int times = 1; times <= 3; times++)
                foreach (bool inverse in Choices)
                {
                    int color = start / 2;
                    for (int step = 0; step < times; step++) color = map[color];
                    int shape = start % 2 ^ (inverse ? 1 : 0);
                    Add("ColorMap", $"色のリレー 赤→{Colors[map[0]]} 青→{Colors[map[1]]} 黄→{Colors[map[2]]}\n{Label(start)}から、色を{times}回つないでね。\nでも形は、{(inverse ? "逆にしてね" : "そのままだよ")}！",
                        $"色を{times}回つないで、形は{(inverse ? "逆" : "そのまま")}。着いた？",
                        sign => sign / 2 == color && sign % 2 == shape);
                }
            }
        }
        return Array.AsReadOnly(questions.ToArray());
    }
}
