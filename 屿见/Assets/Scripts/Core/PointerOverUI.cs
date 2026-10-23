using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yujian.Core
{
    /// <summary>
    /// 判断屏幕坐标是否落在 UI 上的唯一实现。
    ///
    /// 之前 MiningInput / BuildingPlacement / BuildingCameraController 各自复制了一份
    /// EventSystem.RaycastAll 写法。三份逻辑一旦要改（例如排除某个透明遮罩、支持多点触控），
    /// 很容易只改一处，表现为“挖矿挡住了、建造没挡住”这种极难定位的差异。统一放在这里。
    ///
    /// 为什么不用 EventSystem.IsPointerOverGameObject()：
    ///   · 它只认当前鼠标位置，触屏要额外传 fingerId；
    ///   · 结果受 EventSystem 与调用方的执行顺序影响（本帧的 UI 状态可能还没刷新）。
    /// 按坐标自己射线，每个指尖传各自的屏幕坐标即可支持多指。
    /// </summary>
    public static class PointerOverUI
    {
        // 复用一个列表，避免每次点击都分配。UI 射线只在点击 / 拖拽开始时调用，频率极低
        private static readonly List<RaycastResult> results = new List<RaycastResult>();

        /// <summary>屏幕坐标是否落在任意 UI 上。没有 EventSystem 时返回 false（视为没有 UI 吃点击）。</summary>
        public static bool IsOver(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;

            if (eventSystem == null)
            {
                return false;
            }

            PointerEventData eventData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
            };

            results.Clear();
            eventSystem.RaycastAll(eventData, results);
            return results.Count > 0;
        }

        /// <summary>
        /// 带“缺 EventSystem”警告的版本。每个调用方传自己的 <paramref name="alreadyWarned"/>，
        /// 保证同一条警告只打一次，同时报错信息仍能指向具体脚本。
        /// </summary>
        /// <param name="screenPosition">要判定的屏幕坐标。</param>
        /// <param name="context">打印警告时关联的组件，可为 null。</param>
        /// <param name="alreadyWarned">调用方自己的“已警告过”标记。</param>
        public static bool IsOver(Vector2 screenPosition, Object context, ref bool alreadyWarned)
        {
            EventSystem eventSystem = EventSystem.current;

            if (eventSystem == null)
            {
                // 没有 EventSystem 就说明场景里没有能吃掉点击的 UI，放行即可。
                // 但场景里明明有面板却漏了 EventSystem 时，这一点必须喊出来，否则只会表现为“点 UI 没反应”。
                if (!alreadyWarned)
                {
                    alreadyWarned = true;
                    Debug.LogWarning("[PointerOverUI] 场景中没有 EventSystem，无法判断点击是否落在 UI 上；" +
                                     "后续点击一律不做 UI 屏蔽。若已有面板，请补一个 EventSystem。", context);
                }

                return false;
            }

            PointerEventData eventData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
            };

            results.Clear();
            eventSystem.RaycastAll(eventData, results);
            return results.Count > 0;
        }
    }
}
