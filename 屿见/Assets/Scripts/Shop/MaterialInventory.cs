using System;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Core.Events;

namespace Yujian.Shop
{
    /// <summary>
    /// 一种原料在库存里的数量。例如"小砖 ×2"。
    /// 字段私有 + 只读属性，外部只能通过 MaterialInventory 的接口增减数量。
    /// </summary>
    [Serializable]
    public struct MaterialStack
    {
        [SerializeField] private MaterialData material;
        [SerializeField] private int count;

        public MaterialStack(MaterialData material, int count)
        {
            this.material = material;
            this.count = count;
        }

        /// <summary>原料种类。</summary>
        public MaterialData Material => material;

        /// <summary>数量。</summary>
        public int Count => count;

        /// <summary>是否就是指定的原料。</summary>
        public bool Matches(MaterialData otherMaterial)
        {
            return material == otherMaterial;
        }

        /// <summary>仅供 MaterialInventory 内部改写数量。</summary>
        internal void SetCount(int value)
        {
            count = value;
        }
    }

    /// <summary>
    /// 玩家持有的原料库存，是"玩家拥有多少原料"的唯一真相源。
    /// 职责：按原料种类分格存取原料，并在数量变化时广播事件。
    /// 不买、不卖、不算钱——购买由 Shop 负责，本类只管账。
    ///
    /// 颜色变体已移除：同一种原料只有一种外观，库存也不再按颜色分格。
    /// 旧场景 / 旧存档里同原料的多条记录会在 Awake / RestoreStacks 时合并求和。
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

        private void Awake()
        {
            // 旧场景里的库存按 (原料, 颜色) 分了多条，读进来后把同原料的合并成一条
            MergeDuplicateStacks();
        }

        /// <summary>
        /// 增加原料。
        /// </summary>
        /// <param name="material">原料种类，不可为 null。</param>
        /// <param name="amount">增加数量，必须为正数。</param>
        public void AddMaterial(MaterialData material, int amount = 1)
        {
            if (!ValidateArguments(material, amount, nameof(AddMaterial)))
            {
                return;
            }

            int index = IndexOf(material);

            if (index < 0)
            {
                stacks.Add(new MaterialStack(material, amount));
            }
            else
            {
                MaterialStack stack = stacks[index];
                stack.SetCount(stack.Count + amount);
                stacks[index] = stack;
            }

            if (logInventoryChanges)
            {
                Debug.Log($"[MaterialInventory] 增加 {material.MaterialName} ×{amount}，" +
                          $"现有 {GetMaterialCount(material)}");
            }

            OnInventoryChanged?.Invoke();
            EventBus.Publish(new InventoryChanged(material, amount));
        }

        /// <summary>
        /// 读档用：清空当前库存，按给定的 (原料, 数量) 快照整体重建，最后只广播一次变化。
        ///
        /// 旧存档可能对同一种原料存在多条颜色记录；这里按原料合并求和，不会丢数量。
        /// 快照里的空格位（原料为空 / 数量非正）会被跳过并明确报错，不静默吞掉。
        /// </summary>
        /// <param name="snapshot">库存快照，可为 null（等价于清空）。</param>
        public void RestoreStacks(IReadOnlyList<MaterialStack> snapshot)
        {
            stacks.Clear();

            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    MaterialStack incoming = snapshot[i];

                    if (incoming.Material == null)
                    {
                        Debug.LogError($"[MaterialInventory] 存档里的库存第 {i} 项原料为空，已跳过。", this);
                        continue;
                    }

                    if (incoming.Count <= 0)
                    {
                        Debug.LogWarning($"[MaterialInventory] 存档里的「{incoming.Material.MaterialName}」数量为 " +
                                         $"{incoming.Count}，已跳过。", this);
                        continue;
                    }

                    AddOrMerge(incoming.Material, incoming.Count);
                }
            }

            // 读档整体替换不发 EventBus：那是“刷新界面”，不是“玩家拿到了材料”，
            // 否则音频管理会为每一格库存响一次。
            OnInventoryChanged?.Invoke();

            if (logInventoryChanges)
            {
                Debug.Log($"[MaterialInventory] 已从存档恢复 {stacks.Count} 格、共 {GetTotalCount()} 个原料。", this);
            }
        }

        /// <summary>
        /// 扣除原料。数量不足时返回 false，且不扣除任何原料。
        /// 阶段 3 的建筑消耗走这里。
        /// </summary>
        /// <param name="material">原料种类，不可为 null。</param>
        /// <param name="amount">扣除数量，必须为正数。</param>
        /// <returns>是否扣除成功。</returns>
        public bool RemoveMaterial(MaterialData material, int amount = 1)
        {
            if (!ValidateArguments(material, amount, nameof(RemoveMaterial)))
            {
                return false;
            }

            int index = IndexOf(material);
            int owned = index < 0 ? 0 : stacks[index].Count;

            if (owned < amount)
            {
                Debug.LogWarning($"[MaterialInventory] 扣除失败：{material.MaterialName} " +
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
                Debug.Log($"[MaterialInventory] 扣除 {material.MaterialName} ×{amount}，" +
                          $"剩余 {GetMaterialCount(material)}");
            }

            OnInventoryChanged?.Invoke();
            EventBus.Publish(new InventoryChanged(material, -amount));
            return true;
        }

        /// <summary>查询指定原料的数量，没有则返回 0。</summary>
        public int GetMaterialCount(MaterialData material)
        {
            if (material == null)
            {
                return 0;
            }

            int index = IndexOf(material);
            return index < 0 ? 0 : stacks[index].Count;
        }

        /// <summary>是否持有至少 amount 个指定原料。</summary>
        public bool HasMaterial(MaterialData material, int amount = 1)
        {
            if (amount <= 0)
            {
                return true;
            }

            return GetMaterialCount(material) >= amount;
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
        private int IndexOf(MaterialData material)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Matches(material))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>同原料已存在就累加，否则新增一格。读档与旧场景合并共用。</summary>
        private void AddOrMerge(MaterialData material, int amount)
        {
            int index = IndexOf(material);

            if (index < 0)
            {
                stacks.Add(new MaterialStack(material, amount));
            }
            else
            {
                MaterialStack existing = stacks[index];
                existing.SetCount(existing.Count + amount);
                stacks[index] = existing;
            }
        }

        /// <summary>把旧场景里按颜色拆开的同原料条目合并，并丢掉空 / 非法条目。</summary>
        private void MergeDuplicateStacks()
        {
            if (stacks == null)
            {
                stacks = new List<MaterialStack>();
                return;
            }

            List<MaterialStack> merged = new List<MaterialStack>(stacks.Count);

            for (int i = 0; i < stacks.Count; i++)
            {
                MaterialStack stack = stacks[i];

                if (stack.Material == null || stack.Count <= 0)
                {
                    continue;
                }

                int index = -1;

                for (int j = 0; j < merged.Count; j++)
                {
                    if (merged[j].Matches(stack.Material))
                    {
                        index = j;
                        break;
                    }
                }

                if (index < 0)
                {
                    merged.Add(new MaterialStack(stack.Material, stack.Count));
                }
                else
                {
                    MaterialStack existing = merged[index];
                    existing.SetCount(existing.Count + stack.Count);
                    merged[index] = existing;
                }
            }

            stacks = merged;
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
