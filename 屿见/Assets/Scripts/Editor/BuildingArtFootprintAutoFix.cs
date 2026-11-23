using UnityEditor;
using UnityEngine;
using Yujian.Building;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 一次性自动修复：让每座建筑的占地格数贴合它自己的模型宽深。
    ///
    /// 2026-11-20 用户要求「调整建筑的面积，让面积贴合现在的模型」：
    ///   · 之前 5 座博物馆统一 6×6 格（12×12 世界），非正方形的模型会被管理器拉变形；
    ///   · 现在按每件预制体 BoxCollider（归一化后的模型包围盒）÷ 单格 2，四舍五入得到各自的格子数：
    ///     例如 12×8.17 → 6×4，8.60×12 → 4×6，12×10.37 → 6×5。
    ///
    /// 本脚本只改 5 份 BuildingData 的 footprintCells，不重新生成预制体。
    /// 条件式执行：格子数已经贴合时什么也不做。
    /// 手动入口：屿见/建筑/让占地贴合模型。
    /// </summary>
    [InitializeOnLoad]
    internal static class BuildingArtFootprintAutoFix
    {
        // 2026-11-20 用户定义：一单位面积长度 = 10 世界单位。
        private const float CellSize = 10f;

        private static readonly string[] DataPaths =
        {
            "Assets/Data/Buildings/Building_风琴博物馆.asset",
            "Assets/Data/Buildings/Building_八卦楼.asset",
            "Assets/Data/Buildings/Building_菽庄花园.asset",
            "Assets/Data/Buildings/Building_日光岩.asset",
            "Assets/Data/Buildings/Building_海天堂构.asset",
            "Assets/Data/Buildings/Building_回收站.asset",
        };

        static BuildingArtFootprintAutoFix()
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

        [MenuItem("屿见/建筑/让占地贴合模型", false, 518)]
        private static void MenuRun()
        {
            TryRun();
        }

        private static void TryRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }

            int changed = 0;

            for (int i = 0; i < DataPaths.Length; i++)
            {
                if (Fix(DataPaths[i]))
                {
                    changed++;
                }
            }

            if (changed == 0)
            {
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildingFootprint] 已按模型实际宽深调整 {changed} 座建筑的占地格数。");
        }

        private static bool Fix(string dataPath)
        {
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(dataPath);

            if (data == null)
            {
                Debug.LogWarning($"[BuildingFootprint] 找不到建筑数据：{dataPath}");
                return false;
            }

            SerializedObject serialized = new SerializedObject(data);
            SerializedProperty prefabProperty = serialized.FindProperty("buildingPrefab");
            SerializedProperty cellsProperty = serialized.FindProperty("footprintCells");

            if (prefabProperty == null || cellsProperty == null)
            {
                return false;
            }

            GameObject prefab = prefabProperty.objectReferenceValue as GameObject;
            BoxCollider collider = prefab != null ? prefab.GetComponent<BoxCollider>() : null;

            if (collider == null)
            {
                Debug.LogWarning($"[BuildingFootprint] 「{data.name}」的预制体上没有 BoxCollider，跳过。");
                return false;
            }

            Vector2Int expected = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(collider.size.x / CellSize)),
                Mathf.Max(1, Mathf.RoundToInt(collider.size.z / CellSize)));

            if (cellsProperty.vector2IntValue == expected)
            {
                return false;
            }

            Debug.Log($"[BuildingFootprint] {data.name}：{cellsProperty.vector2IntValue} → {expected}" +
                      $"（模型 {collider.size.x:0.##}×{collider.size.z:0.##}，单格 {CellSize}）");

            cellsProperty.vector2IntValue = expected;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            return true;
        }
    }
}
