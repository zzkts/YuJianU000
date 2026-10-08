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
    ///
    /// 显隐：蓝图在场期间（跟随鼠标 + 已落位填料）面板一直挂着，蓝图一关就收起来。
    /// 滑入滑出交给 SlidingPanel，本类只说「该显示 / 该收起」。
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
        [Tooltip("面板内容根物体。没有滑入过渡时用它瞬间显隐")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("滑入 / 滑出过渡。面板在蓝图模式时从屏幕底部滑出来，退出蓝图模式时滑回去。" +
                 "留空则退回「瞬间显示 / 隐藏」")]
        [SerializeField] private SlidingPanel slide;

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

            if (slide == null)
            {
                // 这条几乎总是同一个原因：阶段 4 的向导没重跑（或者跑完没按 Ctrl+S 存场景）。
                // 场景里既没有 SlidingPanel 组件，面板也还停在早先摆的位置（旧布局是屏幕右上角），
                // 于是「按钮明明在场景里、却怎么都在屏幕底部找不到它」。
                // 把两个后果都点名，别让人再去猜（规则 5：不许静默失败）
                Debug.LogWarning("[BuildingMaterialPanel] Sliding Panel 未配置。两个后果：" +
                                 "① 面板瞬间显隐，不会从屏幕底部滑入；" +
                                 "② 面板仍停在场景里摆的旧位置（早先那一版是屏幕右上角），不是底部居中。" +
                                 "请跑菜单「屿见/配置建造系统（阶段 4）」并 Ctrl+S 保存场景。", this);
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
            // 蓝图在场（跟随鼠标 + 已落位填料）就算「蓝图模式」，面板一直挂着；
            // 蓝图一关（建成 / 撤回 / 取消）就收回去。
            // 用 IsPlacing 而不是 IsFilling：点一下建筑按钮就该看到这座建筑要什么料。
            bool visible = buildingManager != null && buildingManager.IsPlacing;

            if (slide != null)
            {
                if (slide.IsShown != visible)
                {
                    slide.SetShown(visible);

                    if (visible)
                    {
                        OnPanelRevealed();
                    }
                }
            }
            else if (panelRoot != null && panelRoot.activeSelf != visible)
            {
                panelRoot.SetActive(visible);

                if (visible)
                {
                    OnPanelRevealed();
                }
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

            if (construction == null)
            {
                // 这两个按钮的监听全靠这里挂。引用缺一个就变成「点了毫无反应」的死按钮，
                // 而且不会有任何报错——必须当场说清楚（规则 5）
                Debug.LogError("[BuildingMaterialPanel] Building Construction 未配置，" +
                               "「撤回」「确认建造」点了不会有任何反应。" +
                               "请重跑菜单「屿见/配置建造系统（阶段 4）」。", this);
            }

            if (undoButton == null)
            {
                Debug.LogError("[BuildingMaterialPanel] Undo Button 未配置，「撤回」点了不会有任何反应。", this);
            }
            else if (construction != null)
            {
                undoButton.onClick.AddListener(HandleUndoClicked);
            }

            if (confirmButton == null)
            {
                Debug.LogError("[BuildingMaterialPanel] Confirm Button 未配置，「确认建造」点了不会有任何反应。", this);
            }
            else if (construction != null)
            {
                confirmButton.onClick.AddListener(HandleConfirmClicked);
            }

            // 引用在、连线也在、矩形也对，却「看不见也点不到」—— 那就只剩一种可能：
            // 按钮自己在场景里就是关闭的，而且没有任何代码会打开它。
            WarnIfInactive(undoButton, "撤回");
            WarnIfInactive(confirmButton, "确认建造");
        }

        /// <summary>
        /// 按钮自身在场景里被摆成关闭（activeSelf = false）时点出来。
        /// 这类故障不报错、不影响任何别的代码，只是按钮永远不出现——是最难查的一种静默失败（规则 5）。
        /// 判的是 activeSelf 而不是 activeInHierarchy：面板平时整个是关的（SlidingPanel 会停用 Content），
        /// 那一刻 activeInHierarchy 本来就该是 false，判它只会天天误报。
        /// </summary>
        private void WarnIfInactive(Button button, string label)
        {
            if (button != null && !button.gameObject.activeSelf)
            {
                Debug.LogError($"[BuildingMaterialPanel]「{label}」按钮在场景里是关闭的" +
                               "（Inspector 里名字左边的勾没打上），面板显示时它也不会出现、更收不到点击。" +
                               "多半是向导从隐藏的按钮样品上克隆时漏了激活——请重跑菜单「屿见/配置建造系统（阶段 4）」。",
                               button);
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

        /// <summary>
        /// 面板刚显示出来时补一次刷新。
        /// 条目是在面板**隐藏**的时候克隆出来的（选中建筑那一刻容器还没激活），
        /// 那时它们不在激活层级里，数量就停在初始化时的 0。
        /// 露头时统一刷一次，面板一出现就是「红色小砖 ×3」而不是 ×0。
        /// </summary>
        private void OnPanelRevealed()
        {
            RefreshAll();
        }

        /// <summary>按建筑的需求重建条目列表。</summary>
        private void RebuildItems(BuildingData data)
        {
            lastBuiltBuilding = data;
            ClearItems();

            if (itemContainer == null)
            {
                Debug.LogError("[BuildingMaterialPanel] Item Container 未配置，面板里不会出现任何材料条目，" +
                               "材料也就没法拖进蓝图。请指定一个条目容器。", this);
                return;
            }

            // itemTemplate 缺失在 Awake 里已经报过一次了，这里不重复刷屏
            if (data == null || itemTemplate == null)
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

            // 材料齐了「确认建造」才可点，没齐就灰掉（阶段 6 用户拍板，推翻阶段 4 的决策 28）。
            //
            // 为什么改：需求七要求「不可放置则 √ 灰掉」。蓝图落位之后几何上必然可放置
            // （决策 17/18：只有合法落点才能 anchor），落位之前那个 √ 根本不显示，
            // 所以「不可放置」只有「材料还没配齐」这一种解释。
            //
            // 代价与补偿：BuildingConstruction.ConfirmBuild() 里那句
            // 「材料还不够：小砖 0/3」从此走不到（那句代码保留不删，将来别的调用方还会用到）。
            // 「还差几个」的信息由 ProgressText 一直显示着（形如「材料：小砖 2/3」），不会丢。
            confirmButton.interactable = construction.IsSatisfied;

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
