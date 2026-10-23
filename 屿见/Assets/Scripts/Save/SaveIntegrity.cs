using System;
using System.Text;
using Yujian.Shop;

namespace Yujian.Save
{
    /// <summary>
    /// 存档完整性工具：校验和 + 结构校验。
    ///
    /// 为什么需要：
    ///   JsonUtility 对缺失字段不报错，`{}` 会被解析成「金币 0、空库存」的合法对象；
    ///   截断成恰好是合法 JSON 的文件也一样。没有这一层，读档会静默地把玩家进度清空，
    ///   下一次自动保存再把空状态写回磁盘。
    ///
    /// 校验和算法用 FNV-1a 64 位：无依赖、够快、对截断/改字节敏感。
    /// 它不是加密，只是完整性检查，防不了有意篡改（单机项目暂不需要）。
    /// </summary>
    public static class SaveIntegrity
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>对一段文本计算 FNV-1a 64 位校验和，返回 16 位小写十六进制。</summary>
        public static string Compute(string text)
        {
            ulong hash = FnvOffsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);

            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
            }

            return hash.ToString("x16");
        }

        /// <summary>
        /// 校验存档的结构与取值。只检查「不可能由正常游玩产生」的状态，
        /// 不检查资产是否还存在——那由各参与者解析失败时处理。
        /// </summary>
        /// <returns>通过返回 true；否则 error 说明第一处问题。</returns>
        public static bool Validate(SaveData data, out string error)
        {
            error = null;

            if (data == null)
            {
                error = "存档对象为空。";
                return false;
            }

            if (data.version <= 0)
            {
                error = "缺少 version 字段（可能是空文件、截断文件或非存档 JSON）。";
                return false;
            }

            if (data.version > SaveData.CurrentVersion)
            {
                error = $"存档版本 {data.version} 高于当前支持的 {SaveData.CurrentVersion}。";
                return false;
            }

            if (data.version >= 2 && data.savedAtUtc <= 0)
            {
                error = "缺少有效的保存时间。";
                return false;
            }

            if (data.currency < 0)
            {
                error = $"金币为负数（{data.currency}）。";
                return false;
            }

            if (!ValidateMaterials(data.materials, "库存", out error))
            {
                return false;
            }

            if (!ValidateMaterials(data.blueprintRefund, "蓝图内材料", out error))
            {
                return false;
            }

            if (data.buildings == null)
            {
                error = "建筑列表为空引用（Normalize 未生效）。";
                return false;
            }

            for (int i = 0; i < data.buildings.Count; i++)
            {
                BuildingSave building = data.buildings[i];

                if (building == null)
                {
                    error = $"建筑列表第 {i} 项为空。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(building.buildingId))
                {
                    error = $"建筑列表第 {i} 项没有 buildingId。";
                    return false;
                }

                if (building.width <= 0 || building.height <= 0)
                {
                    error = $"建筑「{building.buildingId}」的占地 {building.width}×{building.height} 非法。";
                    return false;
                }

                if (building.layerColors == null)
                {
                    error = $"建筑「{building.buildingId}」的楼层颜色列表为空引用。";
                    return false;
                }

                for (int j = 0; j < building.layerColors.Count; j++)
                {
                    if (!Enum.IsDefined(typeof(MaterialColor), building.layerColors[j]))
                    {
                        error = $"建筑「{building.buildingId}」第 {j} 层的颜色值 " +
                                $"{building.layerColors[j]} 不是合法的 MaterialColor。";
                        return false;
                    }
                }
            }

            if (data.mining == null)
            {
                error = "矿石数据为空引用（Normalize 未生效）。";
                return false;
            }

            if (data.mining.tiers == null)
            {
                error = "矿石档位列表为空引用（Normalize 未生效）。";
                return false;
            }

            for (int i = 0; i < data.mining.tiers.Count; i++)
            {
                MiningTierSave tier = data.mining.tiers[i];

                if (tier == null || tier.entryIds == null)
                {
                    error = $"矿石档位列表第 {i} 项结构不完整。";
                    return false;
                }

                for (int j = 0; j < tier.entryIds.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(tier.entryIds[j]))
                    {
                        error = $"矿石档位 {tier.tier} 第 {j} 条记录 ID 为空。";
                        return false;
                    }
                }
            }

            if (data.fragments == null)
            {
                error = "文化碎片数据为空引用（Normalize 未生效）。";
                return false;
            }

            if (data.fragments.unlockedIndices == null)
            {
                error = "文化碎片下标列表为空引用（Normalize 未生效）。";
                return false;
            }

            for (int i = 0; i < data.fragments.unlockedIndices.Count; i++)
            {
                if (data.fragments.unlockedIndices[i] < 0)
                {
                    error = $"文化碎片下标为负数（{data.fragments.unlockedIndices[i]}）。";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateMaterials(
            System.Collections.Generic.List<MaterialSave> entries, string label, out string error)
        {
            error = null;

            if (entries == null)
            {
                error = $"{label}列表为空引用（Normalize 未生效）。";
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                MaterialSave entry = entries[i];

                if (entry == null)
                {
                    error = $"{label}列表第 {i} 项为空。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.materialId))
                {
                    error = $"{label}列表第 {i} 项没有 materialId。";
                    return false;
                }

                if (entry.count <= 0)
                {
                    error = $"{label}「{entry.materialId}」的数量为 {entry.count}。";
                    return false;
                }

                if (!Enum.IsDefined(typeof(MaterialColor), entry.color))
                {
                    error = $"{label}「{entry.materialId}」的颜色值 {entry.color} 不是合法的 MaterialColor。";
                    return false;
                }
            }

            return true;
        }
    }
}
