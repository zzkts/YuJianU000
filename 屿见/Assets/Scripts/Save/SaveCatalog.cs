using System.Collections.Generic;
using UnityEngine;
using Yujian.Building;
using Yujian.Shop;

namespace Yujian.Save
{
    /// <summary>
    /// 存档目录：把“存档里的稳定 ID”解析回工程里的 ScriptableObject 资产。
    ///
    /// JsonUtility 不能序列化资产引用，所以存档只写字符串 ID；
    /// 读档时必须有这样一张表才能还原成 MaterialData。目录由菜单「屿见/通用模块/配置存档系统」自动填充。
    ///
    /// 目前登记材料与建筑。第 3 步会加入 OreGroup / OreEntry。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/存档/存档目录", fileName = "SaveCatalog")]
    public class SaveCatalog : ScriptableObject
    {
        [Tooltip("所有可能需要写进存档的原料。由菜单「屿见/通用模块/配置存档系统」自动补齐")]
        [SerializeField] private List<MaterialData> materials = new List<MaterialData>();

        [Tooltip("所有可能需要写进存档的建筑。由菜单「屿见/通用模块/配置存档系统」自动补齐")]
        [SerializeField] private List<BuildingData> buildings = new List<BuildingData>();

        private Dictionary<string, MaterialData> materialLookup;
        private Dictionary<string, BuildingData> buildingLookup;

        /// <summary>目录里登记的全部材料，只读。</summary>
        public IReadOnlyList<MaterialData> Materials => materials;

        /// <summary>目录里登记的全部建筑，只读。</summary>
        public IReadOnlyList<BuildingData> Buildings => buildings;

        /// <summary>按稳定 ID 查找材料。找不到返回 false，不抛异常。</summary>
        public bool TryGetMaterial(string saveId, out MaterialData material)
        {
            material = null;

            if (string.IsNullOrEmpty(saveId))
            {
                return false;
            }

            EnsureLookup();
            return materialLookup.TryGetValue(saveId, out material);
        }

        /// <summary>
        /// 取材料的稳定 ID。素材不在目录里或 ID 为空时明确报错并返回 null，
        /// 调用方会跳过这一格，不会写出一个读档时解析不了的 ID。
        /// </summary>
        public string GetMaterialId(MaterialData material)
        {
            if (material == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(material.SaveId))
            {
                Debug.LogError($"[SaveCatalog] 原料资产「{material.name}」没有 Save Id，无法写入存档。" +
                               "请运行菜单「屿见/通用模块/配置存档系统」。", material);
                return null;
            }

            EnsureLookup();

            if (!materialLookup.TryGetValue(material.SaveId, out MaterialData found) || found != material)
            {
                Debug.LogError($"[SaveCatalog] 原料「{material.name}」不在存档目录「{name}」里，" +
                               "或者它的 Save Id 与目录中的记录冲突。请运行菜单「屿见/通用模块/配置存档系统」补齐目录。", this);
                return null;
            }

            return material.SaveId;
        }

        /// <summary>按稳定 ID 查找建筑。找不到返回 false，不抛异常。</summary>
        public bool TryGetBuilding(string saveId, out BuildingData building)
        {
            building = null;

            if (string.IsNullOrEmpty(saveId))
            {
                return false;
            }

            EnsureBuildingLookup();
            return buildingLookup.TryGetValue(saveId, out building);
        }

        /// <summary>
        /// 取建筑的稳定 ID。建筑不在目录里或 ID 为空时明确报错并返回 null，
        /// 调用方会跳过这一座，不会写出一个读档时解析不了的 ID。
        /// </summary>
        public string GetBuildingId(BuildingData building)
        {
            if (building == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(building.SaveId))
            {
                Debug.LogError($"[SaveCatalog] 建筑资产「{building.name}」没有 Save Id，无法写入存档。" +
                               "请运行菜单「屿见/通用模块/配置存档系统」。", building);
                return null;
            }

            EnsureBuildingLookup();

            if (!buildingLookup.TryGetValue(building.SaveId, out BuildingData found) || found != building)
            {
                Debug.LogError($"[SaveCatalog] 建筑「{building.name}」不在存档目录「{name}」里，" +
                               "或者它的 Save Id 与目录中的记录冲突。请运行菜单「屿见/通用模块/配置存档系统」补齐目录。", this);
                return null;
            }

            return building.SaveId;
        }

        private void EnsureBuildingLookup()
        {
            if (buildingLookup != null)
            {
                return;
            }

            buildingLookup = new Dictionary<string, BuildingData>();

            if (buildings == null)
            {
                return;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingData building = buildings[i];

                if (building == null)
                {
                    Debug.LogError($"[SaveCatalog] 目录「{name}」的建筑列表第 {i} 项是空引用。", this);
                    continue;
                }

                string saveId = building.SaveId;

                if (string.IsNullOrWhiteSpace(saveId))
                {
                    Debug.LogError($"[SaveCatalog] 目录里的建筑「{building.name}」没有 Save Id，读档时无法解析。", this);
                    continue;
                }

                if (buildingLookup.TryGetValue(saveId, out BuildingData existing))
                {
                    Debug.LogError($"[SaveCatalog] 建筑存档 ID 重复：「{building.name}」与「{existing.name}」" +
                                   $"都是 {saveId}。读档时只会解析到先登记的那一个。", this);
                    continue;
                }

                buildingLookup.Add(saveId, building);
            }
        }

        private void EnsureLookup()
        {
            if (materialLookup != null)
            {
                return;
            }

            materialLookup = new Dictionary<string, MaterialData>();

            if (materials == null)
            {
                return;
            }

            for (int i = 0; i < materials.Count; i++)
            {
                MaterialData material = materials[i];

                if (material == null)
                {
                    Debug.LogError($"[SaveCatalog] 目录「{name}」第 {i} 项是空引用。", this);
                    continue;
                }

                string saveId = material.SaveId;

                if (string.IsNullOrWhiteSpace(saveId))
                {
                    Debug.LogError($"[SaveCatalog] 目录里的原料「{material.name}」没有 Save Id，读档时无法解析。", this);
                    continue;
                }

                if (materialLookup.TryGetValue(saveId, out MaterialData existing))
                {
                    Debug.LogError($"[SaveCatalog] 存档 ID 重复：「{material.name}」与「{existing.name}」" +
                                   $"都是 {saveId}。读档时只会解析到先登记的那一个。", this);
                    continue;
                }

                materialLookup.Add(saveId, material);
            }
        }

        private void OnValidate()
        {
            // 目录在 Inspector 里被改动后，下一次查询重新建索引
            materialLookup = null;
            buildingLookup = null;
        }
    }
}
