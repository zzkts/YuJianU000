using System.Collections.Generic;
using UnityEngine;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 挂在已建成的建筑实例上，记录「我是哪座建筑、占哪几格、每层用了哪种原料」。
    ///
    /// 为什么需要：BuildingManager 只保存 GameObject 列表，实例本身不知道自己来自哪个
    /// BuildingData，也不保留楼层材料。存档参与者必须能把实例翻译回稳定 ID + 数值，
    /// 所以由建造 / 读档两条路径在生成实例时写入这份运行时信息。
    ///
    /// 只存在于场景实例上，不要挂到预制体资产里。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacedBuildingInfo : MonoBehaviour
    {
        private BuildingData data;
        private RectInt footprintRect;
        private readonly List<MaterialData> layerMaterials = new List<MaterialData>();

        /// <summary>玩家在这座建筑上选过的强化选项 ID。存档按 ID 恢复。</summary>
        private readonly List<string> enhancementOptionIds = new List<string>();

        [Tooltip("建筑等级。建成与读档恢复时写入，升级时由 BuildingUpgrade 更新。" +
                 "旧存档没有该字段时按 1 级处理")]
        [SerializeField, Min(1)] private int level = 1;

        [Tooltip("建筑朝向：旋转了几个 90°（0~3）。迁移旋转功能写入，" +
                 "存档按这个值恢复；旧存档没有该字段时为 0（不旋转）")]
        [SerializeField, Range(0, 3)] private int quarterTurns;

        /// <summary>建筑静态数据。可能为空（旧实例或手动挂载），调用方需判空。</summary>
        public BuildingData Data => data;

        /// <summary>占地格子矩形（x → 世界 X，y → 世界 Z）。迁移后由 BuildingManager 更新。</summary>
        public RectInt FootprintRect => footprintRect;

        /// <summary>自下而上的楼层材料，只读。</summary>
        public IReadOnlyList<MaterialData> LayerMaterials => layerMaterials;

        /// <summary>建筑等级，恒 >= 1。</summary>
        public int Level => Mathf.Max(1, level);

        /// <summary>建筑朝向：0~3 个 90°，恒为合法值。</summary>
        public int QuarterTurns => BuildingRotation.Normalize(quarterTurns);

        /// <summary>这座建筑选过的强化选项 ID，只读。</summary>
        public IReadOnlyList<string> EnhancementOptionIds => enhancementOptionIds;

        /// <summary>
        /// 当前等级下还没有选择的强化节点。没有升级表 / 节点表或已全部选完时返回 null。
        /// 读档后按“等级 + 已选 ID”现算，不单独保存待选状态。
        /// </summary>
        public BuildingEnhancementNode PendingEnhancementNode
        {
            get
            {
                BuildingEnhancementNodeTable table = NodeTable;
                return table != null ? table.GetLowestUnresolvedNode(Level, enhancementOptionIds) : null;
            }
        }

        /// <summary>是否已经选过某个强化选项。</summary>
        public bool HasEnhancementOption(string optionId)
        {
            if (string.IsNullOrEmpty(optionId))
            {
                return false;
            }

            for (int i = 0; i < enhancementOptionIds.Count; i++)
            {
                if (enhancementOptionIds[i] == optionId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>某个强化节点是否已经选择过。</summary>
        public bool HasEnhancementForNode(BuildingEnhancementNode node)
        {
            BuildingEnhancementNodeTable table = NodeTable;
            return table != null && table.IsNodeResolved(node, enhancementOptionIds);
        }

        /// <summary>
        /// 把当前等级下所有强化加成按「作用范围 + 数值类型」汇总。
        /// 返回的是可直接参与乘法的加成值，例如 0.15 表示 +15%。
        /// 未知 ID（配置改过 / 存档脏数据）会被静默跳过。
        /// </summary>
        public float GetEnhancementBonus(BuildingEnhancementScope scope, BuildingEnhancementStat stat)
        {
            BuildingEnhancementNodeTable table = NodeTable;

            if (table == null || enhancementOptionIds.Count == 0)
            {
                return 0f;
            }

            HashSet<string> countedNonStackable = null;
            float total = 0f;

            for (int i = 0; i < enhancementOptionIds.Count; i++)
            {
                if (!table.TryGetOption(enhancementOptionIds[i], out BuildingEnhancementOption option) ||
                    !option.IsValid)
                {
                    continue;
                }

                if (option.Scope != scope || option.Stat != stat)
                {
                    continue;
                }

                if (!option.Stackable)
                {
                    countedNonStackable = countedNonStackable ?? new HashSet<string>();

                    if (!countedNonStackable.Add(option.OptionId))
                    {
                        continue;
                    }
                }

                total += option.Magnitude;
            }

            return total;
        }

        /// <summary>
        /// 由 BuildingManager 在建造成 / 读档恢复时调用。会复制材料列表与强化选择，
        /// 调用方之后修改自己的列表不会影响这里。
        /// </summary>
        /// <param name="initialLevel">初始等级。建成默认 1，读档传入存档等级。</param>
        /// <param name="enhancements">存档里的强化选项 ID。旧存档传 null。</param>
        public void Initialize(
            BuildingData source, RectInt rect, IReadOnlyList<MaterialData> materials, int initialLevel = 1,
            IReadOnlyList<string> enhancements = null, int initialQuarterTurns = 0)
        {
            data = source;
            footprintRect = rect;
            level = Mathf.Max(1, initialLevel);
            quarterTurns = BuildingRotation.Normalize(initialQuarterTurns);

            layerMaterials.Clear();

            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    layerMaterials.Add(materials[i]);
                }
            }

            SetEnhancementOptionIds(enhancements);
        }

        /// <summary>写入建筑等级。低于 1 的一律按 1 处理。</summary>
        public void SetLevel(int value)
        {
            level = Mathf.Max(1, value);
        }

        /// <summary>记录一个强化选择。重复 ID 不会重复写入。</summary>
        public void AddEnhancementOption(string optionId)
        {
            if (string.IsNullOrEmpty(optionId) || HasEnhancementOption(optionId))
            {
                return;
            }

            enhancementOptionIds.Add(optionId);
        }

        /// <summary>整体写入强化选择。未知 ID 会保留（表以后可能补回来），计算时跳过。</summary>
        public void SetEnhancementOptionIds(IReadOnlyList<string> ids)
        {
            enhancementOptionIds.Clear();

            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                string optionId = ids[i];

                if (!string.IsNullOrEmpty(optionId))
                {
                    enhancementOptionIds.Add(optionId);
                }
            }
        }

        /// <summary>迁移后更新占地矩形。占用登记本身由 BuildingManager 负责。</summary>
        public void SetFootprintRect(RectInt rect)
        {
            footprintRect = rect;
        }

        /// <summary>
        /// 写入建筑朝向。只改记录，不转 transform——旋转外观由 BuildingManager / BuildingMigration 负责，
        /// 这样存档参与者不必关心表现层。
        /// </summary>
        public void SetQuarterTurns(int turns)
        {
            quarterTurns = BuildingRotation.Normalize(turns);
        }

        /// <summary>本建筑升级表上的强化节点组，可能为 null。</summary>
        private BuildingEnhancementNodeTable NodeTable =>
            data != null && data.UpgradeTable != null ? data.UpgradeTable.EnhancementNodeTable : null;
    }
}
