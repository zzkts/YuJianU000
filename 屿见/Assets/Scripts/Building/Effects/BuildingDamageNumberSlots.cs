using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Building.Effects
{
    /// <summary>
    /// 建筑伤害数字的「显示车道」注册表。
    ///
    /// 目的：来自不同建筑的伤害数字错开显示（字体 / 颜色保持一致），
    /// 多座建筑同时攻击同一只怪时不会叠成一团。
    ///
    /// 为什么按建筑实例分配固定槽位，而不是按建筑位置算方向：
    ///   · 建筑在岛屿上，关卡视角里看不见，方向没有视觉意义；
    ///   · 建筑都集中在岛的同一侧，按方向错开会让数字全挤到同一边。
    ///
    /// 槽位在建筑效果初始化时申请、建筑销毁时释放；槽位号只决定显示偏移，
    /// 不参与伤害结算。同类多座建筑按建造顺序依次拿到不同车道。
    /// </summary>
    public static class BuildingDamageNumberSlots
    {
        /// <summary>相邻两对车道的水平间距基准（世界单位）。</summary>
        public const float DefaultSpacing = 0.9f;

        /// <summary>默认车道总数：1 条中 + 左右各 4 条，共 9 条。</summary>
        public const int DefaultLaneCount = 9;

        /// <summary>车道编号 → 建筑实例 ID。一个建筑（含其全部效果）只占一个槽位。</summary>
        private static readonly Dictionary<int, int> OwnerSlots = new Dictionary<int, int>();

        /// <summary>
        /// 为建筑实例申请一个槽位。同一 ID 重复申请返回同一个槽位，
        /// 所以同一座建筑上的多个效果会共用一条车道。
        /// </summary>
        /// <param name="ownerId">建筑实例 ID，通常取 PlacedBuildingInfo.GetInstanceID()。</param>
        /// <returns>从 0 开始的槽位号。没有空闲槽位时继续递增（显示时按车道数取模复用）。</returns>
        public static int Acquire(int ownerId)
        {
            if (OwnerSlots.TryGetValue(ownerId, out int existing))
            {
                return existing;
            }

            int slot = 0;

            while (ContainsSlot(slot))
            {
                slot++;
            }

            OwnerSlots.Add(ownerId, slot);
            return slot;
        }

        /// <summary>释放建筑实例占用的槽位。建筑销毁时调用；没申请过时无副作用。</summary>
        public static void Release(int ownerId)
        {
            OwnerSlots.Remove(ownerId);
        }

        /// <summary>查一个建筑当前占用的槽位。没申请过返回 false。</summary>
        public static bool TryGetSlot(int ownerId, out int slot)
        {
            return OwnerSlots.TryGetValue(ownerId, out slot);
        }

        /// <summary>清空全部槽位。测试用；进入播放模式时也会自动清一次。</summary>
        public static void Clear()
        {
            OwnerSlots.Clear();
        }

        /// <summary>
        /// 槽位号 → 相对矿石默认位置的显示偏移。
        ///
        /// 排布：槽 0 居中（第一座建筑保持原来的显示位置），之后左、右成对向外扩展，
        /// 每对外移一点、同时抬高一点。超过车道数后取模复用。
        /// </summary>
        /// <param name="slot">槽位号，负数按 0 处理。</param>
        /// <param name="laneCount">车道总数（含中间车道）。小于 1 时按 1 处理。</param>
        /// <param name="spacing">水平间距；小于等于 0 时返回零偏移（等于关闭错开）。</param>
        public static Vector3 GetOffset(int slot, int laneCount, float spacing)
        {
            if (spacing <= 0f)
            {
                return Vector3.zero;
            }

            int lanes = Mathf.Max(1, laneCount);
            int lane = Mathf.Max(0, slot) % lanes;

            // 车道 0 = 中间，保持原位置
            if (lane == 0)
            {
                return Vector3.zero;
            }

            int sideIndex = lane - 1;
            int pair = sideIndex / 2;
            float side = (sideIndex % 2) == 0 ? -1f : 1f;

            // 每一对外移 60% 的基准间距，同时抬高 35%——数字向上飘，抬高能进一步拉开视觉距离
            float distance = spacing * (1f + pair * 0.6f);
            float height = spacing * pair * 0.35f;

            return new Vector3(side * distance, height, 0f);
        }

        private static bool ContainsSlot(int slot)
        {
            foreach (int used in OwnerSlots.Values)
            {
                if (used == slot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 关闭「Enter Play Mode Options → 不重载域」时静态字典会跨播放保留，
        /// 这里在每次进入播放前清一次，避免拿到上一局的槽位。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            Clear();
        }
    }
}
