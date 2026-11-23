using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Core;
using Yujian.Levels;
using Yujian.Mining;
using Scene = UnityEngine.SceneManagement.Scene;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 关卡难度曲线调整窗口（2026-11-20）。
    ///
    /// 只做一件事：把「档位 → 波次 / BOSS」一次性算好，批量写进当前场景里的所有 LevelEntrance，
    /// 避免手改每道门的 5 个字段。曲线的「单只 HP / 奖励」来自 OreLevelTable + OreTypeTable，
    /// 「工具攻击 / 升级费」来自 MiningToolData，本窗口只负责节奏类参数。
    ///
    /// BOSS 公式（与设计稿第 5 节一致）：
    ///   · BOSS HP = 该档工具攻击 × BOSS 目标击杀秒数 × 点击频率
    ///   · BOSS 奖励 = 该档单只加权奖励 × 奖励波数（不要再乘平均 HP：×6 后 HP / 奖励已脱钩）
    ///   · BOSS 回血 = BOSS HP × 每秒回血百分比
    ///
    /// 入口：菜单栏 → 屿见 → 数值 → 关卡难度曲线…
    /// </summary>
    public class LevelCurveTuningWindow : EditorWindow
    {
        private const string LevelTablePath = "Assets/Data/Mining/OreLevelTable.asset";
        private const string ToolPath = "Assets/Data/Mining/Tools/MiningTool_石头.asset";
        private const string EconomyPath = "Assets/Data/Economy/EconomySettings.asset";
        private const string PrefPrefix = "Yujian.LevelCurve.";

        private OreLevelTable levelTable;
        private MiningToolData tool;
        private EconomySettings economy;
        private LevelEntrance[] entrances = Array.Empty<LevelEntrance>();

        // ---------------- 节奏参数（会存 EditorPrefs） ----------------
        private bool useFixedWaves = true;
        private int fixedWaves = 104;
        private float targetSeconds = 300f;
        private float clickRate = 2f;
        private float spawnDelay = 0.2f;
        private float bossKillSeconds = 20f;
        private int bossRewardWaves = 30;
        private float bossRegenPercentPerSecond = 0.5f;
        private float bossCountdownSeconds = 30f;
        private bool oneLevelPerTier = true;
        private bool alignUnlockLevel = true;
        private bool renumberLevels = true;
        private bool clearGroupOverrides = true;
        private bool saveAfterApply;

        private Vector2 scroll;
        private readonly List<Row> rows = new List<Row>();
        private string setupError;
        private int skippedCustom;

        private struct Row
        {
            public int Index;
            public LevelEntrance Entrance;
            public int Tier;
            public int Waves;
            public float AverageHealth;
            public float AverageReward;
            public float Hits;
            public float BossHealth;
            public int BossReward;
            public float BossRegen;
            public float Duration;
            public float IncomeGarbage;
            public float IncomeCoins;
            public int ToolCost;
            public float ToolCostShare;
            public string Error;
        }

        [MenuItem("屿见/数值/关卡难度曲线…", false, 402)]
        public static void Open()
        {
            LevelCurveTuningWindow window = GetWindow<LevelCurveTuningWindow>("关卡难度曲线");
            window.minSize = new Vector2(680f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPrefs();
            ReloadAssets();
            RefreshScene();
            Recalculate();
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        private void OnFocus()
        {
            RefreshScene();
            Recalculate();
        }

        private void OnHierarchyChange()
        {
            RefreshScene();
            Recalculate();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (setupError != null)
            {
                EditorGUILayout.HelpBox(setupError, MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();
            DrawParameters();
            if (EditorGUI.EndChangeCheck())
            {
                Recalculate();
            }

            DrawSummary();
            DrawTable();
            DrawFooter();
        }

        // ---------------- 数据 ----------------

        private void LoadPrefs()
        {
            useFixedWaves = EditorPrefs.GetBool(PrefPrefix + "useFixedWaves", true);
            fixedWaves = EditorPrefs.GetInt(PrefPrefix + "fixedWaves", 104);
            targetSeconds = EditorPrefs.GetFloat(PrefPrefix + "targetSeconds", 300f);
            clickRate = EditorPrefs.GetFloat(PrefPrefix + "clickRate", 2f);
            spawnDelay = EditorPrefs.GetFloat(PrefPrefix + "spawnDelay", 0.2f);
            bossKillSeconds = EditorPrefs.GetFloat(PrefPrefix + "bossKillSeconds", 20f);
            bossRewardWaves = EditorPrefs.GetInt(PrefPrefix + "bossRewardWaves", 30);
            bossRegenPercentPerSecond = EditorPrefs.GetFloat(PrefPrefix + "bossRegenPercent", 0.5f);
            bossCountdownSeconds = EditorPrefs.GetFloat(PrefPrefix + "bossCountdown", 30f);
            oneLevelPerTier = EditorPrefs.GetBool(PrefPrefix + "oneLevelPerTier", true);
            alignUnlockLevel = EditorPrefs.GetBool(PrefPrefix + "alignUnlockLevel", true);
            renumberLevels = EditorPrefs.GetBool(PrefPrefix + "renumberLevels", true);
            clearGroupOverrides = EditorPrefs.GetBool(PrefPrefix + "clearGroupOverrides", true);
            saveAfterApply = EditorPrefs.GetBool(PrefPrefix + "saveAfterApply", false);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(PrefPrefix + "useFixedWaves", useFixedWaves);
            EditorPrefs.SetInt(PrefPrefix + "fixedWaves", fixedWaves);
            EditorPrefs.SetFloat(PrefPrefix + "targetSeconds", targetSeconds);
            EditorPrefs.SetFloat(PrefPrefix + "clickRate", clickRate);
            EditorPrefs.SetFloat(PrefPrefix + "spawnDelay", spawnDelay);
            EditorPrefs.SetFloat(PrefPrefix + "bossKillSeconds", bossKillSeconds);
            EditorPrefs.SetInt(PrefPrefix + "bossRewardWaves", bossRewardWaves);
            EditorPrefs.SetFloat(PrefPrefix + "bossRegenPercent", bossRegenPercentPerSecond);
            EditorPrefs.SetFloat(PrefPrefix + "bossCountdown", bossCountdownSeconds);
            EditorPrefs.SetBool(PrefPrefix + "oneLevelPerTier", oneLevelPerTier);
            EditorPrefs.SetBool(PrefPrefix + "alignUnlockLevel", alignUnlockLevel);
            EditorPrefs.SetBool(PrefPrefix + "renumberLevels", renumberLevels);
            EditorPrefs.SetBool(PrefPrefix + "clearGroupOverrides", clearGroupOverrides);
            EditorPrefs.SetBool(PrefPrefix + "saveAfterApply", saveAfterApply);
        }

        private void ReloadAssets()
        {
            levelTable = AssetDatabase.LoadAssetAtPath<OreLevelTable>(LevelTablePath);
            tool = AssetDatabase.LoadAssetAtPath<MiningToolData>(ToolPath);
            economy = AssetDatabase.LoadAssetAtPath<EconomySettings>(EconomyPath);
        }

        private void RefreshScene()
        {
            LevelEntrance[] found = UnityEngine.Object.FindObjectsByType<LevelEntrance>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Array.Sort(found, (a, b) =>
            {
                int byLevel = a.LevelNumber.CompareTo(b.LevelNumber);
                return byLevel != 0 ? byLevel : string.CompareOrdinal(a.name, b.name);
            });

            entrances = found;
        }

        private void ReadSceneSpawnDelay()
        {
            OreSpawner spawner = UnityEngine.Object.FindFirstObjectByType<OreSpawner>(
                FindObjectsInactive.Include);

            if (spawner == null)
            {
                Debug.LogWarning("[关卡难度曲线] 当前场景找不到 OreSpawner，刷新间隔保持窗口里的值。");
                return;
            }

            SerializedObject serialized = new SerializedObject(spawner);
            SerializedProperty property = serialized.FindProperty("spawnDelay");

            if (property != null)
            {
                spawnDelay = property.floatValue;
                Debug.Log($"[关卡难度曲线] 已读取场景 OreSpawner 的刷新间隔：{spawnDelay:0.###} 秒。", spawner);
            }
        }

        private void Recalculate()
        {
            rows.Clear();
            setupError = null;
            skippedCustom = 0;

            if (levelTable == null)
            {
                setupError = $"找不到矿石等级表：{LevelTablePath}";
                return;
            }

            if (tool == null)
            {
                setupError = $"找不到挖矿工具资产：{ToolPath}";
                return;
            }

            if (levelTable.TypeTable == null)
            {
                setupError = "OreLevelTable 上没有接 OreTypeTable，无法按种类表解析单只 HP / 奖励。";
                return;
            }

            if (entrances == null || entrances.Length == 0)
            {
                setupError = "当前场景没有 LevelEntrance。请先打开关卡场景，或运行「屿见/关卡/配置关卡入口（阶段 1）」。";
                return;
            }

            for (int i = 0; i < entrances.Length; i++)
            {
                LevelEntrance entrance = entrances[i];

                if (entrance == null)
                {
                    continue;
                }

                // 顺序波次 / 中Boss 等手调节奏由入口自己控制，窗口不参与，避免整批覆盖
                if (entrance.UsesCustomPacing)
                {
                    skippedCustom++;
                    continue;
                }

                Row row = new Row
                {
                    Index = i,
                    Entrance = entrance,
                    Tier = oneLevelPerTier ? i + 1 : Mathf.Max(1, entrance.DifficultyTier),
                };

                IReadOnlyList<OreGroup> groups = ResolveGroups(entrance, row.Tier);

                if (!TryComputeTier(row.Tier, groups, out row.AverageHealth, out row.AverageReward,
                        out row.Error))
                {
                    rows.Add(row);
                    continue;
                }

                float attack = tool.GetAttackAtLevel(row.Tier);
                float safeClickRate = Mathf.Max(0.1f, clickRate);
                float perOreSeconds = row.AverageHealth / Mathf.Max(0.0001f, attack) / safeClickRate +
                                      Mathf.Max(0f, spawnDelay);
                float safeBossSeconds = Mathf.Max(1f, bossKillSeconds);

                row.Hits = row.AverageHealth / Mathf.Max(0.0001f, attack);
                row.Waves = useFixedWaves
                    ? Mathf.Max(1, fixedWaves)
                    : Mathf.Max(1, Mathf.FloorToInt(
                        (Mathf.Max(safeBossSeconds + 1f, targetSeconds) - safeBossSeconds) /
                        Mathf.Max(0.01f, perOreSeconds)));
                row.BossHealth = attack * safeBossSeconds * safeClickRate;
                row.BossReward = Mathf.Max(0, Mathf.RoundToInt(
                    row.AverageReward * Mathf.Max(0, bossRewardWaves)));
                row.BossRegen = row.BossHealth * Mathf.Max(0f, bossRegenPercentPerSecond) / 100f;
                row.Duration = row.Waves * perOreSeconds + safeBossSeconds;
                row.IncomeGarbage = row.Waves * row.AverageReward + row.BossReward;
                row.IncomeCoins = row.IncomeGarbage /
                                  Mathf.Max(1, economy != null ? economy.GarbagePerCoin : 5);
                row.ToolCost = tool.GetUpgradeCost(row.Tier);
                row.ToolCostShare = row.ToolCost > 0 && row.IncomeCoins > 0f
                    ? row.ToolCost / row.IncomeCoins
                    : 0f;

                rows.Add(row);
            }
        }

        private IReadOnlyList<OreGroup> ResolveGroups(LevelEntrance entrance, int tier)
        {
            if (!clearGroupOverrides && entrance.AggregateGroups != null &&
                entrance.AggregateGroups.Length > 0)
            {
                return entrance.AggregateGroups;
            }

            if (levelTable.TryGetLevel(tier, out OreLevelDefinition definition))
            {
                return definition.Groups;
            }

            return null;
        }

        private bool TryComputeTier(int tier, IReadOnlyList<OreGroup> groups,
            out float averageHealth, out float averageReward, out string error)
        {
            averageHealth = 0f;
            averageReward = 0f;
            error = null;

            if (groups == null || groups.Count == 0)
            {
                error = $"第 {tier} 档没有配置矿石组。请在 OreLevelTable 里补上这一档。";
                return false;
            }

            OreTypeTable typeTable = levelTable.TypeTable;
            float healthSum = 0f;
            float rewardSum = 0f;
            float totalWeight = 0f;

            for (int g = 0; g < groups.Count; g++)
            {
                OreGroup group = groups[g];

                if (group == null)
                {
                    continue;
                }

                IReadOnlyList<OreEntry> entries = group.Entries;

                for (int i = 0; i < entries.Count; i++)
                {
                    OreEntry entry = entries[i];

                    if (entry == null || !entry.IsSelectable)
                    {
                        continue;
                    }

                    typeTable.Resolve(entry, tier, levelTable,
                        out _, out _, out float health, out int reward);

                    healthSum += health * entry.Weight;
                    rewardSum += reward * entry.Weight;
                    totalWeight += entry.Weight;
                }
            }

            if (totalWeight <= 0f)
            {
                error = $"第 {tier} 档没有可抽取条目（权重全部 ≤ 0）。";
                return false;
            }

            averageHealth = healthSum / totalWeight;
            averageReward = rewardSum / totalWeight;
            return true;
        }

        // ---------------- 界面 ----------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(levelTable != null ? $"关卡表：{levelTable.name}" : "关卡表：未找到",
                EditorStyles.miniLabel);
            GUILayout.Space(8f);
            GUILayout.Label(tool != null ? $"工具：{tool.DisplayName}" : "工具：未找到",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                ReloadAssets();
                RefreshScene();
                Recalculate();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawParameters()
        {
            EditorGUILayout.LabelField("节奏参数", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            useFixedWaves = EditorGUILayout.ToggleLeft(
                "固定波次（推荐：工具 / 照明价格按 104 波标定）", useFixedWaves);

            if (useFixedWaves)
            {
                fixedWaves = EditorGUILayout.IntSlider("每关波次", fixedWaves, 1, 300);
            }
            else
            {
                targetSeconds = EditorGUILayout.Slider("单关目标时长（秒）", targetSeconds, 60f, 900f);
                EditorGUILayout.HelpBox(
                    "按目标时长反算波次只用于试玩。波次一变，工具费占收入的比例会跟着变，" +
                    "不要用它来改价格。", MessageType.Warning);
            }

            clickRate = EditorGUILayout.Slider("点击频率（次/秒）", clickRate, 0.5f, 5f);
            EditorGUILayout.BeginHorizontal();
            spawnDelay = EditorGUILayout.Slider("刷新间隔（秒）", spawnDelay, 0f, 1f);
            if (GUILayout.Button("读场景值", GUILayout.Width(80f)))
            {
                ReadSceneSpawnDelay();
                Recalculate();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("BOSS", EditorStyles.boldLabel);
            bossKillSeconds = EditorGUILayout.Slider("目标击杀（秒）", bossKillSeconds, 5f, 60f);
            bossRewardWaves = EditorGUILayout.IntSlider("奖励 = 多少波小怪", bossRewardWaves, 0, 100);
            bossRegenPercentPerSecond = EditorGUILayout.Slider(
                "回血（% 最大生命/秒）", bossRegenPercentPerSecond, 0f, 2f);
            bossCountdownSeconds = EditorGUILayout.Slider("倒计时（秒）", bossCountdownSeconds, 10f, 60f);

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("写入口径", EditorStyles.boldLabel);
            oneLevelPerTier = EditorGUILayout.ToggleLeft(
                "按关卡顺序一关一档（第 N 关 → 第 N 档）", oneLevelPerTier);
            alignUnlockLevel = EditorGUILayout.ToggleLeft(
                "解锁等级对齐档位（第 N 关解锁等级 = N）", alignUnlockLevel);
            renumberLevels = EditorGUILayout.ToggleLeft(
                "按当前顺序重排关卡号（1..N）", renumberLevels);
            clearGroupOverrides = EditorGUILayout.ToggleLeft(
                "清空每关的专属聚合体组，统一走等级表", clearGroupOverrides);
            saveAfterApply = EditorGUILayout.ToggleLeft("应用后自动保存场景（慎用）", saveAfterApply);

            EditorGUILayout.EndVertical();

            if (clickRate <= 0.1f || spawnDelay < 0f)
            {
                EditorGUILayout.HelpBox("点击频率必须大于 0，刷新间隔不能为负。", MessageType.Error);
            }
        }

        private void DrawSummary()
        {
            int badTiers = 0;
            int overrides = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].Error))
                {
                    badTiers++;
                }

                OreGroup[] groups = rows[i].Entrance != null ? rows[i].Entrance.AggregateGroups : null;
                if (groups != null && groups.Length > 0)
                {
                    overrides++;
                }
            }

            StringBuilder summary = new StringBuilder();
            summary.Append($"场景入口 {entrances.Length} 个；等级表 1~{levelTable.MaxLevel} 档；");
            summary.Append($"工具 1~{tool.MaxLevel} 级。");

            if (oneLevelPerTier)
            {
                summary.Append($" 本次写成 1~{entrances.Length} 档。");
            }
            else
            {
                summary.Append(" 本次沿用每道门现有档位。");
            }

            if (clearGroupOverrides && overrides > 0)
            {
                summary.Append($" 会清空 {overrides} 个入口的 aggregateGroups。");
            }

            if (skippedCustom > 0)
            {
                summary.Append($" {skippedCustom} 个入口勾了「自定义节奏」，预览与应用都会跳过。");
            }

            MessageType type = MessageType.Info;

            if (entrances.Length > levelTable.MaxLevel)
            {
                summary.Append($"\n入口数超过等级表档位数（{levelTable.MaxLevel}），多出来的关卡会报错。");
                type = MessageType.Warning;
            }

            if (badTiers > 0)
            {
                summary.Append($"\n{badTiers} 行有错误，先修数据再应用。");
                type = MessageType.Error;
            }

            EditorGUILayout.HelpBox(summary.ToString(), type);
        }

        private void DrawTable()
        {
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "工具费占比 = 升下一级工具的金币 ÷ 本关总收入（小怪 + BOSS，按 5 垃圾 = 1 金币折算）。" +
                "目标 20%~30%；超出 15%~35% 标红。", MessageType.None);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            Cell("入口", 130f, EditorStyles.boldLabel);
            Cell("档", 32f, EditorStyles.boldLabel);
            Cell("波次", 45f, EditorStyles.boldLabel);
            Cell("击/只", 50f, EditorStyles.boldLabel);
            Cell("BOSS HP", 85f, EditorStyles.boldLabel);
            Cell("BOSS 奖励", 85f, EditorStyles.boldLabel);
            Cell("时长", 55f, EditorStyles.boldLabel);
            Cell("收入(垃圾)", 95f, EditorStyles.boldLabel);
            Cell("工具费占比", 70f, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180f));

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                LevelEntrance entrance = row.Entrance;

                EditorGUILayout.BeginVertical("box");

                if (!string.IsNullOrEmpty(row.Error))
                {
                    EditorGUILayout.LabelField(entrance != null ? entrance.name : "（空入口）",
                        EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(row.Error, MessageType.Error);
                    EditorGUILayout.EndVertical();
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                Cell(entrance != null ? entrance.name : "（空）", 130f);
                Cell(row.Tier.ToString(), 32f);
                Cell(row.Waves.ToString(), 45f);
                Cell(row.Hits.ToString("0.00"), 50f);
                Cell(row.BossHealth.ToString("N0"), 85f);
                Cell(row.BossReward.ToString("N0"), 85f);
                Cell($"{row.Duration / 60f:0.0} min", 55f);
                Cell(row.IncomeGarbage.ToString("N0"), 95f);

                bool shareOff = row.ToolCost > 0 && (row.ToolCostShare < 0.15f || row.ToolCostShare > 0.35f);
                Color previous = GUI.color;
                if (shareOff)
                {
                    GUI.color = new Color(1f, 0.55f, 0.45f);
                }

                Cell(row.ToolCost > 0 ? $"{row.ToolCostShare * 100f:0.0}%" : "满级", 70f);
                GUI.color = previous;

                EditorGUILayout.EndHorizontal();

                if (entrance != null)
                {
                    int groupCount = entrance.AggregateGroups != null ? entrance.AggregateGroups.Length : 0;
                    EditorGUILayout.LabelField(
                        $"当前：档 {entrance.DifficultyTier} / {entrance.WaveCount} 波 / " +
                        $"BOSS {entrance.BossMaxHealth:0.#} / 奖励 {entrance.BossReward} / " +
                        $"回血 {entrance.BossHealthRegenPerSecond:0.##} / 组 {groupCount} 个",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();

            bool hasErrors = false;

            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].Error))
                {
                    hasErrors = true;
                    break;
                }
            }

            GUI.enabled = !hasErrors && rows.Count > 0 && clickRate > 0.1f;

            if (GUILayout.Button("应用到场景", GUILayout.Height(30f)))
            {
                Apply();
            }

            GUI.enabled = true;

            if (GUILayout.Button("刷新预览", GUILayout.Height(30f), GUILayout.Width(110f)))
            {
                RefreshScene();
                Recalculate();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("应用后按 Ctrl+S 保存场景；整批改动可以用 Ctrl+Z 一次撤销。",
                MessageType.None);
        }

        private static void Cell(string text, float width, GUIStyle style = null)
        {
            if (style != null)
            {
                GUILayout.Label(text, style, GUILayout.Width(width));
            }
            else
            {
                GUILayout.Label(text, GUILayout.Width(width));
            }
        }

        // ---------------- 写入 ----------------

        private void Apply()
        {
            if (rows.Count == 0)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].Error))
                {
                    EditorUtility.DisplayDialog("应用关卡难度曲线",
                        $"第 {rows[i].Tier} 档还有错误：{rows[i].Error}\n先修数据再应用。", "知道了");
                    return;
                }
            }

            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "应用关卡难度曲线",
                    BuildApplySummary() + "\n\n会覆盖这些入口的档位 / 解锁等级 / 波次 / BOSS 三项。" +
                    (clearGroupOverrides ? "\n并清空 aggregateGroups。" : string.Empty) +
                    "\n\n继续？",
                    "写入", "取消"))
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("应用关卡难度曲线");

            HashSet<Scene> touchedScenes = new HashSet<Scene>();

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                LevelEntrance entrance = row.Entrance;

                if (entrance == null)
                {
                    continue;
                }

                Undo.RecordObject(entrance, "应用关卡难度曲线");

                SerializedObject serialized = new SerializedObject(entrance);
                serialized.FindProperty("difficultyTier").intValue = row.Tier;

                if (alignUnlockLevel)
                {
                    serialized.FindProperty("unlockLevel").intValue = Mathf.Max(1, row.Tier);
                }

                if (renumberLevels)
                {
                    serialized.FindProperty("levelNumber").intValue = row.Index + 1;
                }

                serialized.FindProperty("waveCount").intValue = row.Waves;
                serialized.FindProperty("bossMaxHealth").floatValue = row.BossHealth;
                serialized.FindProperty("bossReward").intValue = row.BossReward;
                serialized.FindProperty("bossHealthRegenPerSecond").floatValue = row.BossRegen;
                serialized.FindProperty("bossCountdownSeconds").floatValue = bossCountdownSeconds;

                if (clearGroupOverrides)
                {
                    SerializedProperty groups = serialized.FindProperty("aggregateGroups");

                    if (groups != null)
                    {
                        groups.arraySize = 0;
                    }
                }

                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(entrance);

                Scene scene = entrance.gameObject.scene;

                if (scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    touchedScenes.Add(scene);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (saveAfterApply)
            {
                foreach (Scene scene in touchedScenes)
                {
                    if (scene.IsValid() && scene.isDirty)
                    {
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }

            Debug.Log("[关卡难度曲线] 已写入：\n" + BuildTableText() +
                      (saveAfterApply ? "\n场景已保存。" : "\n请按 Ctrl+S 保存场景。"));
            Repaint();
        }

        private string BuildApplySummary()
        {
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                builder.Append(row.Entrance != null ? row.Entrance.name : "（空）");
                builder.Append("：档 ").Append(row.Tier);
                builder.Append("，").Append(row.Waves).Append(" 波");
                builder.Append("，BOSS ").Append(row.BossHealth.ToString("N0"));
                builder.Append(" / 奖励 ").Append(row.BossReward.ToString("N0"));
                builder.Append("，回血 ").Append(row.BossRegen.ToString("0.##"));
                builder.Append("，约 ").Append((row.Duration / 60f).ToString("0.0")).Append(" 分钟");

                if (i < rows.Count - 1)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }

        private string BuildTableText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("入口 | 档 | 波次 | 击/只 | BOSS HP | BOSS 奖励 | 时长(min) | 收入(垃圾) | 费占比");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                builder.Append(row.Entrance != null ? row.Entrance.name : "（空）").Append(" | ");
                builder.Append(row.Tier).Append(" | ");
                builder.Append(row.Waves).Append(" | ");
                builder.Append(row.Hits.ToString("0.00")).Append(" | ");
                builder.Append(row.BossHealth.ToString("0")).Append(" | ");
                builder.Append(row.BossReward).Append(" | ");
                builder.Append((row.Duration / 60f).ToString("0.0")).Append(" | ");
                builder.Append(row.IncomeGarbage.ToString("0")).Append(" | ");
                builder.Append(row.ToolCost > 0
                    ? (row.ToolCostShare * 100f).ToString("0.0") + "%"
                    : "满级");
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
