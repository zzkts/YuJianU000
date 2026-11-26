using System.Collections.Generic;
using UnityEngine;
using Yujian.Core;
using Yujian.Levels;

namespace Yujian.Player
{
    /// <summary>
    /// 关卡内的垃圾暂存池（2026-11-26 用户要求）。
    ///
    /// 规则：
    ///   · 背包满时，攻击垃圾聚合体掉落的垃圾**不再消失**，而是记到「这一关的暂存池」里；
    ///   · 玩家下次**进入关卡**时，如果背包还有容量，就把暂存池里的垃圾回灌进背包，灌到上限为止；
    ///   · 灌不下的部分继续留在池子里，等下一次进关。
    ///
    /// 为什么按关卡分开存：第 1 关打满留在场上的垃圾，不应该跑到第 2 关被吸走。
    /// 键是 <see cref="LevelEntrance.LevelNumber"/>，与关卡进度同一套编号。
    ///
    /// 与 <see cref="GarbageBag"/> 的分工：
    ///   · GarbageBag 是「随身携带、会满、回岛才卸」的容器；
    ///   · 本类是「掉在关卡地上、没人捡」的那一堆，只进不出（除了回灌背包）。
    ///
    /// 本类不碰 UI、不碰存档（GarbageOverflowSaveParticipant 负责）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GarbageOverflow : MonoBehaviour
    {
        /// <summary>拿不到关卡号时用的兜底键。下次任意关卡进关都会先清它，保证垃圾不会彻底丢。</summary>
        private const int UnknownLevelKey = 0;

        [Header("依赖")]
        [Tooltip("垃圾背包。回灌目标；留空时自动查找")]
        [SerializeField] private GarbageBag bag;

        [Tooltip("区域状态。进入清洁区时触发回灌；留空时自动查找")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("关卡管理器。用来取当前关卡号；留空时自动查找")]
        [SerializeField] private LevelManager levels;

        [Header("调试")]
        [SerializeField] private bool logOverflowFlow = true;

        private readonly Dictionary<int, int> pendingByLevel = new Dictionary<int, int>();

        /// <summary>池子内容变化时触发：(关卡号, 该关暂存量)。</summary>
        public event System.Action<int, int> OnOverflowChanged;

        /// <summary>当前关卡号。拿不到时为 0。</summary>
        public int CurrentLevelNumber
        {
            get
            {
                if (levels != null && levels.ActiveLevel != null)
                {
                    return levels.ActiveLevel.LevelNumber;
                }

                return UnknownLevelKey;
            }
        }

        /// <summary>当前关卡暂存了多少垃圾。</summary>
        public int CurrentPending => GetPending(CurrentLevelNumber);

        /// <summary>全部暂存（含兜底键），存档用。</summary>
        public IReadOnlyDictionary<int, int> Snapshot => pendingByLevel;

        private void Awake()
        {
            if (bag == null)
            {
                bag = FindFirstObjectByType<GarbageBag>(FindObjectsInactive.Include);
            }

            if (areas == null)
            {
                areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }

            if (levels == null)
            {
                levels = FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include);
            }
        }

        private void Start()
        {
            if (areas != null)
            {
                areas.OnAreaChanged += HandleAreaChanged;

                if (areas.CurrentArea == GameArea.Mining)
                {
                    DrainCurrentLevel();
                }
            }
            else
            {
                Debug.LogError("[GarbageOverflow] 找不到 GameAreaController，进关卡时不会自动回灌。", this);
            }
        }

        private void OnDestroy()
        {
            if (areas != null)
            {
                areas.OnAreaChanged -= HandleAreaChanged;
            }
        }

        /// <summary>
        /// 把垃圾记进「当前关卡」的暂存池。
        /// 只有背包拒收时才该调这个——正常能塞进背包的垃圾不要走这里。
        /// </summary>
        public void Deposit(int amount)
        {
            DepositFor(CurrentLevelNumber, amount);
        }

        /// <summary>按关卡号存（测试与读档用）。amount &lt;= 0 忽略。</summary>
        public void DepositFor(int levelNumber, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            int key = levelNumber <= 0 ? UnknownLevelKey : levelNumber;
            int next = GetPending(key) + amount;
            pendingByLevel[key] = next;

            if (logOverflowFlow)
            {
                Debug.Log($"[GarbageOverflow] 第 {key} 关暂存 +{amount}，累计 {next}（背包已满，垃圾留在关卡内）。", this);
            }

            OnOverflowChanged?.Invoke(key, next);
        }

        /// <summary>把当前关卡暂存的垃圾回灌进背包，灌到背包满为止。返回实际灌进去多少。</summary>
        public int DrainCurrentLevel()
        {
            return DrainFor(CurrentLevelNumber);
        }

        /// <summary>
        /// 回灌指定关卡。会先清兜底键（UnknownLevelKey），再灌这一关。
        /// 返回实际转移量；背包没容量时返回 0，池子原样不动。
        /// </summary>
        public int DrainFor(int levelNumber)
        {
            if (bag == null)
            {
                return 0;
            }

            // 兜底池先清：这些是「关卡号没取到」时掉下来的，不能永远躺着
            int drained = DrainKey(UnknownLevelKey);

            int key = levelNumber <= 0 ? UnknownLevelKey : levelNumber;

            if (key != UnknownLevelKey)
            {
                drained += DrainKey(key);
            }

            return drained;
        }

        private int DrainKey(int key)
        {
            int pending = GetPending(key);

            if (pending <= 0 || bag == null)
            {
                return 0;
            }

            int room = bag.Room;

            if (room <= 0)
            {
                return 0;
            }

            int take = Mathf.Min(pending, room);
            int accepted = bag.TryAdd(take);

            if (accepted <= 0)
            {
                return 0;
            }

            int left = pending - accepted;
            pendingByLevel[key] = left;
            OnOverflowChanged?.Invoke(key, left);

            if (logOverflowFlow)
            {
                Debug.Log($"[GarbageOverflow] 第 {key} 关回灌 {accepted} 进背包，池子剩余 {left}。", this);
            }

            return accepted;
        }

        /// <summary>指定关卡当前暂存多少。</summary>
        public int GetPending(int levelNumber)
        {
            int key = levelNumber <= 0 ? UnknownLevelKey : levelNumber;
            return pendingByLevel.TryGetValue(key, out int amount) ? Mathf.Max(0, amount) : 0;
        }

        /// <summary>存档用：把所有非零池子摊成列表。</summary>
        public List<KeyValuePair<int, int>> CaptureEntries()
        {
            List<KeyValuePair<int, int>> entries = new List<KeyValuePair<int, int>>(pendingByLevel.Count);

            foreach (KeyValuePair<int, int> pair in pendingByLevel)
            {
                if (pair.Value > 0)
                {
                    entries.Add(pair);
                }
            }

            return entries;
        }

        /// <summary>读档用：清空池子。之后再逐条 <see cref="RestoreOne"/>。</summary>
        public void ClearAll()
        {
            pendingByLevel.Clear();
        }

        /// <summary>读档用：写入某一关的暂存量（覆盖）。</summary>
        public void RestoreOne(int levelNumber, int amount)
        {
            if (levelNumber <= 0)
            {
                return;
            }

            if (amount <= 0)
            {
                pendingByLevel.Remove(levelNumber);
                return;
            }

            pendingByLevel[levelNumber] = amount;
        }

        /// <summary>编辑器向导调用。</summary>
        public void ConfigureForSetup(GarbageBag newBag, GameAreaController newAreas, LevelManager newLevels)
        {
            bag = newBag;
            areas = newAreas;
            levels = newLevels;
        }

        private void HandleAreaChanged(GameArea area)
        {
            if (area != GameArea.Mining)
            {
                return;
            }

            // 进入关卡：先看背包还有没有容量，有就回灌
            DrainCurrentLevel();
        }
    }
}
