using UnityEngine;

namespace Yujian.Levels
{
    /// <summary>
    /// 单个关卡的运行时进度（策划案 2.3.3「保留」项）。
    ///
    /// 保留：小怪累计清理数、BOSS 是否已经出现过、是否已通关（净化进度）；
    /// 不保留：BOSS 当前血量——退出关卡时按满血重新生成，所以这里不存血量。
    ///
    /// 只存数据与只读换算，不碰矿石、不碰 UI、不推进流程——那些归 LevelManager。
    /// </summary>
    public class LevelProgress
    {
        /// <summary>本关累计清除的垃圾聚合体数量。按 2.3.6，一个聚合体 = 一波。</summary>
        public int ClearedAggregates;

        /// <summary>当前是第几波（从 1 开始）。等于已清除数 + 1；BOSS 出现后不再推进。</summary>
        public int CurrentWave = 1;

        /// <summary>BOSS 是否已经出现过。出现后退出再进仍直接进入 BOSS 战，不重打小怪。</summary>
        public bool BossAppeared;

        /// <summary>BOSS 是否已被清除（策划案 2.3.4 的通关定义）。</summary>
        public bool BossDefeated;

        /// <summary>是否已经通关。</summary>
        public bool IsCleared => BossDefeated;

        /// <summary>
        /// 净化进度 0~1。小怪阶段占 80%，BOSS 被清除后到 100%。
        /// 策划案没有给具体公式，这里是阶段 2 的占位规则，阶段 3 做视觉层时再按表现调整。
        /// </summary>
        public float GetPurificationProgress(int totalAggregates)
        {
            if (BossDefeated)
            {
                return 1f;
            }

            if (totalAggregates <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01(ClearedAggregates / (float)totalAggregates) * 0.8f;
        }
    }
}
