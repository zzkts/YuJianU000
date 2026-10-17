using UnityEngine;

namespace Yujian.Island
{
    /// <summary>
    /// 把海岛生成参数包成一个可以在 Inspector 里调色的资产。
    /// 向导首次运行时会自动创建它；之后改完参数再执行一次菜单即可重建。
    /// </summary>
    [CreateAssetMenu(fileName = "IslandSettings", menuName = "屿见/低模海岛参数")]
    public class IslandSettingsAsset : ScriptableObject
    {
        public IslandSettings settings = new IslandSettings();
    }
}
