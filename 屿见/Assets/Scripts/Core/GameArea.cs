namespace Yujian.Core
{
    /// <summary>
    /// 玩家当前所在的区域。底部导航 [挖矿] [建筑] [商店] 就是在切换它。
    ///
    /// 区域只决定三件事：**相机去哪、哪些面板显示、能不能挖矿**。
    /// 它不决定玩法规则——挖矿怎么算伤害、建筑怎么消耗材料，全部还归各自的老系统管，
    /// 本枚举与 <see cref="GameAreaController"/> 一个字都不碰那些规则。
    ///
    /// 新增区域时追加到末尾（与 MaterialColor / MaterialType 同一条约定：
    /// 枚举数值会进场景 YAML，改已有值会让旧数据错位）。
    /// </summary>
    public enum GameArea
    {
        Mining = 0,
        Building = 1,
        Shop = 2,
    }

    /// <summary>GameArea 的中文名。只用于日志与按钮文字，不参与任何判定。</summary>
    public static class GameAreaUtility
    {
        /// <summary>取该区域的中文名，例如 Mining → "挖矿"。</summary>
        public static string ToChineseName(this GameArea area)
        {
            switch (area)
            {
                case GameArea.Mining:
                    return "挖矿";
                case GameArea.Building:
                    return "建筑";
                case GameArea.Shop:
                    return "商店";
                default:
                    // 没补 case 的新枚举值直接回退成英文名，至少日志里认得出是哪个
                    return area.ToString();
            }
        }
    }
}
