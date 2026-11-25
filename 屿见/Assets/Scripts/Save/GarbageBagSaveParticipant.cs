using UnityEngine;
using Yujian.Player;

namespace Yujian.Save
{
    /// <summary>
    /// 垃圾背包的存档适配器（策划案 2.1.6，2026-11-25 新增）。
    /// 与 <see cref="GarbageSaveParticipant"/> 对称：那个存岛屿存储站，这个存背包。
    /// 两者必须都存——只存一边，读档时垃圾不是凭空多出来就是人间蒸发。
    /// </summary>
    public class GarbageBagSaveParticipant : SaveParticipant
    {
        [Tooltip("垃圾背包。留空会明确报错，背包等级与存量不会进存档")]
        [SerializeField] private GarbageBag bag;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (bag == null)
            {
                Debug.LogError("[GarbageBagSaveParticipant] GarbageBag 未配置，背包不会写入存档。", this);
                data.bag = new GarbageBagSave();
                return;
            }

            if (data.bag == null)
            {
                data.bag = new GarbageBagSave();
            }

            data.bag.level = bag.Level;
            data.bag.garbage = bag.Current;
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (bag == null)
            {
                Debug.LogError("[GarbageBagSaveParticipant] GarbageBag 未配置，背包无法恢复。", this);
                return;
            }

            GarbageBagSave saved = data.bag ?? new GarbageBagSave();
            bag.Restore(saved.level, saved.garbage);
        }
    }
}
