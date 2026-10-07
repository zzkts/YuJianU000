namespace Yujian.Shop
{
    /// <summary>
    /// 原料大类。用于后续建筑配方的需求匹配，与"颜色"是两个独立维度。
    /// 新增类型时请追加到枚举末尾：插入到中间会改变已有资产里已序列化的整数值。
    /// </summary>
    public enum MaterialType
    {
        /// <summary>砖块。</summary>
        Brick = 0,
    }

    /// <summary>MaterialType 的中文显示名。UI 只读这里，不要在 UI 里散写 switch。</summary>
    public static class MaterialTypeUtility
    {
        /// <summary>取该类型的中文名，例如 Brick → "砖块"。</summary>
        public static string ToChineseName(this MaterialType type)
        {
            switch (type)
            {
                case MaterialType.Brick:
                    return "砖块";
                default:
                    return type.ToString();
            }
        }
    }
}
