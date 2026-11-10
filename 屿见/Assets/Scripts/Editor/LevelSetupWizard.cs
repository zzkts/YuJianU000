using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.Core;
using Yujian.Levels;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 关卡系统阶段 1 配置向导（策划案 2.3.1～2.3.3）。
    ///
    /// 做四件事：
    ///   1. 在 GameAreaSystem 上挂 <see cref="LevelManager"/> 并接好依赖；
    ///   2. 把现有的「清洁区入口」升级成第一个 <see cref="LevelEntrance"/>（解锁等级 1）；
    ///   3. 生成 / 复用灰化材质 Assets/Materials/Level/Level_Block.mat；
    ///   4. 保存场景。
    ///
    /// 入口：菜单栏 → 屿见 → 关卡 → 配置关卡入口（阶段 1）
    /// </summary>
    public static class LevelSetupWizard
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MaterialsFolder = "Assets/Materials";
        private const string LevelFolder = "Assets/Materials/Level";
        private const string LockedMaterialPath = "Assets/Materials/Level/Level_Block.mat";
        private const string LockedShaderName = "Yujian/Level Block";
        private const string EntranceObjectName = "清洁区入口";

        [MenuItem("屿见/关卡/配置关卡入口（阶段 1）", false, 260)]
        public static void Setup()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("关卡系统", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            Scene scene = EnsureSceneOpen();

            if (!scene.IsValid())
            {
                Debug.LogError("[关卡向导] 当前没有打开场景，无法配置关卡入口。");
                return;
            }

            GameAreaController areaController = Object.FindFirstObjectByType<GameAreaController>(
                FindObjectsInactive.Include);

            if (areaController == null)
            {
                Debug.LogError("[关卡向导] 场景里找不到 GameAreaController，" +
                               "关卡无法切换区域。请先运行菜单「屿见/整合/配置 MVP 整合（阶段 6）」。");
                return;
            }

            MiningEntrance miningEntrance = FindMiningEntrance();

            if (miningEntrance == null)
            {
                Debug.LogError("[关卡向导] 场景里找不到「清洁区入口」（MiningEntrance）。" +
                               "请先运行菜单「屿见/挖矿/一键修复矿洞入口与照明」。");
                return;
            }

            PlayerLevel playerLevel = Object.FindFirstObjectByType<PlayerLevel>(FindObjectsInactive.Include);
            OreSpawner oreSpawner = Object.FindFirstObjectByType<OreSpawner>(FindObjectsInactive.Include);
            Ore ore = Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);
            Material lockedMaterial = EnsureLockedMaterial();

            if (playerLevel == null)
            {
                Debug.LogWarning("[关卡向导] 找不到 PlayerLevel，关卡解锁状态在游戏里不会随建筑数变化。");
            }

            if (oreSpawner == null)
            {
                Debug.LogWarning("[关卡向导] 找不到 OreSpawner，退出关卡时聚合体刷新不会暂停。");
            }

            if (ore == null)
            {
                Debug.LogWarning("[关卡向导] 找不到 Ore，关卡内容根会留空，" +
                                 "粒子 / 动画暂停退化为运行时自动查找。");
            }

            LevelManager manager = areaController.GetComponent<LevelManager>();

            if (manager == null)
            {
                manager = Undo.AddComponent<LevelManager>(areaController.gameObject);
            }

            LevelEntrance levelEntrance = miningEntrance.GetComponent<LevelEntrance>();
            bool levelEntranceWasNew = levelEntrance == null;

            if (levelEntranceWasNew)
            {
                levelEntrance = Undo.AddComponent<LevelEntrance>(miningEntrance.gameObject);
            }

            Undo.RecordObject(levelEntrance, "Configure Level Entrance");

            if (levelEntranceWasNew)
            {
                levelEntrance.ConfigureForSetup(
                    1, 1, new Vector2Int(4, 4),
                    miningEntrance.transform,
                    ore != null ? ore.transform : null,
                    miningEntrance.transform,
                    lockedMaterial,
                    3, null,
                    40f, 80, 0.5f, 1.6f, OreType.HuaGangYan, 30f);
            }
            else
            {
                // 重跑向导不覆盖用户在 Inspector 里调过的解锁等级 / 占地 / 内容根，
                // 只补上缺失的灰化材质
                levelEntrance.EnsureLockedMaterialForSetup(lockedMaterial);
            }

            EditorUtility.SetDirty(levelEntrance);

            LevelEntrance[] entrances = Object.FindObjectsByType<LevelEntrance>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Undo.RecordObject(manager, "Configure Level Manager");
            manager.ConfigureForSetup(
                playerLevel,
                areaController,
                oreSpawner,
                entrances,
                levelEntrance,
                ore != null ? ore.transform : null);
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[关卡向导] 配置完成：关卡入口「{miningEntrance.name}」解锁等级 1，" +
                      $"占用 {levelEntrance.FootprintCells.x}×{levelEntrance.FootprintCells.y} 格，" +
                      $"波次 {levelEntrance.WaveCount}（一个聚合体一波），" +
                      $"BOSS {levelEntrance.BossMaxHealth:0.##} HP / 倒计时 {levelEntrance.BossCountdownSeconds:0.#} 秒，" +
                      $"灰化材质 {(lockedMaterial != null ? LockedMaterialPath : "未生成")}，场景已保存。");
        }

        private static MiningEntrance FindMiningEntrance()
        {
            MiningEntrance[] entrances = Object.FindObjectsByType<MiningEntrance>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < entrances.Length; i++)
            {
                if (entrances[i] != null && entrances[i].name == EntranceObjectName)
                {
                    return entrances[i];
                }
            }

            return entrances.Length > 0 ? entrances[0] : null;
        }

        /// <summary>
        /// 批处理时没有打开场景，先打开主场景再找；
        /// 编辑器里当前场景没有 GameAreaController 时也回退到主场景。
        /// </summary>
        private static Scene EnsureSceneOpen()
        {
            Scene active = EditorSceneManager.GetActiveScene();

            if (Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include) != null)
            {
                return active;
            }

            if (!File.Exists(SampleScenePath))
            {
                return active;
            }

            if (Application.isBatchMode || string.IsNullOrEmpty(active.path))
            {
                return EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            return active;
        }

        /// <summary>生成 / 复用未解锁灰化材质。材质资产保证打包时 Shader 不会被剥离。</summary>
        private static Material EnsureLockedMaterial()
        {
            Shader shader = Shader.Find(LockedShaderName);

            if (shader == null)
            {
                Debug.LogError($"[关卡向导] 找不到 Shader「{LockedShaderName}」。" +
                               "请确认 Assets/Shaders/LevelBlockShader.shader 已导入（切回 Unity 或按 Ctrl+R）。");
                return null;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(LockedMaterialPath);

            if (existing != null)
            {
                if (existing.shader != shader)
                {
                    existing.shader = shader;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }

                return existing;
            }

            if (!AssetDatabase.IsValidFolder(LevelFolder))
            {
                AssetDatabase.CreateFolder(MaterialsFolder, "Level");
            }

            Material material = new Material(shader)
            {
                name = "Level_Block",
            };
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_GrayTint", new Color(0.82f, 0.84f, 0.86f, 1f));
            material.SetFloat("_GrayStrength", 1f);
            material.SetFloat("_Brightness", 1.15f);

            AssetDatabase.CreateAsset(material, LockedMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
