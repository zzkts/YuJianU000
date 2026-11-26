using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 游戏内参数调试器（2026-11-26）。
    ///
    /// 用法：F1 开关 → 顶部选「步长」→ 每行只留 [−] [+] [重置]。
    /// 想加 100 就先点「100」，之后每下都是 100，不用在一堆按钮里找。
    ///
    /// 为什么自动创建、不挂场景：
    /// 之前「关卡暂存池」「超级暴击」都因为少接一根线而静默失效。调试工具更不该依赖手工接线，
    /// 所以用 <see cref="RuntimeInitializeOnLoadMethod"/> 在任意场景启动时自建一个。
    /// 只在编辑器与 Development Build 里自动创建；正式包不会带进来。
    ///
    /// 只做调试，不参与任何玩法判定：所有改动都走各系统的公开入口
    /// （RestoreMoney / RestoreGarbage / SetLevel），保证事件照常广播、界面同步刷新。
    /// </summary>
    [DisallowMultipleComponent]
    public class DebugPanel : MonoBehaviour
    {
        /// <summary>面板是否展开。给其他系统判断「现在不该响应点击」用。</summary>
        public static bool IsOpen { get; private set; }

        [Header("开关")]
        [Tooltip("展开 / 收起的按键")]
        [SerializeField] private Key toggleKey = Key.F1;

        [Tooltip("进入 Play 时是否直接展开")]
        [SerializeField] private bool openOnStart = false;

        [Header("步长")]
        [Tooltip("默认选中的量级下标：0=1、1=10、2=100…9=1B")]
        [SerializeField, Range(0, 9)] private int defaultStepIndex = 2;

        // 2026-11-26：量级扩到 1B——金币能到十亿级，10K 显然不够用
        private static readonly int[] StepValues =
            { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000 };

        private static readonly string[] StepLabels =
            { "1", "10", "100", "1K", "10K", "100K", "1M", "10M", "100M", "1B" };

        private const float PanelWidth = 470f;
        private const float RowHeight = 76f;
        private const float ButtonWidth = 62f;
        private const float ButtonHeight = 32f;
        private const float ButtonGap = 6f;
        private const int RowCount = 4;

        private static readonly Color ButtonColor = new Color(0.18f, 0.22f, 0.3f, 0.95f);

        private PlayerCurrency currency;
        private PlayerGarbage garbage;
        private MiningToolProgress tool;
        private MiningLightTool light;

        private GameObject root;
        private Text moneyValue;
        private Text garbageValue;
        private Text toolValue;
        private Text lightValue;
        private Text stepValue;
        private Text stepHint;

        private int stepIndex;

        private int Step => StepValues[Mathf.Clamp(stepIndex, 0, StepValues.Length - 1)];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (FindFirstObjectByType<DebugPanel>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            GameObject host = new GameObject("DebugPanel");
            host.AddComponent<DebugPanel>();
#endif
        }

        private void Awake()
        {
            stepIndex = Mathf.Clamp(defaultStepIndex, 0, StepValues.Length - 1);
            ResolveReferences();
            Build();
            SetOpen(openOnStart);
        }

        private void OnDestroy()
        {
            IsOpen = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
            {
                SetOpen(!IsOpen);
            }
        }

        // ---------------- 数据 ----------------

        private void ResolveReferences()
        {
            currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            garbage = FindFirstObjectByType<PlayerGarbage>(FindObjectsInactive.Include);
            tool = FindFirstObjectByType<MiningToolProgress>(FindObjectsInactive.Include);
            light = FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
        }

        private void AddMoney(int delta)
        {
            if (currency == null)
            {
                return;
            }

            currency.RestoreMoney(ClampToInt((long)currency.CurrentMoney + delta));
            RefreshValues();
        }

        private void SetMoney(int value)
        {
            if (currency == null)
            {
                return;
            }

            currency.RestoreMoney(Mathf.Max(0, value));
            RefreshValues();
        }

        private void AddGarbage(int delta)
        {
            if (garbage == null)
            {
                return;
            }

            // 走 RestoreGarbage 而不是 AddGarbage：调试要直接改「仓库」，
            // 不能因为在关卡里就被背包拦下来
            garbage.RestoreGarbage(ClampToInt((long)garbage.CurrentGarbage + delta));
            RefreshValues();
        }

        private void SetGarbage(int value)
        {
            if (garbage == null)
            {
                return;
            }

            garbage.RestoreGarbage(Mathf.Max(0, value));
            RefreshValues();
        }

        private void AddToolLevels(int count)
        {
            if (tool == null)
            {
                return;
            }

            tool.SetLevel(Mathf.Clamp(tool.CurrentLevel + count, 1, tool.MaxLevel));
            RefreshValues();
        }

        private void AddLightLevels(int count)
        {
            if (light == null)
            {
                return;
            }

            light.SetLevel(Mathf.Clamp(light.Level + count, 0, light.MaxLevel));
            RefreshValues();
        }

        private static int ClampToInt(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private void SetOpen(bool open)
        {
            IsOpen = open;

            if (root != null)
            {
                root.SetActive(open);
            }

            if (open)
            {
                ResolveReferences();
                RefreshValues();
            }
        }

        private void RefreshValues()
        {
            if (moneyValue != null)
            {
                moneyValue.text = currency != null ? currency.CurrentMoney.ToString("N0") : "未找到";
            }

            if (garbageValue != null)
            {
                garbageValue.text = garbage != null ? garbage.CurrentGarbage.ToString("N0") : "未找到";
            }

            if (toolValue != null)
            {
                toolValue.text = tool != null
                    ? $"Lv.{tool.CurrentLevel}　伤害 {tool.CurrentAttack:0.##}"
                    : "未找到";
            }

            if (lightValue != null)
            {
                lightValue.text = light != null
                    ? $"Lv.{light.Level}　超暴 {light.SuperCritChance * 100f:0}%"
                    : "未找到";
            }

            if (stepValue != null)
            {
                int labelIndex = Mathf.Clamp(stepIndex, 0, StepLabels.Length - 1);
                stepValue.text = $"×{StepLabels[labelIndex]}";
            }

            if (stepHint != null)
            {
                stepHint.text = $"每下 ±{Step:N0}　← → 调整量级";
            }
        }

        private void SetStep(int index)
        {
            stepIndex = Mathf.Clamp(index, 0, StepValues.Length - 1);
            RefreshValues();
        }

        // ---------------- 界面 ----------------

        private void Build()
        {
            Canvas canvas = ResolveCanvas();

            if (canvas == null)
            {
                Debug.LogError("[DebugPanel] 找不到 Canvas，调试面板无法显示。", this);
                enabled = false;
                return;
            }

            float height = 96f + RowHeight * RowCount + 12f;

            root = new GameObject("DebugPanelRoot", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvas.transform, false);
            root.transform.SetAsLastSibling();

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);
            rect.sizeDelta = new Vector2(PanelWidth, height);

            Image background = root.GetComponent<Image>();
            background.color = new Color(0.05f, 0.06f, 0.09f, 0.93f);
            background.raycastTarget = true;

            CreateText(root.transform, "Title", $"调试面板　{toggleKey} 开关", new Vector2(16f, -12f),
                new Vector2(300f, 26f), 18, new Color(0.75f, 0.8f, 0.88f), false);

            BuildStepRow();

            BuildRow(0, "金币", out moneyValue, new (string, Action)[]
            {
                ("-", () => AddMoney(-Step)),
                ("+", () => AddMoney(Step)),
                ("清零", () => SetMoney(0)),
            });

            BuildRow(1, "仓库垃圾", out garbageValue, new (string, Action)[]
            {
                ("-", () => AddGarbage(-Step)),
                ("+", () => AddGarbage(Step)),
                ("清零", () => SetGarbage(0)),
            });

            BuildRow(2, "清洁工具", out toolValue, new (string, Action)[]
            {
                ("−1", () => AddToolLevels(-1)),
                ("+1", () => AddToolLevels(1)),
                ("+10", () => AddToolLevels(10)),
            });

            BuildRow(3, "照明工具", out lightValue, new (string, Action)[]
            {
                ("−1", () => AddLightLevels(-1)),
                ("+1", () => AddLightLevels(1)),
                ("+10", () => AddLightLevels(10)),
            });
        }

        private void BuildStepRow()
        {
            CreateText(root.transform, "StepLabel", "步长", new Vector2(16f, -50f),
                new Vector2(72f, 26f), 16, new Color(0.62f, 0.68f, 0.76f), false);

            CreateButton(root.transform, "StepPrev", "←", new Vector2(96f, -46f),
                new Vector2(48f, ButtonHeight), () => SetStep(stepIndex - 1));

            stepValue = CreateText(root.transform, "StepValue", string.Empty, new Vector2(150f, -46f),
                new Vector2(170f, ButtonHeight), 20, new Color(1f, 0.85f, 0.45f), true);
            stepValue.alignment = TextAnchor.MiddleCenter;

            CreateButton(root.transform, "StepNext", "→", new Vector2(326f, -46f),
                new Vector2(48f, ButtonHeight), () => SetStep(stepIndex + 1));

            stepHint = CreateText(root.transform, "StepHint", string.Empty, new Vector2(16f, -76f),
                new Vector2(380f, 22f), 14, new Color(0.5f, 0.55f, 0.62f), false);
        }

        private void BuildRow(int index, string label, out Text valueText, (string text, Action action)[] buttons)
        {
            float top = -96f - index * RowHeight;

            CreateText(root.transform, $"Label{index}", label, new Vector2(16f, top),
                new Vector2(180f, 24f), 16, new Color(0.62f, 0.68f, 0.76f), false);

            valueText = CreateText(root.transform, $"Value{index}", "—",
                new Vector2(16f, top - 26f), new Vector2(190f, 30f), 20,
                new Color(1f, 0.85f, 0.45f), true);

            for (int i = 0; i < buttons.Length; i++)
            {
                float x = 208f + i * (ButtonWidth + ButtonGap);
                Action action = buttons[i].action;
                string text = buttons[i].text;

                CreateButton(root.transform, $"Btn{index}_{i}", text,
                    new Vector2(x, top - 6f), new Vector2(ButtonWidth, ButtonHeight), action);
            }
        }

        private static Canvas ResolveCanvas()
        {
            BottomNavUI nav = FindFirstObjectByType<BottomNavUI>(FindObjectsInactive.Include);

            if (nav != null)
            {
                Canvas navCanvas = nav.GetComponentInParent<Canvas>();

                if (navCanvas != null)
                {
                    return navCanvas;
                }
            }

            return FindFirstObjectByType<Canvas>(FindObjectsInactive.Exclude);
        }

        private static Text CreateText(Transform parent, string name, string content, Vector2 position,
            Vector2 size, int fontSize, Color color, bool bold)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        private static Image CreateButton(Transform parent, string name, string label, Vector2 position,
            Vector2 size, Action onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image background = go.GetComponent<Image>();
            background.color = ButtonColor;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            Text text = CreateText(go.transform, "Label", label, Vector2.zero, size, 14,
                new Color(0.92f, 0.94f, 0.98f), false);
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return background;
        }
    }
}
