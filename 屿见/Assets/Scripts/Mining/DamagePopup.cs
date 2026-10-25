using UnityEngine;
using UnityEngine.UI;
using Yujian.Core;

namespace Yujian.Mining
{
    /// <summary>
    /// 伤害数字。
    /// 职责：把一次伤害按配置表规定的样式显示在 Canvas 上，向上飘动并淡出，播放结束后自毁。
    /// 样式（颜色 / 字号 / 粗体）来自 <see cref="DamageNumberTable"/>，本类不硬编码具体数值。
    /// 使用工程现有的 uGUI Text，不引入 TextMeshPro（项目规则 10）。
    /// 不是 UI 框架的一部分，只服务于挖矿反馈。
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("伤害数字配置表。没接会在 Console 报错，并回退到策划案默认值")]
        [SerializeField] private DamageNumberTable styleTable;

        [Tooltip("显示用的 Canvas。留空则运行时自动查找场景根 Canvas")]
        [SerializeField] private Canvas targetCanvas;

        [Header("动画")]
        [Tooltip("显示时长（秒）")]
        [SerializeField] private float lifetime = 0.8f;

        [Tooltip("向上飘动的距离（世界单位）")]
        [SerializeField] private float riseDistance = 0.8f;

        [Header("暴击")]
        [Tooltip("暴击数字颜色，默认橙红")]
        [SerializeField] private Color criticalColor = new Color(1f, 0.42f, 0.15f, 1f);

        [Tooltip("暴击数字相对普通数字的放大倍数")]
        [Min(1f)]
        [SerializeField] private float criticalFontScale = 1.3f;

        [Header("调试")]
        [Tooltip("配置表缺失或缺行时是否在 Console 报错")]
        [SerializeField] private bool logStyleFallback = true;

        // 配置表缺失时的兜底值，与策划案 2.1-3 的默认表一致：
        // Type_1 建筑 = 蓝 / 16 / 不加粗；Type_2 玩家 = 黄 / 18 / 粗体。
        private static readonly DamageNumberTable.Style BuildingFallback = new DamageNumberTable.Style(
            DamageSource.Building, "Type_1", new Color(0.25f, 0.55f, 1f, 1f), 16, false);

        private static readonly DamageNumberTable.Style PlayerFallback = new DamageNumberTable.Style(
            DamageSource.Player, "Type_2", new Color(1f, 0.85f, 0.3f, 1f), 18, true);

        private Text label;
        private RectTransform labelRect;
        private RectTransform canvasRect;
        private Camera canvasCamera;
        private Camera viewCamera;
        private Color baseColor = Color.white;
        private Vector3 startWorldPosition;
        private float elapsed;
        private bool playing;

        // 每次受击都会新建一个 DamagePopup，警告用静态标记，避免刷屏
        private static bool warnedAboutMissingCanvas;
        private static bool warnedAboutStyleFallback;

        /// <summary>
        /// 播放一次伤害数字。由 Ore 在生成本对象后立即调用。
        /// 策划案 2.1-3 的触发条件：玩家不在挖矿系统内时不显示。
        /// </summary>
        /// <param name="damage">要显示的伤害数值。</param>
        /// <param name="source">伤害来源，决定使用配置表里的哪一行样式。</param>
        public void Play(float damage, DamageSource source)
        {
            Play(damage, source, false);
        }

        /// <summary>
        /// 播放一次伤害数字；暴击时改用更醒目的颜色、字号和 CRIT 前缀。
        /// </summary>
        /// <param name="damage">要显示的伤害数值。</param>
        /// <param name="source">伤害来源，决定使用配置表里的哪一行样式。</param>
        /// <param name="isCritical">本次伤害是否暴击。</param>
        public void Play(float damage, DamageSource source, bool isCritical)
        {
            if (!IsInMiningArea())
            {
                Destroy(gameObject);
                return;
            }

            if (!EnsureLabel())
            {
                Destroy(gameObject);
                return;
            }

            DamageNumberTable.Style style = ResolveStyle(source);
            baseColor = isCritical ? criticalColor : style.Color;

            // 伤害数字使用 Unity 内置字体，不含中文字形，所以暴击标记用 CRIT 而不是「暴击」，
            // 避免显示成方框。以后换上中文字体可以再改成中文。
            label.text = isCritical ? $"CRIT {damage:0.##}" : damage.ToString("0.##");
            label.fontSize = Mathf.Max(1, Mathf.RoundToInt(style.FontSize * (isCritical ? Mathf.Max(1f, criticalFontScale) : 1f)));
            label.fontStyle = style.Bold ? FontStyle.Bold : FontStyle.Normal;
            label.color = baseColor;
            label.enabled = true;

            startWorldPosition = transform.position;
            elapsed = 0f;
            playing = true;

            UpdateVisuals(0f);
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float progress = lifetime > 0f ? Mathf.Clamp01(elapsed / lifetime) : 1f;

            UpdateVisuals(progress);

            if (progress >= 1f)
            {
                playing = false;
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            // 数字本体挂在 Canvas 下，与伤害数字控制器不是父子关系，必须手动回收
            if (label != null)
            {
                Destroy(label.gameObject);
            }
        }

        /// <summary>按来源查配置表；查不到时明确报错并回退到策划案默认样式。</summary>
        private DamageNumberTable.Style ResolveStyle(DamageSource source)
        {
            if (styleTable != null && styleTable.TryGet(source, out DamageNumberTable.Style style))
            {
                return style;
            }

            if (logStyleFallback && !warnedAboutStyleFallback)
            {
                warnedAboutStyleFallback = true;

                if (styleTable == null)
                {
                    Debug.LogError("[DamagePopup] 伤害数字配置表未配置，已使用内置默认样式。" +
                                   "请运行菜单「屿见/挖矿/配置伤害数字配置表（挖矿 2.1-3）」。", this);
                }
                else
                {
                    Debug.LogError($"[DamagePopup] 配置表 {styleTable.name} 里没有伤害来源 {source} 的行，" +
                                   "已使用内置默认样式。请在配置表里补一行。", this);
                }
            }

            return source == DamageSource.Building ? BuildingFallback : PlayerFallback;
        }

        /// <summary>
        /// 策划案 2.1-3：玩家处于挖矿系统内才显示伤害数字。
        /// GameManager 不存在时（独立测试场景）按挖矿区处理，避免数字全部消失。
        /// </summary>
        private static bool IsInMiningArea()
        {
            GameManager manager = GameManager.Instance;
            return manager == null || manager.CurrentArea == GameArea.Mining;
        }

        /// <summary>保证 Canvas 下已经有一个可用的 uGUI Text。失败时返回 false。</summary>
        private bool EnsureLabel()
        {
            if (label != null)
            {
                return true;
            }

            Canvas canvas = targetCanvas != null ? targetCanvas : FindRootCanvas();

            if (canvas == null)
            {
                if (!warnedAboutMissingCanvas)
                {
                    warnedAboutMissingCanvas = true;
                    Debug.LogError("[DamagePopup] 场景里找不到可用的 Canvas，伤害数字无法显示。" +
                                   "请确认场景里有 Screen Space 的 Canvas。", this);
                }

                return false;
            }

            canvasRect = canvas.transform as RectTransform;
            canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            GameObject labelObject = new GameObject(
                "DamageText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));

            labelObject.transform.SetParent(canvas.transform, false);

            // 放在最底层：伤害数字不该盖住商店 / 建筑面板、底部导航条等 UI
            labelObject.transform.SetAsFirstSibling();

            labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(240f, 80f);
            labelRect.anchoredPosition = Vector2.zero;

            label = labelObject.GetComponent<Text>();
            label.font = LoadBuiltinFont();
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            if (label.font == null)
            {
                Debug.LogError("[DamagePopup] 找不到 Unity 内置字体，伤害数字将无法显示。" +
                               "请手动为生成的 Text 指定字体。", this);
            }

            return true;
        }

        private void UpdateVisuals(float progress)
        {
            Vector3 worldPosition = startWorldPosition + Vector3.up * (riseDistance * progress);
            UpdateLabelPosition(worldPosition);

            if (label == null)
            {
                return;
            }

            Color faded = baseColor;
            faded.a = baseColor.a * (1f - progress);
            label.color = faded;
        }

        /// <summary>把伤害数字的世界坐标重投影到 Canvas 上。相机移动时数字仍贴着矿石。</summary>
        private void UpdateLabelPosition(Vector3 worldPosition)
        {
            if (label == null || labelRect == null || canvasRect == null)
            {
                return;
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (viewCamera == null)
            {
                label.enabled = false;
                return;
            }

            Vector3 screenPoint = viewCamera.WorldToScreenPoint(worldPosition);

            // 在相机背后时不显示（否则会投到屏幕另一侧）
            if (screenPoint.z <= 0f)
            {
                label.enabled = false;
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPoint, canvasCamera, out Vector2 localPoint))
            {
                labelRect.anchoredPosition = localPoint;
            }

            label.enabled = true;
        }

        private static Canvas FindRootCanvas()
        {
#if UNITY_2023_1_OR_NEWER
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
#endif

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];

                if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace)
                {
                    continue;
                }

                return canvas;
            }

            return null;
        }

        private static Font LoadBuiltinFont()
        {
            Font builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtinFont == null)
            {
                builtinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return builtinFont;
        }
    }
}
