using UnityEngine;
using UnityEngine.UI;
using Yujian.Core;
using Yujian.Levels;

namespace Yujian.UI
{
    /// <summary>
    /// 阶段 6：屏幕底部的 [挖矿] [建筑] 导航条。
    /// 2026-11-05 商店策划案：商店不再是底部主系统，改由建筑区右下角的入口打开浮窗，
    /// 因此这里移除了「商店」按钮。
    ///
    /// 它**只做转发**：点哪个按钮就调 <see cref="GameAreaController.SetArea"/>，
    /// 相机与面板由订阅了区域事件的系统各自负责。本类不持有任何状态，
    /// 和阶段 3 的 <c>BuildingUI</c> 是同一种角色（见 开发进度.md 第五节）。
    ///
    /// 唯一的附加规则：**再次点击当前系统的按钮**不是「无操作」，而是退出该系统、
    /// 进入观察模式（<see cref="GameAreaController.EnterObservationMode"/>）。
    ///
    /// 「清洁」按钮会记住玩家上一次进入的关卡：
    ///   · 在关卡内：再点一次退出到观察模式（原行为）；
    ///   · 在岛上 / 建筑系统：回到上一次进入的关卡（走正常的关卡入口流程）；
    ///   · 从未进过关卡：自动进入关卡列表里编号最低的一关；
    ///   · 等级不够：由 LevelManager 提示「当前等级不足，请建更多建筑」。
    ///
    /// 按钮的 OnClick 由编辑器向导「屿见/整合/配置 MVP 整合（阶段 6）」接成持久监听
    /// （决策 15 的老做法：接线在场景里看得见、改得动）。
    /// 两个 Show* 方法就是给 UnityEvent 用的，所以必须是 public 且无参数。
    /// </summary>
    public class BottomNavUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域状态。三个按钮都是转发给它的")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("关卡管理器。用来记住并回到玩家上一次进入的关卡；留空会自动查找")]
        [SerializeField] private LevelManager levelManager;

        [Header("按钮")]
        [SerializeField] private Button miningButton;
        [SerializeField] private Button buildingButton;

        [Header("两侧布局")]
        [Tooltip("勾选后把「清洁」「建筑」分别贴到屏幕左下 / 右下，底部中间让给垃圾背包（2026-11-25 用户拍板）")]
        [SerializeField] private bool placeButtonsOnSides = true;

        [Tooltip("贴边留白（像素）")]
        [SerializeField] private Vector2 sidePadding = new Vector2(24f, 0f);

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
                               "请重新运行菜单「屿见/整合/配置 MVP 整合（阶段 6）」。", this);
            }

            ResolveLevelManager();

            WarnIfMissingButton(miningButton, "挖矿");
            WarnIfMissingButton(buildingButton, "建筑");

            ApplySideLayout();
            RefreshHighlight(areas != null ? areas.CurrentArea : GameArea.Mining);
        }

        /// <summary>
        /// 把两个按钮贴到屏幕两侧。运行时改锚点，不动场景序列化值：
        /// 想回到居中布局，取消 Inspector 上的「Place Buttons On Sides」即可。
        /// 编辑器向导在配置背包时会调一次同名 public 方法，保证保存进场景的也是两侧布局。
        /// </summary>
        public void ApplySideLayout()
        {
            if (!placeButtonsOnSides)
            {
                return;
            }

            PlaceButtonAtSide(miningButton, true);
            PlaceButtonAtSide(buildingButton, false);
        }

        private void PlaceButtonAtSide(Button button, bool left)
        {
            if (button == null)
            {
                return;
            }

            RectTransform rect = button.transform as RectTransform;

            if (rect == null)
            {
                return;
            }

            // 父物体若挂了 LayoutGroup，锚点会被它每帧覆盖，先关掉（本项目导航栏目前没有）
            if (rect.parent != null)
            {
                LayoutGroup group = rect.parent.GetComponent<LayoutGroup>();

                if (group != null && group.enabled)
                {
                    group.enabled = false;
                }
            }

            Vector2 anchor = new Vector2(left ? 0f : 1f, 0.5f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = new Vector2(left ? sidePadding.x : -sidePadding.x, sidePadding.y);

            if (logNavFlow)
            {
                Debug.Log($"[BottomNavUI] 「{(left ? "清洁" : "建筑")}」移到屏幕{(left ? "左" : "右")}侧。", this);
            }
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

        /// <summary>
        /// 「清洁」按钮。
        /// 已经在关卡内：沿用原行为（再点一次退出到观察模式）；
        /// 不在关卡内：上一次进的关还没通关就继续那一关；
        /// 已通关 / 从未进过关卡：自动进入编号最低的未通关关卡（等级不够时由 LevelManager 提示）。
        /// </summary>
        public void ShowMiningArea()
        {
            if (areas != null && areas.CurrentArea == GameArea.Mining)
            {
                RequestArea(GameArea.Mining);
                return;
            }

            LevelManager manager = ResolveLevelManager();

            if (manager == null)
            {
                ToastUI.Show("关卡系统未就绪");
                return;
            }

            LevelEntrance last = manager.LastEnteredLevel;

            if (last != null && !manager.IsLevelCleared(last))
            {
                if (logNavFlow)
                {
                    Debug.Log($"[BottomNavUI] 继续上一次进入的关卡「{last.name}」");
                }

                // 走正常的关卡入口流程：解锁检查 + 黑场转场 + 区域切换。
                manager.TryEnter(last);
                return;
            }

            LevelEntrance nextLevel = manager.FindNextLevelEntrance();

            if (nextLevel != null)
            {
                if (logNavFlow)
                {
                    Debug.Log($"[BottomNavUI] 自动进入下一关「{nextLevel.name}」");
                }

                manager.TryEnter(nextLevel);
                return;
            }

            ToastUI.Show("暂无可进入的关卡");
        }

        private LevelManager ResolveLevelManager()
        {
            if (levelManager == null)
            {
                levelManager = FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include);
            }

            return levelManager;
        }

        /// <summary>「建筑」按钮。</summary>
        public void ShowBuildingArea()
        {
            RequestArea(GameArea.Building);
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

            if (areas.CurrentArea == area)
            {
                // 再点一次已经在的系统 = 退出它，落到观察模式。
                // 这里必须由导航判断，不能交给 SetArea：SetArea 对「切到当前区域」
                // 是刻意忽略的（避免重复点按钮重置相机），语义不同。
                if (logNavFlow)
                {
                    Debug.Log($"[BottomNavUI] 再次点击「{area.ToChineseName()}」，退出该系统，进入观察模式");
                }

                areas.EnterObservationMode();
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
                               "请重新运行菜单「屿见/整合/配置 MVP 整合（阶段 6）」。", this);
            }
        }
    }
}
