using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Player;

namespace Yujian.Mining
{
    /// <summary>
    /// 矿石生成器：管理场上唯一一块矿石的生成顺序。
    ///
    /// 职责：
    ///   1. 根据玩家等级选择"当前矿石等级"；
    ///   2. 从该等级的矿石组里随机抽组、组内按权重抽条目；
    ///   3. 矿石被击碎后，在固定位置生成下一块；
    ///   4. 当前等级所有组的所有条目都抽过后，解锁下一等级组。
    ///
    /// 不负责：伤害、金币、受击表现、点击输入。
    /// 进度只存在于运行时，不做存档——本轮要求里没有存档。
    /// </summary>
    public class OreSpawner : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("场上唯一一块矿石。生成器直接复用这个对象，不实例化新对象")]
        [SerializeField] private Ore ore;

        [Tooltip("玩家等级，提供建筑数换算出的当前等级")]
        [SerializeField] private PlayerLevel playerLevel;

        [Tooltip("矿石等级表：等级 → 该等级的矿石组")]
        [SerializeField] private OreLevelTable levelTable;

        [Header("节奏")]
        [Tooltip("上一块矿石击碎后，隔多久生成下一块（秒）。0 表示下一帧立刻生成")]
        [SerializeField] private float spawnDelay = 0.2f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印生成与等级切换日志")]
        [SerializeField] private bool logSpawnFlow = true;

        /// <summary>等级 → 已抽过的条目。同一等级内不会重复抽同一条目。</summary>
        private readonly Dictionary<int, HashSet<OreEntry>> drawnEntriesByTier =
            new Dictionary<int, HashSet<OreEntry>>();

        private int currentTier;
        private bool initialized;
        private Coroutine respawnRoutine;

        /// <summary>当前使用的矿石等级。</summary>
        public int CurrentTier => currentTier;

        private void OnEnable()
        {
            if (ore != null)
            {
                ore.OnDied += HandleOreDied;
            }

            if (playerLevel != null)
            {
                playerLevel.OnLevelChanged += HandleLevelChanged;
            }
        }

        private void OnDisable()
        {
            if (ore != null)
            {
                ore.OnDied -= HandleOreDied;
            }

            if (playerLevel != null)
            {
                playerLevel.OnLevelChanged -= HandleLevelChanged;
            }
        }

        private void Start()
        {
            if (!ValidateDependencies())
            {
                return;
            }

            currentTier = ResolveTier(Mathf.Max(1, playerLevel != null ? playerLevel.CurrentLevel : 1));
            initialized = true;

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 初始化完成，起始矿石等级 {currentTier}");
            }

            SpawnNextOre();
        }

        /// <summary>
        /// 检查依赖。返回 false 时整套生成逻辑不启动，避免出现"矿石消失但没人再生成"的半死状态。
        /// </summary>
        private bool ValidateDependencies()
        {
            if (ore == null)
            {
                Debug.LogError("[OreSpawner] Ore 未配置，无法生成矿石。", this);
                return false;
            }

            if (levelTable == null)
            {
                Debug.LogError("[OreSpawner] OreLevelTable 未配置，无法选择矿石组。", this);
                return false;
            }

            if (levelTable.Levels == null || levelTable.Levels.Count == 0)
            {
                Debug.LogError("[OreSpawner] OreLevelTable 是空表，没有任何等级配置。", this);
                return false;
            }

            if (!ore.UsesExternalSpawnControl)
            {
                Debug.LogError("[OreSpawner] 目标 Ore 没有开启 externalSpawnControl，" +
                               "它自己的整批刷新会和本生成器冲突。生成逻辑已停用，" +
                               "请重跑配置向导或手动勾选该选项。", this);
                return false;
            }

            if (playerLevel == null)
            {
                Debug.LogWarning("[OreSpawner] PlayerLevel 未配置，将固定使用 1 级矿石组。", this);
            }

            return true;
        }

        private void HandleOreDied(Ore deadOre)
        {
            if (!initialized || deadOre != ore)
            {
                return;
            }

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }

            respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            if (spawnDelay > 0f)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
            else
            {
                // 至少等一帧，避免在 Ore.TakeDamage 的调用栈里同步重生成
                yield return null;
            }

            respawnRoutine = null;
            SpawnNextOre();
        }

        /// <summary>
        /// 抽取并生成下一块矿石。抽不到任何条目时矿石保持隐藏，并打印明确错误。
        /// </summary>
        private void SpawnNextOre()
        {
            OreGroup group;
            OreEntry entry = SelectNextEntry(out group);

            if (entry == null)
            {
                Debug.LogError("[OreSpawner] 没有可生成的矿石条目，矿石保持隐藏。" +
                               "请检查矿石等级表与各矿石组的数据。", this);
                return;
            }

            ore.ApplyDefinition(entry.Type, entry.MaxHealth, entry.Reward);

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 等级 {currentTier} / 组「{group.name}」生成" +
                          $"「{OreTypeInfo.DisplayName(entry.Type)}」（HP {entry.MaxHealth}，奖励 {entry.Reward}）");
            }
        }

        /// <summary>
        /// 从当前等级的未挖完组里抽一条矿石定义。
        /// 当前等级挖完后自动切到下一等级；已是最高等级时重置该等级的抽取记录并循环。
        /// </summary>
        private OreEntry SelectNextEntry(out OreGroup selectedGroup)
        {
            selectedGroup = null;

            // guard 只是防御配置异常导致的死循环，正常情况下最多循环"等级数量 + 1"次
            for (int guard = 0; guard < 64; guard++)
            {
                int resolvedTier = ResolveTier(currentTier);
                if (resolvedTier != currentTier)
                {
                    currentTier = resolvedTier;
                }

                OreLevelDefinition definition;
                if (!levelTable.TryGetLevel(currentTier, out definition))
                {
                    Debug.LogError($"[OreSpawner] 等级表里找不到可用的等级（请求 {currentTier}）。", this);
                    return null;
                }

                HashSet<OreEntry> drawn = GetDrawnSet(currentTier);
                List<OreGroup> candidates = CollectAvailableGroups(definition, drawn);

                if (candidates.Count == 0)
                {
                    int nextTier = FindNextConfiguredTier(currentTier);

                    if (nextTier > currentTier)
                    {
                        if (logSpawnFlow)
                        {
                            Debug.Log($"[OreSpawner] 等级 {currentTier} 的矿石组已全部挖完，切换到等级 {nextTier}");
                        }

                        currentTier = nextTier;
                        continue;
                    }

                    if (!HasAnySelectableEntry(definition))
                    {
                        Debug.LogError($"[OreSpawner] 等级 {currentTier} 的矿石组没有任何可抽取条目" +
                                       "（权重与生命值都必须大于 0）。", this);
                        return null;
                    }

                    if (logSpawnFlow)
                    {
                        Debug.LogWarning($"[OreSpawner] 已是最高等级 {currentTier}，所有组都挖完了，" +
                                         "重置本等级的抽取记录后继续循环。", this);
                    }

                    drawn.Clear();
                    continue;
                }

                selectedGroup = candidates[Random.Range(0, candidates.Count)];
                OreEntry entry = PickWeightedEntry(selectedGroup, drawn);

                if (entry == null)
                {
                    // 候选组是按"还有未抽条目"筛的，正常不会走到这里；继续找下一组以防数据被运行时改坏
                    continue;
                }

                drawn.Add(entry);
                return entry;
            }

            Debug.LogError("[OreSpawner] 抽取矿石条目时超过最大循环次数，请检查矿石组配置。", this);
            return null;
        }

        /// <summary>收集当前等级下所有"还有未抽条目"的组。</summary>
        private static List<OreGroup> CollectAvailableGroups(OreLevelDefinition definition, HashSet<OreEntry> drawn)
        {
            List<OreGroup> candidates = new List<OreGroup>();

            if (definition.Groups == null)
            {
                return candidates;
            }

            for (int i = 0; i < definition.Groups.Count; i++)
            {
                OreGroup group = definition.Groups[i];

                if (group == null || !HasUndrawnSelectableEntry(group, drawn))
                {
                    continue;
                }

                candidates.Add(group);
            }

            return candidates;
        }

        private static bool HasUndrawnSelectableEntry(OreGroup group, HashSet<OreEntry> drawn)
        {
            if (group.Entries == null)
            {
                return false;
            }

            for (int i = 0; i < group.Entries.Count; i++)
            {
                OreEntry entry = group.Entries[i];

                if (entry != null && entry.IsSelectable && !drawn.Contains(entry))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnySelectableEntry(OreLevelDefinition definition)
        {
            if (definition.Groups == null)
            {
                return false;
            }

            for (int i = 0; i < definition.Groups.Count; i++)
            {
                OreGroup group = definition.Groups[i];

                if (group != null && group.HasSelectableEntry)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>在组内按权重抽一条还没抽过的矿石定义。</summary>
        private static OreEntry PickWeightedEntry(OreGroup group, HashSet<OreEntry> drawn)
        {
            float totalWeight = 0f;

            for (int i = 0; i < group.Entries.Count; i++)
            {
                OreEntry entry = group.Entries[i];

                if (entry != null && entry.IsSelectable && !drawn.Contains(entry))
                {
                    totalWeight += entry.Weight;
                }
            }

            if (totalWeight <= 0f)
            {
                return null;
            }

            float roll = Random.value * totalWeight;
            OreEntry last = null;

            for (int i = 0; i < group.Entries.Count; i++)
            {
                OreEntry entry = group.Entries[i];

                if (entry == null || !entry.IsSelectable || drawn.Contains(entry))
                {
                    continue;
                }

                last = entry;
                roll -= entry.Weight;

                if (roll <= 0f)
                {
                    return entry;
                }
            }

            // 浮点误差导致没在循环里选中时，返回最后一条，保证一定有结果
            return last;
        }

        private HashSet<OreEntry> GetDrawnSet(int tier)
        {
            HashSet<OreEntry> drawn;

            if (!drawnEntriesByTier.TryGetValue(tier, out drawn))
            {
                drawn = new HashSet<OreEntry>();
                drawnEntriesByTier.Add(tier, drawn);
            }

            return drawn;
        }

        /// <summary>
        /// 把请求等级映射到表里实际配置的等级：优先取不超过请求值的最高等级，否则取最低等级。
        /// 这样等级表可以有空档（例如只配 1、3、6），也不会因为查不到而卡死。
        /// </summary>
        private int ResolveTier(int requestedLevel)
        {
            if (levelTable == null || levelTable.Levels == null || levelTable.Levels.Count == 0)
            {
                return Mathf.Max(1, requestedLevel);
            }

            int bestBelow = int.MinValue;
            int lowest = int.MaxValue;

            for (int i = 0; i < levelTable.Levels.Count; i++)
            {
                OreLevelDefinition definition = levelTable.Levels[i];

                if (definition == null)
                {
                    continue;
                }

                int level = definition.Level;

                if (level <= requestedLevel && level > bestBelow)
                {
                    bestBelow = level;
                }

                if (level < lowest)
                {
                    lowest = level;
                }
            }

            if (bestBelow != int.MinValue)
            {
                return bestBelow;
            }

            if (lowest != int.MaxValue)
            {
                return lowest;
            }

            return Mathf.Max(1, requestedLevel);
        }

        /// <summary>找下一个配置过的、比当前更高的等级；没有则返回当前等级。</summary>
        private int FindNextConfiguredTier(int currentLevel)
        {
            int next = int.MaxValue;

            for (int i = 0; i < levelTable.Levels.Count; i++)
            {
                OreLevelDefinition definition = levelTable.Levels[i];

                if (definition == null)
                {
                    continue;
                }

                if (definition.Level > currentLevel && definition.Level < next)
                {
                    next = definition.Level;
                }
            }

            return next == int.MaxValue ? currentLevel : next;
        }

        private void HandleLevelChanged(int newLevel)
        {
            if (!initialized)
            {
                // 还没初始化时由 Start 读取 CurrentLevel，不需要在这里响应
                return;
            }

            int targetTier = ResolveTier(newLevel);

            if (targetTier <= currentTier)
            {
                return;
            }

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 玩家等级变为 {newLevel}，矿石等级 {currentTier} → {targetTier}" +
                          "（当前这块矿石打碎后生效）");
            }

            currentTier = targetTier;
        }
    }
}
