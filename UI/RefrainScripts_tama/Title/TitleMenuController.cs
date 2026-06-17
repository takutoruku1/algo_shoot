using UnityEngine;
using System;

namespace Refrain.UI
{
    /// <summary>スタート画面。メニュー選択（↑↓）と決定（Z）。HTML: Refrain Title。</summary>
    public class TitleMenuController : MonoBehaviour
    {
        public enum TitleItem { NewGame, Continue, Gallery, Settings, Quit }

        public static readonly (TitleItem item, string jp, string en)[] Items =
        {
            (TitleItem.NewGame,  "はじめから", "NEW GAME"),
            (TitleItem.Continue, "つづきから", "CONTINUE"),
            (TitleItem.Gallery,  "ギャラリー", "GALLERY"),
            (TitleItem.Settings, "設定",       "SETTINGS"),
            (TitleItem.Quit,     "おわる",     "QUIT"),
        };

        public int Selected { get; private set; }
        public event Action<TitleItem> OnConfirm;

        public void Move(int dir)
        {
            Selected = (Selected + dir + Items.Length) % Items.Length;
            // 選択ハイライト = RefrainTheme.Purify（シアン）。
        }

        public void Confirm() => OnConfirm?.Invoke(Items[Selected].item);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
            if (Input.GetKeyDown(KeyCode.UpArrow))   Move(-1);
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Return)) Confirm();
        }
    }
}
