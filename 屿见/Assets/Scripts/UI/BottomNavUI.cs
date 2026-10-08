using UnityEngine;
using UnityEngine.UI;
using Yujian.Core;

namespace Yujian.UI
{
    /// <summary>
    /// 阶段 6：屏幕底部的 [挖矿] [建筑] [商店] 导航条。
    ///
    /// 它**只做转发**：点哪个按钮就调 <see cref="GameAreaController.SetArea"/>，
    /// 相机与面板由订阅了区域事件的系统各自负责。本类不持有任何状态，
    /// 和阶段 3 的 <c>BuildingUI</c> 是同一种角色（见 开发进度.md 第五节）。
    ///
    /// 按钮的 OnClick 由编辑器向导「屿见/配置 MVP 整合（阶段 6）」接成持久监听
    /// （决策 15 的老做法：接线在场景里看得见、改得动）。
    /// 三个 Show* 方法就是给 UnityEvent 用的，所以必须是 public 且无参数。
    /// </summary>
    public class BottomNavUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域状态。三个按钮都是转发给它的")]
        [SerializeField] private GameAreaController areas;

        [Header("按钮")]
        [SerializeField] private Button miningButton;
        [SerializeField] private Button buildingButton;
        [SerializeField] private Button shopButton;

        [Header("高亮配色")]
        [Tooltip("不在当前区域时的按钮底色")]
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("当前区域的按钮底色")]
        [SerializeField] private Color activeColor = new Color(1f, 0.84f, 0.42f, 1f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印导航点击日志")]
        [SerializeField] private bool logNavFlow = true;

        private void OnEnable()
        {
            if (areas != null)
            {
                areas.OnAreaChanged += HandleAreaChanged;
            }
            else
            {
                Debug.LogError("[BottomNavUI] GameAreaController 未配置，底部导航点了不会有任何反应。" +
                               "请重新运行菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
            }

            WarnIfMissingButton(miningButton, "挖矿");
            WarnIfMissingButton(buildingButton, "建筑");
            WarnIfMissingButton(shopButton, "商店");

            RefreshHighlight(areas != null ? areas.CurrentArea : GameArea.Mining);
        }

        private void OnDestroy()
        {
            // 阶段 5 的教训：不留残留订阅
            if (areas != null)
            {
                areas.OnAreaChanged -= HandleAreaChanged;
            }
        }

        // ---------------- 给 UnityEvent（按钮 OnClick）用的入口 ----------------

        /// <summary>「挖矿」按钮。</summary>
        public void ShowMiningArea()
        {
            RequestArea(GameArea.Mining);
        }

        /// <summary>「建筑」按钮。</summary>
        public void ShowBuildingArea()
        {
            RequestArea(GameArea.Building);
        }

        /// <summary>「商店」按钮。</summary>
        public void ShowShopArea()
        {
            RequestArea(GameArea.Shop);
        }

        // ---------------- 内部 ----------------

        private void RequestArea(GameArea area)
        {
            if (areas == null)
            {
                Debug.LogError($"[BottomNavUI] 点了「{area.ToChineseName()}」但 GameAreaController 没配，" +
                               "区域不会切换。", this);
                return;
            }

            if (logNavFlow)
            {
                Debug.Log($"[BottomNavUI] 点击「{area.ToChineseName()}」");
            }

            areas.SetArea(area);
        }

        private void HandleAreaChanged(GameArea area)
        {
            RefreshHighlight(area);
        }

        private void RefreshHighlight(GameArea current)
        {
            ApplyTint(miningButton, current == GameArea.Mining);
            ApplyTint(buildingButton, current == GameArea.Building);
            ApplyTint(shopButton, current == GameArea.Shop);
        }

        /// <summary>
        /// 给按钮上色。
        ///
        /// 这里改的是 <see cref="ColorBlock"/> 而**不是** <c>Image.color</c>：
        /// Selectable 的状态机（normal/highlighted/pressed/selected）每次状态变化都会
        /// 拿 ColorBlock 重算一次 targetGraphic 的颜色，直接写 Image.color 会在鼠标悬停时被覆盖掉。
        /// 所以把四个状态色一起写成同一个值——顺带让悬停不会闪。
        /// </summary>
        private void ApplyTint(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }

            Color tint = active ? activeColor : normalColor;

            ColorBlock block = button.colors;
            block.normalColor = tint;
            block.highlightedColor = tint;
            block.selectedColor = tint;
            block.pressedColor = new Color(tint.r * 0.85f, tint.g * 0.85f, tint.b * 0.85f, tint.a);
            button.colors = block;
        }

        private void WarnIfMissingButton(Button button, string label)
        {
            if (button == null)
            {
                Debug.LogError($"[BottomNavUI] 「{label}」按钮未配置，这个按钮不会被高亮。" +
                               "请重新运行菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
            }
        }
    }
}
