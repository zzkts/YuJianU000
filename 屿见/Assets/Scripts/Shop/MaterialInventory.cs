using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Shop
{
    /// <summary>
    /// 一种 (原料, 颜色) 组合在库存里的数量。例如"红色小砖 ×2"。
    /// 字段私有 + 只读属性，外部只能通过 MaterialInventory 的接口增减数量。
    /// </summary>
    [Serializable]
    public struct MaterialStack
    {
        [SerializeField] private MaterialData material;
        [SerializeField] private MaterialColor color;
        [SerializeField] private int count;

        public MaterialStack(MaterialData material, MaterialColor color, int count)
        {
            this.material = material;
            this.color = color;
            this.count = count;
        }

        /// <summary>原料种类。</summary>
        public MaterialData Material => material;

        /// <summary>该原料的颜色。</summary>
        public MaterialColor Color => color;

        /// <summary>数量。</summary>
        public int Count => count;

        /// <summary>是否就是指定的 (原料, 颜色) 组合。</summary>
        public bool Matches(MaterialData otherMaterial, MaterialColor otherColor)
        {
            return material == otherMaterial && color == otherColor;
        }

        /// <summary>仅供 MaterialInventory 内部改写数量。</summary>
        internal void SetCount(int value)
        {
            count = value;
        }
    }

    /// <summary>
    /// 玩家持有的原料库存，是"玩家拥有多少原料"的唯一真相源。
    /// 职责：按 (原料, 颜色) 分格存取原料，并在数量变化时广播事件。
    /// 不买、不卖、不算钱——购买由 Shop 负责，本类只管账。
    /// </summary>
    public class MaterialInventory : MonoBehaviour
    {
        [Header("库存内容（运行时自动增删，此处仅供查看）")]
        [SerializeField] private List<MaterialStack> stacks = new List<MaterialStack>();

        [Header("调试")]
        [Tooltip("是否在 Console 打印库存变化日志")]
        [SerializeField] private bool logInventoryChanges = true;

        /// <summary>库存内容变化时触发。参数不携带细节，订阅方自行重新读取。</summary>
        public event Action OnInventoryChanged;

        /// <summary>当前库存内容，只读。空格不会被保留，所以这里不会出现数量为 0 的条目。</summary>
        public IReadOnlyList<MaterialStack> Stacks => stacks;

        /// <summary>
        /// 增加原料。
        /// </summary>
        /// <param name="material">原料种类，不可为 null。</param>
        /// <param name="color">颜色，与 material 一起构成库存的格位。</param>
        /// <param name="amount">增加数量，必须为正数。</param>
        public void AddMaterial(MaterialData material, MaterialColor color, int amount = 1)
        {
            if (!ValidateArguments(material, amount, nameof(AddMaterial)))
            {
                return;
            }

            int index = IndexOf(material, color);

            if (index < 0)
            {
                stacks.Add(new MaterialStack(material, color, amount));
            }
            else
            {
                MaterialStack stack = stacks[index];
                stack.SetCount(stack.Count + amount);
                stacks[index] = stack;
            }

            if (logInventoryChanges)
            {
                Debug.Log($"[MaterialInventory] 增加 {color.ToChineseName()}{material.MaterialName} ×{amount}，" +
                          $"现有 {GetMaterialCount(material, color)}");
            }

            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// 扣除原料。数量不足时返回 false，且不扣除任何原料。
        /// 阶段 3 的建筑消耗走这里。
        /// </summary>
        /// <param name="material">原料种类，不可为 null。</param>
        /// <param name="color">颜色。</param>
        /// <param name="amount">扣除数量，必须为正数。</param>
        /// <returns>是否扣除成功。</returns>
        public bool RemoveMaterial(MaterialData material, MaterialColor color, int amount = 1)
        {
            if (!ValidateArguments(material, amount, nameof(RemoveMaterial)))
            {
                return false;
            }

            int index = IndexOf(material, color);
            int owned = index < 0 ? 0 : stacks[index].Count;

            if (owned < amount)
            {
                Debug.LogWarning($"[MaterialInventory] 扣除失败：{color.ToChineseName()}{material.MaterialName} " +
                                 $"只有 {owned} 个，请求扣除 {amount} 个。未扣除任何原料。", this);
                return false;
            }

            if (owned == amount)
            {
                // 数量归零则整格移除，避免留下 0 条目的空壳
                stacks.RemoveAt(index);
            }
            else
            {
                MaterialStack stack = stacks[index];
                stack.SetCount(stack.Count - amount);
                stacks[index] = stack;
            }

            if (logInventoryChanges)
            {
                Debug.Log($"[MaterialInventory] 扣除 {color.ToChineseName()}{material.MaterialName} ×{amount}，" +
                          $"剩余 {GetMaterialCount(material, color)}");
            }

            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>查询指定 (原料, 颜色) 的数量，没有则返回 0。</summary>
        public int GetMaterialCount(MaterialData material, MaterialColor color)
        {
            if (material == null)
            {
                return 0;
            }

            int index = IndexOf(material, color);
            return index < 0 ? 0 : stacks[index].Count;
        }

        /// <summary>是否持有至少 amount 个指定 (原料, 颜色)。</summary>
        public bool HasMaterial(MaterialData material, MaterialColor color, int amount = 1)
        {
            if (amount <= 0)
            {
                return true;
            }

            return GetMaterialCount(material, color) >= amount;
        }

        /// <summary>某种原料所有颜色的合计数量，没有则返回 0。</summary>
        public int GetTotalCount(MaterialData material)
        {
            if (material == null)
            {
                return 0;
            }

            int total = 0;

            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Material == material)
                {
                    total += stacks[i].Count;
                }
            }

            return total;
        }

        /// <summary>全部原料的合计数量。</summary>
        public int GetTotalCount()
        {
            int total = 0;

            for (int i = 0; i < stacks.Count; i++)
            {
                total += stacks[i].Count;
            }

            return total;
        }

        /// <summary>线性查找格位下标，找不到返回 -1。原料格数极少，不值得上字典。</summary>
        private int IndexOf(MaterialData material, MaterialColor color)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Matches(material, color))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>参数校验。不合法时打印明确原因，避免静默失败。</summary>
        private bool ValidateArguments(MaterialData material, int amount, string caller)
        {
            if (material == null)
            {
                Debug.LogError($"[MaterialInventory] {caller} 收到空的 MaterialData。", this);
                return false;
            }

            if (amount <= 0)
            {
                Debug.LogWarning($"[MaterialInventory] {caller} 收到非正数({amount})，已忽略。", this);
                return false;
            }

            return true;
        }

        private void OnValidate()
        {
            if (stacks == null)
            {
                stacks = new List<MaterialStack>();
                return;
            }

            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Material == null)
                {
                    Debug.LogWarning($"[MaterialInventory] 库存第 {i} 项的原料引用为空，运行时会被当作无效格位。", this);
                }
                else if (stacks[i].Count <= 0)
                {
                    Debug.LogWarning($"[MaterialInventory] 库存第 {i} 项（{stacks[i].Material.MaterialName}）" +
                                     $"数量为 {stacks[i].Count}，运行时会被当作空格位。", this);
                }
            }
        }
    }
}
