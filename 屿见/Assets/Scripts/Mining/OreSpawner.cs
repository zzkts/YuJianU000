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

        [Tooltip("矿石等级表：等级 → 该等级的矿石组；垃圾种类表也挂在它上面")]
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
        private bool paused;
        private bool spawnPending;

        /// <summary>关卡系统 2.3.1「垃圾聚合体组」：非空时只从这些组里抽，忽略等级表里的组。</summary>
        private OreGroup[] groupOverride;
        private readonly HashSet<OreEntry> overrideDrawn = new HashSet<OreEntry>();

        /// <summary>
        /// 关卡锁定的怪物档位。0 = 不锁，跟随玩家等级。
        /// 锁定期间不看玩家等级、也不自动跳档，本档抽完就清记录循环。
        /// </summary>
        private int tierOverride;

        /// <summary>当前使用的矿石等级。</summary>
        public int CurrentTier => currentTier;

        /// <summary>场上共享的那一块矿石。关卡系统用它判断 BOSS、放大 BOSS。</summary>
        public Ore CurrentOre => ore;

        /// <summary>关卡是否处于暂停状态（离开关卡时由 LevelManager 打开）。</summary>
        public bool IsPaused => paused;

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

            // 关卡可能比本组件先 Start（直接开局在关卡里），优先用已经锁定的档位
            currentTier = ResolveTier(tierOverride > 0 ? tierOverride : GetBaseTierLevel());
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

            if (levelTable.TypeTable == null)
            {
                Debug.LogWarning("[OreSpawner] 矿石等级表上没有接垃圾种类表，将退回矿石组条目上的生命 / 奖励数值。" +
                                 "新数据里条目的数值是 0（跟随种类表），不接表会生成不出矿石。", this);
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

        /// <summary>
        /// 暂停 / 恢复生成（关卡系统 2.3.3：退出关卡后聚合体停止刷新）。
        /// 暂停期间被打死的矿石不会立刻补位，恢复时再生成，保证关卡进度不会在岛上看不见时推进。
        /// </summary>
        public void SetPaused(bool value)
        {
            if (paused == value)
            {
                return;
            }

            paused = value;

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 关卡生成{(paused ? "暂停" : "恢复")}。");
            }

            if (!paused && spawnPending)
            {
                spawnPending = false;
                SpawnNextOre();
            }
        }

        /// <summary>
        /// 指定本关卡的「垃圾聚合体组」（策划案 2.3.1）。
        /// 传空数组表示不覆盖，继续按矿石等级表抽取。
        /// 只在组集合真的变化时清空抽过的记录，退出 / 重进关卡不会重置抽取进度。
        /// </summary>
        public void SetGroupOverride(OreGroup[] groups)
        {
            OreGroup[] normalized = groups != null && groups.Length > 0 ? groups : null;

            if (SameGroups(groupOverride, normalized))
            {
                return;
            }

            groupOverride = normalized;
            overrideDrawn.Clear();

            if (logSpawnFlow)
            {
                Debug.Log(normalized != null
                    ? $"[OreSpawner] 本关使用专属聚合体组，共 {normalized.Length} 组。"
                    : "[OreSpawner] 本关没有专属聚合体组，改回按矿石等级表抽取。");
            }
        }

        /// <summary>
        /// 锁定本关卡的怪物档位（关卡入口的 difficultyTier）。
        /// 传 0 解除锁定，恢复成跟随玩家等级。
        /// 锁定期间：不响应玩家等级变化，本档种类抽完也不跳档，而是清记录从头循环。
        /// </summary>
        public void SetTierOverride(int tier)
        {
            int normalized = Mathf.Max(0, tier);

            if (tierOverride == normalized)
            {
                return;
            }

            tierOverride = normalized;

            if (tierOverride > 0)
            {
                int resolvedTier = ResolveTier(tierOverride);

                // 同一档反复进出关卡时保留抽取记录，换档才重来
                if (resolvedTier != currentTier)
                {
                    GetDrawnSet(resolvedTier).Clear();
                }

                currentTier = resolvedTier;

                if (logSpawnFlow)
                {
                    Debug.Log($"[OreSpawner] 本关锁定怪物档位 {tierOverride}（实际使用 {currentTier} 档），" +
                              "不跟玩家等级走。");
                }

                return;
            }

            int playerTier = ResolveTier(GetBaseTierLevel());

            if (playerTier > currentTier)
            {
                currentTier = playerTier;
            }

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 解除档位锁定，恢复跟随玩家等级（当前 {currentTier} 档）。");
            }
        }

        /// <summary>
        /// 按当前档位与专属组立刻重刷场上这一块（暂停 / 未初始化时不做）。
        /// 进入关卡时调用，把进门前按玩家等级刷出来的那块换成门的档位。
        /// </summary>
        public void RefreshCurrentOre()
        {
            if (!initialized || paused || ore == null)
            {
                return;
            }

            // 丢弃欠下的那次刷新，避免下一秒又补一块
            StopSpawning();
            SpawnNextOre();
        }

        /// <summary>
        /// 立即停止刷新，并丢弃「暂停期间欠下的一次生成」。
        /// 进入 BOSS 战 / 关卡通关后调用，否则回到关卡时可能凭空补出一块小怪。
        /// </summary>
        public void StopSpawning()
        {
            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }

            spawnPending = false;
        }

        /// <summary>
        /// 把场上共享的矿石切换成 BOSS（策划案 2.3.4 / 2.3.6）。
        /// 会停掉欠下的刷新并暂停生成，再用指定数值重写这一块矿石；
        /// BOSS 被击碎后由 LevelManager 决定是否通关，本类不会再补位。
        /// </summary>
        /// <returns>是否成功应用 BOSS 定义。</returns>
        public bool SpawnBoss(OreType type, float maxHealth, int reward)
        {
            if (ore == null)
            {
                Debug.LogError("[OreSpawner] 没有可用的矿石对象，无法生成 BOSS。", this);
                return false;
            }

            SetPaused(true);
            StopSpawning();
            ore.ApplyDefinition(type, maxHealth, reward);

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] BOSS 已生成：{OreTypeInfo.DisplayName(type)}，" +
                          $"生命值 {maxHealth:0.##}，奖励 {reward}。");
            }

            return true;
        }

        private static bool SameGroups(OreGroup[] a, OreGroup[] b)
        {
            if (a == b)
            {
                return true;
            }

            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
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
            if (paused)
            {
                // 关卡暂停中：记住还欠一块矿石，恢复时补上
                spawnPending = true;

                if (logSpawnFlow)
                {
                    Debug.Log("[OreSpawner] 关卡暂停中，矿石改为恢复后生成。");
                }

                return;
            }

            spawnPending = false;

            OreGroup group;
            OreEntry entry = SelectNextEntry(out group);

            if (entry == null)
            {
                Debug.LogError("[OreSpawner] 没有可生成的矿石条目，矿石保持隐藏。" +
                               "请检查矿石等级表与各矿石组的数据。", this);
                return;
            }

            // 数值来源：垃圾种类表（每种垃圾只写一次基础值）
            // 条目上的 maxHealth / reward 是覆盖值，0 表示跟随种类表；没有种类表时退回旧算法
            int tier = Mathf.Max(1, currentTier);
            float baseHealth;
            int baseReward;
            float health;
            int reward;

            if (levelTable.TypeTable != null)
            {
                levelTable.TypeTable.Resolve(entry, tier, levelTable,
                    out baseHealth, out baseReward, out health, out reward);
            }
            else
            {
                baseHealth = entry.MaxHealth;
                baseReward = entry.Reward;
                health = baseHealth * levelTable.GetHealthMultiplier(tier);
                reward = levelTable.GetRewardAtTier(baseReward, tier);
            }

            if (health <= 0f)
            {
                Debug.LogError($"[OreSpawner] 「{OreTypeInfo.DisplayName(entry.Type)}」解析出的生命值不大于 0，" +
                               "请检查垃圾种类表里是否配置了这一种（或条目上的生命覆盖值）。矿石保持隐藏。", this);
                return;
            }

            ore.ApplyDefinition(entry.Type, health, reward);

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 等级 {currentTier} / 组「{group.name}」生成" +
                          $"「{OreTypeInfo.DisplayName(entry.Type)}」（基础 HP {baseHealth:0.##} → " +
                          $"实际 HP {health:0.##}，基础奖励 {baseReward} → {reward}）");
            }
        }

        /// <summary>
        /// 从当前等级的未挖完组里抽一条矿石定义。
        /// 当前等级挖完后自动切到下一等级；已是最高等级时重置该等级的抽取记录并循环。
        /// </summary>
        private OreEntry SelectNextEntry(out OreGroup selectedGroup)
        {
            selectedGroup = null;

            // 关卡指定了专属聚合体组时，等级只用于血量 / 奖励成长，组和条目都从专属表里抽
            if (groupOverride != null)
            {
                return SelectOverrideEntry(out selectedGroup);
            }

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
                    // 关卡锁了档位：本档抽完不跳档，清记录从头循环，保证整关都在同一档
                    if (tierOverride > 0)
                    {
                        if (!HasAnySelectableEntry(definition))
                        {
                            Debug.LogError($"[OreSpawner] 锁定的等级 {currentTier} 没有任何可抽取条目" +
                                           "（权重与生命值都必须大于 0）。", this);
                            return null;
                        }

                        if (logSpawnFlow)
                        {
                            Debug.Log($"[OreSpawner] 锁定的等级 {currentTier} 已全部抽过一遍，" +
                                      "重置抽取记录后继续循环（不跳档）。");
                        }

                        drawn.Clear();
                        continue;
                    }

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

        /// <summary>
        /// 从关卡专属的聚合体组里抽一条：组间等概率，组内按权重，抽过的不重复；
        /// 全部抽完后清空记录重新循环，保证关卡可以一直刷到 BOSS 出现。
        /// </summary>
        private OreEntry SelectOverrideEntry(out OreGroup selectedGroup)
        {
            selectedGroup = null;

            for (int guard = 0; guard < 64; guard++)
            {
                List<OreGroup> candidates = new List<OreGroup>();
                bool anySelectable = false;

                for (int i = 0; i < groupOverride.Length; i++)
                {
                    OreGroup group = groupOverride[i];

                    if (group == null)
                    {
                        continue;
                    }

                    if (group.HasSelectableEntry)
                    {
                        anySelectable = true;
                    }

                    if (HasUndrawnSelectableEntry(group, overrideDrawn))
                    {
                        candidates.Add(group);
                    }
                }

                if (candidates.Count == 0)
                {
                    if (!anySelectable)
                    {
                        Debug.LogError("[OreSpawner] 关卡专属聚合体组里没有任何可抽取条目" +
                                       "（权重与生命值都必须大于 0）。", this);
                        return null;
                    }

                    if (logSpawnFlow)
                    {
                        Debug.Log("[OreSpawner] 关卡专属聚合体组已全部抽过一遍，重置抽取记录后继续循环。");
                    }

                    overrideDrawn.Clear();
                    continue;
                }

                selectedGroup = candidates[Random.Range(0, candidates.Count)];
                OreEntry entry = PickWeightedEntry(selectedGroup, overrideDrawn);

                if (entry == null)
                {
                    continue;
                }

                overrideDrawn.Add(entry);
                return entry;
            }

            Debug.LogError("[OreSpawner] 抽取关卡专属聚合体时超过最大循环次数，请检查配置。", this);
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

        /// <summary>关卡没锁档位时用来算档位的基础等级：玩家等级。</summary>
        private int GetBaseTierLevel()
        {
            return Mathf.Max(1, playerLevel != null ? playerLevel.CurrentLevel : 1);
        }

        private void HandleLevelChanged(int newLevel)
        {
            if (!initialized)
            {
                // 还没初始化时由 Start 读取 CurrentLevel，不需要在这里响应
                return;
            }

            if (tierOverride > 0)
            {
                // 关卡锁了档位：玩家升级不影响本关的怪物血量 / 奖励
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
