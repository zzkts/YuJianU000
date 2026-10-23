using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yujian.UI
{
    /// <summary>
    /// 全局 UI 面板注册表与开关入口。
    ///
    /// 职责边界（刻意做小）：
    ///   · 按 <see cref="UIPanelId"/> 找到面板，提供 Open / Close / Toggle / CloseTop；
    ///   · 维护一个“打开顺序”栈，Esc / 返回键关掉最上面那个；
    ///   · 回答“当前有没有模态面板开着”，供输入层判断要不要让路。
    ///
    /// **不**做：布局、动画（那是 <see cref="UIPanel"/> / SlidingPanel 的）、
    /// 业务显隐规则（区域切换仍由 GameAreaController 管）。
    ///
    /// 场景里没有挂本组件也能用：第一次调用静态 API 时会自动补一个，并扫描全场景的 UIPanel。
    /// 这样新面板只要挂 UIPanel、填好 ID，不必先改场景接线。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class UIManager : MonoBehaviour
    {
        private static readonly Dictionary<UIPanelId, UIPanel> registry = new Dictionary<UIPanelId, UIPanel>();
        private static readonly List<UIPanel> openStack = new List<UIPanel>();
        private static bool warnedAboutMissingInstance;

        /// <summary>当前实例。没有时由 <see cref="EnsureInstance"/> 自动创建。</summary>
        public static UIManager Instance { get; private set; }

        [Header("调试")]
        [Tooltip("是否打印面板开关日志")]
        [SerializeField] private bool logUIFlow = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UIManager] 场景中存在多个 UIManager，已保留最先初始化的实例，" +
                                 "本实例不会接收面板注册。", this);
                enabled = false;
                return;
            }

            Instance = this;
            ScanScene();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                registry.Clear();
                openStack.Clear();
            }
        }

        /// <summary>注册一个面板。重复 ID 时明确报错并保留第一个，避免 Open 指向随机面板。</summary>
        public static void Register(UIPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            UIPanelId id = panel.Id;

            if (id == UIPanelId.None)
            {
                Debug.LogWarning($"[UIManager] 「{panel.name}」的 UIPanel 没有指定 Panel ID，" +
                                 "它不会出现在 UIManager 里。请在 Inspector 里选一个 ID。", panel);
                return;
            }

            if (registry.TryGetValue(id, out UIPanel existing))
            {
                // 旧面板已被销毁（场景重载）→ 直接替换，不算冲突
                if (existing == null || existing == panel)
                {
                    registry[id] = panel;
                    return;
                }

                Debug.LogError($"[UIManager] 面板 ID「{id}」重复：" +
                               $"已注册的是「{existing.name}」，本次尝试注册的是「{panel.name}」。" +
                               "已保留先注册的那个；请删掉或多改一个 ID。", panel);
                return;
            }

            registry[id] = panel;
        }

        /// <summary>按 ID 取面板。没有注册时返回 null（并打印一次明确错误，不静默）。</summary>
        public static UIPanel Get(UIPanelId id)
        {
            if (registry.TryGetValue(id, out UIPanel panel) && panel != null)
            {
                return panel;
            }

            Debug.LogError($"[UIManager] 找不到面板「{id}」。请确认场景里有一个挂了 UIPanel、" +
                           "并且 Panel ID 填对的面板。");
            return null;
        }

        /// <summary>面板是否处于显示状态。没有注册时返回 false，不报错。</summary>
        public static bool IsOpen(UIPanelId id)
        {
            return registry.TryGetValue(id, out UIPanel panel) && panel != null && panel.IsVisible;
        }

        /// <summary>打开面板。已经在显示时只把它提到栈顶。</summary>
        public static bool Open(UIPanelId id, bool instant = false)
        {
            // 第一次调用时补齐实例；它的 Awake 会把场景里已经挂好的 UIPanel 扫进来
            EnsureInstance();

            UIPanel panel = Get(id);

            if (panel == null)
            {
                return false;
            }

            panel.Show(instant);
            MoveToTop(panel);

            if (Instance != null && Instance.logUIFlow)
            {
                Debug.Log($"[UIManager] 打开「{id}」，当前打开 {openStack.Count} 个面板。");
            }

            return true;
        }

        /// <summary>关闭面板。没有打开时也返回 true（幂等）。</summary>
        public static bool Close(UIPanelId id, bool instant = false)
        {
            UIPanel panel = Get(id);

            if (panel == null)
            {
                return false;
            }

            panel.Hide(instant);
            openStack.Remove(panel);
            return true;
        }

        /// <summary>切换面板显隐。</summary>
        public static bool Toggle(UIPanelId id, bool instant = false)
        {
            return IsOpen(id) ? Close(id, instant) : Open(id, instant);
        }

        /// <summary>关闭最后打开的那个面板。用于 Esc / 返回键。</summary>
        public static bool CloseTop(bool instant = false)
        {
            for (int i = openStack.Count - 1; i >= 0; i--)
            {
                UIPanel panel = openStack[i];

                if (panel == null)
                {
                    openStack.RemoveAt(i);
                    continue;
                }

                openStack.RemoveAt(i);

                if (panel.IsVisible)
                {
                    panel.Hide(instant);
                    return true;
                }
            }

            return false;
        }

        /// <summary>当前是否有模态面板开着。输入层可以据此让路。</summary>
        public static bool HasModalOpen
        {
            get
            {
                for (int i = 0; i < openStack.Count; i++)
                {
                    UIPanel panel = openStack[i];

                    if (panel != null && panel.IsModal && panel.IsVisible)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>把当前场景里所有 UIPanel（含未激活物体）注册一遍。重复调用是安全的。</summary>
        public static void ScanScene()
        {
            UIPanel[] panels = FindObjectsByType<UIPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < panels.Length; i++)
            {
                Register(panels[i]);
            }
        }

        private static void MoveToTop(UIPanel panel)
        {
            openStack.Remove(panel);
            openStack.Add(panel);
        }

        /// <summary>
        /// 没有实例时自动补一个。只在 Play Mode 下创建——编辑模式下调用静态 API 没有意义，
        /// 也不该往场景里偷偷塞物体。
        /// </summary>
        private static UIManager EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            if (!Application.isPlaying)
            {
                if (!warnedAboutMissingInstance)
                {
                    warnedAboutMissingInstance = true;
                    Debug.LogWarning("[UIManager] 编辑模式下没有 UIManager 实例，静态开关 API 只在 Play Mode 有效。");
                }

                return null;
            }

            GameObject host = new GameObject("[UIManager]");
            Instance = host.AddComponent<UIManager>();
            return Instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter()
        {
            // 关闭 Domain Reload 时静态字典会把上一局的引用带进新一局
            registry.Clear();
            openStack.Clear();
            Instance = null;
            warnedAboutMissingInstance = false;
        }

        /// <summary>场景重载后清掉已被销毁的面板引用。新面板的 Awake 会重新注册。</summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Prune();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private static void Prune()
        {
            List<UIPanelId> staleKeys = null;

            foreach (KeyValuePair<UIPanelId, UIPanel> pair in registry)
            {
                if (pair.Value == null)
                {
                    staleKeys ??= new List<UIPanelId>();
                    staleKeys.Add(pair.Key);
                }
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                registry.Remove(staleKeys[i]);
            }

            openStack.RemoveAll(panel => panel == null);
        }
    }
}
