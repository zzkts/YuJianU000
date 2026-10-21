using System.Collections.Generic;
using UnityEngine;
using Yujian.Shop;

namespace Yujian.Save
{
    /// <summary>
    /// 存档目录：把“存档里的稳定 ID”解析回工程里的 ScriptableObject 资产。
    ///
    /// JsonUtility 不能序列化资产引用，所以存档只写字符串 ID；
    /// 读档时必须有这样一张表才能还原成 MaterialData。目录由菜单「屿见/配置存档系统」自动填充。
    ///
    /// 目前只登记材料。第 2、3 步会加入 BuildingData / OreGroup / OreEntry。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/存档/存档目录", fileName = "SaveCatalog")]
    public class SaveCatalog : ScriptableObject
    {
        [Tooltip("所有可能需要写进存档的原料。由菜单「屿见/配置存档系统」自动补齐")]
        [SerializeField] private List<MaterialData> materials = new List<MaterialData>();

        private Dictionary<string, MaterialData> materialLookup;

        /// <summary>目录里登记的全部材料，只读。</summary>
        public IReadOnlyList<MaterialData> Materials => materials;

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
                               "请运行菜单「屿见/配置存档系统」。", material);
                return null;
            }

            EnsureLookup();

            if (!materialLookup.TryGetValue(material.SaveId, out MaterialData found) || found != material)
            {
                Debug.LogError($"[SaveCatalog] 原料「{material.name}」不在存档目录「{name}」里，" +
                               "或者它的 Save Id 与目录中的记录冲突。请运行菜单「屿见/配置存档系统」补齐目录。", this);
                return null;
            }

            return material.SaveId;
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
        }
    }
}
