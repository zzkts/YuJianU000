using UnityEngine;
using Yujian.Mining;

namespace Yujian.Save
{
    /// <summary>
    /// 照明工具等级存档（D17=A / D21）。
    ///
    /// 只写等级，不写暴击点、不写上限与停留时长——那些都由等级换算出来，
    /// 属于派生值，不存进存档。
    /// </summary>
    public class MiningLightToolSaveParticipant : SaveParticipant
    {
        [Tooltip("照明工具。留空时运行时自动查找；找不到会明确报错")]
        [SerializeField] private MiningLightTool lightTool;

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

            if (lightTool == null)
            {
                Debug.LogError("[MiningLightToolSaveParticipant] 场景里找不到 MiningLightTool，" +
                               "照明工具等级不会写入存档。", this);
                return;
            }

            data.mining.lightToolLevel = lightTool.Level;
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            ResolveDependencies();

            if (lightTool == null)
            {
                Debug.LogError("[MiningLightToolSaveParticipant] 场景里找不到 MiningLightTool，" +
                               "照明工具等级无法恢复。", this);
                return;
            }

            lightTool.SetLevel(data.mining != null ? data.mining.lightToolLevel : 0);
        }

        private void ResolveDependencies()
        {
            if (lightTool == null)
            {
                lightTool = FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            }
        }
    }
}
