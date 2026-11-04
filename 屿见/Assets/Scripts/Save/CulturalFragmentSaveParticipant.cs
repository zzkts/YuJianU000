using System.Collections.Generic;
using UnityEngine;
using Yujian.Mining;

namespace Yujian.Save
{
    /// <summary>
    /// 文化碎片存档。累计获得次数与已解锁下标都保存；
    /// 具体文案仍放在 CulturalFragmentDropper 里，不写进存档。
    /// </summary>
    public class CulturalFragmentSaveParticipant : SaveParticipant
    {
        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (data.fragments == null)
            {
                data.fragments = new FragmentSave();
            }

            data.fragments.totalCollected = CulturalFragmentDropper.TotalCollected;
            data.fragments.unlockedIndices =
                new List<int>(CulturalFragmentDropper.UnlockedIndices);
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            int total = data.fragments != null ? data.fragments.totalCollected : 0;
            IReadOnlyList<int> indices = data.fragments != null
                ? data.fragments.unlockedIndices
                : null;

            CulturalFragmentDropper.RestoreProgress(total, indices);
        }
    }
}