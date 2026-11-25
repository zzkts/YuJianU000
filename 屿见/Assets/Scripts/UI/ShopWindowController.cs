using UnityEngine;
using UnityEngine.UI;
using Yujian.Core;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑区里的轻量化商店浮窗控制器（2026-11-05 商店策划案）。
    ///
    /// 规则：
    ///   · 只有进入建筑区才显示右下角的「商店」入口按钮；
    ///   · 点击入口打开 / 再次点击关闭浮窗；
    ///   · 离开建筑区（点挖矿、再点建筑回观察等）自动关闭浮窗并隐藏入口；
    ///   · 商店不再作为底部导航的独立主系统，因此不参与 GameAreaController 的面板数组，
    ///     显隐只由本类负责。
    ///
    /// 2026-11-24：原料退出核心循环（<see cref="GameFeatureFlags.MaterialsEnabled"/> = false）后，
    /// 这家店只卖原料、已经没有可买的东西，入口与浮窗一律不出现。
    /// 组件与场景物体保留，原料回来时不用重做 UI。
    ///
    /// 本类不碰购买、不碰金币，只做显隐与区域联动。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopWindowController : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域状态。进入 / 离开建筑区时联动商店浮窗")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("轻量化商店窗口（Canvas/ShopPanel）")]
        [SerializeField] private GameObject shopWindow;

        [Tooltip("建筑区右下角的商店入口按钮")]
        [SerializeField] private GameObject entryButton;

        [Header("初始状态")]
        [Tooltip("进入 Play 时是否关闭浮窗。勾选后只有点击入口才打开")]
        [SerializeField] private bool startClosed = true;

        [Header("调试")]
        [SerializeField] private bool logWindowFlow = true;

        private Button entryButtonComponent;
        private bool isOpen;

        /// <summary>浮窗当前是否打开。</summary>
        public bool IsOpen => isOpen;

        /// <summary>编辑器向导调用，一次性接好三个引用。</summary>
        public void ConfigureForSetup(GameAreaController newAreas, GameObject window, GameObject entry)
        {
            areas = newAreas;
            shopWindow = window;
            entryButton = entry;
        }

        private void Awake()
        {
            ResolveDependencies();

            if (entryButton != null)
            {
                entryButtonComponent = entryButton.GetComponent<Button>();
            }

            if (!GameFeatureFlags.MaterialsEnabled)
            {
                // 店里只有原料，原料已剔除 → 入口永远不出现。
                // 这里立刻关掉，是为了避免 Start / 区域事件之前有一帧穿帮。
                if (entryButton != null)
                {
                    entryButton.SetActive(false);
                }

                if (logWindowFlow)
                {
                    Debug.Log("[ShopWindowController] 原料系统已关闭，材料商店入口保持隐藏。", this);
                }
            }
        }

        private void Start()
        {
            if (startClosed)
            {
                Close();
            }

            ApplyArea(areas != null ? areas.CurrentArea : GameArea.Observation);
        }

        private void OnEnable()
        {
            if (areas != null)
            {
                areas.OnAreaChanged += HandleAreaChanged;
            }

            if (entryButtonComponent != null)
            {
                entryButtonComponent.onClick.AddListener(Toggle);
            }
        }

        private void OnDisable()
        {
            if (areas != null)
            {
                areas.OnAreaChanged -= HandleAreaChanged;
            }

            if (entryButtonComponent != null)
            {
                entryButtonComponent.onClick.RemoveListener(Toggle);
            }
        }

        /// <summary>打开浮窗。原料已剔除时不做任何事（没有可买的东西）。</summary>
        public void Open()
        {
            if (!GameFeatureFlags.MaterialsEnabled)
            {
                if (logWindowFlow)
                {
                    Debug.Log("[ShopWindowController] 原料系统已关闭，商店不打开。", this);
                }

                return;
            }

            if (shopWindow == null)
            {
                Debug.LogError("[ShopWindowController] Shop Window 未配置，商店打不开。", this);
                return;
            }

            isOpen = true;
            shopWindow.SetActive(true);

            if (logWindowFlow)
            {
                Debug.Log("[ShopWindowController] 打开商店浮窗");
            }
        }

        /// <summary>关闭浮窗。</summary>
        public void Close()
        {
            isOpen = false;

            if (shopWindow != null && shopWindow.activeSelf)
            {
                shopWindow.SetActive(false);

                if (logWindowFlow)
                {
                    Debug.Log("[ShopWindowController] 关闭商店浮窗");
                }
            }
        }

        /// <summary>入口按钮用：开 / 关切换。</summary>
        public void Toggle()
        {
            if (isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void HandleAreaChanged(GameArea area)
        {
            ApplyArea(area);
        }

        /// <summary>
        /// 按区域应用：入口只在建筑区出现；离开建筑区时无条件关窗。
        /// 原料系统关闭时，入口在任何区域都不出现。
        /// </summary>
        private void ApplyArea(GameArea area)
        {
            bool inBuilding = area == GameArea.Building && GameFeatureFlags.MaterialsEnabled;

            if (entryButton != null && entryButton.activeSelf != inBuilding)
            {
                entryButton.SetActive(inBuilding);
            }

            if (!inBuilding)
            {
                Close();
            }
        }

        private void ResolveDependencies()
        {
            if (areas == null)
            {
                areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }

            if (areas == null)
            {
                Debug.LogError("[ShopWindowController] GameAreaController 未配置，" +
                               "商店入口不会随建筑区显隐。请重跑菜单「屿见/商店/重建轻量化商店窗口」。", this);
            }

            if (shopWindow == null)
            {
                Debug.LogError("[ShopWindowController] Shop Window 未配置，商店打不开。", this);
            }
        }
    }
}
