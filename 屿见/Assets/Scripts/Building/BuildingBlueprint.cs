using UnityEngine;

namespace Yujian.Building
{
    /// <summary>蓝图的两种状态。可放置 = Valid（绿），不可放置 = Invalid（红）。</summary>
    public enum BlueprintState
    {
        Valid = 0,
        Invalid = 1,
    }

    /// <summary>
    /// 建筑蓝图。职责：它是"跟着鼠标走的那半透明影子"本身——
    /// 记住自己代表哪个建筑（BuildingData）、记录当前 Valid/Invalid 状态，并把状态画出来。
    ///
    /// 不做射线、不算坐标、不判断能不能放（那是 BuildingPlacement 的事），
    /// 也不决定自己什么时候出现/消失（那是 BuildingManager 的事）。
    /// </summary>
    public class BuildingBlueprint : MonoBehaviour
    {
        [Header("外观")]
        [Tooltip("可放置时套用的材质。建议是一个 URP/Unlit 或 URP/Lit 的**透明**材质（Surface Type = Transparent），" +
                 "基色取绿、Alpha 约 0.4。留空则退化为用 _BaseColor 染色，但材质本身不透明的话不会半透明")]
        [SerializeField] private Material validMaterial;

        [Tooltip("不可放置时套用的材质。同上，基色取红")]
        [SerializeField] private Material invalidMaterial;

        [Tooltip("未指定上面两个材质时的兜底染色（仅改颜色，不保证半透明）")]
        [SerializeField] private Color validTint = new Color(0.24f, 0.85f, 0.35f, 0.45f);

        [SerializeField] private Color invalidTint = new Color(0.85f, 0.25f, 0.22f, 0.45f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印状态切换日志。跟随鼠标时会频繁切换，正式游玩建议关闭")]
        [SerializeField] private bool logStateChanges = false;

        // 两个着色器属性名都写进去：URP 用 _BaseColor，Built-in 用 _Color。
        // MaterialPropertyBlock 会忽略目标着色器里不存在的属性，所以多设一个没有副作用。
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private BuildingData data;
        private BlueprintState state = BlueprintState.Valid;
        private Renderer[] renderers;
        private MaterialPropertyBlock propertyBlock;

        private bool warnedAboutMissingMaterials;
        private bool cached;

        /// <summary>本蓝图代表的建筑数据。Initialize 之前为 null。</summary>
        public BuildingData Data => data;

        /// <summary>当前状态。</summary>
        public BlueprintState State => state;

        /// <summary>占地尺寸（X = 宽，Y = 深）。数据缺失时返回零，调用方应视为不可放置。</summary>
        public Vector2 Footprint => data != null ? data.Footprint : Vector2.zero;

        /// <summary>
        /// 由 BuildingManager 在实例化后立即调用，把蓝图与它的建筑数据绑定起来。
        /// </summary>
        public void Initialize(BuildingData buildingData)
        {
            if (buildingData == null)
            {
                Debug.LogError("[BuildingBlueprint] Initialize 收到空的 BuildingData，蓝图将没有任何数据。", this);
            }

            data = buildingData;
            EnsureCached();

            // 初始按 Valid 画一次，避免第一帧保持预制体自带的外观
            state = BlueprintState.Valid;
            ApplyAppearance();
        }

        /// <summary>切换可放置状态。状态没变时直接返回，不做任何渲染操作。</summary>
        public void SetState(BlueprintState newState)
        {
            if (cached && state == newState)
            {
                return;
            }

            state = newState;
            EnsureCached();
            ApplyAppearance();

            if (logStateChanges)
            {
                Debug.Log($"[BuildingBlueprint] {(data != null ? data.BuildingName : name)} → " +
                          $"{(newState == BlueprintState.Valid ? "可放置(绿)" : "不可放置(红)")}");
            }
        }

        /// <summary>
        /// 缓存所有 Renderer，并强制关掉蓝图身上的一切 Collider。
        ///
        /// 关 Collider 是必须的：蓝图经常是直接复制建筑预制体做出来的，而建筑预制体带着 Collider。
        /// 如果蓝图留着 Collider，Physics.CheckBox 会检测到"蓝图自己"，导致蓝图永远判定为不可放置；
        /// 蓝图也不该被挖矿射线或任何物理查询碰到。
        /// </summary>
        private void EnsureCached()
        {
            if (cached)
            {
                return;
            }

            cached = true;
            renderers = GetComponentsInChildren<Renderer>(true);
            propertyBlock = new MaterialPropertyBlock();

            Collider[] colliders = GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            if (colliders.Length > 0)
            {
                Debug.Log($"[BuildingBlueprint] 已自动禁用蓝图「{name}」上的 {colliders.Length} 个 Collider。" +
                          "蓝图不应参与任何物理查询，这是正常处理，不是错误。", this);
            }

            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[BuildingBlueprint] 蓝图「{name}」的子物体上找不到任何 Renderer，" +
                                 "它将是完全不可见的。请检查蓝图预制体是否包含网格。", this);
            }
        }

        /// <summary>把当前状态画出来：优先换材质，材质没配就退化为染色。</summary>
        private void ApplyAppearance()
        {
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            Material target = state == BlueprintState.Valid ? validMaterial : invalidMaterial;

            if (target != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    // 用 sharedMaterial 而不是 material：后者会给每个 Renderer 克隆一份材质实例，
                    // 蓝图会反复生成销毁，那样会持续泄漏材质实例
                    renderers[i].sharedMaterial = target;
                }

                return;
            }

            if (!warnedAboutMissingMaterials)
            {
                warnedAboutMissingMaterials = true;
                Debug.LogWarning($"[BuildingBlueprint] 蓝图「{name}」没有指定 Valid/Invalid 材质，" +
                                 "已退化为用 _BaseColor 染色。若原材质是不透明材质，看到的只会是变色而不是半透明。" +
                                 "建议在 Assets/Materials 下各建一个 Surface Type = Transparent 的 URP 材质并赋值。", this);
            }

            Color tint = state == BlueprintState.Valid ? validTint : invalidTint;

            propertyBlock.Clear();
            propertyBlock.SetColor(BaseColorId, tint);
            propertyBlock.SetColor(ColorId, tint);

            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].SetPropertyBlock(propertyBlock);
            }
        }
    }
}
