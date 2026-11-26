using System.Collections.Generic;
using UnityEngine;
using Yujian.Player;

namespace Yujian.Save
{
    /// <summary>
    /// 垃圾背包 + 关卡暂存池的存档适配器（策划案 2.1.6，2026-11-25 新增；2026-11-26 并入暂存池）。
    ///
    /// 与 <see cref="GarbageSaveParticipant"/> 对称：那个存岛屿存储站，这个存背包与「掉在关卡里的」。
    /// 三者必须都存——只存一边，读档时垃圾不是凭空多出来就是人间蒸发。
    ///
    /// 为什么暂存池并到这里、而不是单独一个参与者：
    /// 单独的参与者要在场景里多挂一个组件、并在 SaveSystem 的列表里多接一次线，
    /// 少接一次就会「打满后退出游戏 → 那部分奖励蒸发」。并进来以后，
    /// 已经存在的背包参与者自动带上，不需要任何新的场景改动。
    /// </summary>
    public class GarbageBagSaveParticipant : SaveParticipant
    {
        [Tooltip("垃圾背包。留空会明确报错，背包等级与存量不会进存档")]
        [SerializeField] private GarbageBag bag;

        [Tooltip("关卡暂存池。留空时运行时自动查找 / 自动补组件")]
        [SerializeField] private GarbageOverflow overflow;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            CaptureBag(data);
            CaptureOverflow(data);
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            RestoreBag(data);
            RestoreOverflow(data);
        }

        private void CaptureBag(SaveData data)
        {
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

        private void RestoreBag(SaveData data)
        {
            if (bag == null)
            {
                Debug.LogError("[GarbageBagSaveParticipant] GarbageBag 未配置，背包无法恢复。", this);
                return;
            }

            GarbageBagSave saved = data.bag ?? new GarbageBagSave();
            bag.Restore(saved.level, saved.garbage);
        }

        private void CaptureOverflow(SaveData data)
        {
            if (data.levelOverflow == null)
            {
                data.levelOverflow = new List<LevelOverflowSave>();
            }

            data.levelOverflow.Clear();

            GarbageOverflow target = ResolveOverflow();

            if (target == null)
            {
                return;
            }

            List<KeyValuePair<int, int>> entries = target.CaptureEntries();

            for (int i = 0; i < entries.Count; i++)
            {
                data.levelOverflow.Add(new LevelOverflowSave
                {
                    levelNumber = entries[i].Key,
                    garbage = entries[i].Value,
                });
            }
        }

        private void RestoreOverflow(SaveData data)
        {
            GarbageOverflow target = ResolveOverflow();

            if (target == null)
            {
                Debug.LogWarning("[GarbageBagSaveParticipant] 找不到 GarbageOverflow，" +
                                 "关卡暂存垃圾无法恢复（下次进关时不会自动灌回）。", this);
                return;
            }

            target.ClearAll();

            if (data.levelOverflow == null)
            {
                return;
            }

            for (int i = 0; i < data.levelOverflow.Count; i++)
            {
                LevelOverflowSave entry = data.levelOverflow[i];

                if (entry != null)
                {
                    target.RestoreOne(entry.levelNumber, entry.garbage);
                }
            }
        }

        private GarbageOverflow ResolveOverflow()
        {
            if (overflow == null)
            {
                overflow = FindFirstObjectByType<GarbageOverflow>(FindObjectsInactive.Include);
            }

            return overflow;
        }
    }
}
