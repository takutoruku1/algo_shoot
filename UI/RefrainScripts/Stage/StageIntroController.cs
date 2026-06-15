using UnityEngine;
using System;

namespace Refrain.UI
{
    /// <summary>STAGE START = 相手の X 投稿へ「ダイブ」する導入。HTML: Refrain Screens / STAGE START。</summary>
    public class StageIntroController : MonoBehaviour
    {
        [Serializable]
        public struct TargetPost
        {
            public string displayName;  // レイ
            public string handle;       // @rei_0w0
            public string body;         // ごめん、もう無理かも。…
            public int    replies;
            public int    reposts;
            public int    impressions;
            public string time;         // 3:14 AM
        }

        [SerializeField] int stageNumber = 1;
        [SerializeField] TargetPost target;
        public event Action OnDive;

        /// <summary>「心の核 検出」→ スキャン → 大見出し "○○ へダイブ" を表示する流れの起点。</summary>
        public void Show(int stage, TargetPost post) { stageNumber = stage; target = post; }

        public string Headline => target.displayName + " へダイブ";

        public void Dive() => OnDive?.Invoke();

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Z)) Dive();
        }
    }
}
