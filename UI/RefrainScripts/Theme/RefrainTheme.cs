using UnityEngine;

namespace Refrain
{
    /// <summary>
    /// 全画面共通のデザイントークン（SnsTowerDefense デザインシステム準拠）。
    /// HTML 版の役割色・フォント・余白をそのまま定数化。
    /// </summary>
    public static class RefrainTheme
    {
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // ---- Role colors ----
        public static readonly Color Hp        = Hex("#e8769c"); // 体力
        public static readonly Color Mina      = Hex("#9a72d9"); // BOMB / ミナ（紫）
        public static readonly Color Purify    = Hex("#6cbcd8"); // 浄化 / 光 / 選択ハイライト
        public static readonly Color PurifyHi  = Hex("#d7f3ff");
        public static readonly Color Kegare    = Hex("#e072ac"); // ボス穢れ（マゼンタ）
        public static readonly Color Score     = Hex("#e8c45a"); // SCORE / インプレ（金）
        public static readonly Color Light     = Hex("#ffd98a"); // 本人（光）

        // ---- Surfaces / text ----
        public static readonly Color BgDeep    = Hex("#070a16");
        public static readonly Color Panel     = Hex("#14101e");
        public static readonly Color GlassFill = new Color(16f/255f, 14f/255f, 26f/255f, 0.60f);
        public static readonly Color Text      = Hex("#eef1f6");
        public static readonly Color Text2     = Hex("#c8b8d8");
        public static readonly Color Text3     = Hex("#8a7a9a");

        // ---- Character name colors ----
        public static Color CharColor(CharacterId id)
        {
            switch (id)
            {
                case CharacterId.Boy:   return Purify; // 少年=青系
                case CharacterId.Mina:  return Mina;   // ミナ=紫
                case CharacterId.Akari: return Kegare; // あかり=ピンク
                default:                return Text3;
            }
        }

        // ---- Fonts (Project に取り込んだ TMP フォント名へ差し替え) ----
        public const string FontDisplay = "Zen Kaku Gothic New";
        public const string FontBody    = "Zen Kaku Gothic New";
        public const string FontMono    = "JetBrains Mono";

        // ---- Internal resolution (資料: 384x216 を整数倍した 16:9) ----
        public const int RefWidth  = 1920;
        public const int RefHeight = 1080;
    }

    public enum CharacterId { None, Boy, Mina, Akari }
}
