using System.Collections.Generic;
using UnityEngine;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 楼层堆叠：把「这个建筑用了哪几块材料」翻译成「每一层刷什么颜色」。
    ///
    /// 蓝图与实体建筑共用同一个组件，靠 Inspector 区分：
    ///   · 实体建筑：layerMaterial = 不透明材质，layerAlpha = 1
    ///   · 蓝图：    layerMaterial = Valid.mat 这类半透明材质，layerAlpha ≈ 0.8
    ///
    /// 层数由调用方通过 Apply() 传入的列表长度决定（风琴博物馆需要 Brick ×3 → 3 层），
    /// **不写死**。预制体里预置的那几层只是「样板」：层数不够时按样板的宽深自动往上克隆，
    /// 层数变少时多出来的样板层会被隐藏。
    ///
    /// 自下而上：第 0 块材料在最低层。第 i 层中心高度 = (i + 0.5) × (总高 / 层数)。
    /// 所以 3 层总高 4 → 每层 4/3，中心在 0.667 / 2 / 3.333；5 层则每层 0.8。
    /// </summary>
    public class BuildingLayerStack : MonoBehaviour
    {
        [Header("结构")]
        [Tooltip("楼层挂点。留空则用本物体自身")]
        [SerializeField] private Transform layerRoot;

        [Tooltip("建筑总高（世界单位）。层数均分这个高度。当前两个预制体的总高都是 4（y 从 0 到 4）")]
        [SerializeField] private float totalHeight = 4f;

        [Header("外观")]
        [Tooltip("楼层材质。留空则沿用样板层自带的材质（这时只能染色，改不了透明度）")]
        [SerializeField] private Material layerMaterial;

        [Tooltip("楼层颜色的不透明度。实体建筑填 1；蓝图建议 0.7~0.85，好让背后的东西透出来")]
        [Range(0f, 1f)]
        [SerializeField] private float layerAlpha = 1f;

        [Tooltip("建筑一条材料需求都没有时用的兜底色。没有兜底的话整栋楼会完全不可见")]
        [SerializeField] private Color fallbackColor = new Color(0.78f, 0.76f, 0.70f, 1f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印每层改色日志。蓝图会频繁改色，正式游玩建议关闭")]
        [SerializeField] private bool logLayerChanges = false;

        // URP 用 _BaseColor，Built-in 用 _Color。两个都写进去，不存在的那一个会被忽略
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Transform> layers = new List<Transform>();
        private readonly List<Renderer> layerRenderers = new List<Renderer>();

        private MaterialPropertyBlock propertyBlock;

        /// <summary>样板层的宽/深，克隆新层时照抄。取自第 0 层的 localScale</summary>
        private float layerWidth;
        private float layerDepth;

        private int activeLayerCount;
        private bool cached;
        private bool warnedAboutMissingLayers;

        /// <summary>楼层挂点。BuildingBlueprint 会用它来把楼层排除在「整体换材质」之外。</summary>
        public Transform LayerRoot => layerRoot != null ? layerRoot : transform;

        /// <summary>样板层总数（含被隐藏的）。</summary>
        public int SlotCount => layers.Count;

        /// <summary>当前生效的层数（= 上一次 EnsureLayout 传入的数量）。</summary>
        public int ActiveLayerCount => activeLayerCount;

        private void Awake()
        {
            EnsureCached();
        }

        /// <summary>
        /// 按指定层数排布楼层：克隆到够用，均分总高，自下而上叠放。
        /// 只负责「有几层、每层多大、摆在哪」，不负责颜色。
        /// </summary>
        /// <param name="count">层数，必须为正。</param>
        public void EnsureLayout(int count)
        {
            EnsureCached();

            if (count <= 0)
            {
                Debug.LogWarning($"[BuildingLayerStack] EnsureLayout 收到非正的层数 {count}，已忽略。", this);
                return;
            }

            if (layers.Count == 0)
            {
                WarnAboutMissingLayers();
                return;
            }

            // 样板不够就按最后一层克隆。克隆出来的层会继承材质的 propertyBlock，
            // 不过下面排布完就会由 SetLayerColor / SetLayerVisible 重新写过
            while (layers.Count < count)
            {
                Transform clone = Instantiate(layers[layers.Count - 1], LayerRoot);
                clone.name = $"层{layers.Count}";
                layers.Add(clone);

                Renderer cloneRenderer = clone.GetComponent<Renderer>();
                layerRenderers.Add(cloneRenderer);

                if (cloneRenderer != null)
                {
                    cloneRenderer.enabled = true;
                }
            }

            float layerHeight = totalHeight / count;

            for (int i = 0; i < count; i++)
            {
                Transform layer = layers[i];
                layer.localRotation = Quaternion.identity;
                layer.localScale = new Vector3(layerWidth, layerHeight, layerDepth);
                layer.localPosition = new Vector3(0f, (i + 0.5f) * layerHeight, 0f);
            }

            activeLayerCount = count;
        }

        /// <summary>按材料颜色列表刷色：第 i 块材料刷第 i 层。列表长度同时决定层数。</summary>
        public void Apply(IReadOnlyList<MaterialColor> colors)
        {
            int count = colors != null ? colors.Count : 0;

            // 一条需求都没有的建筑（或调用方传空）也要看得见，至少给一层兜底色
            if (count <= 0)
            {
                count = 1;
            }

            EnsureLayout(count);

            for (int i = 0; i < count; i++)
            {
                if (colors != null && i < colors.Count)
                {
                    SetLayerColor(i, colors[i]);
                }
                else
                {
                    SetLayerColor(i, fallbackColor);
                }
            }

            for (int i = count; i < layers.Count; i++)
            {
                SetLayerVisible(i, false);
            }
        }

        /// <summary>把第 index 层刷成指定材料颜色。</summary>
        public void SetLayerColor(int index, MaterialColor color)
        {
            SetLayerColor(index, color.ToDisplayColor());
        }

        /// <summary>把第 index 层刷成任意颜色（alpha 会被 layerAlpha 覆盖）。</summary>
        public void SetLayerColor(int index, Color color)
        {
            if (!TryGetLayerRenderer(index, out Renderer renderer))
            {
                return;
            }

            if (layerMaterial != null && renderer.sharedMaterial != layerMaterial)
            {
                // sharedMaterial 而不是 material：楼层会随蓝图反复生成销毁，
                // 用 material 会给每一层克隆一份材质实例，持续泄漏
                renderer.sharedMaterial = layerMaterial;
            }

            color.a = layerAlpha;

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            propertyBlock.Clear();
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(propertyBlock);
            renderer.enabled = true;

            if (logLayerChanges)
            {
                Debug.Log($"[BuildingLayerStack]「{name}」第 {index} 层 → {color}");
            }
        }

        /// <summary>隐藏第 index 层（保留样板，不销毁）。</summary>
        public void SetLayerVisible(int index, bool visible)
        {
            if (!TryGetLayerRenderer(index, out Renderer renderer))
            {
                return;
            }

            renderer.enabled = visible;
        }

        private bool TryGetLayerRenderer(int index, out Renderer renderer)
        {
            EnsureCached();

            if (index < 0 || index >= layers.Count)
            {
                renderer = null;
                return false;
            }

            renderer = layerRenderers[index];

            if (renderer == null)
            {
                // 样板层是个空物体（没有 Renderer），克隆出来的也没有。
                // 这属于预制体配错，说清楚比默默什么都不做强
                Debug.LogWarning($"[BuildingLayerStack]「{name}」第 {index} 层没有 Renderer，" +
                                 "这一层刷不上颜色。请确认样板层是带 MeshRenderer 的 Cube。", this);
                return false;
            }

            return true;
        }

        private void EnsureCached()
        {
            if (cached)
            {
                return;
            }

            cached = true;
            propertyBlock = new MaterialPropertyBlock();
            layers.Clear();
            layerRenderers.Clear();

            Transform root = LayerRoot;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                layers.Add(child);
                layerRenderers.Add(child.GetComponent<Renderer>());
            }

            if (layers.Count == 0)
            {
                WarnAboutMissingLayers();
                return;
            }

            // 宽深照抄样板层。预制体里样板层通常是一个 scale 4 的 Cube
            layerWidth = layers[0].localScale.x;
            layerDepth = layers[0].localScale.z;
        }

        private void WarnAboutMissingLayers()
        {
            if (warnedAboutMissingLayers)
            {
                return;
            }

            warnedAboutMissingLayers = true;
            Debug.LogError($"[BuildingLayerStack]「{name}」的楼层挂点「{LayerRoot.name}」下没有任何子物体，" +
                           "没有可以当样板的一层，建筑将完全没有外观。\n" +
                           "请在预制体里给这个挂点放至少一个 Cube 作为样板层，" +
                           "或在 Inspector 里把 Layer Root 指到正确的节点。", this);
        }

        [ContextMenu("调试：按 3 层红红蓝刷一次色")]
        private void DebugApplySample()
        {
            Apply(new List<MaterialColor> { MaterialColor.Red, MaterialColor.Red, MaterialColor.Blue });
        }
    }
}
