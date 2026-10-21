using System.Collections.Generic;
using UnityEngine;
using Yujian.Shop;

namespace Yujian.Save
{
    /// <summary>
    /// 材料库存的存档适配器。
    /// 存档里只写 MaterialData 的稳定 SaveId，不写资产引用；读档时通过 SaveCatalog 解析回资产。
    /// </summary>
    public class InventorySaveParticipant : SaveParticipant
    {
        [Tooltip("玩家库存。留空会明确报错")]
        [SerializeField] private MaterialInventory inventory;

        [Tooltip("存档目录：SaveId → MaterialData 的解析表。留空会明确报错")]
        [SerializeField] private SaveCatalog catalog;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.materials.Clear();

            if (inventory == null)
            {
                Debug.LogError("[InventorySaveParticipant] MaterialInventory 未配置，库存不会写入存档。", this);
                return;
            }

            if (catalog == null)
            {
                Debug.LogError("[InventorySaveParticipant] SaveCatalog 未配置，库存无法转换成稳定 ID，不会写入存档。", this);
                return;
            }

            IReadOnlyList<MaterialStack> stacks = inventory.Stacks;

            for (int i = 0; i < stacks.Count; i++)
            {
                MaterialStack stack = stacks[i];
                string materialId = catalog.GetMaterialId(stack.Material);

                if (string.IsNullOrEmpty(materialId))
                {
                    // GetMaterialId 已经打印了具体原因，这里不重复刷屏
                    continue;
                }

                data.materials.Add(new MaterialSave
                {
                    materialId = materialId,
                    color = (int)stack.Color,
                    count = stack.Count,
                });
            }
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (inventory == null)
            {
                Debug.LogError("[InventorySaveParticipant] MaterialInventory 未配置，库存无法恢复。", this);
                return;
            }

            if (catalog == null)
            {
                Debug.LogError("[InventorySaveParticipant] SaveCatalog 未配置，库存无法从 ID 解析回原料。", this);
                return;
            }

            List<MaterialStack> restored = new List<MaterialStack>(data.materials.Count);

            for (int i = 0; i < data.materials.Count; i++)
            {
                MaterialSave entry = data.materials[i];

                if (entry == null)
                {
                    continue;
                }

                if (entry.count <= 0)
                {
                    Debug.LogWarning($"[InventorySaveParticipant] 第 {i} 条库存数量为 {entry.count}，已跳过。", this);
                    continue;
                }

                if (!catalog.TryGetMaterial(entry.materialId, out MaterialData material))
                {
                    Debug.LogError($"[InventorySaveParticipant] 存档里的原料 ID「{entry.materialId}」" +
                                   "在 SaveCatalog 里找不到，已跳过这一格。" +
                                   "请确认该原料资产仍在工程里，且目录已包含它。", this);
                    continue;
                }

                if (!System.Enum.IsDefined(typeof(MaterialColor), entry.color))
                {
                    Debug.LogError($"[InventorySaveParticipant] 存档里的颜色值 {entry.color} 不是合法的 MaterialColor，" +
                                   "已跳过这一格。", this);
                    continue;
                }

                restored.Add(new MaterialStack(material, (MaterialColor)entry.color, entry.count));
            }

            inventory.RestoreStacks(restored);
        }
    }
}
