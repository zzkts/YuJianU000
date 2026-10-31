using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 吸附过渡的公共运动：离目标越远追得越快，贴近目标时减速收尾。
    /// 建筑迁移（<see cref="BuildingMigration"/>）与蓝图放置（<see cref="BuildingPlacement"/>）
    /// 共用这一份，保证两套拖动的手感完全一致。
    ///
    /// 距离 → 速度的曲线：距离 0 时取「基准速度 × 近距离比例」，距离到
    /// <c>accelerateDistance</c> 时取上限，中间用 smoothstep 插值（起步和收尾都不突兀）。
    /// 最后仍用 <see cref="Vector3.MoveTowards"/> 推进，所以帧率无关、不会冲过头、到点精确停住。
    /// </summary>
    public static class SnapFollowMotion
    {
        /// <summary>
        /// 把 <paramref name="current"/> 朝 <paramref name="target"/> 推进一小步，返回新位置。
        /// <paramref name="baseSpeed"/> ≤ 0 表示直接吸附（返回目标点，忽略 deltaTime）；
        /// <paramref name="maxSpeed"/> ≤ 0 时退回 <paramref name="baseSpeed"/>，退化成匀速。
        /// </summary>
        public static Vector3 Advance(
            Vector3 current,
            Vector3 target,
            float baseSpeed,
            float maxSpeed,
            float accelerateDistance,
            float nearSpeedRatio,
            float deltaTime)
        {
            if (baseSpeed <= 0f)
            {
                return target;
            }

            if (maxSpeed <= 0f)
            {
                maxSpeed = baseSpeed;
            }

            float distance = Vector3.Distance(current, target);
            float blend = accelerateDistance <= 0f
                ? 1f
                : Mathf.Clamp01(distance / accelerateDistance);

            // smoothstep：近距离缓慢起步，中段加速，远距离封顶
            blend = blend * blend * (3f - 2f * blend);

            float nearSpeed = baseSpeed * Mathf.Clamp01(nearSpeedRatio);
            float speed = Mathf.Lerp(nearSpeed, maxSpeed, blend);

            return Vector3.MoveTowards(current, target, speed * Mathf.Max(0f, deltaTime));
        }
    }
}
