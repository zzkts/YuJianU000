using System;
using System.Collections.Generic;

namespace Yujian.Save
{
    /// <summary>
    /// 存档根数据。只放“真相”，不放派生值与场景引用。
    ///
    /// 约定：
    ///   · 所有字段都是 public + 可序列化，JsonUtility 才能写进来；
    ///   · 存的是稳定 ID（字符串）与普通数值，不存 MaterialData / BuildingData 这类资产引用；
    ///   · 玩家等级、伤害倍率这类派生值不存，读档后按规则重算；
    ///   · 新增字段一律给默认值，旧存档缺字段时保持默认，不报空引用。
    /// </summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>
        /// 当前存档结构版本。字段含义变化或新增迁移步骤时 +1。
        /// v2 起文件带 <see cref="checksum"/>，读取时校验完整性。
        /// </summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// 存档结构版本。**默认 0 表示未知**，不能默认成 CurrentVersion——
        /// JsonUtility 对 JSON 里不存在的字段会保留字段初始值，
        /// 若这里默认当前版本，旧文件缺失 version 时会被误判成最新版并跳过迁移。
        /// </summary>
        public int version = 0;

        /// <summary>
        /// 存档内容校验和（FNV-1a 64 位，十六进制）。v2 起必填。
        /// 计算时先把本字段清空，再对整个 JSON 求值，见 SaveIntegrity。
        /// </summary>
        public string checksum = string.Empty;

        /// <summary>保存时间（UTC ticks）。只用于日志与“最后游玩时间”，不参与玩法。</summary>
        public long savedAtUtc;

        /// <summary>玩家金币。</summary>
        public int currency;

        /// <summary>材料库存：每条是 (原料 ID, 颜色, 数量)。</summary>
        public List<MaterialSave> materials = new List<MaterialSave>();

        /// <summary>已放置建筑。由 BuildingSaveParticipant 写入 / 恢复。</summary>
        public List<BuildingSave> buildings = new List<BuildingSave>();

        /// <summary>矿石生成进度。第 1 步只定义结构，第 3 步开始写入。</summary>
        public MiningSave mining = new MiningSave();

        /// <summary>文化碎片进度。第 1 步只定义结构，第 3 步开始写入。</summary>
        public FragmentSave fragments = new FragmentSave();

        /// <summary>
        /// 保存瞬间蓝图里已经放入的材料。读档时退回库存，避免退出游戏吞材料。
        /// 由 BlueprintSaveParticipant 写入 / 退款。
        /// </summary>
        public List<MaterialSave> blueprintRefund = new List<MaterialSave>();

        /// <summary>
        /// 把可能为 null 的集合补成空集合。JsonUtility 对缺失字段的处理不够直观，
        /// 读档后统一走一次，后续参与者就不必每个人再判空。
        /// </summary>
        public void Normalize()
        {
            if (materials == null)
            {
                materials = new List<MaterialSave>();
            }

            if (buildings == null)
            {
                buildings = new List<BuildingSave>();
            }

            if (mining == null)
            {
                mining = new MiningSave();
            }

            if (mining.tiers == null)
            {
                mining.tiers = new List<MiningTierSave>();
            }

            for (int i = 0; i < mining.tiers.Count; i++)
            {
                MiningTierSave tier = mining.tiers[i];

                if (tier != null && tier.entryIds == null)
                {
                    tier.entryIds = new List<string>();
                }
            }

            if (fragments == null)
            {
                fragments = new FragmentSave();
            }

            if (fragments.unlockedIndices == null)
            {
                fragments.unlockedIndices = new List<int>();
            }

            if (blueprintRefund == null)
            {
                blueprintRefund = new List<MaterialSave>();
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingSave building = buildings[i];

                if (building != null && building.layerColors == null)
                {
                    building.layerColors = new List<int>();
                }
            }

        }
    }

    /// <summary>一格材料库存。materialId 由 SaveCatalog 解析回 MaterialData。</summary>
    [Serializable]
    public class MaterialSave
    {
        public string materialId;
        public int color;
        public int count;
    }

    /// <summary>一座已放置建筑。格子坐标与 BuildingGrid 的约定一致：x → 世界 X，y → 世界 Z。</summary>
    [Serializable]
    public class BuildingSave
    {
        public string buildingId;
        public int x;
        public int y;
        public int width;
        public int height;

        /// <summary>自下而上的楼层颜色（MaterialColor 的整数值）。</summary>
        public List<int> layerColors = new List<int>();

        /// <summary>
        /// 建筑等级（策划案 3.4）。旧存档没有该字段时为 0，读档按 1 级处理。
        /// </summary>
        public int level;
    }

    /// <summary>矿石生成进度：当前档位 + 每个档位已经抽过的条目。</summary>
    [Serializable]
    public class MiningSave
    {
        public int tier;
        public List<MiningTierSave> tiers = new List<MiningTierSave>();

        /// <summary>
        /// 照明工具等级（D17=A）：0 未拥有、1 火把、2 手电。
        /// 旧存档没有该字段时保持 0，等价于未购买。
        /// </summary>
        public int lightToolLevel;
    }

    /// <summary>单个矿石档位的抽取记录。entryIds 对应 OreEntry 的稳定 ID。</summary>
    [Serializable]
    public class MiningTierSave
    {
        public int tier;
        public List<string> entryIds = new List<string>();
    }

    /// <summary>文化碎片进度。unlockedIndices 是碎片表下标，totalCollected 是累计获得次数。</summary>
    [Serializable]
    public class FragmentSave
    {
        public int totalCollected;
        public List<int> unlockedIndices = new List<int>();
    }
}
