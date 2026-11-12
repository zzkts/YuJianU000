using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 矿石种类。以后要加新矿，在末尾追加一项即可，已有配置不会乱。
    /// </summary>
    public enum OreType
    {
        QingShi = 0,     // 垃圾聚合体1（原青石）：最常见
        HongZhuan = 1,   // 垃圾聚合体2（原红砖）
        MuLiao = 2,      // 垃圾聚合体3（原木料）
        HuaGangYan = 3,  // 垃圾聚合体4（原花岗岩）：稀有
    }

    /// <summary>
    /// 每种矿石的显示名、颜色、参考价值。数值是占位，策划直接改这里。
    /// </summary>
    public static class OreTypeInfo
    {
        public static string DisplayName(OreType type)
        {
            switch (type)
            {
                case OreType.QingShi: return "垃圾聚合体1";
                case OreType.HongZhuan: return "垃圾聚合体2";
                case OreType.MuLiao: return "垃圾聚合体3";
                case OreType.HuaGangYan: return "垃圾聚合体4";
                default: return "未知垃圾";
            }
        }

        public static Color Tint(OreType type)
        {
            switch (type)
            {
                case OreType.QingShi: return new Color(0.55f, 0.62f, 0.72f);
                case OreType.HongZhuan: return new Color(0.72f, 0.35f, 0.28f);
                case OreType.MuLiao: return new Color(0.65f, 0.48f, 0.28f);
                case OreType.HuaGangYan: return new Color(0.35f, 0.36f, 0.40f);
                default: return Color.gray;
            }
        }

        /// <summary>参考价值。实际发放仍以 Ore 组件上的 reward 为准，避免破坏现有经济。</summary>
        public static int SuggestedReward(OreType type)
        {
            switch (type)
            {
                case OreType.QingShi: return 10;
                case OreType.HongZhuan: return 12;
                case OreType.MuLiao: return 15;
                case OreType.HuaGangYan: return 25;
                default: return 10;
            }
        }

        /// <summary>是不是稀有矿（彩蛋必掉、特殊音效都可以按这个区分）。</summary>
        public static bool IsRare(OreType type)
        {
            return type == OreType.HuaGangYan;
        }
    }
}