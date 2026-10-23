using UnityEngine;
using Yujian.Core;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑系统的 UI 入口：把按钮点击翻译成对 BuildingManager / BuildingCameraDirector 的调用。
    ///
    /// 为什么需要这个中间层：Unity 的 Button OnClick 只能传 int / float / string / bool / Object
    /// 这几种参数，而 BuildingManager.SelectBuilding 要的是 BuildingData。
    /// 这里用「下标 + 数组」绕开限制——按钮只传一个整数，
    /// 以后新增建筑只要往 BuildingUI.availableBuildings 里加一项，不用改代码，也不用重新接线。
    ///
    /// 本类不持有任何状态，只做转发。
    /// </summary>
    public class BuildingUI : MonoBehaviour
    {
        [Tooltip("建筑系统入口")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("相机调度。区域引用缺失时的兜底：用它把镜头移回常规机位")]
        [SerializeField] private BuildingCameraDirector cameraDirector;

        [Tooltip("区域状态。点「退出建造」时切到观察模式；留空会报错并退化为只退出相机")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("可供玩家建造的建筑清单。顺序与 UI 按钮传进来的下标一一对应")]
        [SerializeField] private BuildingData[] availableBuildings = new BuildingData[0];

        /// <summary>可选建筑的数量。</summary>
        public int AvailableBuildingCount =>
            availableBuildings == null ? 0 : availableBuildings.Length;

        /// <summary>
        /// 选中清单里第 index 个建筑，进入放置模式。
        /// 供按钮 OnClick 调用，参数是 Unity 事件原生支持的 int。
        /// </summary>
        public void SelectBuilding(int index)
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingUI] BuildingManager 未配置，无法选择建筑。", this);
                return;
            }

            if (availableBuildings == null || index < 0 || index >= availableBuildings.Length)
            {
                Debug.LogError($"[BuildingUI] 建筑下标 {index} 越界" +
                               $"（清单共 {AvailableBuildingCount} 项）。" +
                               "请检查按钮传的下标和 Inspector 里的 Available Buildings。", this);
                return;
            }

            BuildingData data = availableBuildings[index];

            if (data == null)
            {
                Debug.LogError($"[BuildingUI] 建筑清单第 {index} 项是空的，请在 Inspector 里补上。", this);
                return;
            }

            buildingManager.SelectBuilding(data);
        }

        /// <summary>取消当前蓝图，但仍留在建造模式（镜头不动）。</summary>
        public void CancelSelection()
        {
            if (buildingManager == null)
            {
                Debug.LogWarning("[BuildingUI] BuildingManager 未配置，取消操作被忽略。", this);
                return;
            }

            buildingManager.CancelSelection();
        }

        /// <summary>
        /// 退出建造模式：退出建筑系统，落到观察模式。
        ///
        /// 优先走区域切换，而不是只调 cameraDirector.ExitBuildMode()：
        /// 后者只动相机，底部导航仍会高亮「建筑」、建筑面板仍会被区域系统当成打开状态。
        /// 区域切到 Observation 后，AreaCameraDirector 会负责退出建造视角并取消未放置蓝图。
        /// </summary>
        public void ExitBuildMode()
        {
            if (areas != null)
            {
                areas.EnterObservationMode();
                return;
            }

            Debug.LogError("[BuildingUI] GameAreaController 未配置：「退出建造」只会退出相机，" +
                           "底部导航仍会停在建筑区。请重新运行菜单「屿见/场景/配置观察模式系统」" +
                           "把 GameAreaSystem 接上。", this);

            if (cameraDirector != null)
            {
                // 兜底：cameraDirector 内部会顺手取消还没放置的蓝图，这里不要重复取消
                cameraDirector.ExitBuildMode();
                return;
            }

            // 没接相机调度时退化为「只取消蓝图」，至少不会把玩家卡在放置模式里
            CancelSelection();
        }
    }
}
