using System;
using System.Text;
using UnityEngine;
using Yujian.Mining;

namespace Yujian.Core
{
    /// <summary>
    /// 阶段 6：玩家的「在哪个区域」这一份状态，以及跟着它走的两件接线。
    ///
    /// 它**只管三件事**，而且都不是业务逻辑：
    ///   1. 记住 <see cref="GameArea"/> 并在变化时广播 <see cref="OnAreaChanged"/>（相机靠这个动）；
    ///   2. 按区域显示 / 隐藏面板（声明式的三个 GameObject 数组）；
    ///   3. 按区域开关挖矿输入（沿用阶段 3 决策 11 的老手法：直接 enabled = false，不改 MiningInput 的代码）。
    ///
    /// 刻意**不**做的事：不碰伤害、不碰金币、不碰材料、不管相机怎么动（那是 AreaCameraDirector 的事）。
    /// 所以它没有变成第二个 GameManager——见 开发进度.md 第五节职责边界。
    ///
    /// 订阅者请在自己的 OnEnable 里订阅、OnDestroy / OnDisable 里退订，
    /// 并在自己的 Start 里读一次 <see cref="CurrentArea"/>；本类也会在 Start 里补发一次初始区域。
    /// </summary>
    public class GameAreaController : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家身上的 MiningInput。离开挖矿区时会被 enabled = false，" +
                 "这样在商店 / 建筑区点鼠标不会穿过去打矿石。留空会报错")]
        [SerializeField] private MiningInput miningInput;

        // ⚠️ 三个面板数组里**都不要填 Canvas/材料面板**：
        // 它由 BuildingManager.IsPlacing 自己管显隐，而 SlidingPanel 就挂在那个物体上——
        // 外部把它 SetActive(false) 会让面板再也滑不回来（阶段 4 决策 27）。

        [Header("按区域显示的面板")]
        [Tooltip("只在挖矿区显示的面板。挖矿区目前没有面板，留空即可")]
        [SerializeField] private GameObject[] miningAreaPanels = new GameObject[0];

        [Tooltip("只在建筑区显示的面板。填 Canvas/BuildingPanel。" +
                 "不要填材料面板——它自己管自己，见脚本注释")]
        [SerializeField] private GameObject[] buildingAreaPanels = new GameObject[0];

        [Tooltip("只在商店区显示的面板。填 Canvas/ShopPanel")]
        [SerializeField] private GameObject[] shopAreaPanels = new GameObject[0];

        [Header("起始状态")]
        [Tooltip("进 Play Mode 时所在的区域。按需求「启动游戏 → 挖矿区域」，默认 Mining")]
        [SerializeField] private GameArea startArea = GameArea.Mining;

        [Header("调试")]
        [Tooltip("是否在 Console 打印区域切换日志")]
        [SerializeField] private bool logAreaFlow = true;

        /// <summary>当前区域。任何时刻读它都是安全的。</summary>
        public GameArea CurrentArea { get; private set; }

        /// <summary>区域变化时广播（参数是**新**区域）。初始区域在 Start 里也会补发一次。</summary>
        public event Action<GameArea> OnAreaChanged;

        private void Awake()
        {
            // Awake 只写字段、不做任何 SetActive：别的脚本的 Awake 还没跑完，
            // 这时候停用物体会让它们收不到 Awake（踩坑 6 就是这么来的）
            CurrentArea = startArea;
        }

        private void Start()
        {
            ApplyPanels(CurrentArea);
            ApplyMiningGate(CurrentArea);

            if (miningInput == null)
            {
                Debug.LogError("[GameAreaController] MiningInput 未配置：离开挖矿区时挖矿不会被关掉，" +
                               "在商店 / 建筑区点鼠标可能穿过去打到矿石。" +
                               "请把 Player 上的 MiningInput 拖到该字段。", this);
            }

            if (logAreaFlow)
            {
                Debug.Log($"[GameAreaController] 初始区域：{CurrentArea.ToChineseName()}" +
                          $"（面板与挖矿开关已按这个区域应用）");
            }

            // 补发一次：纯事件驱动的订阅者（相机、导航高亮）不必自己再读一遍 CurrentArea
            OnAreaChanged?.Invoke(CurrentArea);
        }

        /// <summary>
        /// 切到某个区域。切到当前区域时不做任何事（重复点同一个按钮不会重置相机）。
        /// </summary>
        public void SetArea(GameArea area)
        {
            if (area == CurrentArea)
            {
                if (logAreaFlow)
                {
                    Debug.Log($"[GameAreaController] 已经在{area.ToChineseName()}区域，忽略这次切换。");
                }

                return;
            }

            string previous = CurrentArea.ToChineseName();
            CurrentArea = area;

            // 先动 UI，再广播：相机订阅者可能会顺手关掉蓝图（找材料退回库存），
            // 那时候材料面板已经该显示成"建筑区"的样子了
            ApplyPanels(area);
            ApplyMiningGate(area);

            if (logAreaFlow)
            {
                Debug.Log($"[GameAreaController] 区域切换：{previous} → {area.ToChineseName()}");
            }

            OnAreaChanged?.Invoke(area);
        }

        /// <summary>
        /// 幂等守卫：不在挖矿区时，挖矿输入必须是关的。
        ///
        /// 为什么非要每帧守一道：`BuildingManager` 关蓝图时会把
        /// 「屏蔽挖矿」之前记下的值恢复成 true（阶段 3 决策 12 的 miningRestorePending）。
        /// 玩家完全可能在建筑区开着蓝图，然后切到商店——那一刻挖矿就被它重新打开了。
        ///
        /// 只在**非挖矿区**写值，挖矿区内一次都不写，所以不会和 BuildingManager 抢。
        /// </summary>
        private void LateUpdate()
        {
            if (CurrentArea != GameArea.Mining && miningInput != null && miningInput.enabled)
            {
                miningInput.enabled = false;
            }
        }

        private void ApplyMiningGate(GameArea area)
        {
            if (miningInput == null)
            {
                return;
            }

            miningInput.enabled = area == GameArea.Mining;
        }

        private void ApplyPanels(GameArea area)
        {
            SetPanelsActive(miningAreaPanels, area == GameArea.Mining);
            SetPanelsActive(buildingAreaPanels, area == GameArea.Building);
            SetPanelsActive(shopAreaPanels, area == GameArea.Shop);

            ReportPanels(area);
        }

        /// <summary>
        /// 把三个面板数组的**实际内容**打出来，并点名「这个区域本来就该有面板、但数组是空的」。
        ///
        /// 为什么非要有这一行：在运行时，「空数组（0 项）」「字段根本没被写过」「整条都是空引用」的表现
        /// **完全一样** —— <see cref="SetPanelsActive"/> 循环 0 次，不激活任何东西，也不报任何错。
        /// 外部看到的就是「相机切过去了，面板却没出来，Console 干干净净」。
        /// 这是本类里唯一一处会把问题藏起来的地方，违反规则 5，所以必须喊出来。
        /// </summary>
        private void ReportPanels(GameArea area)
        {
            if (logAreaFlow)
            {
                Debug.Log($"[GameAreaController] {area.ToChineseName()}区域的面板：" +
                          $"建筑＝{Describe(buildingAreaPanels)}；商店＝{Describe(shopAreaPanels)}" +
                          $"（挖矿＝{Describe(miningAreaPanels)}；挖矿区本来就没有面板）");
            }

            if (area == GameArea.Building && IsUnusable(buildingAreaPanels))
            {
                Debug.LogError("[GameAreaController] 建筑区的面板数组是空的（或整条都是空引用）：" +
                               "切到建筑区时**不会打开任何面板**，而且不会有别的报错。" +
                               "请在 Inspector 里选中本物体，把 Canvas/BuildingPanel 拖进「Building Area Panels」；" +
                               "或重跑菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
            }

            if (area == GameArea.Shop && IsUnusable(shopAreaPanels))
            {
                Debug.LogError("[GameAreaController] 商店区的面板数组是空的（或整条都是空引用）：" +
                               "切到商店区时不会打开商店面板。" +
                               "请把 Canvas/ShopPanel 拖进「Shop Area Panels」；" +
                               "或重跑菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
            }
        }

        /// <summary>数组为 null、长度为 0，或里面**每一项**都是空引用 —— 三种情况都等于「一个面板都没有」。</summary>
        private static bool IsUnusable(GameObject[] panels)
        {
            if (panels == null || panels.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] != null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>把面板数组描述成「1 项[BuildingPanel：自身开]」，供 Console 一眼定位。</summary>
        private static string Describe(GameObject[] panels)
        {
            if (panels == null)
            {
                return "未配置(null)";
            }

            if (panels.Length == 0)
            {
                return "空数组(0 项)";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(panels.Length).Append(" 项[");

            for (int i = 0; i < panels.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('、');
                }

                GameObject panel = panels[i];

                if (panel == null)
                {
                    builder.Append("空引用");
                    continue;
                }

                builder.Append(panel.name);
                builder.Append(panel.activeSelf ? "：自身开" : "：自身关");

                // activeSelf 是「自己开着吗」，activeInHierarchy 是「眼睛看得见吗」。
                // 两者不一致就说明上面某一层被关了（最常见的是整个 Canvas 被停用）——
                // 那种情况表现和「面板没打开」一模一样，必须区分开
                if (panel.activeSelf && !panel.activeInHierarchy)
                {
                    builder.Append("(但上层被关，看不到)");
                }
            }

            builder.Append(']');
            return builder.ToString();
        }

        private void SetPanelsActive(GameObject[] panels, bool active)
        {
            if (panels == null)
            {
                return;
            }

            for (int i = 0; i < panels.Length; i++)
            {
                GameObject panel = panels[i];

                if (panel == null)
                {
                    Debug.LogError($"[GameAreaController] {name} 的面板数组里有空引用（第 {i} 项），" +
                                   "它不会随区域显隐。请重新运行菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
                    continue;
                }

                if (panel == gameObject)
                {
                    // 自己停自己：组件会一起停掉，之后再也回不来。阶段 4 的面板踩过同款坑（Panel Root 不能指向自己）
                    Debug.LogError($"[GameAreaController] 「{panel.name}」不能把自己放进面板数组：" +
                                   "SetActive(false) 会把这个组件也停掉，区域再也切不回来。", this);
                    continue;
                }

                if (panel.activeSelf != active)
                {
                    panel.SetActive(active);
                }
            }
        }
    }
}
