using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 阶段 6 的表现件：建筑被建出来时从 0.8 弹到 1（需求十二「建造成功：Scale 0.8 → 1」）。
    ///
    /// 挂在**建筑预制体的根节点**上，自己播自己的，所以
    /// <c>BuildingManager.BuildAtAnchor</c> 一行都不用改（和阶段 5 的建筑效果同一个路子）。
    ///
    /// 所有写入都在 `Application.isPlaying` 之后 —— 编辑器里加载预制体做检查时
    /// （预制体向导会 <c>LoadPrefabContents</c> 再存回）绝不能改动 localScale，
    /// 否则真的会把 0.8 那个中间状态存进预制体资产里。
    ///
    /// 它只改根节点的 localScale：<c>BuildingLayerStack</c> 调的是子物体材质与位置，两者互不干扰。
    /// </summary>
    public class BuildingSpawnPop : MonoBehaviour
    {
        [Header("弹出")]
        [Tooltip("起始缩放倍率。1 表示不弹；0.8 表示从八成大小弹出")]
        [SerializeField] private float fromScale = 0.8f;

        [Tooltip("弹出时长（秒）。填 0 表示瞬间到位")]
        [SerializeField] private float duration = 0.25f;

        [Tooltip("弹出曲线。默认缓出；首尾值 0 / 1 即可，中间可以拉出回弹")]
        [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印一次弹出日志")]
        [SerializeField] private bool logPop = false;

        /// <summary>建筑本来就该有的缩放。**不假设它是 1** —— 一切倍率都以它为准。</summary>
        private Vector3 baseScale = Vector3.one;

        private float elapsed;
        private bool playing;

        private void Awake()
        {
            // 只读，不改。编辑器里也会跑到这里，所以这里必须是无副作用的
            baseScale = transform.localScale;

            if (baseScale == Vector3.zero)
            {
                // localScale 为 0 的预制体没法按倍率还原，直接按 1 处理并说明原因
                baseScale = Vector3.one;
                Debug.LogWarning("[BuildingSpawnPop] 建筑根节点的 localScale 是 (0,0,0)，" +
                                 "弹出动画已按 (1,1,1) 处理。请检查预制体。", this);
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (fromScale <= 0f)
            {
                Debug.LogWarning($"[BuildingSpawnPop] From Scale 配置为非正数({fromScale})，" +
                                 "已按 1 处理（不播放缩放）。", this);
                return;
            }

            if (duration <= 0f)
            {
                transform.localScale = baseScale;
                return;
            }

            elapsed = 0f;
            playing = true;
            transform.localScale = baseScale * fromScale;

            if (logPop)
            {
                Debug.Log($"[BuildingSpawnPop] {name} 弹出：{fromScale} → 1，用时 {duration:0.##} 秒");
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || !playing)
            {
                return;
            }

            elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / duration);

            // 空曲线会恒返回 0，让建筑永远停在 0.8（同 BuildingCameraDirector 踩过的坑）
            float eased = curve != null && curve.length > 0 ? curve.Evaluate(progress) : progress;

            transform.localScale = Vector3.LerpUnclamped(baseScale * fromScale, baseScale, eased);

            if (progress >= 1f)
            {
                // 收尾精确复位，不留浮点误差
                playing = false;
                transform.localScale = baseScale;
            }
        }
    }
}
