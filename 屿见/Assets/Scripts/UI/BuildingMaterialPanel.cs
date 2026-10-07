using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 建造材料面板（阶段 4）。
    ///
    /// 职责：蓝图落位之后，把「这个建筑需要哪些材料」摊成一个个可拖拽的条目，
    /// 显示每种的持有数量，并把「撤回 / 确认建造」两个按钮接到 BuildingConstruction 上。
    ///
    /// 条目**全部由 BuildingData.RequiredMaterials 推导**：
    ///   需求里的 MaterialData → 它的 AllowedColors → 每种颜色一个条目。
    /// 所以风琴博物馆（小砖 ×3，允许红蓝绿）会列出「红色小砖 / 蓝色小砖 / 绿色小砖」三项，
    /// 数量为 0 的也照样列出——不列的话，「原料不足，请购买」这条提示永远触发不到。
    /// 将来换成「原木 ×5 + 石头 ×2」的建筑，本类一行都不用改。
    ///
    /// 本类只读库存、只转发点击，不修改任何数值。
    /// </summary>
    public class BuildingMaterialPanel : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建造结算。撤回、确认建造、能不能拖都由它判定")]
        [SerializeField] private BuildingConstruction construction;

        [Tooltip("建筑系统入口。用来判断面板该不该出现、当前选的是哪个建筑")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("原料库存。只读，用来显示每个条目还剩几个")]
        [SerializeField] private MaterialInventory inventory;

        [Header("界面")]
        [Tooltip("整个面板的根物体。非填料阶段会被隐藏")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("显示建筑名")]
        [SerializeField] private Text titleText;

        [Tooltip("显示材料进度，例如「小砖 2/3」")]
        [SerializeField] private Text progressText;

        [Tooltip("状态行：原料不足 / 已放入 / 请把材料拖到蓝图上")]
        [SerializeField] private Text statusText;

        [Tooltip("条目容器。运行时生成的条目都挂在这里")]
        [SerializeField] private Transform itemContainer;

        [Tooltip("条目模板。必须是条目的**隐藏原样**，运行时按它克隆。" +
                 "它自身不会被显示，也不会被销毁")]
        [SerializeField] private RectTransform itemTemplate;

        [Tooltip("拖拽残影的父物体。留空则用本物体所在的 Canvas")]
        [SerializeField] private RectTransform ghostParent;

        [SerializeField] private Button undoButton;
        [SerializeField] private Button confirmButton;

        [Header("排版")]
        [Tooltip("条目高度（像素）。模板自带的高度为正时以模板为准")]
        [SerializeField] private float itemHeight = 56f;

        [Tooltip("条目之间的间距（像素）")]
        [SerializeField] private float itemSpacing = 6f;

        [Header("文案")]
        [Tooltip("状态行自动清空的秒数。<=0 表示一直保留")]
        [SerializeField] private float statusClearDelay = 2.5f;

        [Header("配色")]
        [SerializeField] private Color successColor = new Color(0.24f, 0.65f, 0.32f);
        [SerializeField] private Color failureColor = new Color(0.80f, 0.22f, 0.20f);

        [Tooltip("确认按钮不可用时的文字")]
        [SerializeField] private string confirmLabel = "确认建造";

        private readonly List<MaterialDragItem> items = new List<MaterialDragItem>();

        private BuildingData lastBuiltBuilding;
        private float statusClearTime = -1f;
        private bool wired;

        /// <summary>拖拽残影的父物体。MaterialDragItem 会读它。</summary>
        public RectTransform GhostParent => ghostParent;

        private void Awake()
        {
            if (panelRoot == gameObject)
            {
                // 隐藏 panelRoot 会连同本组件一起停掉，Update 不再执行，面板就再也回不来了
                Debug.LogError("[BuildingMaterialPanel] Panel Root 指向了本组件所在的物体。" +
                               "请在它下面建一个子物体当内容容器，把 Panel Root 指到那个子物体上。", this);
                panelRoot = null;
            }

            if (itemTemplate != null)
            {
                // 模板只是原样，永远不显示
                itemTemplate.gameObject.SetActive(false);

                if (itemTemplate.sizeDelta.y > 0f)
                {
                    itemHeight = itemTemplate.sizeDelta.y;
                }
            }
            else
            {
                Debug.LogError("[BuildingMaterialPanel] Item Template 未配置，面板里不会出现任何材料条目，" +
                               "材料也就没法拖进蓝图。请指定一个隐藏的条目模板。", this);
            }

            if (ghostParent == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();

                if (canvas != null)
                {
                    ghostParent = canvas.transform as RectTransform;
                }
            }

            WireButtons();
        }

        private void OnEnable()
        {
            if (construction != null)
            {
                construction.OnConstructionChanged += HandleConstructionChanged;
                construction.OnMessage += ShowStatus;
            }

            if (inventory != null)
            {
                inventory.OnInventoryChanged += HandleInventoryChanged;
            }

            ClearStatus();
            RefreshAll();
        }

        private void OnDisable()
        {
            if (construction != null)
            {
                construction.OnConstructionChanged -= HandleConstructionChanged;
                construction.OnMessage -= ShowStatus;
            }

            if (inventory != null)
            {
                inventory.OnInventoryChanged -= HandleInventoryChanged;
            }
        }

        private void OnDestroy()
        {
            UnwireButtons();
        }

        private void Update()
        {
            bool visible = buildingManager != null && buildingManager.IsFilling;

            if (panelRoot != null && panelRoot.activeSelf != visible)
            {
                panelRoot.SetActive(visible);
            }

            // 选中项换了就重建条目。这里只做一次引用比较，不每帧重算需求
            BuildingData current = construction != null ? construction.SelectedData : null;

            if (current != lastBuiltBuilding)
            {
                RebuildItems(current);

                // 重建之后必须把进度与按钮状态一起拉一次：
                // 落位那一刻不会触发 OnConstructionChanged（一块料都还没放），
                // 只重建条目的会留下上一座建筑的进度文案
                RefreshCounts();
                RefreshProgress();
                RefreshConfirmButton();
            }

            if (statusClearTime >= 0f && Time.unscaledTime >= statusClearTime)
            {
                ClearStatus();
            }
        }

        // ---------------- 给 MaterialDragItem 用的接口 ----------------

        /// <summary>这块材料现在能不能拖。不能拖时把原因写在 reason 里。</summary>
        public bool CanDrag(MaterialData material, MaterialColor color, out string reason)
        {
            if (construction == null)
            {
                reason = "建造系统未配置";
                return false;
            }

            return construction.CanAcceptMaterial(material, color, out reason);
        }

        /// <summary>把材料放到屏幕上某个位置。拖拽松手时由条目调用。</summary>
        public void DropAt(Vector2 screenPosition, MaterialData material, MaterialColor color)
        {
            if (construction == null)
            {
                Debug.LogError("[BuildingMaterialPanel] BuildingConstruction 未配置，材料放不进蓝图。", this);
                return;
            }

            construction.TryPlaceMaterialAt(screenPosition, material, color);
        }

        /// <summary>在状态行显示一条提示。</summary>
        public void ShowStatus(string message, bool success)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = message;
            statusText.color = success ? successColor : failureColor;
            statusClearTime = statusClearDelay > 0f ? Time.unscaledTime + statusClearDelay : -1f;
        }

        // ---------------- 内部 ----------------

        private void WireButtons()
        {
            if (wired)
            {
                return;
            }

            wired = true;

            if (undoButton != null && construction != null)
            {
                undoButton.onClick.AddListener(HandleUndoClicked);
            }

            if (confirmButton != null && construction != null)
            {
                confirmButton.onClick.AddListener(HandleConfirmClicked);
            }
        }

        private void UnwireButtons()
        {
            if (!wired)
            {
                return;
            }

            wired = false;

            if (undoButton != null)
            {
                undoButton.onClick.RemoveListener(HandleUndoClicked);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            }
        }

        private void HandleUndoClicked()
        {
            if (construction == null)
            {
                return;
            }

            construction.Undo();
        }

        private void HandleConfirmClicked()
        {
            if (construction == null)
            {
                return;
            }

            construction.ConfirmBuild();
        }

        private void HandleConstructionChanged()
        {
            RefreshCounts();
            RefreshProgress();
            RefreshConfirmButton();
        }

        private void HandleInventoryChanged()
        {
            RefreshCounts();
        }

        private void RefreshAll()
        {
            BuildingData current = construction != null ? construction.SelectedData : null;

            if (current != lastBuiltBuilding)
            {
                RebuildItems(current);
            }

            RefreshCounts();
            RefreshProgress();
            RefreshConfirmButton();
        }

        /// <summary>按建筑的需求重建条目列表。</summary>
        private void RebuildItems(BuildingData data)
        {
            lastBuiltBuilding = data;
            ClearItems();

            if (data == null || itemTemplate == null || itemContainer == null)
            {
                return;
            }

            IReadOnlyList<BuildingMaterialRequirement> requirements = data.RequiredMaterials;

            for (int i = 0; i < requirements.Count; i++)
            {
                BuildingMaterialRequirement requirement = requirements[i];

                if (!requirement.IsValid)
                {
                    continue;
                }

                MaterialData material = requirement.Material;
                IReadOnlyList<MaterialColor> colors = material.AllowedColors;

                // 同一原料可能出现在多条需求里；颜色的去重只针对本原料，用一个小 HashSet 够了
                HashSet<MaterialColor> seenInThisMaterial = new HashSet<MaterialColor>();

                for (int c = 0; c < colors.Count; c++)
                {
                    if (!seenInThisMaterial.Add(colors[c]))
                    {
                        continue;
                    }

                    CreateItem(material, colors[c]);
                }
            }

            LayoutItems();

            if (titleText != null)
            {
                titleText.text = data.BuildingName;
            }
        }

        private void CreateItem(MaterialData material, MaterialColor color)
        {
            RectTransform clone = Instantiate(itemTemplate, itemContainer);
            clone.name = $"材料_{material.MaterialName}_{color}";
            clone.gameObject.SetActive(true);

            MaterialDragItem item = clone.GetComponent<MaterialDragItem>();

            if (item == null)
            {
                Debug.LogWarning($"[BuildingMaterialPanel] 条目模板「{itemTemplate.name}」上没有 MaterialDragItem 组件，" +
                                 "已自动补上。建议运行菜单「屿见/配置建造系统（阶段 4）」重建面板。", this);
                item = clone.gameObject.AddComponent<MaterialDragItem>();
            }

            item.Initialize(this, material, color);
            items.Add(item);
        }

        private void LayoutItems()
        {
            float y = 0f;

            for (int i = 0; i < items.Count; i++)
            {
                RectTransform rect = items[i].transform as RectTransform;

                if (rect == null)
                {
                    continue;
                }

                // 顶部对齐、从上往下排；宽度横向拉满容器
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -y);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, itemHeight);

                y += itemHeight + itemSpacing;
            }
        }

        private void ClearItems()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    Destroy(items[i].gameObject);
                }
            }

            items.Clear();
        }

        private void RefreshCounts()
        {
            for (int i = 0; i < items.Count; i++)
            {
                MaterialDragItem item = items[i];

                if (item == null)
                {
                    continue;
                }

                int count = inventory != null
                    ? inventory.GetMaterialCount(item.Material, item.Color)
                    : 0;

                item.Refresh(count);
                item.SetDimmed(count <= 0);
            }
        }

        private void RefreshProgress()
        {
            if (progressText == null || construction == null)
            {
                return;
            }

            progressText.text = $"材料：{construction.GetProgressText()}";
        }

        private void RefreshConfirmButton()
        {
            if (confirmButton == null || construction == null)
            {
                return;
            }

            bool ready = construction.IsSatisfied;
            confirmButton.interactable = ready;

            Text label = confirmButton.GetComponentInChildren<Text>(true);

            if (label != null)
            {
                label.text = confirmLabel;
            }
        }

        private void ClearStatus()
        {
            statusClearTime = -1f;

            if (statusText != null)
            {
                statusText.text = string.Empty;
            }
        }
    }
}
