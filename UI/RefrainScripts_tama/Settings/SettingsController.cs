using UnityEngine;
using System;
using System.Collections.Generic;

namespace Refrain.UI
{
    public enum SettingCategory { Display, Audio, Gameplay, Controls, Accessibility, XIntegration }
    public enum SettingType { Slider, Toggle, Segment, Select, KeyBind }

    [Serializable]
    public class SettingDef
    {
        public string key;
        public string label;
        public string sub;
        public SettingType type;
        public string[] options;   // Segment 用
        public string[] keys;      // KeyBind 用
        public float    fValue;    // Slider 0..100
        public bool     bValue;    // Toggle
        public int      iValue;    // Segment / Select index
        public string   sValue;    // Select 表示文字
    }

    /// <summary>設定。6カテゴリ（表示/サウンド/ゲームプレイ/操作/A11y/X連携）。HTML: Refrain Settings。</summary>
    public class SettingsController : MonoBehaviour
    {
        public SettingCategory Category = SettingCategory.Display;
        public event Action OnChanged;

        public readonly Dictionary<SettingCategory, List<SettingDef>> Categories = new Dictionary<SettingCategory, List<SettingDef>>
        {
            { SettingCategory.Display, new List<SettingDef>{
                new SettingDef{ key="mode",  label="画面モード",       type=SettingType.Segment, options=new[]{"ウィンドウ","フルスクリーン"}, iValue=1 },
                new SettingDef{ key="res",   label="解像度",           type=SettingType.Select,  sValue="1920 × 1080" },
                new SettingDef{ key="fps",   label="リフレッシュレート", type=SettingType.Segment, options=new[]{"60","120","144"}, iValue=0 },
                new SettingDef{ key="scan",  label="スキャンライン",   sub="レトロな走査線の濃さ", type=SettingType.Slider, fValue=40 },
                new SettingDef{ key="bloom", label="ブルーム",         sub="光のにじみ",          type=SettingType.Slider, fValue=65 },
                new SettingDef{ key="pixel", label="ピクセルパーフェクト", sub="内部384×216を整数倍で表示", type=SettingType.Toggle, bValue=true },
            }},
            { SettingCategory.Audio, new List<SettingDef>{
                new SettingDef{ key="master", label="マスター音量", type=SettingType.Slider, fValue=80 },
                new SettingDef{ key="bgm",    label="BGM",         type=SettingType.Slider, fValue=70 },
                new SettingDef{ key="se",     label="効果音 (SE)", type=SettingType.Slider, fValue=85 },
                new SettingDef{ key="voice",  label="ボイス",      type=SettingType.Slider, fValue=90 },
                new SettingDef{ key="amb",    label="環境音", sub="心象世界のノイズ", type=SettingType.Slider, fValue=55 },
            }},
            { SettingCategory.Gameplay, new List<SettingDef>{
                new SettingDef{ key="bright", label="弾を明るく表示", sub="視認性を上げる", type=SettingType.Toggle, bValue=true },
                new SettingDef{ key="hitbox", label="当たり判定を常時表示", type=SettingType.Toggle, bValue=false },
                new SettingDef{ key="shake",  label="画面振動", type=SettingType.Slider, fValue=30 },
                new SettingDef{ key="flash",  label="被弾フラッシュ", sub="赤い明滅の強さ", type=SettingType.Slider, fValue=60 },
                new SettingDef{ key="msg",    label="メッセージ速度", type=SettingType.Segment, options=new[]{"遅","中","速"}, iValue=1 },
                new SettingDef{ key="auto",   label="オート会話送り", type=SettingType.Toggle, bValue=false },
            }},
            { SettingCategory.Controls, new List<SettingDef>{
                new SettingDef{ key="move",  label="移動",     type=SettingType.KeyBind, keys=new[]{"↑","↓","←","→"} },
                new SettingDef{ key="shot",  label="ショット", type=SettingType.KeyBind, keys=new[]{"Z"} },
                new SettingDef{ key="bomb",  label="ボム",     type=SettingType.KeyBind, keys=new[]{"X"} },
                new SettingDef{ key="slow",  label="低速移動", sub="判定を見ながら避ける", type=SettingType.KeyBind, keys=new[]{"Shift"} },
                new SettingDef{ key="pause", label="ポーズ",   type=SettingType.KeyBind, keys=new[]{"Esc"} },
            }},
            { SettingCategory.Accessibility, new List<SettingDef>{
                new SettingDef{ key="reduceflash", label="明滅を抑える", sub="フラッシュ表現を軽減", type=SettingType.Toggle, bValue=true },
                new SettingDef{ key="cvd",      label="色覚サポート", type=SettingType.Segment, options=new[]{"OFF","1型","2型","3型"}, iValue=0 },
                new SettingDef{ key="textsize", label="文字サイズ",   type=SettingType.Segment, options=new[]{"小","中","大"}, iValue=1 },
                new SettingDef{ key="softhit",  label="被弾演出を控えめに", type=SettingType.Toggle, bValue=false },
                new SettingDef{ key="contrast", label="高コントラストUI", type=SettingType.Toggle, bValue=false },
            }},
            { SettingCategory.XIntegration, new List<SettingDef>{
                new SettingDef{ key="share",     label="浄化の結果をXに投稿", type=SettingType.Toggle, bValue=true },
                new SettingDef{ key="showid",    label="スコアカードに@IDを表示", type=SettingType.Toggle, bValue=true },
                new SettingDef{ key="realposts", label="「降ってくる言葉」を実投稿から生成", sub="体験版では固定フレーズ", type=SettingType.Toggle, bValue=false },
                new SettingDef{ key="notif",     label="リプ通知演出", type=SettingType.Slider, fValue=50 },
                new SettingDef{ key="filter",    label="センシティブ表現フィルタ", type=SettingType.Toggle, bValue=true },
            }},
        };

        public List<SettingDef> CurrentList => Categories[Category];

        public void SetSlider(string key, float v) { Find(key).fValue = Mathf.Clamp(v, 0, 100); OnChanged?.Invoke(); }
        public void ToggleBool(string key)         { var d = Find(key); d.bValue = !d.bValue;   OnChanged?.Invoke(); }
        public void SetSegment(string key, int i)  { Find(key).iValue = i;                       OnChanged?.Invoke(); }

        SettingDef Find(string key)
        {
            foreach (var list in Categories.Values)
                foreach (var d in list)
                    if (d.key == key) return d;
            return null;
        }
    }
}
