using UnityEngine;

namespace Yujian.Shop
{
    /// <summary>
    /// 原料静态数据（ScriptableObject）。
    /// 职责：只描述"小砖是什么"——名字、类型、价格、效果、可用颜色、预制体。
    /// 纯数据：不知道玩家、不知道金币、不知道库存，也不含任何购买逻辑。
    /// </summary>
    [CreateAssetMenu(fileName = "Material_", menuName = "屿见/原料数据", order = 0)]
    public class MaterialData : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("显示名称，例如：小砖")]
        [SerializeField] private string materialName = "未命名原料";

        [Tooltip("原料大类。与颜色无关——颜色是在购买时另选的")]
        [SerializeField] private MaterialType materialType = MaterialType.Brick;

        [Header("价格")]
        [Tooltip("购买 1 个该原料消耗的金币。填 0 表示免费")]
        [SerializeField] private int price = 2;

        [Header("效果")]
        [Tooltip("该原料为伤害倍率提供的加成，可正可负；0 表示无额外效果。" +
                 "具体何时生效（购买入包时 / 建造消耗时）尚未拍板，阶段 2 只存不算")]
        [SerializeField] private float damageMultiplierBonus = 0.1f;

        [Header("颜色")]
        [Tooltip("默认颜色。购买时未指定颜色则用它")]
        [SerializeField] private MaterialColor defaultColor = MaterialColor.Red;

        [Tooltip("可以把该原料买成哪些颜色。留空 = 只允许默认颜色一种")]
        [SerializeField] private MaterialColor[] allowedColors =
        {
            MaterialColor.Red, MaterialColor.Blue, MaterialColor.Green,
        };

        [Header("表现")]
        [Tooltip("该原料在世界中的预制体。阶段 2 只做购买与库存，预制体暂不实例化，留给阶段 3 建筑系统")]
        [SerializeField] private GameObject prefab;

        /// <summary>显示名称。</summary>
        public string MaterialName => materialName;

        /// <summary>原料大类。</summary>
        public MaterialType MaterialType => materialType;

        /// <summary>单价（金币）。</summary>
        public int Price => price;

        /// <summary>伤害倍率加成。</summary>
        public float DamageMultiplierBonus => damageMultiplierBonus;

        /// <summary>默认颜色。</summary>
        public MaterialColor DefaultColor => defaultColor;

        /// <summary>世界预制体，可能为 null。</summary>
        public GameObject Prefab => prefab;

        /// <summary>
        /// 该原料能否被买成指定颜色。
        /// allowedColors 留空时视为只支持默认颜色。
        /// </summary>
        public bool SupportsColor(MaterialColor color)
        {
            if (allowedColors == null || allowedColors.Length == 0)
            {
                return color == defaultColor;
            }

            for (int i = 0; i < allowedColors.Length; i++)
            {
                if (allowedColors[i] == color)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>效果的可读描述，供商店 UI 直接显示。例如 "伤害倍率 +0.1"。</summary>
        public string EffectDescription
        {
            get
            {
                if (Mathf.Approximately(damageMultiplierBonus, 0f))
                {
                    return "无额外效果";
                }

                string sign = damageMultiplierBonus > 0f ? "+" : "-";
                return $"伤害倍率 {sign}{Mathf.Abs(damageMultiplierBonus):0.##}";
            }
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(materialName))
            {
                Debug.LogWarning($"[MaterialData] 资产「{name}」的显示名称为空。", this);
            }

            if (price < 0)
            {
                Debug.LogWarning($"[MaterialData] 资产「{name}」的价格为负({price})，已按 0 处理。", this);
                price = 0;
            }

            if (allowedColors != null && allowedColors.Length > 0 && !SupportsColor(defaultColor))
            {
                Debug.LogWarning($"[MaterialData] 资产「{name}」的默认颜色 {defaultColor} 不在允许颜色列表内，" +
                                 "这会导致默认颜色的购买被拒绝。", this);
            }
        }
    }
}
