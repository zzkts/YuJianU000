using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Building
{
    /// <summary>强化效果作用范围（策划案 3.4：全局 / 个体）。</summary>
    public enum BuildingEnhancementScope
    {
        /// <summary>只作用在当前这座建筑上。</summary>
        Individual = 0,

        /// <summary>统一应用到全局（所有建筑 / 玩家）。</summary>
        Global = 1,
    }

    /// <summary>强化效果影响的数值。新增类型一律追加到末尾，避免已序列化的整数值错位。</summary>
    public enum BuildingEnhancementStat
    {
        /// <summary>建筑伤害倍率。个体 = 本建筑伤害；全局 = 所有建筑伤害。</summary>
        BuildingDamage = 0,

        /// <summary>攻击速度。数值 +0.1 表示攻击间隔缩短到 1 / 1.1。</summary>
        AttackSpeed = 1,

        /// <summary>玩家伤害倍率。目前只有全局效果。</summary>
        PlayerDamage = 2,

        /// <summary>
        /// 回收站的垃圾转化速度。全局效果：+0.2 表示所有回收站转换速度 +20%
        /// （策划案 Node_Huishouzhan_01「回收速度提升」）。
        /// </summary>
        RecyclingSpeed = 3,

        /// <summary>
        /// 回收站的回收效率。个体效果：+0.1 表示该站每批产出的金币 +10%
        /// （策划案 Node_Huishouzhan_02「回收效率提升」，小数部分累计到下一批）。
        /// </summary>
        RecyclingEfficiency = 4,

        /// <summary>
        /// 暴击点持续时间。全局效果，数值是**比例**（0.2 = 基础持续时间 +20%）。
        /// 由 BuildingDamageBonus 汇总后写进 PlayerStats，CritPointSystem 在刷点时读取。
        /// </summary>
        CritPointDuration = 5,

        /// <summary>
        /// 玩家暴击倍率。全局效果，数值是**绝对值**（+1 = 暴击倍率额外 +1）。
        /// 由 BuildingDamageBonus 汇总后写进 PlayerStats，DamageSystem 计算暴击伤害时读取。
        /// </summary>
        PlayerCriticalMultiplier = 6,
    }

    /// <summary>
    /// 一条强化选项（策划案 3.4 的「强化选项表」）。
    ///
    /// 选项本身是纯数据：不读场景、不扣钱、不知道谁选中了它。
    /// 选择的记录与数值汇总在 <see cref="PlacedBuildingInfo"/>，
    /// 选择入口在 <see cref="BuildingUpgrade"/>。
    /// </summary>
    [Serializable]
    public class BuildingEnhancementOption
    {
        [Tooltip("跨存档稳定 ID。同一个节点表内必须唯一；改掉会让旧存档里的该选择失效")]
        [SerializeField] private string optionId = "";

        [Tooltip("显示给玩家的强化名称")]
        [SerializeField] private string displayName = "";

        [Tooltip("策划案里的「强化效果描述」。留空时 UI 只显示自动生成的数值摘要")]
        [SerializeField, TextArea(2, 5)] private string description = "";

        [Tooltip("全局 / 个体")]
        [SerializeField] private BuildingEnhancementScope scope = BuildingEnhancementScope.Individual;

        [Tooltip("影响的数值类型")]
        [SerializeField] private BuildingEnhancementStat stat = BuildingEnhancementStat.BuildingDamage;

        [Tooltip("是否可叠加。同一个选项如果出现在多个节点里，false 表示只计一次")]
        [SerializeField] private bool stackable = false;

        [Tooltip("效果数值。0.15 = +15%；攻速的 0.1 表示攻击间隔 ÷1.1")]
        [SerializeField] private float magnitude = 0.1f;

        /// <summary>跨存档稳定 ID。</summary>
        public string OptionId => optionId;

        /// <summary>显示名称。</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? optionId : displayName;

        /// <summary>策划案里填写的效果描述，可能为空。</summary>
        public string Description => description;

        /// <summary>全局 / 个体。</summary>
        public BuildingEnhancementScope Scope => scope;

        /// <summary>影响的数值类型。</summary>
        public BuildingEnhancementStat Stat => stat;

        /// <summary>是否可叠加。</summary>
        public bool Stackable => stackable;

        /// <summary>效果数值。</summary>
        public float Magnitude => magnitude;

        /// <summary>这条配置是否可用（ID、名称、数值都不为空）。</summary>
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(optionId) &&
            !string.IsNullOrWhiteSpace(displayName) &&
            Mathf.Abs(magnitude) > 1e-5f;

        /// <summary>「全局」/「个体」。</summary>
        public string ScopeName => scope == BuildingEnhancementScope.Global ? "全局" : "个体";

        /// <summary>数值类型的中文名。</summary>
        public string StatName
        {
            get
            {
                switch (stat)
                {
                    case BuildingEnhancementStat.BuildingDamage:
                        return "建筑伤害";

                    case BuildingEnhancementStat.AttackSpeed:
                        return "攻击速度";

                    case BuildingEnhancementStat.PlayerDamage:
                        return "玩家伤害";

                    case BuildingEnhancementStat.RecyclingSpeed:
                        return "回收速度";

                    case BuildingEnhancementStat.RecyclingEfficiency:
                        return "回收效率";

                    case BuildingEnhancementStat.CritPointDuration:
                        return "暴击点持续时间";

                    case BuildingEnhancementStat.PlayerCriticalMultiplier:
                        return "玩家暴击倍率";

                    default:
                        return stat.ToString();
                }
            }
        }

        /// <summary>自动生成的数值摘要，例如「个体·建筑伤害 +15%」。</summary>
        public string GetEffectSummary()
        {
            string sign = magnitude >= 0f ? "+" : string.Empty;

            // 暴击倍率是绝对值（+1），其余数值按百分比显示
            if (stat == BuildingEnhancementStat.PlayerCriticalMultiplier)
            {
                return $"{ScopeName}·{StatName} {sign}{magnitude:0.##}";
            }

            return $"{ScopeName}·{StatName} {sign}{magnitude * 100f:0.#}%";
        }

        /// <summary>给 UI 用的完整说明：策划描述 + 数值摘要。</summary>
        public string GetFullDescription()
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return GetEffectSummary();
            }

            return $"{description}\n{GetEffectSummary()}";
        }
    }

    /// <summary>
    /// 一个强化节点（策划案 3.4 的「强化节点表」）。
    /// 到达 <see cref="TriggerLevel"/> 时，玩家必须从 <see cref="OptionIds"/> 里选一项。
    /// </summary>
    [Serializable]
    public class BuildingEnhancementNode
    {
        [Tooltip("跨存档稳定 ID，例如 Node_10")]
        [SerializeField] private string nodeId = "";

        [Tooltip("建筑升到这一级时触发强化选择")]
        [SerializeField, Min(2)] private int triggerLevel = 10;

        [Tooltip("可选强化选项 ID。顺序就是 UI 里的显示顺序")]
        [SerializeField] private string[] optionIds = Array.Empty<string>();

        /// <summary>节点 ID。</summary>
        public string NodeId => nodeId;

        /// <summary>触发等级。</summary>
        public int TriggerLevel => Mathf.Max(2, triggerLevel);

        /// <summary>选项 ID 列表，只读。</summary>
        public IReadOnlyList<string> OptionIds => optionIds ?? Array.Empty<string>();

        /// <summary>这个节点是否包含某个选项。</summary>
        public bool ContainsOption(string optionId)
        {
            if (string.IsNullOrEmpty(optionId))
            {
                return false;
            }

            IReadOnlyList<string> ids = OptionIds;

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == optionId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>节点配置是否完整（有 ID 和至少一个选项）。</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(nodeId) && OptionIds.Count > 0;
    }

    /// <summary>
    /// 建筑强化节点表（策划案 3.4 的「强化节点表」+「强化选项表」）。
    ///
    /// 为什么把两张表合进一个资产：
    ///   · 节点对选项是强引用（选项 ID），单独拆成 10+ 个资产只增加配置负担；
    ///   · 选项 ID 在整张表内唯一，存档只记一个字符串；
    ///   · 将来要跨升级表复用选项时，再拆成独立资产也不影响运行时接口。
    ///
    /// 本类只做查表与校验，不保存「玩家选了什么」——选择存在建筑实例上。
    /// </summary>
    [CreateAssetMenu(fileName = "Enhancement_", menuName = "屿见/建筑强化节点表", order = 3)]
    public class BuildingEnhancementNodeTable : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("配置表里的节点组名称，例如 Enhancement_Normal_01")]
        [SerializeField] private string tableId = "Enhancement_Normal_01";

        [Header("强化选项")]
        [Tooltip("所有节点的可选强化。选项 ID 在整张表内唯一")]
        [SerializeField] private BuildingEnhancementOption[] options = Array.Empty<BuildingEnhancementOption>();

        [Header("强化节点")]
        [Tooltip("按触发等级配置。到达触发等级的升级完成时，玩家必须选一项")]
        [SerializeField] private BuildingEnhancementNode[] nodes = Array.Empty<BuildingEnhancementNode>();

        /// <summary>配置表里的节点组名称。</summary>
        public string TableId => string.IsNullOrEmpty(tableId) ? name : tableId;

        /// <summary>全部节点，只读。</summary>
        public IReadOnlyList<BuildingEnhancementNode> Nodes => nodes ?? Array.Empty<BuildingEnhancementNode>();

        /// <summary>全部选项，只读。</summary>
        public IReadOnlyList<BuildingEnhancementOption> Options =>
            options ?? Array.Empty<BuildingEnhancementOption>();

        /// <summary>按 ID 找选项。找不到或配置为空时返回 false。</summary>
        public bool TryGetOption(string optionId, out BuildingEnhancementOption option)
        {
            option = null;

            if (string.IsNullOrEmpty(optionId))
            {
                return false;
            }

            IReadOnlyList<BuildingEnhancementOption> all = Options;

            for (int i = 0; i < all.Count; i++)
            {
                BuildingEnhancementOption candidate = all[i];

                if (candidate != null && candidate.OptionId == optionId)
                {
                    option = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>按节点 ID 找节点。</summary>
        public bool TryGetNode(string nodeId, out BuildingEnhancementNode node)
        {
            node = null;

            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            IReadOnlyList<BuildingEnhancementNode> all = Nodes;

            for (int i = 0; i < all.Count; i++)
            {
                BuildingEnhancementNode candidate = all[i];

                if (candidate != null && candidate.NodeId == nodeId)
                {
                    node = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>正好在 <paramref name="level"/> 级触发的节点。没有则返回 null。</summary>
        public BuildingEnhancementNode GetNodeAtLevel(int level)
        {
            IReadOnlyList<BuildingEnhancementNode> all = Nodes;

            for (int i = 0; i < all.Count; i++)
            {
                BuildingEnhancementNode node = all[i];

                if (node != null && node.TriggerLevel == level && HasAnyValidOption(node))
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// 严格大于 <paramref name="level"/> 的下一个节点触发等级；没有则返回 0。
        /// 用于把 Max 升级停在节点前，避免一次跨过强制选择。
        /// </summary>
        public int GetNextTriggerLevel(int level)
        {
            int best = 0;
            IReadOnlyList<BuildingEnhancementNode> all = Nodes;

            for (int i = 0; i < all.Count; i++)
            {
                BuildingEnhancementNode node = all[i];

                if (node == null || node.TriggerLevel <= level || !HasAnyValidOption(node))
                {
                    continue;
                }

                if (best == 0 || node.TriggerLevel < best)
                {
                    best = node.TriggerLevel;
                }
            }

            return best;
        }

        /// <summary>
        /// 当前等级下**最低的、尚未选择**的节点。
        ///
        /// 一个节点算已解决：玩家已选过该节点选项列表里的任意一项。
        /// 读档后不需要单独保存「待选」状态，按等级 + 已选 ID 现算即可。
        /// 配置错误的节点（没有任何有效选项）会被跳过，避免把玩家锁死。
        /// </summary>
        public BuildingEnhancementNode GetLowestUnresolvedNode(
            int level, IReadOnlyList<string> selectedOptionIds)
        {
            BuildingEnhancementNode found = null;
            IReadOnlyList<BuildingEnhancementNode> all = Nodes;

            for (int i = 0; i < all.Count; i++)
            {
                BuildingEnhancementNode node = all[i];

                if (node == null || node.TriggerLevel > level || !HasAnyValidOption(node))
                {
                    continue;
                }

                if (IsNodeResolved(node, selectedOptionIds))
                {
                    continue;
                }

                if (found == null || node.TriggerLevel < found.TriggerLevel)
                {
                    found = node;
                }
            }

            return found;
        }

        /// <summary>这个节点是否已经有玩家选择。</summary>
        public bool IsNodeResolved(BuildingEnhancementNode node, IReadOnlyList<string> selectedOptionIds)
        {
            if (node == null || selectedOptionIds == null || selectedOptionIds.Count == 0)
            {
                return false;
            }

            IReadOnlyList<string> ids = node.OptionIds;

            for (int i = 0; i < ids.Count; i++)
            {
                string optionId = ids[i];

                // 只认真正存在于选项池里的 ID：存档里的脏数据不会把节点误判成已解决
                if (!TryGetOption(optionId, out BuildingEnhancementOption option) ||
                    !option.IsValid)
                {
                    continue;
                }

                for (int j = 0; j < selectedOptionIds.Count; j++)
                {
                    if (selectedOptionIds[j] == optionId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 把节点里的选项 ID 解析成选项对象。无效 ID 会被跳过。
        /// 返回新列表；只在 UI 重建时调用，不需要每帧跑。
        /// </summary>
        public List<BuildingEnhancementOption> ResolveOptions(BuildingEnhancementNode node)
        {
            List<BuildingEnhancementOption> resolved = new List<BuildingEnhancementOption>();

            if (node == null)
            {
                return resolved;
            }

            IReadOnlyList<string> ids = node.OptionIds;

            for (int i = 0; i < ids.Count; i++)
            {
                if (TryGetOption(ids[i], out BuildingEnhancementOption option) && option.IsValid)
                {
                    resolved.Add(option);
                }
            }

            return resolved;
        }

        /// <summary>节点是否至少有一个可用的选项。</summary>
        public bool HasAnyValidOption(BuildingEnhancementNode node)
        {
            if (node == null)
            {
                return false;
            }

            IReadOnlyList<string> ids = node.OptionIds;

            for (int i = 0; i < ids.Count; i++)
            {
                if (TryGetOption(ids[i], out BuildingEnhancementOption option) && option.IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            HashSet<string> optionIds = new HashSet<string>();

            if (options != null)
            {
                for (int i = 0; i < options.Length; i++)
                {
                    BuildingEnhancementOption option = options[i];

                    if (option == null)
                    {
                        Debug.LogWarning($"[BuildingEnhancementNodeTable]「{name}」选项列表第 {i} 项为空。", this);
                        continue;
                    }

                    if (!option.IsValid)
                    {
                        Debug.LogWarning($"[BuildingEnhancementNodeTable]「{name}」选项「{option.OptionId}」" +
                                         "填写不完整（ID / 名称 / 数值），运行时会被跳过。", this);
                        continue;
                    }

                    if (!optionIds.Add(option.OptionId))
                    {
                        Debug.LogWarning($"[BuildingEnhancementNodeTable]「{name}」选项 ID「{option.OptionId}」" +
                                         "重复。存档按 ID 恢复，重复 ID 会选错强化。", this);
                    }
                }
            }

            if (nodes == null)
            {
                return;
            }

            HashSet<int> triggerLevels = new HashSet<int>();

            for (int i = 0; i < nodes.Length; i++)
            {
                BuildingEnhancementNode node = nodes[i];

                if (node == null)
                {
                    Debug.LogWarning($"[BuildingEnhancementNodeTable]「{name}」节点列表第 {i} 项为空。", this);
                    continue;
                }

                if (!node.IsValid)
                {
                    Debug.LogWarning($"[BuildingEnhancementNodeTable]「{name}」节点「{node.NodeId}」" +
                                     "填写不完整（ID 或选项列表为空），运行时会被跳过。", this);
                    continue;
                }

                if (!triggerLevels.Add(node.TriggerLevel))
                {
                    Debug.LogWarning($"[BuildingEnhancementNodeTable]「{name}」有多个节点都配置在 " +
                                     $"{node.TriggerLevel} 级，只会取第一个。", this);
                }

                IReadOnlyList<string> ids = node.OptionIds;

                for (int j = 0; j < ids.Count; j++)
                {
                    if (!TryGetOption(ids[j], out BuildingEnhancementOption option) || !option.IsValid)
                    {
                        Debug.LogWarning($"[BuildingEnhancementNodeTable]「{name}」节点「{node.NodeId}」" +
                                         $"引用了不存在的选项 ID「{ids[j]}」，该选项会被跳过。", this);
                    }
                }
            }
        }
    }
}
