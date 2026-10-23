using System.Collections.Generic;
using UnityEngine;
using Yujian.Building;
using Yujian.Shop;

namespace Yujian.Save
{
    /// <summary>
    /// 蓝图内材料的存档适配器。
    ///
    /// 材料放进蓝图时已经从库存扣掉了；若此刻退出 / 自动保存，这些材料不在库存快照里。
    /// 本类把它们写进 SaveData.blueprintRefund，读档时按「原库存 + 退款」退回库存。
    ///
    /// 注意：读档**不会恢复蓝图本身**，只把材料还给玩家，玩家重新选择建筑即可。
    /// 因此本参与者必须排在库存参与者之后，避免退款被库存恢复覆盖。
    /// </summary>
    public class BlueprintSaveParticipant : SaveParticipant
    {
        [Tooltip("建造结算，用来读取蓝图里已放入的材料历史。留空会明确报错")]
        [SerializeField] private BuildingConstruction construction;

        [Tooltip("玩家库存，读档时把蓝图材料退回这里。留空会明确报错")]
        [SerializeField] private MaterialInventory inventory;

        [Tooltip("存档目录：materialId → MaterialData 的解析表。留空会明确报错")]
        [SerializeField] private SaveCatalog catalog;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.blueprintRefund.Clear();

            if (construction == null)
            {
                Debug.LogError("[BlueprintSaveParticipant] BuildingConstruction 未配置，" +
                               "蓝图里的材料不会写入存档。", this);
                return;
            }

            if (catalog == null)
            {
                Debug.LogError("[BlueprintSaveParticipant] SaveCatalog 未配置，" +
                               "蓝图材料无法转换成稳定 ID，不会写入存档。", this);
                return;
            }

            IReadOnlyList<BuildingConstruction.PlacedMaterial> history = construction.PlacementHistory;

            for (int i = 0; i < history.Count; i++)
            {
                BuildingConstruction.PlacedMaterial placed = history[i];

                if (!placed.IsValid)
                {
                    Debug.LogWarning($"[BlueprintSaveParticipant] 蓝图历史第 {i} 项原料为空，已跳过。", this);
                    continue;
                }

                string materialId = catalog.GetMaterialId(placed.Material);

                if (string.IsNullOrEmpty(materialId))
                {
                    // GetMaterialId 已经打印了具体原因
                    continue;
                }

                data.blueprintRefund.Add(new MaterialSave
                {
                    materialId = materialId,
                    color = (int)placed.Color,
                    count = 1,
                });
            }
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            // 先把当前场景里正在填料的蓝图丢弃（不退款），
            // 再退存档里记录的蓝图材料，避免两份材料叠加。
            if (construction != null)
            {
                construction.DiscardCurrentBlueprintWithoutRefund();
            }
            else
            {
                Debug.LogError("[BlueprintSaveParticipant] BuildingConstruction 未配置，" +
                               "当前蓝图无法被清理，读档后可能残留。", this);
            }

            if (data.blueprintRefund == null || data.blueprintRefund.Count == 0)
            {
                return;
            }

            if (inventory == null)
            {
                Debug.LogError("[BlueprintSaveParticipant] MaterialInventory 未配置，" +
                               "蓝图里的材料无法退回库存。", this);
                return;
            }

            if (catalog == null)
            {
                Debug.LogError("[BlueprintSaveParticipant] SaveCatalog 未配置，" +
                               "蓝图材料无法从 ID 解析回原料。", this);
                return;
            }

            int refunded = 0;

            for (int i = 0; i < data.blueprintRefund.Count; i++)
            {
                MaterialSave entry = data.blueprintRefund[i];

                if (entry == null || entry.count <= 0)
                {
                    continue;
                }

                if (!catalog.TryGetMaterial(entry.materialId, out MaterialData material))
                {
                    Debug.LogError($"[BlueprintSaveParticipant] 存档里的原料 ID「{entry.materialId}」" +
                                   "在 SaveCatalog 里找不到，已跳过这一块。", this);
                    continue;
                }

                if (!System.Enum.IsDefined(typeof(MaterialColor), entry.color))
                {
                    Debug.LogError($"[BlueprintSaveParticipant] 存档里的颜色值 {entry.color} " +
                                   "不是合法的 MaterialColor，已跳过这一块。", this);
                    continue;
                }

                inventory.AddMaterial(material, (MaterialColor)entry.color, entry.count);
                refunded += entry.count;
            }

            Debug.Log($"[BlueprintSaveParticipant] 已把蓝图里的 {refunded} 块材料退回库存。", this);
        }
    }
}
