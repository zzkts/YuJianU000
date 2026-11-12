using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.Levels;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 原型数值 v1 的关卡参数写入（2026-11-10）。
    ///
    /// 只改场景里两个关卡入口的 5 个数值（关卡号 / 解锁等级 / 波次 / BOSS 血量 / BOSS 奖励），
    /// 不碰灰化材质、内容根、聚合体组、BOSS 种类等引用，所以重复运行结构上是安全的；
    /// 但会覆盖这 5 项的手动调整，运行前会弹确认框把差异列出来。
    ///
    /// 垃圾组 / 成长倍数 / 工具曲线不在本命令里，它们直接写在 .asset 上：
    ///   · Assets/Data/Mining/OreGroup_Lv1~3.asset
    ///   · Assets/Data/Mining/OreLevelTable.asset
    ///   · Assets/Data/Mining/Tools/MiningTool_石头.asset
    ///
    /// 入口：菜单栏 → 屿见 → 数值 → 应用原型数值 v1（关卡参数）
    /// </summary>
    public static class PrototypeTuningWizard
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string FirstEntranceName = "清洁区入口";
        private const string SecondEntranceName = "清洁区入口 (1)";

        private struct LevelTuning
        {
            public int LevelNumber;
            public int UnlockLevel;
            public int Waves;
            public float BossHealth;
            public int BossReward;
        }

        private static readonly LevelTuning FirstLevel = new LevelTuning
        {
            LevelNumber = 1,
            UnlockLevel = 1,
            Waves = 3,
            BossHealth = 40f,
            BossReward = 80,
        };

        private static readonly LevelTuning SecondLevel = new LevelTuning
        {
            LevelNumber = 2,
            UnlockLevel = 2,
            Waves = 5,
            BossHealth = 120f,
            BossReward = 200,
        };

        [MenuItem("屿见/数值/应用原型数值 v1（关卡参数）", false, 400)]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("原型数值", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            Scene scene = EnsureSceneOpen();

            if (!scene.IsValid())
            {
                Debug.LogError("[原型数值] 当前没有打开场景，无法写入关卡参数。");
                return;
            }

            LevelEntrance first = FindEntrance(FirstEntranceName);
            LevelEntrance second = FindEntrance(SecondEntranceName);

            // 改名后按解锁等级兜底：解锁 1 的是第一关，解锁 2 的是第二关
            if (first == null || second == null)
            {
                LevelEntrance[] all = Object.FindObjectsByType<LevelEntrance>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

                if (all.Length >= 2)
                {
                    System.Array.Sort(all, (a, b) => a.UnlockLevel.CompareTo(b.UnlockLevel));
                    first = first != null ? first : all[0];
                    second = second != null ? second : all[1];
                }
            }

            if (first == null || second == null)
            {
                Debug.LogError($"[原型数值] 场景里只找到 {((first != null) ? 1 : 0) + ((second != null) ? 1 : 0)} 个关卡入口，" +
                               "至少需要两个。请先运行「屿见/关卡/配置关卡入口（阶段 1）」并复制一个入口。");
                return;
            }

            // 批处理没有窗口，DisplayDialog 会直接返回 false，所以只在交互模式弹确认
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "应用原型数值 v1",
                    Describe(first, FirstLevel) + "\n\n" + Describe(second, SecondLevel) +
                    "\n\n会覆盖这 5 项的手动调整（其他字段不动）。继续？",
                    "写入", "取消"))
            {
                return;
            }

            ApplyTuning(first, FirstLevel);
            ApplyTuning(second, SecondLevel);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[原型数值] 已写入并保存：\n{Describe(first, FirstLevel)}\n{Describe(second, SecondLevel)}");
        }

        private static LevelEntrance FindEntrance(string objectName)
        {
            LevelEntrance[] entrances = Object.FindObjectsByType<LevelEntrance>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < entrances.Length; i++)
            {
                if (entrances[i] != null && entrances[i].name == objectName)
                {
                    return entrances[i];
                }
            }

            return null;
        }

        private static void ApplyTuning(LevelEntrance entrance, LevelTuning tuning)
        {
            SerializedObject serialized = new SerializedObject(entrance);

            serialized.FindProperty("levelNumber").intValue = tuning.LevelNumber;
            serialized.FindProperty("unlockLevel").intValue = tuning.UnlockLevel;
            serialized.FindProperty("waveCount").intValue = tuning.Waves;
            serialized.FindProperty("bossMaxHealth").floatValue = tuning.BossHealth;
            serialized.FindProperty("bossReward").intValue = tuning.BossReward;

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(entrance);

            Debug.Log($"[原型数值] 「{entrance.name}」：关卡号 {entrance.LevelNumber}，" +
                      $"解锁等级 {entrance.UnlockLevel}，波次 {entrance.WaveCount}，" +
                      $"BOSS {entrance.BossMaxHealth:0.#} HP / 奖励 {entrance.BossReward}。", entrance);
        }

        private static string Describe(LevelEntrance entrance, LevelTuning tuning)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("「").Append(entrance.name).Append("」");
            builder.Append(" 关卡号 ").Append(entrance.LevelNumber).Append(" → ").Append(tuning.LevelNumber);
            builder.Append("，解锁 ").Append(entrance.UnlockLevel).Append(" → ").Append(tuning.UnlockLevel);
            builder.Append("，波次 ").Append(entrance.WaveCount).Append(" → ").Append(tuning.Waves);
            builder.Append("，BOSS ").Append(entrance.BossMaxHealth.ToString("0.#")).Append(" → ")
                .Append(tuning.BossHealth.ToString("0.#"));
            builder.Append("，奖励 ").Append(entrance.BossReward).Append(" → ").Append(tuning.BossReward);
            return builder.ToString();
        }

        /// <summary>
        /// 编辑器里当前场景没有关卡入口时回退到主场景；批处理时直接打开主场景。
        /// </summary>
        private static Scene EnsureSceneOpen()
        {
            Scene active = EditorSceneManager.GetActiveScene();

            if (Object.FindFirstObjectByType<LevelEntrance>(FindObjectsInactive.Include) != null)
            {
                return active;
            }

            if (!System.IO.File.Exists(SampleScenePath))
            {
                return active;
            }

            return EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        }
    }
}
