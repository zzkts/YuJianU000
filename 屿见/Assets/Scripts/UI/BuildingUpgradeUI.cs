using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Core.Events;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑升级界面（策划案 3.4）。
    ///
    /// 显示：
    ///   · 建筑上方的升级按钮，文字是「升级 ×可升级次数」；
    ///   · 按钮下方显示当前等级（Lv.N / 上限）与本次费用；
    ///   · 左下角的升级方式切换块（x1 / Max），只在升级界面打开时出现。
    ///
    /// 退出：选中被清掉（点击别处 / 点另一座建筑时由 BuildingPointerInteraction 处理）。
    ///
    /// 本类只读状态、转发点击，不扣钱也不改等级。
    /// </summary>
    public class BuildingUpgradeUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑升级规则层")]
        [SerializeField] private BuildingUpgrade upgrade;

        [Tooltip("建筑迁移。迁移中隐藏升级界面，避免两套按钮叠在同一座建筑上")]
        [SerializeField] private BuildingMigration migration;

        [Tooltip("建筑管理器。蓝图在场时隐藏升级界面")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("区域状态。只在建筑区显示；留空则不做区域判断")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("把建筑世界坐标投影到屏幕的相机。留空则用 MainCamera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("按钮所在的 Canvas。留空则从父物体上找")]
        [SerializeField] private Canvas canvas;

        [Header("界面")]
        [Tooltip("真正显示 / 隐藏的内容根（组件挂在常开的容器上）")]
        [SerializeField] private RectTransform actionRoot;

        [Tooltip("升级按钮")]
        [SerializeField] private Button upgradeButton;

        [Tooltip("升级按钮上的文字，显示「升级 ×N」")]
        [SerializeField] private Text upgradeButtonText;

        [Tooltip("按钮下方的等级文字，显示「Lv.N / 上限」与费用")]
        [SerializeField] private Text levelText;

        [Tooltip("左下角的 x1 / Max 切换块")]
        [SerializeField] private BuildingUpgradeModeToggle modeToggle;

        [Tooltip("强化节点选择窗口。留空会在运行时自动挂一个（占位 UI，不需要改场景）")]
        [SerializeField] private BuildingUpgradeNodeUI nodeUI;

        [Header("强化节点配色")]
        [Tooltip("有待选强化节点时，升级按钮变绿")]
        [SerializeField] private Color pendingNodeColor = new Color(0.35f, 0.9f, 0.45f, 1f);

        [Header("布局")]
        [Tooltip("按钮组中心相对建筑顶部的屏幕偏移（像素）")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 90f);

        [Tooltip("建筑中心往上抬多少世界单位再投影")]
        [SerializeField] private float worldHeightOffset = 4.2f;

        [Header("调试")]
        [SerializeField] private bool logActions = true;

        private bool wired;
        private ColorBlock defaultButtonColors;
        private bool buttonColorsCaptured;
        private bool pendingColorsApplied;

        private void Awake()
        {
            if (upgrade == null)
            {
                Debug.LogError("[BuildingUpgradeUI] BuildingUpgrade 未配置，升级界面不会工作。", this);
            }

            if (upgradeButton == null)
            {
                Debug.LogError("[BuildingUpgradeUI] 升级按钮未配置，" +
                               "请运行菜单「屿见/建筑/配置建筑迁移与升级」。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (actionRoot == null)
            {
                actionRoot = transform.childCount > 0
                    ? transform.GetChild(0) as RectTransform
                    : transform as RectTransform;
            }

            if (actionRoot == transform as RectTransform)
            {
                Debug.LogError("[BuildingUpgradeUI] Action Root 不能指向本组件所在的物体：" +
                               "隐藏它会把组件一起停掉。请指向其 Content 子物体。", this);
                actionRoot = null;
            }

            EnsureNodeUI();
        }

        /// <summary>
        /// 强化节点窗口默认在运行时自建（占位），不需要跑向导改场景。
        /// 若以后要用场景里手工排版的窗口，把引用挂到 nodeUI 字段即可。
        /// </summary>
        private void EnsureNodeUI()
        {
            if (nodeUI == null)
            {
                nodeUI = GetComponent<BuildingUpgradeNodeUI>();
            }

            if (nodeUI == null)
            {
                nodeUI = gameObject.AddComponent<BuildingUpgradeNodeUI>();
            }

            nodeUI.Configure(upgrade);
        }

        private void OnEnable()
        {
            WireButton();
            Refresh();
        }

        private void OnDisable()
        {
            UnwireButton();
            HideAll();
        }

        private void LateUpdate()
        {
            // 垃圾、等级、建筑位置每帧都可能变，用事件反而容易漏
            Refresh();
        }

        private void Refresh()
        {
            if (actionRoot == null || upgrade == null)
            {
                HideAll();
                return;
            }

            if (!ShouldShow())
            {
                HideAll();
                return;
            }

            GameObject building = upgrade.SelectedBuilding;

            if (building == null)
            {
                HideAll();
                return;
            }

            Vector3 worldPosition = building.transform.position + Vector3.up * worldHeightOffset;

            if (!WorldToCanvas.TryProject(viewCamera, canvas, worldPosition, screenOffset,
                    out Vector2 localPosition))
            {
                HideAll();
                return;
            }

            actionRoot.anchoredPosition = WorldToCanvas.ClampToCanvas(localPosition, actionRoot, canvas);

            BuildingUpgradeMode mode = modeToggle != null ? modeToggle.Mode : BuildingUpgradeMode.OneLevel;
            int affordable = upgrade.GetAffordableLevelCount(mode);
            bool maxed = upgrade.IsMaxed;
            bool hasTable = upgrade.SelectedTable != null;
            bool pendingNode = upgrade.HasPendingNode;

            if (upgradeButtonText != null)
            {
                if (!hasTable)
                {
                    upgradeButtonText.text = "未配置升级表";
                }
                else if (pendingNode)
                {
                    upgradeButtonText.text = "选择强化节点";
                }
                else if (maxed)
                {
                    upgradeButtonText.text = "已满级";
                }
                else
                {
                    upgradeButtonText.text = $"升级 ×{affordable}";
                }
            }

            if (upgradeButton != null)
            {
                upgradeButton.interactable = hasTable &&
                    (pendingNode || (!maxed && affordable > 0));
            }

            ApplyPendingNodeButtonColor(pendingNode);

            if (levelText != null)
            {
                levelText.text = BuildLevelText(mode);
            }

            if (modeToggle != null && modeToggle.gameObject.activeSelf != hasTable)
            {
                modeToggle.gameObject.SetActive(hasTable);
            }

            SetVisible(true);

            if (nodeUI != null)
            {
                if (pendingNode)
                {
                    nodeUI.ShowPendingNode();
                }
                else
                {
                    nodeUI.HideIfNoPending();
                }
            }
        }

        private bool ShouldShow()
        {
            if (!upgrade.HasSelection)
            {
                return false;
            }

            if (buildingManager != null && buildingManager.IsPlacing)
            {
                return false;
            }

            if (migration != null && migration.IsActive)
            {
                return false;
            }

            return areas == null || areas.CurrentArea == GameArea.Building;
        }

        private string BuildLevelText(BuildingUpgradeMode mode)
        {
            int level = upgrade.CurrentLevel;
            int max = upgrade.MaxLevel;

            if (upgrade.SelectedTable == null)
            {
                return $"Lv.{level}";
            }

            if (upgrade.HasPendingNode)
            {
                return $"Lv.{level} / {max}｜请选择强化节点";
            }

            if (upgrade.IsMaxed)
            {
                return $"Lv.{level} / {max}（已满级）";
            }

            if (mode == BuildingUpgradeMode.MaxToInterval)
            {
                int target = upgrade.GetTargetLevel(mode);
                int total = upgrade.SelectedTable.GetTotalCost(level, target);
                return $"Lv.{level} / {max}｜升到 {target} 级共 {total} 垃圾";
            }

            return $"Lv.{level} / {max}｜下一级 {upgrade.NextCost} 垃圾";
        }

        private void WireButton()
        {
            if (wired || upgradeButton == null)
            {
                return;
            }

            wired = true;
            upgradeButton.onClick.AddListener(HandleUpgradeClicked);
        }

        private void UnwireButton()
        {
            if (!wired || upgradeButton == null)
            {
                return;
            }

            wired = false;
            upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
        }

        private void HandleUpgradeClicked()
        {
            if (upgrade == null)
            {
                return;
            }

            // 待选节点时，这个按钮只负责把选择窗口重新拉到最前
            if (upgrade.HasPendingNode)
            {
                if (nodeUI != null)
                {
                    nodeUI.ShowPendingNode();
                }

                return;
            }

            BuildingUpgradeMode mode = modeToggle != null ? modeToggle.Mode : BuildingUpgradeMode.OneLevel;
            bool success = upgrade.TryUpgrade(mode, out string message);

            if (logActions)
            {
                Debug.Log($"[BuildingUpgradeUI] 升级（{mode}）：{message}");
            }

            EventBus.Publish(new MessageShown(message, success));
            Refresh();
        }

        /// <summary>
        /// 策划案 3.4：到达升级节点时升级按钮变绿。
        /// 改 ColorBlock 而不是直接改 Image.color，避免鼠标悬停时被 Selectable 的过渡色盖掉。
        /// </summary>
        private void ApplyPendingNodeButtonColor(bool pending)
        {
            if (upgradeButton == null)
            {
                return;
            }

            if (!buttonColorsCaptured)
            {
                defaultButtonColors = upgradeButton.colors;
                buttonColorsCaptured = true;
            }

            if (pending == pendingColorsApplied)
            {
                return;
            }

            pendingColorsApplied = pending;

            if (!pending)
            {
                upgradeButton.colors = defaultButtonColors;
                return;
            }

            ColorBlock colors = defaultButtonColors;
            colors.normalColor = pendingNodeColor;
            colors.highlightedColor = Color.Lerp(pendingNodeColor, Color.white, 0.2f);
            colors.pressedColor = Color.Lerp(pendingNodeColor, Color.black, 0.2f);
            colors.selectedColor = pendingNodeColor;
            upgradeButton.colors = colors;
        }

        private void SetVisible(bool visible)
        {
            if (actionRoot == null)
            {
                return;
            }

            if (actionRoot.gameObject.activeSelf == visible)
            {
                return;
            }

            actionRoot.gameObject.SetActive(visible);

            if (visible)
            {
                transform.SetAsLastSibling();
            }
        }

        /// <summary>
        /// 连左下角的切换块一起收起。切换块挂在常开的容器上（这样它能锚在屏幕左下角），
        /// 不跟着 Content 一起隐藏，所以必须由本类显式管。
        /// </summary>
        private void HideAll()
        {
            SetVisible(false);

            if (modeToggle != null && modeToggle.gameObject.activeSelf)
            {
                modeToggle.gameObject.SetActive(false);
            }

            // 有待选节点时窗口必须留着（强制选择），否则玩家可以靠切区域逃掉
            if (nodeUI != null)
            {
                nodeUI.HideIfNoPending();
            }
        }
    }
}
