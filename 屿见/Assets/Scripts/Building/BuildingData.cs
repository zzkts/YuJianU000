using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑大类。
    /// 新增类别时**一律追加到末尾**：插到中间会改变已有资产里已序列化的整数值（同 MaterialType / MaterialColor 的约定）。
    /// </summary>
    public enum BuildingType
    {
        Museum = 0,
    }

    /// <summary>BuildingType 的中文显示名。目前只用于日志与将来的 UI 显示。</summary>
    public static class BuildingTypeUtility
    {
        /// <summary>取该建筑类型的中文名，例如 Museum → "博物馆"。</summary>
        public static string ToChineseName(this BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Museum:
                    return "博物馆";
                default:
                    // 未定义的类型返回枚举名：一旦看到英文名就知道枚举加了值但这里没补 case
                    return type.ToString();
            }
        }
    }

    /// <summary>
    /// 一条"建造该建筑需要多少某种原料"的记录。例如"小砖 ×3"。
    ///
    /// 注意：**这里不含颜色**。颜色是购买时才决定的，而"某个需求是否要求指定颜色"
    /// 属于阶段 4 的规则（见开发进度.md 未决事项）。阶段 3 只记录、不消耗、不做任何校验。
    /// </summary>
    [System.Serializable]
    public struct BuildingMaterialRequirement
    {
        [Tooltip("原料种类，指向 Assets/Data/Materials 下的 MaterialData 资产")]
        [SerializeField] private MaterialData material;

        [Tooltip("需要多少个")]
        [SerializeField] private int amount;

        public BuildingMaterialRequirement(MaterialData material, int amount)
        {
            this.material = material;
            this.amount = amount;
        }

        /// <summary>原料种类，可能为 null。</summary>
        public MaterialData Material => material;

        /// <summary>需要数量。</summary>
        public int Amount => amount;

        /// <summary>这条需求是否填写完整（原料非空且数量为正）。</summary>
        public bool IsValid => material != null && amount > 0;
    }

    /// <summary>
    /// 建筑静态数据（ScriptableObject）。
    /// 职责：只描述"这个建筑是什么"——名字、类型、外观预制体、蓝图预制体、占地面积、所需材料。
    /// 纯数据：不知道玩家、不知道库存、不含任何建造逻辑，也不知道自己被谁选中了。
    ///
    /// 阶段 3 不消耗材料：requiredMaterials 只记录，材料检查与扣除属于阶段 4。
    /// </summary>
    [CreateAssetMenu(fileName = "Building_", menuName = "屿见/建筑数据", order = 1)]
    public class BuildingData : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("显示名称，例如：风琴博物馆")]
        [SerializeField] private string buildingName = "未命名建筑";

        [Tooltip("建筑大类")]
        [SerializeField] private BuildingType buildingType = BuildingType.Museum;

        [Header("预制体")]
        [Tooltip("建成后真正留在场景里的建筑预制体。根物体建议自带 Collider，" +
                 "并放在 Building 层——放置时检测建筑之间是否重叠靠的就是它")]
        [SerializeField] private GameObject buildingPrefab;

        [Tooltip("选中该建筑时跟随鼠标的半透明蓝图预制体。根物体上必须挂 BuildingBlueprint 组件。" +
                 "蓝图身上不能有 Collider（BuildingBlueprint 会自动禁用，但最好一开始就别加）")]
        [SerializeField] private GameObject blueprintPrefab;

        [Header("占地")]
        [Tooltip("占地尺寸。X = 宽（世界 X 轴），Y = 深（世界 Z 轴），单位与场景一致。" +
                 "只用于放置时的重叠检测，不改变预制体本身大小")]
        [SerializeField] private Vector2 footprint = new Vector2(4f, 4f);

        [Header("所需材料（阶段 3 只记录，不消耗）")]
        [Tooltip("建造该建筑需要的原料清单。阶段 3 完全不读取这张表——" +
                 "即使玩家一个原料都没有也允许放置。材料检查与扣除是阶段 4 的事")]
        [SerializeField] private BuildingMaterialRequirement[] requiredMaterials = new BuildingMaterialRequirement[0];

        /// <summary>显示名称。</summary>
        public string BuildingName => buildingName;

        /// <summary>建筑大类。</summary>
        public BuildingType BuildingType => buildingType;

        /// <summary>建成后的建筑预制体，可能为 null。</summary>
        public GameObject BuildingPrefab => buildingPrefab;

        /// <summary>蓝图预制体，可能为 null。</summary>
        public GameObject BlueprintPrefab => blueprintPrefab;

        /// <summary>占地尺寸：X = 宽，Y = 深。</summary>
        public Vector2 Footprint => footprint;

        /// <summary>所需材料清单，只读。阶段 3 不消费它。</summary>
        public IReadOnlyList<BuildingMaterialRequirement> RequiredMaterials =>
            requiredMaterials ?? System.Array.Empty<BuildingMaterialRequirement>();

        /// <summary>占地面积是否可用（两个方向都为正）。</summary>
        public bool HasValidFootprint => footprint.x > 0f && footprint.y > 0f;

        /// <summary>所需材料的可读描述，例如 "小砖 ×3"。目前用于日志与将来的建造 UI。</summary>
        public string GetRequirementText()
        {
            if (requiredMaterials == null || requiredMaterials.Length == 0)
            {
                return "无";
            }

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < requiredMaterials.Length; i++)
            {
                if (!requiredMaterials[i].IsValid)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("、");
                }

                builder.Append(requiredMaterials[i].Material.MaterialName)
                       .Append(" ×")
                       .Append(requiredMaterials[i].Amount);
            }

            return builder.Length == 0 ? "无" : builder.ToString();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(buildingName))
            {
                Debug.LogWarning($"[BuildingData] 资产「{name}」的显示名称为空。", this);
            }

            if (!HasValidFootprint)
            {
                Debug.LogWarning($"[BuildingData] 资产「{name}」的占地尺寸 {footprint} 含非正数，" +
                                 "重叠检测会一律判定为「不可放置」。请把宽和深都设成正数。", this);
            }

            if (buildingPrefab == null)
            {
                Debug.LogWarning($"[BuildingData] 资产「{name}」没有配置建筑 Prefab，确认放置时将无法生成实体建筑。", this);
            }

            if (blueprintPrefab == null)
            {
                Debug.LogWarning($"[BuildingData] 资产「{name}」没有配置蓝图 Prefab，该建筑无法被选中。", this);
            }

            if (requiredMaterials == null)
            {
                return;
            }

            for (int i = 0; i < requiredMaterials.Length; i++)
            {
                if (!requiredMaterials[i].IsValid)
                {
                    Debug.LogWarning($"[BuildingData] 资产「{name}」的第 {i} 条材料需求填写不完整" +
                                     "（原料为空或数量非正），阶段 4 结算时会把它当作无效条目。", this);
                }
            }
        }
    }
}
