using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 关卡污染面积掩码（2026-11-21）。
    ///
    /// 复用 <see cref="BuildableAreaMask"/> 的数据结构（网格签名 + 行优先 bool[]），
    /// 但语义相反：**true = 污染 / 未净化时封锁**，而不是可建。
    ///
    /// 每个 <see cref="Yujian.Levels.LevelEntrance"/> 引用一张；
    /// 运行时由 LevelManager 登记到 BuildingGrid.levelBlockMask：
    ///   · 未净化：画过的格子全部封锁；
    ///   · 已净化：只保留污染核心格封锁。
    ///
    /// 由「可建面积画笔」的关卡模式创建和绘制，不进存档。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/关卡/关卡污染面积掩码", fileName = "LevelAreaMask")]
    public class LevelAreaMask : BuildableAreaMask
    {
    }
}
