namespace Yujian.Mining
{
    /// <summary>
    /// 伤害来源。用于伤害数字的样式区分（策划案 2.1-3「伤害 UI 显示功能」）。
    /// 新增来源时：在这里加一项，并在 <see cref="DamageNumberTable"/> 里加一行配置。
    /// </summary>
    public enum DamageSource
    {
        /// <summary>玩家点击挖矿。</summary>
        Player,

        /// <summary>建筑效果（如风琴博物馆自动攻击）。</summary>
        Building,
    }
}
