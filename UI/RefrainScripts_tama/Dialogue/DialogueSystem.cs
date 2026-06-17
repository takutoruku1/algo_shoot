using UnityEngine;
using System;
using System.Collections.Generic;
using TMPro;

namespace Refrain.UI
{
    public enum NodeType { Line, Narration, Plain, Choice }

    [Serializable] public class DialogueChoice { public string label; public int gotoIndex; }

    [Serializable]
    public class DialogueNode
    {
        public NodeType type = NodeType.Line;
        public CharacterId who = CharacterId.None; // Line のみ
        public string text;
        public int next = -1;                       // -1 = 次の index へ
        public List<DialogueChoice> choices;        // Choice のみ
    }

    [Serializable] public class LogEntry { public string name; public Color color; public string text; }

    /// <summary>
    /// 会話システム（採用案 B: シネマ下部バー）。
    /// タイプライター表示 / 選択肢分岐 / AUTO / SKIP / ログ(既読履歴)。
    /// HTML: Refrain Dialogue Play。
    /// </summary>
    public class DialogueSystem : MonoBehaviour
    {
        [Header("View refs")]
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text bodyText;
        [SerializeField] Transform choiceRoot;   // 選択肢ボタンの親
        [SerializeField] GameObject logPanel;

        [Header("Tuning")]
        [SerializeField] float charsPerSecond = 42f;
        [SerializeField] float autoDelay = 1.2f;
        [SerializeField] float skipDelay = 0.14f;

        public bool Auto, Skip, LogOpen;
        public readonly List<LogEntry> History = new List<LogEntry>();

        List<DialogueNode> _script;
        int _index;
        float _revealed;     // 表示済み文字数（float で蓄積）
        bool _done;
        float _wait;

        // ---- Character table (名前色 = RefrainTheme.CharColor) ----
        public static string DisplayName(CharacterId id) => id switch
        {
            CharacterId.Boy   => "少年",
            CharacterId.Mina  => "ミナ",
            CharacterId.Akari => "あかり",
            _ => ""
        };

        void Start()
        {
            _script = BuildSampleScript();
            Goto(0);
        }

        void Update()
        {
            if (LogOpen) return;
            var node = Current;
            if (node == null) return;

            // 入力: クリック / Z で送り
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetMouseButtonDown(0))
                Click();

            if (node.type == NodeType.Choice) return;

            int len = node.text != null ? node.text.Length : 0;
            if (!_done)
            {
                float step = Skip ? len : charsPerSecond * Time.deltaTime;
                _revealed = Mathf.Min(len, _revealed + step);
                if (Mathf.FloorToInt(_revealed) >= len) { _done = true; _wait = 0; }
                RenderBody(len);
            }
            else if (Auto || Skip)
            {
                _wait += Time.deltaTime;
                if (_wait >= (Skip ? skipDelay : autoDelay)) Advance();
            }
        }

        DialogueNode Current => (_script != null && _index < _script.Count) ? _script[_index] : null;

        void RenderBody(int len)
        {
            var node = Current;
            int n = Mathf.Clamp(Mathf.FloorToInt(_revealed), 0, len);
            if (bodyText) bodyText.text = node.text.Substring(0, n);
            if (nameText) nameText.text = node.type == NodeType.Line ? DisplayName(node.who) : "";
        }

        void Goto(int i)
        {
            _index = i; _revealed = 0; _done = false; _wait = 0;
            var node = Current;
            if (node != null && node.type == NodeType.Choice) ShowChoices(node);
            else HideChoices();
            if (node != null) RenderBody(node.text != null ? node.text.Length : 0);
        }

        public void Click()
        {
            var node = Current;
            if (node == null || node.type == NodeType.Choice) return;
            if (!_done) { _revealed = node.text.Length; _done = true; RenderBody(node.text.Length); }
            else Advance();
        }

        public void Advance()
        {
            var node = Current;
            PushLog(node);
            int nx = node.next >= 0 ? node.next : _index + 1;
            if (nx >= _script.Count) nx = 0; // デモ: 先頭へループ
            Goto(nx);
        }

        public void Choose(DialogueChoice c)
        {
            History.Add(new LogEntry { name = "選択", color = RefrainTheme.Purify, text = "▷ " + c.label });
            Goto(c.gotoIndex);
        }

        void PushLog(DialogueNode node)
        {
            if (node == null) return;
            string nm = node.type == NodeType.Line ? DisplayName(node.who)
                      : node.type == NodeType.Narration ? "ナレーション" : "";
            Color col = node.type == NodeType.Line ? RefrainTheme.CharColor(node.who) : RefrainTheme.Text3;
            History.Add(new LogEntry { name = nm, color = col, text = node.text });
        }

        // ---- Controls ----
        public void ToggleAuto() { Auto = !Auto; if (Auto) Skip = false; }
        public void ToggleSkip() { Skip = !Skip; if (Skip) Auto = false; }
        public void ToggleLog()  { LogOpen = !LogOpen; if (logPanel) logPanel.SetActive(LogOpen); }

        void ShowChoices(DialogueNode node) { /* node.choices を choiceRoot に生成し、各ボタン onClick → Choose(c) */ }
        void HideChoices() { /* choiceRoot をクリア */ }

        // ---- Sample script（HTML 版と同一の分岐つきデモ） ----
        public static List<DialogueNode> BuildSampleScript()
        {
            return new List<DialogueNode>
            {
                new DialogueNode{ type=NodeType.Narration, text="放課後の教室。だれもいない。窓の外で、雨の音だけが続いている。" },
                new DialogueNode{ type=NodeType.Line, who=CharacterId.Boy,  text="飛んでくるのは、この人を苦しめてる\"言葉\"だ。本人じゃない。撃って祓っていい。" },
                new DialogueNode{ type=NodeType.Line, who=CharacterId.Mina, text="……わたくしが、受け止めます。あなたは、まっすぐ前だけを。" },
                new DialogueNode{ type=NodeType.Line, who=CharacterId.Boy,  text="無理すんなよ。……お前が壊れたら、意味ないだろ。" },
                new DialogueNode{ type=NodeType.Choice, text="——どうする？", choices=new List<DialogueChoice>{
                    new DialogueChoice{ label="ミナを信じて任せる", gotoIndex=5 },
                    new DialogueChoice{ label="自分が前に出る",     gotoIndex=6 },
                }},
                new DialogueNode{ type=NodeType.Line, who=CharacterId.Mina, text="ありがとう、ございます。……では、いきます。", next=7 },
                new DialogueNode{ type=NodeType.Line, who=CharacterId.Boy,  text="……俺が壊す。お前は下がってろ。", next=7 },
                new DialogueNode{ type=NodeType.Line, who=CharacterId.Akari,text="……なんで、来たの。もう、ほっといてよ。" },
                new DialogueNode{ type=NodeType.Narration, text="彼女の声は、降りやまない雨の音に溶けていった。" },
                new DialogueNode{ type=NodeType.Plain, text="———— おわり（デモ）。クリックで最初に戻ります。", next=0 },
            };
        }
    }
}
