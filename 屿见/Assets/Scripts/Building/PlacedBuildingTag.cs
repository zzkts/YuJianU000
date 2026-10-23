using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 挂在“已经建成的建筑实例”上，记录它来自哪份 BuildingData。
    /// 这样矿石生成器就能区分普通建筑和特殊建筑，而不必去猜预制体名字。
    /// 由 BuildingManager.BuildAtAnchor 在运行时自动添加。
    /// </summary>
    public class PlacedBuildingTag : MonoBehaviour
    {
        [SerializeField] private BuildingData data;

        public BuildingData Data => data;

        public bool IsSpecial => data != null && data.IsSpecialBuilding;

        public void Configure(BuildingData source)
        {
            data = source;
        }
    }
}
