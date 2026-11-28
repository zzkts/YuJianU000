using System;
using UnityEngine;
using Yujian.Player;

namespace Yujian.Mining
{
    /// <summary>
    /// 照明工具（飞书 2.1.4 照明系统）。
    ///
    /// 它本身不发光，也不控制画面明暗；只做玩法层的事：
    /// 玩家买到的等级 → 暴击点刷新冷却 / 半径 / 数量 / 暴击倍率。
    ///
    /// 等级（下标 = Level），共 6 档：
    ///   0 未拥有：不生成暴击点
    ///   1 火把 light_01：冷却 5.0 秒、半径 3.0、暴击点 1 个、暴击 200%
    ///   2 小型手电 light_02：冷却 4.5 秒、半径 3.0、暴击点 2 个、暴击 210%
    ///   3 矿灯 light_03：冷却 4.0 秒、半径 3.5、暴击点 2 个、暴击 220%
    ///   4 强光灯 light_04：冷却 3.5 秒、半径 3.5、暴击点 3 个、暴击 230%
    ///   5 探照灯 light_05：冷却 3.5 秒、半径 4.0、暴击点 3 个、暴击 240%
    ///   6 头灯 light_06：冷却 3.0 秒、半径 4.0、暴击点 3 个、暴击 250%
    ///
    /// 数值全部在 Inspector 上可调，代码不写死。扣钱与升级入口与 MiningToolProgress 同一套规则。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningLightTool : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家金币。留空时运行时自动查找")]
        [SerializeField] private PlayerCurrency playerCurrency;

        [Header("初始状态")]
        [Tooltip("进入游戏时的照明等级。0 = 未拥有，1 = 火把，2 = 手电")]
        [SerializeField, Min(0)] private int startingLevel;

        [Header("等级名称（下标 = 等级）")]
        [SerializeField] private string[] levelNames =
            { "未拥有", "火把", "小型手电", "矿灯", "强光灯", "探照灯", "头灯" };

        [Header("物品 ID（下标 = 等级）")]
        [SerializeField] private string[] itemIds =
            { "none", "light_01", "light_02", "light_03", "light_04", "light_05", "light_06" };

        [Header("暴击点上限（下标 = 等级；0 = 不生成）")]
        [SerializeField] private int[] capacityByLevel = { 0, 1, 2, 2, 3, 3, 3 };

        [Header("刷新冷却，秒（下标 = 等级；0 = 不生成）")]
        [Tooltip("该等级的照明工具每多少秒刷新一个暴击点。飞书：火把 5 秒")]
        [SerializeField] private float[] refreshCooldownByLevel = { 0f, 5f, 4.5f, 4f, 3.5f, 3.5f, 3f };

        [Header("暴击点半径（下标 = 等级）")]
        [Tooltip("暴击点在矿石平面中心附近随机生成的最大半径。飞书：火把 3")]
        [SerializeField] private float[] critPointRadiusByLevel = { 0f, 3f, 3f, 3.5f, 3.5f, 4f, 4f };

        [Header("暴击伤害倍率（下标 = 等级）")]
        [Tooltip("2 = 200%。飞书：火把 200%，小型手电 200%~1000%")]
        [SerializeField] private float[] criticalDamageMultiplierByLevel =
            { 1f, 2f, 2.1f, 2.2f, 2.3f, 2.4f, 2.5f };

        [Header("暴击点停留")]
        [Tooltip("勾选后暴击点永久保留、不显示剩余时间进度条。默认关闭：按下面的等级表倒计时存在")]
        [SerializeField] private bool critPointsPersist;

        [Header("暴击点停留时长，秒（下标 = 等级；仅倒计时模式使用）")]
        [Tooltip("基础存在时间。后续建筑可以在此基础上追加秒数")]
        [SerializeField] private float[] lifetimeByLevel = { 2.5f, 3f, 3.5f, 4f, 4.5f, 5f, 5.5f };

        [Header("购买价格（2026-11-26 起走公式，等级无上限）")]
        [Tooltip("1 级升 2 级的基础花费")]
        [SerializeField, Min(0)] private int upgradeCostBase = 4;

        [Tooltip("每级花费倍数")]
        [SerializeField, Min(1f)] private float upgradeCostGrowth = 1.2f;

        [Tooltip("升到 10/20/30… 级时再乘的花费倍数")]
        [SerializeField, Min(1f)] private float milestoneCostMultiplier = 4f;

        [Header("等级上限")]
        [Tooltip("最高等级。Demo 阶段填 999 等于无限；到顶后不再提供升级")]
        [SerializeField, Min(1)] private int maxLevel = 999;

        [Header("超级暴击（2026-11-26 新增）")]
        [Tooltip("1 级的超级暴击概率。命中暴击点后再按这个概率判定")]
        [SerializeField, Range(0f, 1f)] private float superCritBaseChance = 0.08f;

        [Tooltip("每个里程碑（每 10 级）增加的概率")]
        [SerializeField, Range(0f, 1f)] private float superCritChancePerMilestone = 0.07f;

        [Tooltip("概率上限。36% 时约每 3 秒出一次，和「数字在跳」的节奏对齐")]
        [SerializeField, Range(0f, 1f)] private float superCritChanceCap = 0.36f;

        [Tooltip("1 级的超级暴击倍率（相对普通暴击）")]
        [SerializeField, Min(1f)] private float superCritBaseMultiplier = 5f;

        [Tooltip("每一级增加的超级暴击倍率")]
        [SerializeField, Min(0f)] private float superCritMultiplierPerLevel = 0.15f;

        [Tooltip("每个里程碑额外增加的超级暴击倍率")]
        [SerializeField, Min(0f)] private float superCritMultiplierPerMilestone = 1f;

        [Tooltip("普通暴击倍率。2026-11-26 起固定，不再随等级成长")]
        [SerializeField, Min(1f)] private float normalCritMultiplier = 2f;

        [Header("调试")]
        [SerializeField] private bool logUpgradeFlow = true;

        private int currentLevel;

        /// <summary>当前等级。0 = 未拥有。</summary>
        public int Level => Mathf.Clamp(currentLevel, 0, MaxLevel);

        /// <summary>最高等级。2026-11-26 起由 maxLevel 决定，不再跟名称数组走。</summary>
        public int MaxLevel => Mathf.Max(1, maxLevel);

        /// <summary>里程碑间隔：每多少级一次大跳。</summary>
        public int MilestoneEvery => 10;

        /// <summary>下一次里程碑等级（10/20/30…）。</summary>
        public int NextMilestoneLevel => ((Level / MilestoneEvery) + 1) * MilestoneEvery;

        /// <summary>下一级是不是里程碑。</summary>
        public bool IsNextLevelMilestone => (Level + 1) % MilestoneEvery == 0;

        /// <summary>是否已经买满。</summary>
        public bool IsMaxed => Level >= MaxLevel;

        /// <summary>是否已经解锁（拥有任意照明工具）。</summary>
        public bool IsUnlocked => Level > 0;

        /// <summary>当前等级的物品 ID。</summary>
        public string ItemId => LookupId(Level);

        /// <summary>下一等级的物品 ID；已满级时返回当前 ID。</summary>
        public string NextItemId => IsMaxed ? ItemId : LookupId(Level + 1);

        /// <summary>当前等级下的暴击点同时存在上限。未拥有时为 0。</summary>
        public int CritPointCapacity => Level <= 0 ? 0 : Mathf.Max(1, LookupInt(capacityByLevel, Level, 1));

        /// <summary>下一级的暴击点上限；已满级时返回当前值。</summary>
        public int NextCritPointCapacity => IsMaxed ? CritPointCapacity : LookupInt(capacityByLevel, Level + 1, CritPointCapacity);

        /// <summary>当前照明工具的刷新冷却（秒）。0 表示不生成暴击点。</summary>
        public float RefreshCooldown => Level <= 0 ? 0f : Mathf.Max(0f, LookupFloat(refreshCooldownByLevel, Level, 0f));

        /// <summary>下一级的刷新冷却；已满级时返回当前值。</summary>
        public float NextRefreshCooldown => IsMaxed ? RefreshCooldown : LookupFloat(refreshCooldownByLevel, Level + 1, RefreshCooldown);

        /// <summary>当前等级的暴击点生成半径。</summary>
        public float CritPointRadius => Level <= 0 ? 0f : Mathf.Max(0f, LookupFloat(critPointRadiusByLevel, Level, 0f));

        /// <summary>下一级的暴击点半径；已满级时返回当前值。</summary>
        public float NextCritPointRadius => IsMaxed ? CritPointRadius : LookupFloat(critPointRadiusByLevel, Level + 1, CritPointRadius);

        /// <summary>当前等级的暴击伤害倍率（2 = 200%）。未拥有时为 1。2026-11-26 起固定值。</summary>
        public float CritPointDamageMultiplier => Level <= 0 ? 1f : Mathf.Max(1f, normalCritMultiplier);

        /// <summary>下一级的暴击伤害倍率；现已固定，两个值相同。</summary>
        public float NextCritPointDamageMultiplier => CritPointDamageMultiplier;

        /// <summary>超级暴击概率。0 级为 0（还触发不了，UI 也不该显示）。</summary>
        public float SuperCritChance
        {
            get
            {
                if (Level <= 0)
                {
                    return 0f;
                }

                float chance = superCritBaseChance +
                               superCritChancePerMilestone * (Level / MilestoneEvery);
                return Mathf.Clamp(chance, 0f, Mathf.Clamp01(superCritChanceCap));
            }
        }

        /// <summary>超级暴击倍率（相对普通暴击）。无上限，随等级一直长。</summary>
        public float SuperCritMultiplier
        {
            get
            {
                if (Level <= 0)
                {
                    return 1f;
                }

                int milestones = Level / MilestoneEvery;
                return Mathf.Max(1f,
                    superCritBaseMultiplier +
                    superCritMultiplierPerLevel * (Level - 1) +
                    superCritMultiplierPerMilestone * milestones);
            }
        }

        /// <summary>掷一次超级暴击判定。未拥有时永远为 false。</summary>
        public bool RollSuperCrit()
        {
            float chance = SuperCritChance;
            return chance > 0f && UnityEngine.Random.value < chance;
        }

        /// <summary>暴击点是否永久保留（不显示剩余时间进度条）。</summary>
        public bool CritPointsPersist => critPointsPersist;

        /// <summary>当前是否具备刷新暴击点的条件。</summary>
        public bool CanSpawnCritPoints => Level > 0 && RefreshCooldown > 0f && CritPointCapacity > 0;

        /// <summary>当前等级下的暴击点停留时长（秒）。仅倒计时模式使用。</summary>
        public float CritPointLifetime => LookupFloat(lifetimeByLevel, Level, 2.5f);

        /// <summary>下一级的停留时长；已满级时返回当前值。</summary>
        public float NextCritPointLifetime => IsMaxed
            ? CritPointLifetime
            : LookupFloat(lifetimeByLevel, Level + 1, CritPointLifetime);

        /// <summary>当前等级名称。</summary>
        public string LevelName => Level <= 0 ? "未拥有" : $"照明 Lv.{Level}";

        /// <summary>下一级名称；已满级时返回当前名称。</summary>
        public string NextLevelName => IsMaxed ? LevelName : $"照明 Lv.{Level + 1}";

        /// <summary>升到下一级需要的金币；已满级返回 0。</summary>
        public int NextUpgradeCost => GetUpgradeCostAt(Level);

        /// <summary>
        /// 指定等级升到下一级需要的金币（2026-11-26 起走公式，等级无上限）：
        /// 基础花费 × 成长倍数^(等级-1)，升到 10/20/30… 级那一档再乘里程碑倍数。
        /// </summary>
        public int GetUpgradeCostAt(int level)
        {
            if (level < 0 || level >= MaxLevel)
            {
                return 0;
            }

            float cost = Mathf.Max(0, upgradeCostBase) *
                         Mathf.Pow(Mathf.Max(1f, upgradeCostGrowth), level);

            if ((level + 1) % MilestoneEvery == 0)
            {
                cost *= Mathf.Max(1f, milestoneCostMultiplier);
            }

            if (cost >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Mathf.Max(0, Mathf.RoundToInt(cost));
        }

        /// <summary>指定等级的刷新冷却，供界面逐行查看。</summary>
        public float GetRefreshCooldownAt(int level)
        {
            return Mathf.Max(0f, LookupFloat(refreshCooldownByLevel,
                Mathf.Clamp(level, 0, MaxLevel), 0f));
        }

        /// <summary>指定等级的暴击点上限，供界面逐行查看。</summary>
        public int GetCritPointCapacityAt(int level)
        {
            return Mathf.Max(0, LookupInt(capacityByLevel, Mathf.Clamp(level, 0, MaxLevel), 0));
        }

        /// <summary>指定等级的暴击伤害倍率，供界面逐行查看。</summary>
        public float GetCritPointMultiplierAt(int level)
        {
            return Mathf.Max(1f, LookupFloat(criticalDamageMultiplierByLevel,
                Mathf.Clamp(level, 0, MaxLevel), 1f));
        }

        /// <summary>指定等级的暴击点半径，供界面逐行查看。</summary>
        public float GetCritPointRadiusAt(int level)
        {
            return Mathf.Max(0f, LookupFloat(critPointRadiusByLevel,
                Mathf.Clamp(level, 0, MaxLevel), 0f));
        }

        /// <summary>指定等级的工具名，用于界面上逐行显示。</summary>
        public string GetLevelName(int level)
        {
            return LookupName(Mathf.Clamp(level, 0, MaxLevel));
        }

        /// <summary>指定等级的物品 id，用于界面上逐行显示。</summary>
        public string GetItemId(int level)
        {
            return LookupId(Mathf.Clamp(level, 0, MaxLevel));
        }

        /// <summary>从当前等级连续升 count 级需要的总金币，超过最高等级时按实际级数算。</summary>
        public int GetUpgradeCostForLevels(int count)
        {
            int total = 0;
            int level = Level;

            for (int i = 0; i < count && level < MaxLevel; i++, level++)
            {
                total += GetUpgradeCostAt(level);
            }

            return total;
        }

        /// <summary>用当前金币一次性能升多少级（Max 模式用）。</summary>
        public int GetAffordableUpgradeCount(int money)
        {
            int count = 0;
            int level = Level;
            int spent = 0;

            while (level < MaxLevel)
            {
                int cost = GetUpgradeCostAt(level);

                if (spent + cost > money)
                {
                    break;
                }

                spent += cost;
                count++;
                level++;
            }

            return count;
        }

        /// <summary>等级变化时广播。</summary>
        public event Action<MiningLightTool> OnToolChanged;

        private void Awake()
        {
            currentLevel = Mathf.Clamp(startingLevel, 0, MaxLevel);

            if (playerCurrency == null)
            {
                playerCurrency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }
        }

        /// <summary>编辑器向导调用。</summary>
        public void ConfigureForSetup(PlayerCurrency currency)
        {
            playerCurrency = currency;
        }

        /// <summary>
        /// 编辑器向导调用：设置开局等级。
        /// 1 = 开局直接装备火把；0 = 开局未拥有，需要先花金币解锁。
        /// </summary>
        public void ConfigureStartingLevel(int level)
        {
            startingLevel = Mathf.Clamp(level, 0, MaxLevel);
            currentLevel = startingLevel;
        }

        /// <summary>
        /// 尝试买到下一级。金币不足 / 已满级时返回 false，状态不变。
        /// 价格为 0 表示免费发放。
        /// </summary>
        public bool TryUpgrade()
        {
            return TryUpgrade(1);
        }

        /// <summary>
        /// 连续升 count 级（x1 / Max 共用）。金币按实际级数一次性扣，
        /// 金币不足、count <= 0 或已满级时返回 false，状态不变。
        /// </summary>
        public bool TryUpgrade(int count)
        {
            if (IsMaxed)
            {
                if (logUpgradeFlow)
                {
                    Debug.Log($"[MiningLightTool] 已是最高等级「{LevelName}」，无需再买。", this);
                }

                return false;
            }

            if (count <= 0)
            {
                return false;
            }

            int targetLevel = Mathf.Min(MaxLevel, Level + count);
            int actualCount = targetLevel - Level;
            int cost = GetUpgradeCostForLevels(actualCount);

            if (playerCurrency == null)
            {
                Debug.LogError("[MiningLightTool] 没有 PlayerCurrency，无法扣金币。", this);
                return false;
            }

            if (cost > 0 && !playerCurrency.TrySpendMoney(cost))
            {
                if (logUpgradeFlow)
                {
                    Debug.Log($"[MiningLightTool] 金币不足，升级 {actualCount} 级需要 {cost}，" +
                              $"当前 {playerCurrency.CurrentMoney}。", this);
                }

                return false;
            }

            currentLevel = targetLevel;
            OnToolChanged?.Invoke(this);

            if (logUpgradeFlow)
            {
                Debug.Log($"[MiningLightTool] 升到「{LevelName}」（{ItemId}）：" +
                          $"冷却 {RefreshCooldown:0.#} 秒，半径 {CritPointRadius:0.#}，" +
                          $"上限 {CritPointCapacity}，暴击 {CritPointDamageMultiplier:0.##} 倍。", this);
            }

            return true;
        }

        /// <summary>读档恢复用：直接设置等级，不扣钱、不广播前先夹紧。</summary>
        public void SetLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 0, MaxLevel);

            if (clamped == currentLevel)
            {
                return;
            }

            currentLevel = clamped;
            OnToolChanged?.Invoke(this);
        }

        private string LookupId(int level)
        {
            if (itemIds == null || itemIds.Length == 0)
            {
                return string.Empty;
            }

            int index = Mathf.Clamp(level, 0, itemIds.Length - 1);
            return itemIds[index] ?? string.Empty;
        }

        private string LookupName(int level)
        {
            if (levelNames == null || levelNames.Length == 0)
            {
                return $"等级 {level}";
            }

            int index = Mathf.Clamp(level, 0, levelNames.Length - 1);
            return string.IsNullOrWhiteSpace(levelNames[index]) ? $"等级 {index}" : levelNames[index];
        }

        private static int LookupInt(int[] values, int level, int fallback)
        {
            if (values == null || values.Length == 0)
            {
                return fallback;
            }

            return values[Mathf.Clamp(level, 0, values.Length - 1)];
        }

        private static float LookupFloat(float[] values, int level, float fallback)
        {
            if (values == null || values.Length == 0)
            {
                return fallback;
            }

            return values[Mathf.Clamp(level, 0, values.Length - 1)];
        }
    }
}
