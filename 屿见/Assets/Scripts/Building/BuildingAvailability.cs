using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑栏位（策划案 3.7）里一个建筑按钮的可用状态。
    /// 新增值一律追加到末尾（同 MaterialType 的约定）。
    /// </summary>
    public enum BuildingAvailabilityState
    {
        /// <summary>能建：等级够、没到上限、原料够。</summary>
        Available = 0,

        /// <summary>等级不够。</summary>
        LevelLocked = 1,

        /// <summary>场上已经到上限。</summary>
        LimitReached = 2,

        /// <summary>原料不够。</summary>
        MissingMaterial = 3,

        /// <summary>数据本身有问题（清单里是空引用等），不是玩家的问题。</summary>
        Invalid = 4,
    }

    /// <summary>
    /// 一次「这个建筑现在能不能建」的判定结果。
    ///
    /// 为什么单独抽一个结构：按钮变灰、悬停/按钮内文案、原料刚好够的微光，
    /// 必须和「点下去会怎样」是同一份结论。分开算就一定会出现
    /// 「按钮亮着但点了没反应」或者「按钮灰着但其实能建」。
    ///
    /// 由 BuildingUI.Evaluate(index) 产出，BuildingBarUI 只读不判。
    /// </summary>
    public struct BuildingAvailabilityInfo
    {
        /// <summary>状态。</summary>
        public BuildingAvailabilityState State;

        /// <summary>给玩家看的原因，例如「原料不足」「已达上限 3/3」「需要等级 5」。可建时为空。</summary>
        public string Reason;

        /// <summary>原料不多不少、正好够建这一座。工具栏位用它点亮按钮边框。</summary>
        public bool JustEnoughMaterials;

        /// <summary>场上已有的同种建筑数量。</summary>
        public int BuiltCount;

        /// <summary>数量上限，0 表示不限制。</summary>
        public int Limit;

        /// <summary>现在能不能建。</summary>
        public bool IsAvailable => State == BuildingAvailabilityState.Available;

        /// <summary>造一条 Invalid 结果，避免调用方到处写字段名。</summary>
        public static BuildingAvailabilityInfo Invalid(string reason)
        {
            return new BuildingAvailabilityInfo
            {
                State = BuildingAvailabilityState.Invalid,
                Reason = string.IsNullOrEmpty(reason) ? "数据缺失" : reason,
                JustEnoughMaterials = false,
                BuiltCount = 0,
                Limit = 0,
            };
        }
    }
}