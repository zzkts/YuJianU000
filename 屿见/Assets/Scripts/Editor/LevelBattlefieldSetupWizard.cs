using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.Core;
using Yujian.Levels;
using Yujian.Lighting;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 关卡系统阶段 4：把共用的战斗舞台和关卡入口收进同一个「关卡」根下。
    ///
    /// 整理后的层级：
    ///   关卡
    ///   ├─ 关卡战场                  LevelBattlefield
    ///   │  ├─ 清洁区灯光             背景 + 三盏灯
    ///   │  ├─ 怪物生成点
    ///   │  │  └─ Garbage_01
    ///   │  └─ 相机机位
    ///   └─ 关卡入口
    ///      ├─ 清洁区入口      Lv1
    ///      ├─ 清洁区入口 (2)  Lv2
    ///      ├─ 清洁区入口 (1)  Lv3
    ///      └─ 清洁区入口 (3)  Lv4
    ///
    /// 所有移动都保留世界坐标；重复运行不会重复建物体、也不会覆盖手动指定的战场。
    /// 入口：菜单栏 → 屿见 → 关卡 → 整理关卡战场与入口（阶段 4）
    /// </summary>
    public static class LevelBattlefieldSetupWizard
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        private const string LevelRootName = "关卡";
        private const string BattlefieldRootName = "关卡战场";
        private const string EntrancesRootName = "关卡入口";
        private const string SpawnPointName = "怪物生成点";
        private const string CameraAnchorName = "相机机位";
        private const string BackgroundObjectHint = "清洁区灯光";

        private const string LevelBattlefieldMenuPath = "屿见/关卡/整理关卡战场与入口（阶段 4）";

        [MenuItem(LevelBattlefieldMenuPath, false, 261)]
        public static void Setup()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("关卡战场", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            Scene scene = EnsureSceneOpen();

            if (!scene.IsValid())
            {
                Debug.LogError("[关卡战场向导] 当前没有打开场景，无法整理关卡层级。");
                return;
            }

            Ore ore = UnityEngine.Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);

            if (ore == null)
            {
                Debug.LogError("[关卡战场向导] 场景里找不到 Garbage_01（Ore 组件），" +
                               "无法创建怪物生成点。");
                return;
            }

            LevelEntrance[] entrances = UnityEngine.Object.FindObjectsByType<LevelEntrance>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (entrances == null || entrances.Length == 0)
            {
                Debug.LogError("[关卡战场向导] 场景里没有 LevelEntrance。" +
                               "请先运行菜单「屿见/关卡/配置关卡入口（阶段 1）」。");
                return;
            }

            MiningLightingController lighting = UnityEngine.Object.FindFirstObjectByType<MiningLightingController>(
                FindObjectsInactive.Include);
            LevelManager manager = UnityEngine.Object.FindFirstObjectByType<LevelManager>(
                FindObjectsInactive.Include);
            AreaCameraDirector cameraDirector = UnityEngine.Object.FindFirstObjectByType<AreaCameraDirector>(
                FindObjectsInactive.Include);

            if (lighting == null)
            {
                Debug.LogWarning($"[关卡战场向导] 找不到「{BackgroundObjectHint}」（MiningLightingController），" +
                                 "战场背景根会留空，背景切换不会生效。");
            }

            if (manager == null)
            {
                Debug.LogWarning("[关卡战场向导] 找不到 LevelManager，默认战场不会写入；" +
                                 "每个入口仍会单独绑定战场，运行时也能工作。");
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("整理关卡战场与入口");

            // 1. 关卡根
            GameObject levelRoot = FindOrCreateRoot(LevelRootName);
            GameObject battlefieldObject = FindOrCreateChild(levelRoot.transform, BattlefieldRootName,
                out _);

            LevelBattlefield battlefield = battlefieldObject.GetComponent<LevelBattlefield>();

            if (battlefield == null)
            {
                battlefield = Undo.AddComponent<LevelBattlefield>(battlefieldObject);
            }

            // 2. 怪物生成点：以矿石当前世界位置创建，再把矿石挂进去（世界坐标不变）
            GameObject spawnPoint = FindOrCreateChild(battlefieldObject.transform, SpawnPointName,
                out bool spawnCreated);

            if (spawnCreated)
            {
                spawnPoint.transform.position = ore.transform.position;
            }

            MoveUnder(ore.transform, spawnPoint.transform, "把怪物挂到生成点");

            // 3. 背景：清洁区灯光整体挂到战场下（世界坐标不变）
            if (lighting != null)
            {
                MoveUnder(lighting.transform, battlefieldObject.transform, "把背景挂到战场");
            }

            // 4. 相机机位：新建时取相机组件里记录的挖矿机位；已有则保留手动摆放的位置
            GameObject cameraAnchor = FindOrCreateChild(battlefieldObject.transform, CameraAnchorName,
                out bool cameraAnchorCreated);

            if (cameraAnchorCreated)
            {
                Vector3 posePosition = new Vector3(-20.93f, 0.82f, -12.9f);
                Vector3 poseEulerAngles = Vector3.zero;

                if (cameraDirector != null)
                {
                    SerializedObject directorSo = new SerializedObject(cameraDirector);
                    SerializedProperty positionProperty = directorSo.FindProperty("miningPosition");
                    SerializedProperty eulerProperty = directorSo.FindProperty("miningEulerAngles");

                    if (positionProperty != null)
                    {
                        posePosition = positionProperty.vector3Value;
                    }

                    if (eulerProperty != null)
                    {
                        poseEulerAngles = eulerProperty.vector3Value;
                    }
                }

                cameraAnchor.transform.position = posePosition;
                cameraAnchor.transform.rotation = Quaternion.Euler(poseEulerAngles);
            }

            // 5. 关卡入口统一收到「关卡入口」下（世界坐标不变）
            GameObject entrancesRoot = FindOrCreateChild(levelRoot.transform, EntrancesRootName, out _);

            for (int i = 0; i < entrances.Length; i++)
            {
                if (entrances[i] != null)
                {
                    MoveUnder(entrances[i].transform, entrancesRoot.transform, "整理关卡入口");
                }
            }

            // 6. 接好战场的三项引用
            Undo.RecordObject(battlefield, "配置关卡战场");
            battlefield.ConfigureForSetup(
                lighting != null ? lighting.transform : null,
                spawnPoint.transform,
                cameraAnchor.transform);
            EditorUtility.SetDirty(battlefield);

            // 7. 入口还没绑定战场时，统一指向这个默认战场
            for (int i = 0; i < entrances.Length; i++)
            {
                if (entrances[i] == null)
                {
                    continue;
                }

                Undo.RecordObject(entrances[i], "绑定关卡战场");
                entrances[i].EnsureBattlefieldForSetup(battlefield);
                EditorUtility.SetDirty(entrances[i]);
            }

            // 8. LevelManager 默认战场
            if (manager != null)
            {
                Undo.RecordObject(manager, "配置默认战场");
                manager.EnsureDefaultBattlefieldForSetup(battlefield);
                EditorUtility.SetDirty(manager);
            }

            // 9. 相机调度接上机位锚点（已经手动接过的不覆盖）
            if (cameraDirector != null)
            {
                SerializedObject directorSo = new SerializedObject(cameraDirector);
                SerializedProperty anchorProperty = directorSo.FindProperty("miningPoseAnchor");

                if (anchorProperty != null && anchorProperty.objectReferenceValue == null)
                {
                    anchorProperty.objectReferenceValue = cameraAnchor.transform;
                    directorSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(cameraDirector);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            LogReport(levelRoot, battlefield, spawnPoint, cameraAnchor, entrances,
                lighting != null ? lighting.name : "未找到", scene);
        }

        private static void LogReport(GameObject levelRoot, LevelBattlefield battlefield,
            GameObject spawnPoint, GameObject cameraAnchor, LevelEntrance[] entrances,
            string backgroundName, Scene scene)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"[关卡战场向导] 配置完成，场景已保存：{scene.path}");
            builder.AppendLine($"  层级：{levelRoot.name}/{battlefield.name}" +
                               $"（背景「{backgroundName}」/ 生成点「{spawnPoint.name}」/ " +
                               $"机位「{cameraAnchor.name}」{FormatPosition(cameraAnchor.transform.position)}）");
            builder.AppendLine($"  入口根：{levelRoot.name}/{EntrancesRootName}，共 {entrances.Length} 个");
            builder.AppendLine("  关卡参数（仍可直接在各入口 Inspector 调整）：");

            List<LevelEntrance> sorted = new List<LevelEntrance>(entrances);
            sorted.Sort((a, b) =>
                (a != null ? a.LevelNumber : 0).CompareTo(b != null ? b.LevelNumber : 0));

            for (int i = 0; i < sorted.Count; i++)
            {
                LevelEntrance entrance = sorted[i];

                if (entrance == null)
                {
                    continue;
                }

                builder.AppendLine(
                    $"    Lv{entrance.LevelNumber}「{entrance.name}」" +
                    $" 解锁 {entrance.UnlockLevel} 级 / {entrance.WaveCount} 波 / " +
                    $"BOSS {entrance.BossMaxHealth:0.##} HP / 奖励 {entrance.BossReward} / " +
                    $"档位 {entrance.DifficultyTier} / 入口{FormatPosition(entrance.transform.position)}");
            }

            Debug.Log(builder.ToString());
        }

        private static string FormatPosition(Vector3 position)
        {
            return $" ({position.x:0.##}, {position.y:0.##}, {position.z:0.##})";
        }

        private static GameObject FindOrCreateRoot(string name)
        {
            Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].parent == null && all[i].name == name)
                {
                    return all[i].gameObject;
                }
            }

            GameObject root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "创建" + name);
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject FindOrCreateChild(Transform parent, string name, out bool created)
        {
            Transform existing = parent.Find(name);

            if (existing != null)
            {
                created = false;
                return existing.gameObject;
            }

            GameObject child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "创建" + name);
            Undo.SetTransformParent(child.transform, parent, false, "创建" + name);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            created = true;
            return child;
        }

        private static void MoveUnder(Transform child, Transform parent, string undoName)
        {
            if (child == null || parent == null || child == parent || child.transform.IsChildOf(parent))
            {
                return;
            }

            // 父物体不能是 child 的后代，否则 Unity 会拒绝换父
            if (parent.IsChildOf(child))
            {
                return;
            }

            Undo.SetTransformParent(child, parent, true, undoName);
        }

        /// <summary>
        /// 批处理时没有打开场景，先打开主场景再找；
        /// 编辑器里当前场景没有 GameAreaController 时也回退到主场景。
        /// </summary>
        private static Scene EnsureSceneOpen()
        {
            Scene active = EditorSceneManager.GetActiveScene();

            if (UnityEngine.Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include) != null)
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
    }
}
