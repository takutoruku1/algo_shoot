using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Refrain.UI
{
    /// <summary>
    /// ゲーム中 HUD（採用案 A: Clean Glass）。
    /// 体力ハート / BOMB / 浄化ゲージ / ボス穢れ(X プロフィール) / SCORE / X テレメトリ。
    /// 状態: Normal → Hit(被弾) → PurifyComplete(浄化100%)。
    /// </summary>
    public class HudController : MonoBehaviour
    {
        public enum HudState { Normal, Hit, PurifyComplete }

        [Header("Lives / Bomb")]
        [SerializeField] Image[] hearts;        // 役割色 = RefrainTheme.Hp
        [SerializeField] int maxLives = 6;
        [SerializeField] Transform bombPips;    // 紫ピップ (RefrainTheme.Mina)
        [SerializeField] int maxBombs = 6;

        [Header("Purify gauge")]
        [SerializeField] Image purifyFill;      // 0..1, シアン (RefrainTheme.Purify)
        [SerializeField] TMP_Text purifyLabel;  // "34%"

        [Header("Score / telemetry")]
        [SerializeField] TMP_Text scoreText;    // 金 (RefrainTheme.Score)
        [SerializeField] TMP_Text impressions;  // インプレ "イ 16.2k"
        [SerializeField] TMP_Text reposts;      // リポスト数

        [Header("Boss profile card (X)")]
        [SerializeField] TMP_Text bossName;     // 例: むだなわたし
        [SerializeField] TMP_Text bossHandle;   // @mudana_watashi
        [SerializeField] TMP_Text bossReplies;  // リプ数
        [SerializeField] Image    kegareFill;   // 穢れ 0..1 (RefrainTheme.Kegare)

        [Header("FX")]
        [SerializeField] Image    hurtEdge;     // 赤い縁の明滅
        [SerializeField] GameObject purifyBurst;

        int lives, bombs;
        long score;

        void Awake()
        {
            lives = maxLives; bombs = maxBombs;
            RefreshHearts(); RefreshBombs();
        }

        // ---- Lives ----
        public void SetLives(int v)
        {
            lives = Mathf.Clamp(v, 0, maxLives);
            RefreshHearts();
            if (lives <= 2) SetState(HudState.Hit);
        }
        public void Damage(int amount = 1) => SetLives(lives - amount);

        void RefreshHearts()
        {
            if (hearts == null) return;
            for (int i = 0; i < hearts.Length; i++)
            {
                bool on = i < lives;
                hearts[i].color = on ? RefrainTheme.Hp : new Color(RefrainTheme.Hp.r, RefrainTheme.Hp.g, RefrainTheme.Hp.b, 0.22f);
            }
        }

        // ---- Bomb ----
        public void SetBombs(int v) { bombs = Mathf.Clamp(v, 0, maxBombs); RefreshBombs(); }
        public bool UseBomb() { if (bombs <= 0) return false; bombs--; RefreshBombs(); return true; }
        void RefreshBombs()
        {
            if (bombPips == null) return;
            for (int i = 0; i < bombPips.childCount; i++)
            {
                var img = bombPips.GetChild(i).GetComponent<Image>();
                if (img) img.color = i < bombs ? RefrainTheme.Mina : new Color(RefrainTheme.Mina.r, RefrainTheme.Mina.g, RefrainTheme.Mina.b, 0.28f);
            }
        }

        // ---- Purify (0..100) ----
        public void SetPurify(float percent01)
        {
            percent01 = Mathf.Clamp01(percent01);
            if (purifyFill) purifyFill.fillAmount = percent01;
            if (purifyLabel) purifyLabel.text = Mathf.RoundToInt(percent01 * 100f) + "%";
            if (percent01 >= 1f) SetState(HudState.PurifyComplete);
        }

        // ---- Boss ----
        public void SetBoss(string name, string handle, int replies, float kegare01)
        {
            if (bossName)    bossName.text = name;
            if (bossHandle)  bossHandle.text = handle;
            if (bossReplies) bossReplies.text = replies.ToString("N0");
            if (kegareFill)  kegareFill.fillAmount = Mathf.Clamp01(kegare01);
        }

        // ---- Score ----
        public void AddScore(long v) { score += v; if (scoreText) scoreText.text = score.ToString("000,000"); }

        // ---- State ----
        public void SetState(HudState s)
        {
            if (hurtEdge)    hurtEdge.enabled = (s == HudState.Hit);
            if (purifyBurst) purifyBurst.SetActive(s == HudState.PurifyComplete);
            // PurifyComplete: 穢れ色(マゼンタ)→光(シアン〜金)へ転調する演出は Unity 側 Animator で。
        }
    }
}
