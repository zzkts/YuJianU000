using UnityEngine;

namespace Yujian.Levels
{
    /// <summary>
    /// 一个「关卡战斗舞台」：把某套关卡背景、怪物生成点和战斗相机机位收在同一个根下。
    ///
    /// 现在场景里只有一个舞台（关卡/关卡战场），4 个关卡入口都指向它；
    /// 以后要做多种背景时，复制一份「关卡战场」，把新入口的 <see cref="LevelEntrance.Battlefield"/>
    /// 指过去即可。进入关卡时 <see cref="LevelManager"/> 会：
    ///   · 只打开当前关卡的背景，隐藏其它舞台的背景；
    ///   · 把共享的怪物（Garbage_01）放到当前舞台的怪物生成点；
    ///   · 把当前舞台的相机机位交给 <see cref="Yujian.Core.AreaCameraDirector"/>。
    ///
    /// 本类不主动找任何东西，引用全部由 Inspector / 整理向导接好。
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelBattlefield : MonoBehaviour
    {
        [Header("舞台组成")]
        [Tooltip("背景根（现在的「清洁区灯光」：洞穴外壳 + 三盏灯）。切到别的舞台时会整体隐藏。留空表示不改显隐")]
        [SerializeField] private Transform backgroundRoot;

        [Tooltip("怪物生成点。进入本舞台时，共享的 Garbage_01 会被放到这里。留空使用本物体")]
        [SerializeField] private Transform enemySpawnPoint;

        [Tooltip("战斗相机机位。进入本舞台时挖矿相机会用这个世界位置 / 旋转。留空表示继续用相机组件上的数值")]
        [SerializeField] private Transform cameraAnchor;

        /// <summary>背景根；可能为空。</summary>
        public Transform BackgroundRoot => backgroundRoot;

        /// <summary>怪物生成点；没填时退回本物体。</summary>
        public Transform EnemySpawnPoint => enemySpawnPoint != null ? enemySpawnPoint : transform;

        /// <summary>相机机位；可能为空。</summary>
        public Transform CameraAnchor => cameraAnchor;

        /// <summary>显示 / 隐藏这一套背景。其它组件（怪物、相机）不受影响。</summary>
        public void SetBackgroundVisible(bool visible)
        {
            if (backgroundRoot == null)
            {
                return;
            }

            if (backgroundRoot.gameObject.activeSelf != visible)
            {
                backgroundRoot.gameObject.SetActive(visible);
            }
        }

        /// <summary>编辑器整理向导调用，一次接好舞台的三项引用。</summary>
        public void ConfigureForSetup(Transform newBackgroundRoot, Transform newEnemySpawnPoint,
            Transform newCameraAnchor)
        {
            backgroundRoot = newBackgroundRoot;
            enemySpawnPoint = newEnemySpawnPoint;
            cameraAnchor = newCameraAnchor;
        }
    }
}
