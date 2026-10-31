using System;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Core.Events;

namespace Yujian.Building
{
    /// <summary>
    /// 主要建筑（里程碑）进度。
    ///
    /// 职责只有一个：回答「玩家已经建成哪几座主要建筑、是否三座全建成」。
    /// 不生成建筑、不判断材料、不碰存档字段、不做 UI——UI 订阅 <see cref="OnProgressChanged"/> 即可。
    ///
    /// 统计规则：
    ///   · 只认 <see cref="Milestones"/> 里配置的建筑，**同一座建多遍只算一次**；
    ///   · 真相来自 BuildingManager.PlacedBuildings（每座实例上的 PlacedBuildingInfo.Data），
    ///     所以读档恢复后直接重算即可，不需要额外存一份「完成列表」；
    ///   · 只有本次运行内**新**达成全部里程碑才触发 <see cref="OnAllCompleted"/>；
    ///     读档恢复到 3/3 只静默刷新，不会重复弹结算提示。
    ///
    /// 当前阶段只做骨架：达成后由 UI 层决定怎么祝贺、弹什么窗口。
    /// </summary>
    public class MilestoneProgress : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器。留空会明确报错，进度恒为 0")]
        [SerializeField] private BuildingManager buildingManager;

        [Header("主要建筑")]
        [Tooltip("按玩家看到的顺序配置三座主要建筑。空槽位与重复项会被跳过并警告。" +
                 "同一座建筑建多遍只算一次。")]
        [SerializeField] private BuildingData[] milestones = new BuildingData[0];

        [Header("调试")]
        [Tooltip("是否在 Console 打印进度变化")]
        [SerializeField] private bool logProgress = true;

        private readonly HashSet<BuildingData> completed = new HashSet<BuildingData>();
        private readonly List<BuildingData> validMilestones = new List<BuildingData>();
        private BuildingData[] snapshot = Array.Empty<BuildingData>();

        private bool initialized;
        private bool completionAnnounced;
        private int lastLoggedCompleted = -1;
        private bool warnedAboutMissingManager;
        private bool warnedAboutEmptyMilestones;

        /// <summary>配置有效的里程碑数量（去重、去空后）。</summary>
        public int TotalCount
        {
            get
            {
                EnsureInitialized();
                return snapshot.Length;
            }
        }

        /// <summary>已建成的主要建筑数量。</summary>
        public int CompletedCount
        {
            get
            {
                EnsureInitialized();
                return CountCompleted();
            }
        }

        /// <summary>是否已建成全部主要建筑。没有配置任何里程碑时恒为 false。</summary>
        public bool IsComplete => TotalCount > 0 && CompletedCount >= TotalCount;

        /// <summary>配置的里程碑清单（只读、已去重）。</summary>
        public IReadOnlyList<BuildingData> Milestones
        {
            get
            {
                EnsureInitialized();
                return snapshot;
            }
        }

        /// <summary>进度（数量）发生变化时触发。读档恢复也会触发一次。</summary>
        public event Action OnProgressChanged;

        /// <summary>本次运行内首次建成全部主要建筑时触发。读档恢复到 3/3 不会触发。</summary>
        public event Action OnAllCompleted;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<BuildingPlaced>(HandleBuildingPlaced);
            EventBus.Subscribe<BuildingRemoved>(HandleBuildingRemoved);
            EventBus.Subscribe<LoadCompleted>(HandleLoadCompleted);

            // 订阅只保证「之后」的变化，先把当前场上已有的建筑算一遍
            Recalculate(false);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BuildingPlaced>(HandleBuildingPlaced);
            EventBus.Unsubscribe<BuildingRemoved>(HandleBuildingRemoved);
            EventBus.Unsubscribe<LoadCompleted>(HandleLoadCompleted);
        }

        /// <summary>
        /// 按当前场上建筑重算进度，不触发达成提示。供 UI 初始化或外部兜底调用。
        /// </summary>
        public void Recalculate()
        {
            Recalculate(false);
        }

        /// <summary>某座建筑是否已完成（只统计配置进里程碑的建筑）。</summary>
        public bool IsMilestoneCompleted(BuildingData data)
        {
            if (data == null)
            {
                return false;
            }

            EnsureInitialized();
            return completed.Contains(data);
        }

        /// <summary>该建筑是否属于「特殊建筑」清单（只看配置，不看是否建成）。</summary>
        public bool IsMilestone(BuildingData data)
        {
            if (data == null)
            {
                return false;
            }

            EnsureInitialized();

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] == data)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 该建筑当前是否已经建在场上。
        /// 直接扫 <see cref="BuildingManager.PlacedBuildings"/>，不依赖事件回调顺序，
        /// 供「特殊建筑只能建一座」的入口校验使用。
        /// </summary>
        public bool IsOnField(BuildingData data)
        {
            if (data == null || buildingManager == null)
            {
                return false;
            }

            IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject instance = placed[i];

                if (instance == null)
                {
                    continue;
                }

                PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

                if (info != null && info.Data == data)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 还差哪些特殊建筑没建（按配置顺序）。每次调用都会新建一份列表，
        /// 供进度 UI 一次性读取，不暴露可变内部状态。
        /// </summary>
        public List<BuildingData> GetRemainingMilestones()
        {
            EnsureInitialized();

            List<BuildingData> remaining = new List<BuildingData>(snapshot.Length);

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!IsOnField(snapshot[i]))
                {
                    remaining.Add(snapshot[i]);
                }
            }

            return remaining;
        }

        private void HandleBuildingPlaced(BuildingPlaced evt)
        {
            // BuildingManager 发布事件前已经把实例加进 PlacedBuildings，直接重算即可
            Recalculate(true);
        }

        private void HandleBuildingRemoved(BuildingRemoved evt)
        {
            Recalculate(false);
        }

        private void HandleLoadCompleted(LoadCompleted evt)
        {
            // 读档恢复的建筑不走 BuildingPlaced；这里补齐，且**不**触发达成提示
            Recalculate(false);
        }

        private void Recalculate(bool allowAnnounce)
        {
            EnsureInitialized();

            bool wasComplete = IsComplete;
            int previousCompleted = CountCompleted();

            RebuildCompletedSet();

            int currentCompleted = CountCompleted();

            if (logProgress && currentCompleted != lastLoggedCompleted)
            {
                lastLoggedCompleted = currentCompleted;
                Debug.Log($"[MilestoneProgress] 主要建筑 {currentCompleted}/{snapshot.Length}" +
                          (snapshot.Length > 0 ? $"（{DescribeCompleted()}）" : string.Empty), this);
            }

            if (currentCompleted != previousCompleted)
            {
                OnProgressChanged?.Invoke();
            }

            if (!allowAnnounce || completionAnnounced || wasComplete || !IsComplete)
            {
                return;
            }

            // 只有「本次运行内新建成最后一座」才祝贺。读档恢复到 3/3 会走 Recalculate(false)，
            // 到这里时 completionAnnounced 仍是 false，但 allowAnnounce 为 false，不会弹。
            completionAnnounced = true;

            if (logProgress)
            {
                Debug.Log($"[MilestoneProgress] 三座主要建筑已全部建成（本次由 {previousCompleted}/" +
                          $"{snapshot.Length} → {currentCompleted}/{snapshot.Length}）。", this);
            }

            OnAllCompleted?.Invoke();
        }

        /// <summary>从场上已建成建筑重建完成集合。</summary>
        private void RebuildCompletedSet()
        {
            completed.Clear();

            if (buildingManager == null)
            {
                if (!warnedAboutMissingManager)
                {
                    warnedAboutMissingManager = true;
                    Debug.LogError("[MilestoneProgress] BuildingManager 未配置，里程碑进度恒为 0。" +
                                   "请运行菜单「屿见/建筑/配置主要建筑里程碑」自动接线。", this);
                }

                return;
            }

            IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject instance = placed[i];

                if (instance == null)
                {
                    continue;
                }

                PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

                if (info != null && info.Data != null)
                {
                    completed.Add(info.Data);
                }
            }
        }

        /// <summary>只数配置里的里程碑，场上多建的非里程碑建筑不会把数字顶上去。</summary>
        private int CountCompleted()
        {
            EnsureInitialized();

            int count = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (completed.Contains(snapshot[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private string DescribeCompleted()
        {
            if (snapshot.Length == 0)
            {
                return string.Empty;
            }

            List<string> names = new List<string>(snapshot.Length);

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (completed.Contains(snapshot[i]))
                {
                    names.Add(snapshot[i].BuildingName);
                }
            }

            return names.Count == 0 ? "无" : string.Join("、", names);
        }

        /// <summary>去空、去重，只保留有效的里程碑配置。</summary>
        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            validMilestones.Clear();

            HashSet<BuildingData> seen = new HashSet<BuildingData>();

            if (milestones == null)
            {
                snapshot = Array.Empty<BuildingData>();
                return;
            }

            for (int i = 0; i < milestones.Length; i++)
            {
                BuildingData data = milestones[i];

                if (data == null)
                {
                    Debug.LogWarning($"[MilestoneProgress] Milestones 第 {i} 槽位为空，已跳过。" +
                                     "请把三座主要建筑都填上。", this);
                    continue;
                }

                if (!seen.Add(data))
                {
                    Debug.LogWarning($"[MilestoneProgress] 建筑「{data.BuildingName}」在 Milestones 里" +
                                     "出现了不止一次，重复项已跳过。", this);
                    continue;
                }

                validMilestones.Add(data);
            }

            snapshot = validMilestones.ToArray();

            if (snapshot.Length == 0 && !warnedAboutEmptyMilestones)
            {
                warnedAboutEmptyMilestones = true;
                Debug.LogWarning("[MilestoneProgress] 没有配置任何主要建筑，里程碑进度与达成提示都不会启用。", this);
            }
        }
    }
}
