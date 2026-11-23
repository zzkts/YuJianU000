using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 一次性自动修复：把建筑网格的单格边长从 2 改成 10 世界单位。
    ///
    /// 2026-11-20 用户定义：「以 10ppu 为一单位面积长度」→ 一单位面积长度 = 10 世界单位。
    /// 需要同时处理三件事：
    ///   1. BuildingGrid.cellSize 2 → 10，cellCounts 按同样的世界覆盖范围重算（44×44 → 9×9）；
    ///   2. 手绘可建面积掩码按新旧网格重采样（保留大致的可建形状，44×44 bool → 8×8 bool）；
    ///   3. 保存场景。
    ///
    /// 只跑一次（EditorPrefs）；手动入口：屿见/建筑/应用 10 单位网格。
    /// </summary>
    [InitializeOnLoad]
    internal static class BuildingGridUnitAutoFix
    {
        private const string PrefKey = "Yujian.BuildingGridUnitFix.v1";
        private const float NewCellSize = 10f;

        static BuildingGridUnitAutoFix()
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

        [MenuItem("屿见/建筑/应用 10 单位网格", false, 519)]
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

            BuildingGrid grid = Object.FindFirstObjectByType<BuildingGrid>(FindObjectsInactive.Include);

            if (grid == null)
            {
                Debug.LogWarning("[BuildingGridUnit] 当前场景里找不到 BuildingGrid，跳过；" +
                                 "可用菜单「屿见/建筑/应用 10 单位网格」重试。");
                return;
            }

            if (Mathf.Approximately(grid.CellSize, NewCellSize))
            {
                EditorPrefs.SetBool(PrefKey, true);
                return;
            }

            float oldSize = grid.CellSize;
            Vector2Int oldCounts = grid.CellCounts;
            Vector3 center = grid.GridCenter;

            // ceil：保持旧网格的世界覆盖范围，宁可多一格也不要缩水。
            Vector2Int newCounts = new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(oldCounts.x * oldSize / NewCellSize - 0.0001f)),
                Mathf.Max(1, Mathf.CeilToInt(oldCounts.y * oldSize / NewCellSize - 0.0001f)));

            // 先改网格签名：掩码无效需要按台地烘焙时，BakeProceduralMaskInto 读的是新网格
            SerializedObject serialized = new SerializedObject(grid);
            SerializedProperty cellSize = serialized.FindProperty("cellSize");
            SerializedProperty counts = serialized.FindProperty("cellCounts");

            if (cellSize != null)
            {
                cellSize.floatValue = NewCellSize;
            }

            if (counts != null)
            {
                counts.vector2IntValue = newCounts;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            ResampleAreaMask(grid, center, NewCellSize, newCounts);

            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            EditorSceneManager.SaveScene(grid.gameObject.scene);
            AssetDatabase.SaveAssets();

            EditorPrefs.SetBool(PrefKey, true);

            Debug.Log($"[BuildingGridUnit] 完成：单格 {oldSize:0.##} → {NewCellSize:0.##}，" +
                      $"格数 {oldCounts.x}×{oldCounts.y} → {newCounts.x}×{newCounts.y}，" +
                      $"世界覆盖 {oldCounts.x * oldSize:0.##}×{oldCounts.y * oldSize:0.##} → " +
                      $"{newCounts.x * NewCellSize:0.##}×{newCounts.y * NewCellSize:0.##}。");
        }

        /// <summary>把旧的每格 bool 掩码按世界坐标重采样到新网格（多数格可建才算可建）。</summary>
        private static void ResampleAreaMask(BuildingGrid grid, Vector3 center, float newSize, Vector2Int newCounts)
        {
            BuildableAreaMask mask = grid.AreaMask;

            if (mask == null || !grid.IsAreaMaskActive)
            {
                return;
            }

            bool oldValid = mask.IsValid;
            Vector3 oldCenter = mask.GridCenter;
            float oldSize = mask.CellSize;
            Vector2Int oldCounts = mask.CellCounts;

            bool[,] sampled = new bool[newCounts.x, newCounts.y];

            if (oldValid)
            {
                Vector3 oldOrigin = oldCenter - new Vector3(oldCounts.x * oldSize * 0.5f, 0f, oldCounts.y * oldSize * 0.5f);
                Vector3 newOrigin = center - new Vector3(newCounts.x * newSize * 0.5f, 0f, newCounts.y * newSize * 0.5f);

                for (int ny = 0; ny < newCounts.y; ny++)
                {
                    float minZ = newOrigin.z + ny * newSize;
                    float maxZ = minZ + newSize;

                    for (int nx = 0; nx < newCounts.x; nx++)
                    {
                        float minX = newOrigin.x + nx * newSize;
                        float maxX = minX + newSize;

                        int buildable = 0;
                        int total = 0;

                        // 收集中心落在新格世界矩形里的旧格
                        int oldYMin = Mathf.Max(0, Mathf.FloorToInt((minZ - oldOrigin.z) / oldSize));
                        int oldYMax = Mathf.Min(oldCounts.y - 1, Mathf.FloorToInt((maxZ - oldOrigin.z) / oldSize));
                        int oldXMin = Mathf.Max(0, Mathf.FloorToInt((minX - oldOrigin.x) / oldSize));
                        int oldXMax = Mathf.Min(oldCounts.x - 1, Mathf.FloorToInt((maxX - oldOrigin.x) / oldSize));

                        for (int oy = oldYMin; oy <= oldYMax; oy++)
                        {
                            float cz = oldOrigin.z + (oy + 0.5f) * oldSize;

                            if (cz < minZ || cz >= maxZ)
                            {
                                continue;
                            }

                            for (int ox = oldXMin; ox <= oldXMax; ox++)
                            {
                                float cx = oldOrigin.x + (ox + 0.5f) * oldSize;

                                if (cx < minX || cx >= maxX)
                                {
                                    continue;
                                }

                                total++;

                                if (mask.GetCell(ox, oy))
                                {
                                    buildable++;
                                }
                            }
                        }

                        sampled[nx, ny] = total > 0 && buildable * 2 >= total;
                    }
                }
            }

            mask.Initialize(center, newSize, newCounts, false);

            if (oldValid)
            {
                for (int ny = 0; ny < newCounts.y; ny++)
                {
                    for (int nx = 0; nx < newCounts.x; nx++)
                    {
                        mask.SetCell(nx, ny, sampled[nx, ny]);
                    }
                }
            }
            else
            {
                // 旧掩码本身无效：按海岛台地重新烘焙一份
                grid.BakeProceduralMaskInto(mask);
            }

            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildingGridUnit] 可建面积掩码已重采样：" +
                      $"{oldCounts.x}×{oldCounts.y} @ {oldSize:0.##} → {newCounts.x}×{newCounts.y} @ {newSize:0.##}。");
        }
    }
}
