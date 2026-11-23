using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core.Events;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑升级节点选择窗口（策划案 3.4）的运行时逻辑。
    ///
    /// ⚠ 本组件**不生成任何 UI、不写死任何尺寸 / 颜色 / 字号**。
    ///   场景层级由菜单「屿见/建筑/配置建筑强化节点界面」生成，
    ///   位置、字体、配色、按钮样式全部在 Hierarchy / Inspector 里自己调。
    ///
    /// 运行时只做四件事：
    ///   1. 有待选节点时显示 root，没有时收起；
    ///   2. 把节点标题 / 说明写进标题、说明文本；
    ///   3. 按配置的选项数量克隆 <see cref="optionTemplate"/>，填名称与描述；
    ///   4. 玩家点选项 → 显示确认框 → 是 / 否。
    ///
    /// 强制规则：窗口没有关闭入口，选完之前不能切建筑或退出（由 BuildingUpgrade 兜住）。
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingUpgradeNodeUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑升级规则层。留空会明确报错")]
        [SerializeField] private BuildingUpgrade upgrade;

        [Header("场景 UI 引用（全部可在 Hierarchy 里自己摆）")]
        [Tooltip("全屏遮罩根。显示 / 隐藏的是它；建议盖一层黑色 50% 透明 Image")]
        [SerializeField] private GameObject root;

        [Tooltip("标题文本：显示「建筑强化 · N 级节点」")]
        [SerializeField] private Text titleText;

        [Tooltip("说明文本：显示建筑名、当前等级与「选择后不可更改」")]
        [SerializeField] private Text subtitleText;

        [Tooltip("选项按钮的父节点。建议挂 VerticalLayoutGroup 自动排列克隆出来的选项")]
        [SerializeField] private RectTransform optionContainer;

        [Tooltip("选项按钮模板。运行时克隆它并填文字；模板本身保持隐藏")]
        [SerializeField] private BuildingUpgradeOptionUI optionTemplate;

        [Tooltip("确认遮罩根：点选项后显示，包含确认文字与「是 / 否」按钮")]
        [SerializeField] private GameObject confirmPanel;

        [Tooltip("确认框里的说明文字")]
        [SerializeField] private Text confirmText;

        [Tooltip("确认「是」按钮")]
        [SerializeField] private Button confirmYesButton;

        [Tooltip("确认「否」按钮")]
        [SerializeField] private Button confirmNoButton;

        [Header("文案模板（可在 Inspector 改）")]
        [Tooltip("{0} = 节点触发等级")]
        [SerializeField] private string titleFormat = "建筑强化 · {0} 级节点";

        [Tooltip("{0} = 建筑名，{1} = 当前等级")]
        [SerializeField] private string subtitleFormat = "「{0}」已升到 {1} 级，请选择一项强化（选择后不可更改）";

        [Tooltip("{0} = 强化名称，{1} = 效果描述（含自动生成的数值摘要）")]
        [SerializeField] private string confirmFormat = "确认选择「{0}」？\n\n{1}\n\n选择后不可更改。";

        [Header("调试")]
        [SerializeField] private bool logNodeFlow = true;

        private bool yesWired;
        private bool noWired;
        private bool warnedMissingRefs;
        private string shownNodeId;
        private GameObject shownBuilding;
        private BuildingEnhancementOption pendingChoice;

        /// <summary>由 BuildingUpgradeUI 接线时调用；也用于场景里手动指定 upgrade。</summary>
        public void Configure(BuildingUpgrade newUpgrade)
        {
            upgrade = newUpgrade;
            WireConfirmButtons();
        }

        private void Awake()
        {
            HideOptionTemplate();
            ValidateReferences();
            WireConfirmButtons();
        }

        private void OnEnable()
        {
            HideOptionTemplate();
            WireConfirmButtons();
        }

        private void LateUpdate()
        {
            if (upgrade == null)
            {
                Hide();
                return;
            }

            BuildingEnhancementNode node = upgrade.PendingNode;

            if (node == null || upgrade.SelectedBuilding == null)
            {
                Hide();
                return;
            }

            if (!ValidateReferences())
            {
                return;
            }

            root.SetActive(true);

            if (shownNodeId != node.NodeId || shownBuilding != upgrade.SelectedBuilding)
            {
                shownNodeId = node.NodeId;
                shownBuilding = upgrade.SelectedBuilding;
                RebuildOptions(node);
            }
        }

        /// <summary>把窗口拉到最前并立即刷新。窗口本来就常显时是幂等的。</summary>
        public void ShowPendingNode()
        {
            HideOptionTemplate();

            if (!ValidateReferences() || upgrade == null || !upgrade.HasPendingNode)
            {
                return;
            }

            root.SetActive(true);

            // 整个强化窗口容器也要置顶，否则升级按钮组后显示时会盖住强化选项
            transform.SetAsLastSibling();
            root.transform.SetAsLastSibling();
            LateUpdate();
        }

        /// <summary>没有待选节点时收起。有待选节点时什么都不做（强制选择不能关）。</summary>
        public void HideIfNoPending()
        {
            if (upgrade != null && upgrade.HasPendingNode)
            {
                return;
            }

            Hide();
        }

        private void Hide()
        {
            if (root != null && root.activeSelf)
            {
                root.SetActive(false);
            }

            HideConfirm();
            shownNodeId = null;
            shownBuilding = null;
        }

        // ---------- 选项列表 ----------

        private void RebuildOptions(BuildingEnhancementNode node)
        {
            HideOptionTemplate();

            // 清掉上一次克隆的按钮，模板本身保留并保持隐藏
            for (int i = optionContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = optionContainer.GetChild(i);

                if (optionTemplate != null && child == optionTemplate.transform)
                {
                    continue;
                }

                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            HideConfirm();

            PlacedBuildingInfo info = upgrade.SelectedInfo;
            string buildingName = info != null && info.Data != null ? info.Data.BuildingName : "建筑";

            if (titleText != null)
            {
                titleText.text = string.Format(titleFormat, node.TriggerLevel);
            }

            if (subtitleText != null)
            {
                subtitleText.text = string.Format(subtitleFormat, buildingName, upgrade.CurrentLevel);
            }

            List<BuildingEnhancementOption> options = upgrade.GetPendingNodeOptions();

            if (options.Count == 0)
            {
                // 节点表已经会把「没有任何有效选项」的节点跳过，正常不会走到这里
                Debug.LogError($"[BuildingUpgradeNodeUI] 节点「{node.NodeId}」没有有效选项。" +
                               "请检查强化节点表配置。", this);
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                BuildingEnhancementOption option = options[i];
                BuildingUpgradeOptionUI item = Instantiate(optionTemplate, optionContainer);
                item.gameObject.name = $"选项_{option.OptionId}";
                item.gameObject.SetActive(true);

                BuildingEnhancementOption captured = option;
                item.Bind(captured, () => ShowConfirm(captured));
            }

            // 容器与内容都置顶：按钮组即使同帧被重新显示，也会排在强化窗口前面
            transform.SetAsLastSibling();
            root.transform.SetAsLastSibling();
        }

        // ---------- 确认框 ----------

        private void ShowConfirm(BuildingEnhancementOption option)
        {
            if (option == null || confirmPanel == null)
            {
                return;
            }

            pendingChoice = option;

            if (confirmText != null)
            {
                confirmText.text = string.Format(
                    confirmFormat, option.DisplayName, option.GetFullDescription());
            }

            confirmPanel.SetActive(true);
            confirmPanel.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            if (confirmPanel != null && confirmPanel.activeSelf)
            {
                confirmPanel.SetActive(false);
            }

            pendingChoice = null;
        }

        /// <summary>
        /// 编辑器里也守住这条不变量：模板一旦被误勾成激活，下次 Inspector 刷新 / 场景重载时自动关掉。
        /// 这样不用等进 Play 才看不到占位卡。
        /// </summary>
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                HideOptionTemplate();
            }
        }

        /// <summary>
        /// 选项模板只是克隆源，永远不能在界面上显示。
        /// 即使场景里被误勾成激活，运行时也会强制关掉，避免占位文字被当成一张真实选项。
        /// </summary>
        private void HideOptionTemplate()
        {
            if (optionTemplate != null && optionTemplate.gameObject.activeSelf)
            {
                optionTemplate.gameObject.SetActive(false);
            }
        }

        private void WireConfirmButtons()
        {
            if (confirmYesButton != null && !yesWired)
            {
                yesWired = true;
                confirmYesButton.onClick.AddListener(HandleConfirmYes);
            }

            if (confirmNoButton != null && !noWired)
            {
                noWired = true;
                confirmNoButton.onClick.AddListener(HandleConfirmNo);
            }
        }

        private void HandleConfirmYes()
        {
            if (upgrade == null || pendingChoice == null)
            {
                HideConfirm();
                return;
            }

            string optionId = pendingChoice.OptionId;
            bool success = upgrade.TrySelectEnhancement(optionId, out string message);

            EventBus.Publish(new MessageShown(message, success));

            if (success)
            {
                HideConfirm();
                shownNodeId = null;
                shownBuilding = null;

                if (logNodeFlow)
                {
                    Debug.Log($"[BuildingUpgradeNodeUI] {message}");
                }

                LateUpdate();
                return;
            }

            if (confirmText != null)
            {
                confirmText.text = $"{message}\n\n请重新选择。";
            }
        }

        private void HandleConfirmNo()
        {
            HideConfirm();
        }

        // ---------- 校验 ----------

        /// <summary>
        /// 引用不齐时明确报错一次，并提示去点菜单。不会静默失败，也不会每帧刷屏。
        /// </summary>
        private bool ValidateReferences()
        {
            if (root != null && optionContainer != null && optionTemplate != null &&
                confirmPanel != null && confirmYesButton != null && confirmNoButton != null)
            {
                return true;
            }

            if (!warnedMissingRefs)
            {
                warnedMissingRefs = true;
                Debug.LogError(
                    "[BuildingUpgradeNodeUI] 节点窗口引用不完整，强化选择不会显示。\n" +
                    "请运行菜单「屿见/建筑/配置建筑强化节点界面」生成可自由编辑的初始 UI，" +
                    "再把 Hierarchy 里的对应物体拖到本组件的 Inspector 字段。", this);
            }

            return false;
        }
    }
}
