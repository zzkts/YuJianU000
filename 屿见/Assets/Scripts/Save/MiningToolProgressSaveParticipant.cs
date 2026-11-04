using UnityEngine;
using Yujian.Mining;

namespace Yujian.Save
{
    /// <summary>
    /// 挖矿工具等级存档。
    ///
    /// 只保存当前等级；攻击力、升级价格都由等级和 MiningToolData 重新算出来，
    /// 属于派生值，不写进存档。
    /// </summary>
    public class MiningToolProgressSaveParticipant : SaveParticipant
    {
        [Tooltip("挖矿工具升级进度。留空时运行时自动查找；找不到会明确报错")]
        [SerializeField] private MiningToolProgress toolProgress;

        private void Awake()
        {
            ResolveDependencies();
        }

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            ResolveDependencies();

            if (toolProgress == null)
            {
                Debug.LogError("[MiningToolProgressSaveParticipant] 场景里找不到 MiningToolProgress，" +
                               "挖矿工具等级不会写入存档。", this);
                return;
            }

            data.mining.toolLevel = toolProgress.CurrentLevel;
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            ResolveDependencies();

            if (toolProgress == null)
            {
                Debug.LogError("[MiningToolProgressSaveParticipant] 场景里找不到 MiningToolProgress，" +
                               "挖矿工具等级无法恢复。", this);
                return;
            }

            int savedLevel = data.mining != null ? data.mining.toolLevel : 0;
            toolProgress.SetLevel(savedLevel > 0 ? savedLevel : 1);
        }

        private void ResolveDependencies()
        {
            if (toolProgress == null)
            {
                toolProgress = FindFirstObjectByType<MiningToolProgress>(FindObjectsInactive.Include);
            }
        }
    }
}