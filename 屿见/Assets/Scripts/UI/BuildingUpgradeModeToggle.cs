using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yujian.Building;

namespace Yujian.UI
{
    /// <summary>
    /// 升级方式切换块（策划案 3.4：整体 UI 左下角的 x1 / Max 模块）。
    ///
    /// 点一次切一次；光标悬停时在按钮上方显示当前方式的说明
    /// （「x1——每次升一级」/「Max——升级到等级间隔」）。
    ///
    /// 只拥有「当前是哪种方式」这一份状态，具体扣钱升级在 BuildingUpgrade。
    /// </summary>
    public class BuildingUpgradeModeToggle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("界面")]
        [Tooltip("点击切换的按钮")]
        [SerializeField] private Button button;

        [Tooltip("按钮上的文字，显示 x1 / Max")]
        [SerializeField] private Text labelText;

        [Tooltip("悬停提示文字。留空则不显示提示")]
        [SerializeField] private Text tooltipText;

        [Header("文案")]
        [Tooltip("一级一级升时的按钮文字")]
        [SerializeField] private string oneLevelLabel = "x1";

        [Tooltip("升到下一个等级间隔时的按钮文字")]
        [SerializeField] private string maxLabel = "Max";

        [Tooltip("x1 模式的悬停说明")]
        [SerializeField] private string oneLevelTooltip = "x1——每次升一级";

        [Tooltip("Max 模式的悬停说明")]
        [SerializeField] private string maxTooltip = "Max——升级到等级间隔";

        private bool wired;

        /// <summary>当前升级方式。</summary>
        public BuildingUpgradeMode Mode { get; private set; } = BuildingUpgradeMode.OneLevel;

        /// <summary>方式变化时触发。</summary>
        public event Action<BuildingUpgradeMode> OnModeChanged;

        private void Awake()
        {
            WireButton();
            Refresh();
            HideTooltip();
        }

        private void OnEnable()
        {
            WireButton();
            Refresh();
            HideTooltip();
        }

        private void OnDisable()
        {
            UnwireButton();
            HideTooltip();
        }

        /// <summary>切换一次方式。</summary>
        public void Cycle()
        {
            Mode = Mode == BuildingUpgradeMode.OneLevel
                ? BuildingUpgradeMode.MaxToInterval
                : BuildingUpgradeMode.OneLevel;

            Refresh();
            OnModeChanged?.Invoke(Mode);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltipText == null)
            {
                return;
            }

            tooltipText.text = Mode == BuildingUpgradeMode.OneLevel ? oneLevelTooltip : maxTooltip;
            tooltipText.gameObject.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideTooltip();
        }

        private void WireButton()
        {
            if (wired || button == null)
            {
                return;
            }

            wired = true;
            button.onClick.AddListener(Cycle);
        }

        private void UnwireButton()
        {
            if (!wired || button == null)
            {
                return;
            }

            wired = false;
            button.onClick.RemoveListener(Cycle);
        }

        private void Refresh()
        {
            if (labelText != null)
            {
                labelText.text = Mode == BuildingUpgradeMode.OneLevel ? oneLevelLabel : maxLabel;
            }

            if (tooltipText != null && tooltipText.gameObject.activeSelf)
            {
                tooltipText.text = Mode == BuildingUpgradeMode.OneLevel ? oneLevelTooltip : maxTooltip;
            }
        }

        private void HideTooltip()
        {
            if (tooltipText != null && tooltipText.gameObject.activeSelf)
            {
                tooltipText.gameObject.SetActive(false);
            }
        }
    }
}
