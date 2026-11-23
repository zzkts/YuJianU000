using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 标记「这座建筑的外观来自导入的真实模型」，而不是按玩家放入的材料分层刷色的方块。
    ///
    /// 背景（2026-11-20 用户拍板）：
    ///   · 原建筑外观由 <see cref="BuildingLayerStack"/> 用三层 Cube 拼出来，逐层刷材料色；
    ///   · 真实模型是一整块网格，**原料的样式不影响建筑外观**；
    ///   · 于是建筑预制体不再需要 BuildingLayerStack，但 <see cref="BuildingManager"/>
    ///     过去遇到没有楼层的预制体会直接报错。
    ///
    /// 这个组件就是给 BuildingManager 的显式信号：看到它 = 外观由模型负责，
    /// 跳过「把材料色刷到楼层」这一步，也不要求楼层组件存在。
    ///
    /// 蓝图（<see cref="BuildingBlueprint"/>）不需要它：蓝图本来就没有 BuildingLayerStack，
    /// 由 Valid / InValid 材质整体染色。
    /// </summary>
    public class BuildingModelVisual : MonoBehaviour
    {
        [Tooltip("真实模型挂点。留空则用本物体。只用于定位 / 将来的表现扩展，" +
                 "缩放与碰撞框由建筑美术向导在预制体里配好")]
        [SerializeField] private Transform modelRoot;

        /// <summary>真实模型挂点，永不为 null。</summary>
        public Transform ModelRoot => modelRoot != null ? modelRoot : transform;
    }
}
