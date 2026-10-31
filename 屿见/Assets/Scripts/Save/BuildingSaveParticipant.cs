using System.Collections.Generic;
using UnityEngine;
using Yujian.Building;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.Save
{
    /// <summary>
    /// 已放置建筑的存档适配器。
    ///
    /// 每座建筑写：稳定 buildingId、占地格子矩形（x/y/width/height）、自下而上的楼层颜色。
    /// 读档时先清空 BuildingManager 记录的现存建筑，再按存档逐座重建；
    /// 重建使用新的 InstanceID 重新登记占地，最后统一重算玩家等级。
    ///
    /// 建筑实例上的建筑数据与颜色来自 <see cref="PlacedBuildingInfo"/>，
    /// 由 BuildingManager 在建造 / 恢复时写入。
    /// </summary>
    public class BuildingSaveParticipant : SaveParticipant
    {
        [Tooltip("建筑管理器。留空会明确报错，建筑不会写入存档")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("存档目录：buildingId → BuildingData 的解析表。留空会明确报错")]
        [SerializeField] private SaveCatalog catalog;

        [Tooltip("玩家等级。建筑恢复后调用 Recalculate()。留空会警告，等级不会立即刷新")]
        [SerializeField] private PlayerLevel playerLevel;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.buildings.Clear();

            if (buildingManager == null)
            {
                Debug.LogError("[BuildingSaveParticipant] BuildingManager 未配置，建筑不会写入存档。", this);
                return;
            }

            if (catalog == null)
            {
                Debug.LogError("[BuildingSaveParticipant] SaveCatalog 未配置，建筑无法转换成稳定 ID，不会写入存档。", this);
                return;
            }

            IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject instance = placed[i];

                if (instance == null)
                {
                    Debug.LogWarning($"[BuildingSaveParticipant] 已放置建筑列表第 {i} 项是空引用，已跳过。", this);
                    continue;
                }

                PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

                if (info == null || info.Data == null)
                {
                    Debug.LogError($"[BuildingSaveParticipant] 建筑「{instance.name}」上没有 " +
                                   "PlacedBuildingInfo 或建筑数据为空，已跳过。\n" +
                                   "这通常意味着它是由旧代码建造的，请重新建造一次。", instance);
                    continue;
                }

                string buildingId = catalog.GetBuildingId(info.Data);

                if (string.IsNullOrEmpty(buildingId))
                {
                    // GetBuildingId 已经打印了具体原因
                    continue;
                }

                RectInt rect = info.FootprintRect;

                if (rect.width <= 0 || rect.height <= 0)
                {
                    Debug.LogError($"[BuildingSaveParticipant] 建筑「{instance.name}」的占地矩形 {rect} 非法，" +
                                   "已跳过。请重新建造一次。", instance);
                    continue;
                }

                BuildingSave save = new BuildingSave
                {
                    buildingId = buildingId,
                    x = rect.x,
                    y = rect.y,
                    width = rect.width,
                    height = rect.height,
                    level = info.Level,
                };

                IReadOnlyList<MaterialColor> colors = info.LayerColors;

                for (int j = 0; j < colors.Count; j++)
                {
                    save.layerColors.Add((int)colors[j]);
                }

                data.buildings.Add(save);
            }
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (buildingManager == null)
            {
                Debug.LogError("[BuildingSaveParticipant] BuildingManager 未配置，建筑无法恢复。", this);
                return;
            }

            if (catalog == null)
            {
                Debug.LogError("[BuildingSaveParticipant] SaveCatalog 未配置，建筑无法从 ID 解析回资产。", this);
                return;
            }

            // 用户已拍板：读档时清空 BuildingManager 记录的建筑，再按存档重建
            int cleared = buildingManager.ClearPlacedBuildings();

            if (cleared > 0)
            {
                Debug.Log($"[BuildingSaveParticipant] 读档前移除了 {cleared} 座已有建筑。", this);
            }

            int restored = 0;
            int failed = 0;

            for (int i = 0; i < data.buildings.Count; i++)
            {
                BuildingSave entry = data.buildings[i];

                if (entry == null)
                {
                    continue;
                }

                if (!catalog.TryGetBuilding(entry.buildingId, out BuildingData building))
                {
                    Debug.LogError($"[BuildingSaveParticipant] 存档里的建筑 ID「{entry.buildingId}」" +
                                   "在 SaveCatalog 里找不到，已跳过这一座。" +
                                   "请确认该建筑资产仍在工程里，且目录已包含它。", this);
                    failed++;
                    continue;
                }

                RectInt rect = new RectInt(entry.x, entry.y, entry.width, entry.height);
                List<MaterialColor> colors = new List<MaterialColor>(entry.layerColors.Count);

                for (int j = 0; j < entry.layerColors.Count; j++)
                {
                    colors.Add((MaterialColor)entry.layerColors[j]);
                }

                // 旧存档没有 level 字段时为 0，按 1 级恢复
                int level = entry.level > 0 ? entry.level : 1;

                if (buildingManager.RestoreBuilding(building, rect, colors, level, out string error))
                {
                    restored++;
                }
                else
                {
                    failed++;
                    Debug.LogError($"[BuildingSaveParticipant] 第 {i} 座建筑「{building.BuildingName}」" +
                                   $"恢复失败：{error}", this);
                }
            }

            if (failed > 0)
            {
                Debug.LogError($"[BuildingSaveParticipant] 建筑恢复完成：成功 {restored} 座，失败 {failed} 座。" +
                               "失败的建筑已跳过，其余状态仍会正常恢复。", this);
            }

            if (playerLevel != null)
            {
                playerLevel.Recalculate();
            }
            else
            {
                Debug.LogWarning("[BuildingSaveParticipant] PlayerLevel 未配置，建筑恢复后等级不会立即刷新。" +
                                 "请运行菜单「屿见/通用模块/配置存档系统」自动接线。", this);
            }
        }
    }
}
