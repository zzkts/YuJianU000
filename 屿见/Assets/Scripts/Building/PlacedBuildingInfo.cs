using System.Collections.Generic;
using UnityEngine;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 挂在已建成的建筑实例上，记录「我是哪座建筑、占哪几格、每层什么颜色」。
    ///
    /// 为什么需要：BuildingManager 只保存 GameObject 列表，实例本身不知道自己来自哪个
    /// BuildingData，也不保留楼层颜色。存档参与者必须能把实例翻译回稳定 ID + 数值，
    /// 所以由建造 / 读档两条路径在生成实例时写入这份运行时信息。
    ///
    /// 只存在于场景实例上，不要挂到预制体资产里。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacedBuildingInfo : MonoBehaviour
    {
        private BuildingData data;
        private RectInt footprintRect;
        private readonly List<MaterialColor> layerColors = new List<MaterialColor>();

        /// <summary>建筑静态数据。可能为空（旧实例或手动挂载），调用方需判空。</summary>
        public BuildingData Data => data;

        /// <summary>占地格子矩形（x → 世界 X，y → 世界 Z）。</summary>
        public RectInt FootprintRect => footprintRect;

        /// <summary>自下而上的楼层颜色，只读。</summary>
        public IReadOnlyList<MaterialColor> LayerColors => layerColors;

        /// <summary>
        /// 由 BuildingManager 在建造成 / 读档恢复时调用。会复制颜色列表，
        /// 调用方之后修改自己的列表不会影响这里。
        /// </summary>
        public void Initialize(
            BuildingData source, RectInt rect, IReadOnlyList<MaterialColor> colors)
        {
            data = source;
            footprintRect = rect;

            layerColors.Clear();

            if (colors == null)
            {
                return;
            }

            for (int i = 0; i < colors.Count; i++)
            {
                layerColors.Add(colors[i]);
            }
        }
    }
}
