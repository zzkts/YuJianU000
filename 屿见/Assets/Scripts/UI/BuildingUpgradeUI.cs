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

        [Header("布局")]
        [Tooltip("按钮组中心相对建筑顶部的屏幕偏移（像素）")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 90f);

        [Tooltip("建筑中心往上抬多少世界单位再投影")]
        [SerializeField] private float worldHeightOffset = 4.2f;

        [Header("调试")]
        [SerializeField] private bool logActions = true;

        private bool wired;

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
            // 金币、等级、建筑位置每帧都可能变，用事件反而容易漏
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

            if (upgradeButtonText != null)
            {
                if (!hasTable)
                {
                    upgradeButtonText.text = "未配置升级表";
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
                upgradeButton.interactable = hasTable && !maxed && affordable > 0;
            }

            if (levelText != null)
            {
                levelText.text = BuildLevelText(mode);
            }

            if (modeToggle != null && modeToggle.gameObject.activeSelf != hasTable)
            {
                modeToggle.gameObject.SetActive(hasTable);
            }

            SetVisible(true);
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

            if (upgrade.IsMaxed)
            {
                return $"Lv.{level} / {max}（已满级）";
            }

            if (mode == BuildingUpgradeMode.MaxToInterval)
            {
                int target = upgrade.GetTargetLevel(mode);
                int total = upgrade.SelectedTable.GetTotalCost(level, target);
                return $"Lv.{level} / {max}｜升到 {target} 级共 {total} 金币";
            }

            return $"Lv.{level} / {max}｜下一级 {upgrade.NextCost} 金币";
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

            BuildingUpgradeMode mode = modeToggle != null ? modeToggle.Mode : BuildingUpgradeMode.OneLevel;
            bool success = upgrade.TryUpgrade(mode, out string message);

            if (logActions)
            {
                Debug.Log($"[BuildingUpgradeUI] 升级（{mode}）：{message}");
            }

            EventBus.Publish(new MessageShown(message, success));
            Refresh();
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
        }
    }
}
