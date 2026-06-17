using UnityEngine;
using System;
using System.Collections.Generic;

namespace Refrain.UI
{
    public enum Difficulty { Easy, Normal, Hard, Lunatic }

    /// <summary>難易度選択。弾密度と報酬倍率が変動。ルナティックは解禁条件付き。HTML: Refrain Screens / 難易度選択。</summary>
    public class DifficultySelectController : MonoBehaviour
    {
        [Serializable]
        public class DifficultyOption
        {
            public Difficulty id;
            public string name;
            public string desc;
            public float rewardMult;     // 報酬倍率（緑表示 = --cat-yougo #7ec880）
            public int   bulletDensity;  // 0..5 メーター
            public bool  locked;
            public string unlockCond;    // 紫表示（解禁条件）
        }

        public readonly List<DifficultyOption> Options = new List<DifficultyOption>
        {
            new DifficultyOption{ id=Difficulty.Easy,    name="やさしい",     desc="弾は少なく、ゆっくり。物語を追いたい人へ。", rewardMult=0.7f, bulletDensity=2 },
            new DifficultyOption{ id=Difficulty.Normal,  name="ふつう",       desc="標準的な弾幕。",                         rewardMult=1.0f, bulletDensity=3 },
            new DifficultyOption{ id=Difficulty.Hard,    name="むずかしい",   desc="弾が増え、密度が上がる。",                 rewardMult=1.4f, bulletDensity=4 },
            new DifficultyOption{ id=Difficulty.Lunatic, name="ルナティック", desc="",                                       rewardMult=1.8f, bulletDensity=5,
                                  locked=true, unlockCond="フォロワー 300 または 威力 Lv4" },
        };

        public int Selected { get; private set; } = 1; // 既定 = ふつう
        public event Action<Difficulty> OnDive;

        public void Move(int dir)
        {
            Selected = Mathf.Clamp(Selected + dir, 0, Options.Count - 1);
        }

        public void Confirm()
        {
            var o = Options[Selected];
            if (o.locked) return;            // ロック中は不可
            OnDive?.Invoke(o.id);
        }

        /// <summary>解禁判定（例）。実データに接続して Options[Lunatic].locked を更新する。</summary>
        public static bool LunaticUnlocked(int followers, int powerLevel)
            => followers >= 300 || powerLevel >= 4;
    }
}
