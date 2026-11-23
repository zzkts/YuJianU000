using UnityEditor;
using UnityEngine;
using Yujian.Building;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 一次性自动修复：把 5 座博物馆建筑整体放大 3 倍，并让占地格数与视觉尺寸匹配。
    ///
    /// 2026-11-20 用户要求：
    ///   · 建筑整体 ×3（不是只改 Transform 数字）；
    ///   · 实际大小必须与网格占地匹配 → 占地从 2×2 格改为 6×6 格（世界 12×12）。
    ///
    /// <see cref="BuildingArtSetupWizard"/> 现在按 12×12 归一化模型与 BoxCollider，
    /// 并把 5 份 BuildingData 的 footprintCells 写成 6×6。这个脚本负责在现有工程上跑一次。
    /// 用 EditorPrefs 只跑一次；手动入口：屿见/建筑/应用建筑 3 倍缩放。
    /// </summary>
    [InitializeOnLoad]
    internal static class BuildingArtScaleAutoFix
    {
        private const string PrefKey = "Yujian.BuildingArtScaleFix.v1";
        private const string ProbeData = "Assets/Data/Buildings/Building_风琴博物馆.asset";
        private const string ProbePrefab = "Assets/Prefabs/建筑/建筑_风琴博物馆.prefab";
        private const string ModelNodeName = "模型";
        private const int ExpectedCells = 1;
        private const float ExpectedModelScale = 600f; // 12 归一化后约 630

        static BuildingArtScaleAutoFix()
        {
            EditorApplication.delayCall += TryRun;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += TryRun;
            }
        }

        [MenuItem("屿见/建筑/应用建筑 3 倍缩放", false, 517)]
        private static void MenuRun()
        {
            EditorPrefs.DeleteKey(PrefKey);
            TryRun();
        }

        private static void TryRun()
        {
            if (EditorPrefs.GetBool(PrefKey, false))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }

            if (!NeedsFix())
            {
                EditorPrefs.SetBool(PrefKey, true);
                return;
            }

            EditorPrefs.SetBool(PrefKey, true);
            Debug.Log("[BuildingArtScale] 检测到建筑仍是 2×2 格 / 4×4 尺寸，正在按 3 倍重新生成（6×6 格 / 12×12）...");

            BuildingArtSetupWizard.Configure(false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BuildingArtScale] 完成：5 座博物馆已整体放大 3 倍，占地 6×6。");
        }

        private static bool NeedsFix()
        {
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(ProbeData);

            if (data != null)
            {
                SerializedObject serialized = new SerializedObject(data);
                SerializedProperty cells = serialized.FindProperty("footprintCells");

                if (cells != null && cells.vector2IntValue.x != ExpectedCells)
                {
                    return true;
                }
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ProbePrefab);

            if (root == null)
            {
                return false;
            }

            try
            {
                Transform model = root.transform.Find(ModelNodeName);

                if (model == null)
                {
                    return false;
                }

                return model.localScale.x < ExpectedModelScale;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
