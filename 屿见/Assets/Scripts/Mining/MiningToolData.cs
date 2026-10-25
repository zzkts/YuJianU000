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

        [Tooltip("每升一级增加的基础攻击力")]
        [SerializeField, Min(0)] private int attackPerLevel = 1;

        [Tooltip("1 级升 2 级的基础花费")]
        [SerializeField, Min(0)] private int goldCostPerLevel = 15;

        [Tooltip("每升一级，升级花费额外增加多少金币。15 表示 15 / 30 / 45 / 60…")]
        [SerializeField, Min(0)] private int goldCostIncreasePerLevel = 15;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string ItemId => itemId;
        public int BaseAttack => Mathf.Max(0, baseAttack);
        public int AttackPerLevel => Mathf.Max(0, attackPerLevel);
        public int GoldCostPerLevel => Mathf.Max(0, goldCostPerLevel);
        public int GoldCostIncreasePerLevel => Mathf.Max(0, goldCostIncreasePerLevel);

        /// <summary>指定等级的工具攻击力。1 级 = 基础攻击力。</summary>
        public int GetAttackAtLevel(int level)
        {
            int safeLevel = Mathf.Max(1, level);
            return BaseAttack + (safeLevel - 1) * AttackPerLevel;
        }

        /// <summary>
        /// 指定等级升到下一级需要的金币。
        /// 费用逐级递增：基础花费 + (当前等级 - 1) × 每级增量。
        /// </summary>
        public int GetUpgradeCost(int level)
        {
            int safeLevel = Mathf.Max(1, level);
            return Mathf.Max(0, goldCostPerLevel + (safeLevel - 1) * goldCostIncreasePerLevel);
        }

        public void ConfigureForSetup(string newDisplayName, string newItemId,
            int newBaseAttack, int newAttackPerLevel, int newGoldCostPerLevel,
            int newGoldCostIncreasePerLevel = 15)
        {
            displayName = string.IsNullOrWhiteSpace(newDisplayName) ? "石头" : newDisplayName;
            itemId = string.IsNullOrWhiteSpace(newItemId) ? "mine_01" : newItemId;
            baseAttack = Mathf.Max(0, newBaseAttack);
            attackPerLevel = Mathf.Max(0, newAttackPerLevel);
            goldCostPerLevel = Mathf.Max(0, newGoldCostPerLevel);
            goldCostIncreasePerLevel = Mathf.Max(0, newGoldCostIncreasePerLevel);
        }
    }
}
