using UnityEngine;

namespace Yujian.Shop
{
    /// <summary>
    /// 原料颜色。
    /// 颜色是"同一种原料的变体"，不是不同的原料——所以只有一份"小砖"的 MaterialData，
    /// 库存按 (原料, 颜色) 二元组分格存放，以此区分红色小砖与蓝色小砖。
    /// 新增颜色时追加到末尾，理由同 MaterialType。
    /// </summary>
    public enum MaterialColor
    {
        Red = 0,
        Blue = 1,
        Green = 2,
    }

    /// <summary>MaterialColor 的显示色与中文名。阶段 2 只用于 UI 上色，阶段 3 起用于建筑外观。</summary>
    public static class MaterialColorUtility
    {
        /// <summary>取该颜色对应的显示用颜色。取值偏灰、适合低多边形风格的深色系。</summary>
        public static Color ToDisplayColor(this MaterialColor color)
        {
            switch (color)
            {
                case MaterialColor.Red:
                    return new Color(0.80f, 0.22f, 0.20f);
                case MaterialColor.Blue:
                    return new Color(0.20f, 0.42f, 0.80f);
                case MaterialColor.Green:
                    return new Color(0.24f, 0.65f, 0.32f);
                default:
                    // 未定义的颜色返回洋红：一旦看到洋红就知道枚举加了值但这里没补 case
                    return Color.magenta;
            }
        }

        /// <summary>取该颜色的中文名，例如 Red → "红色"。</summary>
        public static string ToChineseName(this MaterialColor color)
        {
            switch (color)
            {
                case MaterialColor.Red:
                    return "红色";
                case MaterialColor.Blue:
                    return "蓝色";
                case MaterialColor.Green:
                    return "绿色";
                default:
                    return color.ToString();
            }
        }
    }
}
