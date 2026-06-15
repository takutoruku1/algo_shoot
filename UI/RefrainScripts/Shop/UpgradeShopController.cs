using UnityEngine;
using System;
using System.Collections.Generic;

namespace Refrain.UI
{
    /// <summary>強化プレビューの種別（詳細パネルの射撃プレビューを切り替える）。</summary>
    public enum UpgradeKind { Power, Rate, Homing, Spread, Mobility, Evade, Bomb, MaxHp, Impression, Combo, ComingSoon }

    [Serializable]
    public class UpgradeDef
    {
        public string id;
        public string name;
        public UpgradeKind kind;
        public int  maxLevel;
        public int  level;       // 習得済みレベル（ピップ = RefrainTheme.Mina 紫）
        public int  cost;        // 必要インプレ（金）。<0 = 準備中
        public string effectNow; // 例: 威力 +12%
        public string effectNext;// 例: 威力 +18%
        public string desc;      // 届ける光の威力 UP
        public string note;      // 玉サイズ・輝度 UP / NEW 自動追尾 等

        public bool   IsSoon => kind == UpgradeKind.ComingSoon || cost < 0;
    }

    /// <summary>ミナ強化ショップ。HTML: Refrain Screens / ミナ強化（インタラクティブ＋射撃プレビュー）。</summary>
    public class UpgradeShopController : MonoBehaviour
    {
        public int Impressions = 1280; // 所持インプレ（通貨, 金）

        public readonly List<UpgradeDef> Upgrades = new List<UpgradeDef>
        {
            new UpgradeDef{ id="power",   name="光の出力",     kind=UpgradeKind.Power,      maxLevel=5, level=2, cost=800,  effectNow="威力 +12%", effectNext="威力 +18%", desc="届ける光の威力 UP", note="玉サイズ・輝度 UP" },
            new UpgradeDef{ id="rate",    name="連射速度",     kind=UpgradeKind.Rate,       maxLevel=4, level=1, cost=700,  effectNow="間隔 0.20s", effectNext="間隔 0.14s", desc="光を撃つ間隔が縮む", note="連射数 UP" },
            new UpgradeDef{ id="homing",  name="ホーミング",   kind=UpgradeKind.Homing,     maxLevel=3, level=0, cost=1000, effectNow="追尾 なし",  effectNext="弱 追尾",    desc="光が穢れに吸い寄せられる", note="NEW 自動追尾" },
            new UpgradeDef{ id="spread",  name="拡散力",       kind=UpgradeKind.Spread,     maxLevel=3, level=0, cost=600,  effectNow="3 way",      effectNext="5 way",      desc="光が扇状に広がる", note="同時着弾 UP" },
            new UpgradeDef{ id="mobility",name="機動力",       kind=UpgradeKind.Mobility,   maxLevel=3, level=1, cost=500,  effectNow="速度 100%",  effectNext="速度 130%",  desc="ミナの移動が速くなる", note="残像 強化" },
            new UpgradeDef{ id="evade",   name="回避域",       kind=UpgradeKind.Evade,      maxLevel=3, level=0, cost=1200, effectNow="判定 4px",   effectNext="判定 3px",   desc="被弾判定が小さくなる", note="グレイズ域 拡大" },
            new UpgradeDef{ id="bomb",    name="ボム所持",     kind=UpgradeKind.Bomb,       maxLevel=3, level=1, cost=900,  effectNow="所持 +1",    effectNext="所持 +2",    desc="ボム最大所持 UP", note="画面浄化" },
            new UpgradeDef{ id="bombpow", name="ボム威力",     kind=UpgradeKind.ComingSoon, maxLevel=3, level=0, cost=-1 },
            new UpgradeDef{ id="maxhp",   name="最大体力",     kind=UpgradeKind.MaxHp,      maxLevel=3, level=1, cost=1500, effectNow="体力 5",     effectNext="体力 6",     desc="最大体力 UP", note="ハート +1" },
            new UpgradeDef{ id="impr",    name="インプレ倍率", kind=UpgradeKind.Impression, maxLevel=4, level=2, cost=600,  effectNow="×1.2",       effectNext="×1.4",       desc="獲得インプレ UP", note="報酬 増加" },
            new UpgradeDef{ id="combo",   name="コンボ持続",   kind=UpgradeKind.Combo,      maxLevel=3, level=1, cost=600,  effectNow="2.0s",       effectNext="2.6s",       desc="コンボ保持時間 UP", note="倍率 維持" },
            new UpgradeDef{ id="taint",   name="汚染耐性",     kind=UpgradeKind.ComingSoon, maxLevel=3, level=0, cost=-1 },
            new UpgradeDef{ id="subspr",  name="拡散サブ",     kind=UpgradeKind.ComingSoon, maxLevel=2, level=0, cost=-1 },
        };

        public int Selected { get; private set; }
        public event Action<UpgradeDef> OnSelectionChanged;
        public event Action<UpgradeDef> OnPurchased;

        public UpgradeDef Current => Upgrades[Selected];

        public void Select(int index)
        {
            Selected = Mathf.Clamp(index, 0, Upgrades.Count - 1);
            OnSelectionChanged?.Invoke(Current);
        }
        public void Move(int dir) => Select(Selected + dir);

        public bool CanAfford(UpgradeDef u) => !u.IsSoon && u.level < u.maxLevel && Impressions >= u.cost;

        /// <summary>Z で購入。強化は永続（汚染やリトライで失われない）。</summary>
        public bool Buy()
        {
            var u = Current;
            if (!CanAfford(u)) return false;
            Impressions -= u.cost;
            u.level = Mathf.Min(u.level + 1, u.maxLevel);
            OnPurchased?.Invoke(u);
            return true;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
            if (Input.GetKeyDown(KeyCode.UpArrow))   Move(-1);
            if (Input.GetKeyDown(KeyCode.Z))         Buy();
        }
    }
}
