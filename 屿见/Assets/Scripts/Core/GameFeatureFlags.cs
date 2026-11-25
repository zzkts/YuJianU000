namespace Yujian.Core
{
    /// <summary>
    /// 全局玩法开关。
    ///
    /// 目前只有一项：原料系统（MaterialData / 材料商店 / 拖材料建造）是否参与游戏。
    ///
    /// 2026-11-24 用户拍板：Demo 阶段「先剔除原料，只保留核心游戏循环」——
    ///   清洁关卡拿垃圾 → 回收站换金币 → 金币建建筑 / 升级 → 建筑数提升玩家等级 → 解锁更难关卡。
    ///
    /// 关闭原料时的行为（由本开关驱动，不在各处写死）：
    ///   · 建筑一律花金币建造，<see cref="Yujian.Shop.BuildingGoldShop.IsGoldModeActive"/> 恒为 true；
    ///   · 建筑栏不再检查原料，改为检查金币余额；
    ///   · 建筑区右下角的材料商店入口不再出现；
    ///   · 蓝图拒绝放入任何材料（<see cref="Yujian.Building.BuildingConstruction.CanAcceptMaterial"/> 返回 false）。
    ///
    /// 刻意**不删除**的东西：材料库存、材料存档、材料资产、编辑器向导、原有材料建造分支。
    /// 它们是旧存档的兼容层，也是以后想把原料加回来时的现成代码。
    ///
    /// 恢复原料流程：把 <see cref="MaterialsEnabledByDefault"/> 改回 true 即可，其余代码不用动。
    /// 测试会显式设置 <see cref="MaterialsEnabled"/>，并在 TearDown 里 Reset，互不污染。
    /// </summary>
    public static class GameFeatureFlags
    {
        /// <summary>原料系统默认是否参与游戏。改这一行就等于恢复 / 关闭原料循环。</summary>
        public const bool MaterialsEnabledByDefault = false;

        private static bool materialsEnabled = MaterialsEnabledByDefault;

        /// <summary>
        /// 原料系统当前是否参与游戏。
        /// 默认取 <see cref="MaterialsEnabledByDefault"/>；只有测试需要临时改写。
        /// </summary>
        public static bool MaterialsEnabled
        {
            get => materialsEnabled;
            set => materialsEnabled = value;
        }

        /// <summary>是否处于「没有原料」的纯核心循环模式。</summary>
        public static bool MaterialFreeLoop => !materialsEnabled;

        /// <summary>恢复成默认值。测试 TearDown 用，运行时不需要调。</summary>
        public static void ResetToDefault()
        {
            materialsEnabled = MaterialsEnabledByDefault;
        }
    }
}
