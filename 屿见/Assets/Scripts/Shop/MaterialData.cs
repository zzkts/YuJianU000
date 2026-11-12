using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Shop
{
    /// <summary>
    /// 原料静态数据（ScriptableObject）。
    /// 职责：只描述"小砖是什么"——名字、类型、价格、效果、外观色、预制体。
    /// 纯数据：不知道玩家、不知道金币、不知道库存，也不含任何购买逻辑。
    /// </summary>
    [CreateAssetMenu(fileName = "Material_", menuName = "屿见/原料数据", order = 0)]
    public class MaterialData : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("显示名称，例如：小砖")]
        [SerializeField] private string materialName = "未命名原料";

        [Tooltip("原料大类。只用于分类，不影响数值")]
        [SerializeField] private MaterialType materialType = MaterialType.Brick;

        [Header("价格")]
        [Tooltip("购买 1 个该原料消耗的金币。填 0 表示免费")]
        [SerializeField] private int price = 2;

        [Header("效果")]
        [Tooltip("该原料被建成消耗后为**玩家**伤害倍率提供的加成，可正可负；0 表示无额外效果。" +
                 "买进背包、拖进蓝图都不生效，只有建筑建成才计入（2026-10-29 已拍板 D1=B）")]
        [SerializeField] private float damageMultiplierBonus = 0.1f;

        [Tooltip("该原料被建成消耗后为**建筑**伤害倍率提供的加成（风琴博物馆自动攻击等），可正可负；" +
                 "0 表示无额外效果。与玩家伤害倍率是两条独立的加成")]
        [SerializeField] private float buildingDamageMultiplierBonus;

        [Header("表现")]
        [Tooltip("该原料在建筑楼层上的固定外观色。颜色不再是可购买变体，每种原料只有这一个颜色")]
        [SerializeField] private Color displayColor = new Color(0.80f, 0.22f, 0.20f, 1f);
        [Tooltip("该原料在世界中的预制体。阶段 2 只做购买与库存，预制体暂不实例化，留给阶段 3 建筑系统")]
        [SerializeField] private GameObject prefab;

        [Header("存档")]
        [Tooltip("跨存档稳定 ID。由菜单「屿见/通用模块/配置存档系统」按资产 GUID 自动生成，不要手改。" +
                 "改掉它会让旧存档里的该原料无法被识别")]
        [SerializeField] private string saveId = "";

        /// <summary>显示名称。</summary>
        public string MaterialName => materialName;

        /// <summary>原料大类。</summary>
        public MaterialType MaterialType => materialType;

        /// <summary>单价（金币）。</summary>
        public int Price => price;

        /// <summary>玩家伤害倍率加成。</summary>
        public float DamageMultiplierBonus => damageMultiplierBonus;

        /// <summary>建筑伤害倍率加成。</summary>
        public float BuildingDamageMultiplierBonus => buildingDamageMultiplierBonus;

        /// <summary>该原料在建筑楼层上的固定外观色。</summary>
        public Color DisplayColor => displayColor;

        /// <summary>世界预制体，可能为 null。</summary>
        public GameObject Prefab => prefab;

        /// <summary>跨存档稳定 ID。可能为空——为空时存档会明确报错并跳过该原料。</summary>
        public string SaveId => saveId;

        /// <summary>
        /// 效果的可读描述，供商店 UI 直接显示。
        /// D1=B 起加成只有建成后才计入，所以结尾统一标注「建成后生效」，
        /// 避免玩家理解成购买即生效。
        /// </summary>
        public string EffectDescription
        {
            get
            {
                bool hasPlayerBonus = !Mathf.Approximately(damageMultiplierBonus, 0f);
                bool hasBuildingBonus = !Mathf.Approximately(buildingDamageMultiplierBonus, 0f);

                if (!hasPlayerBonus && !hasBuildingBonus)
                {
                    return "无额外效果";
                }

                var parts = new List<string>();

                if (hasPlayerBonus)
                {
                    parts.Add($"玩家伤害倍率 {FormatBonus(damageMultiplierBonus)}");
                }

                if (hasBuildingBonus)
                {
                    parts.Add($"建筑伤害倍率 {FormatBonus(buildingDamageMultiplierBonus)}");
                }

                return string.Join("，", parts) + "（建成后生效）";
            }
        }

        /// <summary>把加成格式化成「+0.1」/「-0.1」/「0」。</summary>
        private static string FormatBonus(float value)
        {
            return value > 0f ? $"+{value:0.##}" : value.ToString("0.##");
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

            if (string.IsNullOrWhiteSpace(saveId))
            {
                // 不拦着资产使用，但存档时这条原料会被跳过。规则 5：不静默失败
                Debug.LogWarning($"[MaterialData] 资产「{name}」没有 Save Id，存档时会跳过它。" +
                                 "请运行菜单「屿见/通用模块/配置存档系统」自动生成。", this);
            }
        }
    }
}
