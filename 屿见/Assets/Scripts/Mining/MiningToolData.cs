using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 挖矿工具数据。对应策划表：
    /// 挖矿物品名称、物品 id、基础攻击力、每升一级增加的基础攻击力、每升一级需要的金币。
    /// 只存数值，不处理升级、不扣金币、不参与伤害计算。
    /// </summary>
    [CreateAssetMenu(fileName = "MiningTool", menuName = "屿见/挖矿/挖矿工具", order = 30)]
    public class MiningToolData : ScriptableObject
    {
        [Tooltip("显示名称，例如：石头")]
        [SerializeField] private string displayName = "石头";

        [Tooltip("物品 id，例如：mine_01")]
        [SerializeField] private string itemId = "mine_01";

        [Tooltip("1 级时的基础攻击力")]
        [SerializeField, Min(0)] private int baseAttack = 1;

        [Tooltip("每升一级的攻击力成长倍数（几何曲线，D7=A）。1.35 表示每级 ×1.35。" +
                 "填 1 或更小则退回旧的「每级固定加 attackPerLevel」线性模式")]
        [SerializeField, Min(1f)] private float attackGrowthPerLevel = 1.35f;

        [Tooltip("线性模式下的每级固定攻击力增量。仅当 attackGrowthPerLevel <= 1 时生效")]
        [SerializeField, Min(0)] private int attackPerLevel = 1;

        [Tooltip("1 级升 2 级的基础花费")]
        [SerializeField, Min(0)] private int goldCostPerLevel = 15;

        [Tooltip("升级花费的逐级成长倍数（D7=A 与攻击曲线同速）。1.35 表示 15 / 20 / 27 / 37…。" +
                 "填 1 或更小则退回「每级固定加 goldCostIncreasePerLevel」")]
        [SerializeField, Min(1f)] private float goldCostGrowthPerLevel = 1.35f;

        [Tooltip("线性模式下每升一级增加的金币花费。仅当 goldCostGrowthPerLevel <= 1 时生效")]
        [SerializeField, Min(0)] private int goldCostIncreasePerLevel = 15;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string ItemId => itemId;
        public int BaseAttack => Mathf.Max(0, baseAttack);
        public float AttackGrowthPerLevel => Mathf.Max(1f, attackGrowthPerLevel);
        public int AttackPerLevel => Mathf.Max(0, attackPerLevel);
        public int GoldCostPerLevel => Mathf.Max(0, goldCostPerLevel);
        public float GoldCostGrowthPerLevel => Mathf.Max(1f, goldCostGrowthPerLevel);
        public int GoldCostIncreasePerLevel => Mathf.Max(0, goldCostIncreasePerLevel);

        /// <summary>
        /// 指定等级的工具攻击力。1 级 = 基础攻击力。
        /// D7=A 起默认走几何曲线：基础攻击力 × 成长倍数^(等级-1)。
        /// </summary>
        public float GetAttackAtLevel(int level)
        {
            int safeLevel = Mathf.Max(1, level);

            if (AttackGrowthPerLevel > 1.0001f)
            {
                return BaseAttack * Mathf.Pow(AttackGrowthPerLevel, safeLevel - 1);
            }

            return BaseAttack + (safeLevel - 1) * AttackPerLevel;
        }

        /// <summary>
        /// 指定等级升到下一级需要的金币。
        /// D7=A 起默认走几何曲线：基础花费 × 成长倍数^(等级-1)，与攻击曲线同速。
        /// </summary>
        public int GetUpgradeCost(int level)
        {
            int safeLevel = Mathf.Max(1, level);

            if (GoldCostGrowthPerLevel > 1.0001f)
            {
                return Mathf.Max(0, Mathf.RoundToInt(
                    goldCostPerLevel * Mathf.Pow(GoldCostGrowthPerLevel, safeLevel - 1)));
            }

            return Mathf.Max(0, goldCostPerLevel + (safeLevel - 1) * goldCostIncreasePerLevel);
        }

        public void ConfigureForSetup(string newDisplayName, string newItemId,
            int newBaseAttack, int newAttackPerLevel, int newGoldCostPerLevel,
            int newGoldCostIncreasePerLevel = 15, float newAttackGrowthPerLevel = 1.35f,
            float newGoldCostGrowthPerLevel = 1.35f)
        {
            displayName = string.IsNullOrWhiteSpace(newDisplayName) ? "石头" : newDisplayName;
            itemId = string.IsNullOrWhiteSpace(newItemId) ? "mine_01" : newItemId;
            baseAttack = Mathf.Max(0, newBaseAttack);
            attackPerLevel = Mathf.Max(0, newAttackPerLevel);
            goldCostPerLevel = Mathf.Max(0, newGoldCostPerLevel);
            goldCostIncreasePerLevel = Mathf.Max(0, newGoldCostIncreasePerLevel);
            attackGrowthPerLevel = Mathf.Max(1f, newAttackGrowthPerLevel);
            goldCostGrowthPerLevel = Mathf.Max(1f, newGoldCostGrowthPerLevel);
        }
    }
}
