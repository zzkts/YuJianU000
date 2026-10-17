using UnityEngine;

namespace Yujian.Island
{
    /// <summary>
    /// 挂在海岛根物体上的轻量描述组件。
    /// 只记录可建台地的范围和水面高度，给后续方格放置系统留一个稳定的查询接口；
    /// 本身不做任何每帧逻辑，也不参与建筑放置判定。
    /// </summary>
    [DisallowMultipleComponent]
    public class IslandDefinition : MonoBehaviour
    {
        [Tooltip("可建台地的世界中心，通常就是海岛根物体的位置。")]
        [SerializeField] private Vector3 buildableCenter = Vector3.zero;

        [Tooltip("可建台地的半尺寸（X = 半宽，Y = 半深）。")]
        [SerializeField] private Vector2 buildableHalfExtents = new Vector2(52f, 52f);

        [Tooltip("海平面高度。")]
        [SerializeField] private float waterLevel = -1.4f;

        public Vector3 BuildableCenter => buildableCenter;
        public Vector2 BuildableHalfExtents => buildableHalfExtents;
        public float WaterLevel => waterLevel;

        /// <summary>
        /// 世界坐标是否落在平整台地内。
        /// 目前只做矩形范围判断；方格放置系统接入后可以换成更精确的判定。
        /// </summary>
        public bool IsWorldPositionBuildable(Vector3 worldPosition)
        {
            Vector3 local = worldPosition - buildableCenter;
            return Mathf.Abs(local.x) <= buildableHalfExtents.x
                && Mathf.Abs(local.z) <= buildableHalfExtents.y;
        }

        /// <summary>
        /// 由编辑器向导写入配置。运行时不需要调用。
        /// </summary>
        public void Configure(Vector3 center, Vector2 halfExtents, float water)
        {
            buildableCenter = center;
            buildableHalfExtents = halfExtents;
            waterLevel = water;
        }
    }
}
