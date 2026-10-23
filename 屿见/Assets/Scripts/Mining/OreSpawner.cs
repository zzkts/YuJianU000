using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Building;

namespace Yujian.Mining
{
    /// <summary>
    /// 单一矿位的矿石生成器。
    ///
    /// 规则（对应策划文档）：
    ///   1. 场上只有一个固定矿位，始终复用同一块 Ore；
    ///   2. 当前等级有多个矿石组，按权重随机挑一个还有剩余次数的组；
    ///   3. 当前矿石血量归零后，下一帧立刻在同一位置生成下一块；
    ///   4. 当前等级所有组都挖完，自动切到下一等级；
    ///   5. 角色等级由建筑数量决定，建筑等级表可把等级跳得更高。
    ///
    /// 所有数值都在 OreDefinition / OreLevelData / OreSpawnTable /
    /// BuildingLevelTable 资产里，以及本组件的 Inspector 里调。
    /// </summary>
    public class OreSpawner : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("场景里唯一保留的矿石。推荐直接拖 Ore_01")]
        [SerializeField] private Ore ore;

        [Tooltip("矿石等级表：每一级有哪些矿石组、各出现几次、权重多少")]
        [SerializeField] private OreSpawnTable spawnTable;

        [Tooltip("建筑数量等级表：建筑总数达到多少座时，用哪一级矿石")]
        [SerializeField] private BuildingLevelTable buildingLevelTable;

        [Tooltip("场景里的 BuildingManager。留空时会在运行时自动查找")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("固定生成位置。留空时使用矿石自己的位置")]
        [SerializeField] private Transform anchor;

        [Header("生成节奏")]
        [Tooltip("矿石击碎后等多久生成下一块。0 = 下一帧立刻生成")]
        [SerializeField, Min(0f)] private float nextSpawnDelay = 0f;

        [Tooltip("多久检查一次建筑数量，单位秒")]
        [SerializeField, Min(0.05f)] private float buildingLevelCheckInterval = 0.5f;

        [Tooltip("进入 Play 时是否立刻生成第一块")]
        [SerializeField] private bool spawnOnStart = true;

        [Tooltip("是否在 Console 打印生成、升级和剩余次数日志")]
        [SerializeField] private bool logSpawnFlow = true;

        [Header("出场果冻")]
        [Tooltip("整段出场动画的时长（秒）")]
        [SerializeField, Min(0.01f)] private float spawnDuration = 0.45f;

        [Tooltip("前多少比例的时间用来放大，剩下的时间回落。0.65 = 前 65% 放大")]
        [SerializeField, Range(0.1f, 0.95f)] private float spawnRisePortion = 0.65f;

        [Tooltip("出场时的起始缩放，相对原始大小")]
        [SerializeField, Min(0.01f)] private float spawnStartScale = 0.18f;

        [Tooltip("放大到的峰值，1.12 = 比原始大小大 12%")]
        [SerializeField, Min(1f)] private float spawnOvershoot = 1.12f;

        private readonly List<int> remainingCounts = new List<int>();

        private int currentLevelIndex;
        private int lastTotalCount = -1;
        private float buildingCheckTimer;
        private bool waitingForNextSpawn;
        private bool initialized;
        private Ore subscribedOre;

        public Ore CurrentOre => ore;

        public int CurrentTotalBuildingCount { get; private set; }

        public int CurrentNormalBuildingCount { get; private set; }

        public int CurrentSpecialBuildingCount { get; private set; }

        private OreLevelData CurrentLevel =>
            spawnTable != null ? spawnTable.GetLevelByIndex(currentLevelIndex) : null;

        public int CurrentOreLevelNumber =>
            CurrentLevel != null ? CurrentLevel.Level : 1;

        public int RemainingInCurrentLevel
        {
            get
            {
                int total = 0;
                for (int i = 0; i < remainingCounts.Count; i++)
                {
                    total += Mathf.Max(0, remainingCounts[i]);
                }

                return total;
            }
        }

        private void Awake()
        {
            if (ore == null)
            {
                ore = GetComponentInChildren<Ore>(true);
            }

            if (anchor == null && ore != null)
            {
                anchor = ore.transform;
            }

            if (buildingManager == null)
            {
                buildingManager = FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
            }
        }

        private void OnEnable()
        {
            SubscribeToOre();
        }

        private void OnDisable()
        {
            UnsubscribeFromOre();
            waitingForNextSpawn = false;
        }

        private void Start()
        {
            SubscribeToOre();

            if (!ValidateSetup())
            {
                enabled = false;
                return;
            }

            if (anchor == null)
            {
                anchor = ore.transform;
            }

            int totalCount;
            int normalCount;
            int specialCount;
            currentLevelIndex = GetBuildingLevelIndex(out totalCount, out normalCount, out specialCount);
            lastTotalCount = totalCount;
            CurrentTotalBuildingCount = totalCount;
            CurrentNormalBuildingCount = normalCount;
            CurrentSpecialBuildingCount = specialCount;

            InitializeLevel(currentLevelIndex);
            initialized = true;

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 初始化完成：建筑总数 {totalCount} 座（普通 {normalCount}、特殊 {specialCount}），" +
                          $"使用矿石第 {CurrentOreLevelNumber} 级，本等级共 {RemainingInCurrentLevel} 块。", this);
            }

            if (spawnOnStart)
            {
                SpawnNext();
            }
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            buildingCheckTimer += Time.deltaTime;
            if (buildingCheckTimer < buildingLevelCheckInterval)
            {
                return;
            }

            buildingCheckTimer = 0f;
            RefreshBuildingLevel();
        }

        /// <summary>编辑器向导调用，把场景引用一次性接好。</summary>
        public void ConfigureForSetup(Ore newOre, OreSpawnTable table,
            BuildingLevelTable buildingTable, BuildingManager manager, Transform newAnchor)
        {
            ore = newOre;
            spawnTable = table;
            buildingLevelTable = buildingTable;
            buildingManager = manager;
            anchor = newAnchor;
        }

        private bool ValidateSetup()
        {
            if (ore == null)
            {
                Debug.LogError("[OreSpawner] 没有指定 Ore。请把 Ore_01 拖到 Ore 字段。", this);
                return false;
            }

            if (spawnTable == null || spawnTable.LevelCount == 0)
            {
                Debug.LogError("[OreSpawner] 没有指定矿石生成表，或表里没有任何等级。", this);
                return false;
            }

            if (buildingLevelTable == null)
            {
                Debug.LogWarning("[OreSpawner] 没有指定建筑等级表，将永远使用第 1 级矿石。", this);
            }

            if (!ore.gameObject.activeInHierarchy)
            {
                Debug.LogError("[OreSpawner] 指定的 Ore 是关闭状态，矿石不会出现。请把它激活。", this);
                return false;
            }

            return true;
        }

        private void SubscribeToOre()
        {
            if (ore == subscribedOre)
            {
                return;
            }

            UnsubscribeFromOre();
            subscribedOre = ore;

            if (subscribedOre != null)
            {
                subscribedOre.Died += OnOreDied;
            }
        }

        private void UnsubscribeFromOre()
        {
            if (subscribedOre != null)
            {
                subscribedOre.Died -= OnOreDied;
                subscribedOre = null;
            }
        }

        private void OnOreDied(Ore deadOre)
        {
            if (deadOre != ore || waitingForNextSpawn)
            {
                return;
            }

            waitingForNextSpawn = true;
            StartCoroutine(SpawnNextAfterDelay());
        }

        private IEnumerator SpawnNextAfterDelay()
        {
            if (nextSpawnDelay > 0f)
            {
                yield return new WaitForSeconds(nextSpawnDelay);
            }

            // 至少等一帧：让 MiningInput / 自动攻击先读取完本次奖励
            yield return null;

            SpawnNext();
            waitingForNextSpawn = false;
        }

        private void SpawnNext()
        {
            if (!initialized || ore == null || spawnTable == null)
            {
                return;
            }

            if (RemainingInCurrentLevel <= 0)
            {
                if (!TryAdvanceLevel())
                {
                    ResetCurrentLevelCounts();
                }
            }

            int groupIndex = PickGroupIndex();
            if (groupIndex < 0)
            {
                ResetCurrentLevelCounts();
                groupIndex = PickGroupIndex();
            }

            if (groupIndex < 0)
            {
                Debug.LogError("[OreSpawner] 当前等级没有任何可生成的矿石组，请检查生成表。", this);
                return;
            }

            OreLevelData level = CurrentLevel;
            OreGroup group = level.Groups[groupIndex];

            if (group == null || group.Ore == null)
            {
                Debug.LogError($"[OreSpawner] 第 {level.Level} 级的第 {groupIndex} 个矿石组没有指定 OreDefinition。", this);
                return;
            }

            remainingCounts[groupIndex] = Mathf.Max(0, remainingCounts[groupIndex] - 1);

            Vector3 spawnPosition = anchor != null ? anchor.position : ore.transform.position;
            ore.transform.position = spawnPosition;
            ore.ApplyDefinition(group.Ore);
            ore.PlaySpawnJelly(spawnStartScale, spawnOvershoot, spawnDuration, spawnRisePortion);

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 生成「{group.Ore.DisplayName}」" +
                          $"（第 {level.Level} 级，{OreTypeInfo.DisplayName(group.Ore.Type)}），" +
                          $"本等级还剩 {RemainingInCurrentLevel} 块。", this);
            }
        }

        private int PickGroupIndex()
        {
            OreLevelData level = CurrentLevel;
            if (level == null || level.Groups == null || level.Groups.Count == 0)
            {
                return -1;
            }

            float totalWeight = 0f;
            for (int i = 0; i < level.Groups.Count; i++)
            {
                if (remainingCounts[i] <= 0)
                {
                    continue;
                }

                OreGroup group = level.Groups[i];
                if (group == null || group.Ore == null)
                {
                    continue;
                }

                totalWeight += group.Weight;
            }

            if (totalWeight <= 0f)
            {
                return FirstRemainingIndex(level);
            }

            float roll = Random.value * totalWeight;
            for (int i = 0; i < level.Groups.Count; i++)
            {
                if (remainingCounts[i] <= 0)
                {
                    continue;
                }

                OreGroup group = level.Groups[i];
                if (group == null || group.Ore == null || group.Weight <= 0f)
                {
                    continue;
                }

                roll -= group.Weight;
                if (roll <= 0f)
                {
                    return i;
                }
            }

            return FirstRemainingIndex(level);
        }

        private int FirstRemainingIndex(OreLevelData level)
        {
            for (int i = 0; i < remainingCounts.Count; i++)
            {
                if (remainingCounts[i] > 0 && level.Groups[i] != null && level.Groups[i].Ore != null)
                {
                    return i;
                }
            }

            return -1;
        }

        private void InitializeLevel(int levelIndex)
        {
            currentLevelIndex = Mathf.Clamp(levelIndex, 0, Mathf.Max(0, spawnTable.LevelCount - 1));
            remainingCounts.Clear();

            OreLevelData level = CurrentLevel;
            if (level == null || level.Groups == null)
            {
                Debug.LogError($"[OreSpawner] 第 {currentLevelIndex + 1} 个等级数据为空。", this);
                return;
            }

            for (int i = 0; i < level.Groups.Count; i++)
            {
                OreGroup group = level.Groups[i];
                remainingCounts.Add(group != null && group.Ore != null ? group.Count : 0);
            }

            if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 进入矿石第 {level.Level} 级「{level.DisplayName}」，" +
                          $"共 {RemainingInCurrentLevel} 块。", this);
            }
        }

        private void ResetCurrentLevelCounts()
        {
            InitializeLevel(currentLevelIndex);
        }

        private bool TryAdvanceLevel()
        {
            int nextIndex = currentLevelIndex + 1;
            if (spawnTable == null || nextIndex >= spawnTable.LevelCount)
            {
                return false;
            }

            InitializeLevel(nextIndex);
            return true;
        }

        private void RefreshBuildingLevel()
        {
            int totalCount;
            int normalCount;
            int specialCount;
            int targetIndex = GetBuildingLevelIndex(out totalCount, out normalCount, out specialCount);

            bool countsChanged = totalCount != lastTotalCount;
            CurrentTotalBuildingCount = totalCount;
            CurrentNormalBuildingCount = normalCount;
            CurrentSpecialBuildingCount = specialCount;

            if (!countsChanged)
            {
                return;
            }

            lastTotalCount = totalCount;

            if (targetIndex > currentLevelIndex)
            {
                currentLevelIndex = targetIndex;
                InitializeLevel(currentLevelIndex);

                if (logSpawnFlow)
                {
                    Debug.Log($"[OreSpawner] 建筑数量变化：总数 {totalCount}（普通 {normalCount}、特殊 {specialCount}），" +
                              $"矿石等级提升到第 {CurrentOreLevelNumber} 级。", this);
                }
            }
            else if (logSpawnFlow)
            {
                Debug.Log($"[OreSpawner] 建筑数量变化：总数 {totalCount}（普通 {normalCount}、特殊 {specialCount}），" +
                          $"当前仍是矿石第 {CurrentOreLevelNumber} 级。", this);
            }
        }

        private int GetBuildingLevelIndex(out int totalCount, out int normalCount, out int specialCount)
        {
            CountBuildings(out totalCount, out normalCount, out specialCount);

            int levelNumber = buildingLevelTable != null
                ? buildingLevelTable.EvaluateOreLevel(totalCount)
                : 1;

            return ResolveLevelIndex(levelNumber);
        }

        private int ResolveLevelIndex(int levelNumber)
        {
            int index = spawnTable != null ? spawnTable.FindIndexByLevelNumber(levelNumber) : -1;
            if (index >= 0)
            {
                return index;
            }

            // 找不到对应编号时按顺序兜底，避免建筑表填了表里没有的等级就直接报错停机
            return Mathf.Clamp(levelNumber - 1, 0, Mathf.Max(0, spawnTable.LevelCount - 1));
        }

        private void CountBuildings(out int totalCount, out int normalCount, out int specialCount)
        {
            totalCount = 0;
            normalCount = 0;
            specialCount = 0;

            if (buildingManager == null)
            {
                buildingManager = FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
            }

            if (buildingManager == null)
            {
                return;
            }

            IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;
            for (int i = 0; i < placed.Count; i++)
            {
                GameObject building = placed[i];
                if (building == null)
                {
                    continue;
                }

                totalCount++;

                PlacedBuildingTag tag = building.GetComponent<PlacedBuildingTag>();
                if (tag != null && tag.IsSpecial)
                {
                    specialCount++;
                }
                else
                {
                    normalCount++;
                }
            }
        }

        [ContextMenu("调试：打印当前生成状态")]
        private void DebugPrintState()
        {
            OreLevelData level = CurrentLevel;
            Debug.Log($"[OreSpawner] 当前第 {CurrentOreLevelNumber} 级" +
                      $"（{level?.DisplayName ?? "无"}），本等级剩余 {RemainingInCurrentLevel} 块；" +
                      $"建筑总数 {CurrentTotalBuildingCount}（普通 {CurrentNormalBuildingCount}、特殊 {CurrentSpecialBuildingCount}）。", this);
        }
    }
}
