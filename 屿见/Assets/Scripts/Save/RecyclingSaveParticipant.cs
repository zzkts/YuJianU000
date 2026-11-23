using UnityEngine;
using Yujian.Building.Recycling;

namespace Yujian.Save
{
    /// <summary>
    /// 回收站共享队列的存档适配器（v4 新增）。
    ///
    /// 只存「整条共享队列」的垃圾数与当前批次金币进度，不存每座站的局部状态——
    /// 站数 / 容量由建筑存档恢复后重新计算。
    /// 读档时 <see cref="RecyclingSystem.RestoreQueue"/> 会先挂起，
    /// 等站点 Start 全部注册完再恢复（见 RecyclingSystem.ApplyPendingRestore）。
    /// </summary>
    public class RecyclingSaveParticipant : SaveParticipant
    {
        [Tooltip("回收站系统。留空会明确报错，队列不会写入存档")]
        [SerializeField] private RecyclingSystem recycling;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (data.recycling == null)
            {
                data.recycling = new RecyclingSave();
            }

            if (recycling == null)
            {
                Debug.LogError("[RecyclingSaveParticipant] RecyclingSystem 未配置，回收队列不会写入存档。", this);
                data.recycling.queuedGarbage = 0;
                data.recycling.coinProgress = 0f;
                return;
            }

            data.recycling.queuedGarbage = recycling.QueuedGarbage;
            data.recycling.coinProgress = (float)recycling.CoinProgress;
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (recycling == null)
            {
                Debug.LogError("[RecyclingSaveParticipant] RecyclingSystem 未配置，回收队列无法恢复。", this);
                return;
            }

            RecyclingSave saved = data.recycling ?? new RecyclingSave();
            recycling.RestoreQueue(saved.queuedGarbage, saved.coinProgress);
        }
    }
}
