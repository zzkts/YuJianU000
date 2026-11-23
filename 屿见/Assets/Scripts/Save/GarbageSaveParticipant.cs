using UnityEngine;
using Yujian.Player;

namespace Yujian.Save
{
    /// <summary>
    /// 垃圾的存档适配器（v4 新增）。
    /// 与 <see cref="CurrencySaveParticipant"/> 对称：只负责 PlayerGarbage 的读写。
    /// </summary>
    public class GarbageSaveParticipant : SaveParticipant
    {
        [Tooltip("玩家垃圾。留空会明确报错，垃圾不会进入存档")]
        [SerializeField] private PlayerGarbage garbage;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (garbage == null)
            {
                Debug.LogError("[GarbageSaveParticipant] PlayerGarbage 未配置，垃圾不会写入存档。", this);
                data.garbage = 0;
                return;
            }

            data.garbage = garbage.CurrentGarbage;
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (garbage == null)
            {
                Debug.LogError("[GarbageSaveParticipant] PlayerGarbage 未配置，垃圾无法恢复。", this);
                return;
            }

            garbage.RestoreGarbage(data.garbage);
        }
    }
}
