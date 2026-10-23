using UnityEngine;

namespace Yujian.Core
{
    /// <summary>
    /// 相机引用的统一解析顺序：手动指定 → 本物体上的 Camera → Camera.main。
    ///
    /// 之前 BuildingCameraDirector / BuildingCameraController / AreaCameraDirector 各写了一份，
    /// 三处一旦出现分歧（例如某处先找了子物体），同一个场景里就会有两个导演驱动不同的 Transform。
    /// 统一放在这里，行为只有一份。
    /// </summary>
    public static class CameraResolver
    {
        /// <summary>
        /// 解析要驱动的相机 Transform。找不到时返回 null，由调用方决定怎么报错
        /// （不同脚本的报错文案与严重程度不同，本类不替它们决定）。
        /// </summary>
        /// <param name="configured">Inspector 里手动指定的引用，优先使用。</param>
        /// <param name="host">当前脚本所在的物体，用来找挂在同一物体上的 Camera。</param>
        public static Transform Resolve(Transform configured, GameObject host)
        {
            if (configured != null)
            {
                return configured;
            }

            if (host != null)
            {
                Camera ownCamera = host.GetComponent<Camera>();

                if (ownCamera != null)
                {
                    return ownCamera.transform;
                }
            }

            Camera mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }
    }
}
