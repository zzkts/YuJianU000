using System;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Building;

namespace Yujian.Shop
{
    /// <summary>
    /// 建筑金币价目表里的一条：哪座建筑、多少金币。
    ///
    /// 只描述「卖多少钱」，不持有玩家、不扣钱、不知道商店的存在。
    /// price = 0 表示免费（回收站仍是启动入口）。
    /// </summary>
    [Serializable]
    public struct BuildingShopEntry
    {
        [Tooltip("对应的建筑资产（Assets/Data/Buildings 下的 BuildingData）")]
        [SerializeField] private BuildingData building;

        [Tooltip("建造一次需要的金币；0 = 免费")]
        [SerializeField, Min(0)] private int price;

        public BuildingShopEntry(BuildingData building, int price)
        {
            this.building = building;
            this.price = Mathf.Max(0, price);
        }

        /// <summary>建筑资产，可能为空。</summary>
        public BuildingData Building => building;

        /// <summary>金币价，恒 >= 0。</summary>
        public int Price => Mathf.Max(0, price);

        /// <summary>这条价目是否可用（建筑非空、价格非负）。</summary>
        public bool IsValid => building != null && price >= 0;
    }

    /// <summary>
    /// 建筑金币价目表（ScriptableObject）。
    ///
    /// 隔离商店系统的数据层：建筑不再看 RequiredMaterials，而是查这张表得到一个金币价。
    /// 数值全部是占位值，后续直接改资产即可，不用改代码。
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingShopCatalog", menuName = "屿见/商店/建筑金币价目表", order = 20)]
    public class BuildingShopCatalog : ScriptableObject
    {
        [Tooltip("建筑与金币价的对应表。可以留空；留空的建筑建造时会提示「未配置金币价格」")]
        [SerializeField] private BuildingShopEntry[] entries = new BuildingShopEntry[0];

        /// <summary>全部价目（只读，可能为空数组）。</summary>
        public IReadOnlyList<BuildingShopEntry> Entries =>
            entries ?? Array.Empty<BuildingShopEntry>();

        /// <summary>在价目表里查建筑的金币价。找不到返回 false，price 为 0。</summary>
        public bool TryGetPrice(BuildingData building, out int price)
        {
            price = 0;

            if (building == null || entries == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Building == building)
                {
                    price = entries[i].Price;
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Building == null)
                {
                    Debug.LogWarning($"[BuildingShopCatalog]「{name}」第 {i} 条价目没有指定建筑，会被忽略。", this);
                }

                for (int j = i + 1; j < entries.Length; j++)
                {
                    if (entries[i].Building != null && entries[i].Building == entries[j].Building)
                    {
                        Debug.LogWarning($"[BuildingShopCatalog]「{name}」里「{entries[i].Building.BuildingName}」" +
                                         "出现了多条价目，实际只会使用第一条。", this);
                        break;
                    }
                }
            }
        }
    }
}
