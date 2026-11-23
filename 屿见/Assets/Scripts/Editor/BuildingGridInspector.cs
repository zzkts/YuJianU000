using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;

namespace Yujian.EditorTools
{
    /// <summary>
    /// <see cref="BuildingGrid"/> 的自定义 Inspector。
    ///
    /// 主要目的：让「网格长宽」变成可自己决定的参数——
    ///   · 「网格长宽（格）」始终可编辑，填多少就是多少；
    ///   · 自动适配开启时字段显示按海岛算出的值，手改任意一格就自动关闭自动适配；
    ///   · 实时显示换算后的世界长宽，避免格数 / 世界单位混淆；
    ///   · 改了格数后自动 Rebuild，并提示手绘掩码签名需要重建。
    ///
    /// 纯编辑器 UI：判定逻辑仍然全部在 BuildingGrid 内。
    /// </summary>
    [CustomEditor(typeof(BuildingGrid))]
    public class BuildingGridInspector : Editor
    {
        private SerializedProperty islandDefinition;
        private SerializedProperty islandSettings;
        private SerializedProperty gridCenter;
        private SerializedProperty cellSize;
        private SerializedProperty cellCounts;
        private SerializedProperty autoFitToIsland;
        private SerializedProperty buildableInset;
        private SerializedProperty areaMask;
        private SerializedProperty useAreaMask;
        private SerializedProperty logGridFlow;

        private void OnEnable()
        {
            islandDefinition = serializedObject.FindProperty("islandDefinition");
            islandSettings = serializedObject.FindProperty("islandSettings");
            gridCenter = serializedObject.FindProperty("gridCenter");
            cellSize = serializedObject.FindProperty("cellSize");
            cellCounts = serializedObject.FindProperty("cellCounts");
            autoFitToIsland = serializedObject.FindProperty("autoFitToIsland");
            buildableInset = serializedObject.FindProperty("buildableInset");
            areaMask = serializedObject.FindProperty("areaMask");
            useAreaMask = serializedObject.FindProperty("useAreaMask");
            logGridFlow = serializedObject.FindProperty("logGridFlow");
        }

        public override void OnInspectorGUI()
        {
            BuildingGrid grid = (BuildingGrid)target;

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            bool autoFit = autoFitToIsland.boolValue;
            bool rebuildRequested = false;

            EditorGUILayout.LabelField("依赖", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(islandDefinition);
            EditorGUILayout.PropertyField(islandSettings);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("网格", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(autoFitToIsland);

            using (new EditorGUI.DisabledScope(autoFit))
            {
                EditorGUILayout.PropertyField(gridCenter);

                if (autoFit)
                {
                    EditorGUILayout.HelpBox(
                        "勾选了「自动适配海岛」：网格中心由海岛可建台地中心决定，关掉后可以手填。",
                        MessageType.None);
                }
            }

            EditorGUILayout.PropertyField(cellSize);

            if (cellSize.floatValue < 0.01f)
            {
                cellSize.floatValue = 0.01f;
            }

            // 「网格长宽」始终可直接编辑。自动适配开启时字段里显示的是按海岛算出的值；
            // 一旦手改，就自动关掉自动适配，保证填进去的数字不会被下一帧重算覆盖。
            Vector2Int serializedCounts = cellCounts.vector2IntValue;
            Vector2Int shownCounts = autoFit ? grid.ComputeIslandFitCounts() : serializedCounts;

            Vector2Int editedCounts = EditorGUILayout.Vector2IntField("网格长宽（格）", shownCounts);
            editedCounts.x = Mathf.Max(1, editedCounts.x);
            editedCounts.y = Mathf.Max(1, editedCounts.y);

            bool switchedToManual = false;

            if (editedCounts != shownCounts)
            {
                cellCounts.vector2IntValue = editedCounts;

                if (autoFit)
                {
                    autoFitToIsland.boolValue = false;
                    autoFit = false;
                    switchedToManual = true;
                }
            }

            float size = cellSize.floatValue;
            EditorGUILayout.LabelField(
                "世界长宽",
                $"{editedCounts.x * size:0.##} × {editedCounts.y * size:0.##}（X × Z）");

            if (autoFit)
            {
                EditorGUILayout.HelpBox(
                    $"当前是自动适配：{shownCounts.x}×{shownCounts.y} 格" +
                    $"（海岛可建范围 ÷ 单格，按 ceil 取整）。\n" +
                    "想自己决定长宽，直接在上面填数字即可，会自动关闭自动适配。",
                    MessageType.Info);
            }
            else if (switchedToManual)
            {
                EditorGUILayout.HelpBox(
                    "已自动关闭「自动适配海岛」：网格长宽现在以你填的格数为准。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "网格长宽完全由这两个值决定，网格以「网格中心」为中心对称展开。\n" +
                    "改完后如果用了手绘可建面积掩码，需要在画笔里点「按当前网格重建」，否则判定会暂时回退到海岛台地。",
                    MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(autoFit ? "重新适配海岛" : "改为自动适配海岛"))
                {
                    autoFitToIsland.boolValue = true;
                    rebuildRequested = true;
                }

                if (GUILayout.Button("用当前参数重建"))
                {
                    rebuildRequested = true;
                }
            }

            EditorGUILayout.PropertyField(buildableInset);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("可建面积（手绘）", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(areaMask);
            EditorGUILayout.PropertyField(useAreaMask);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("调试", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(logGridFlow);

            bool changed = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();

            if ((changed || rebuildRequested) && !Application.isPlaying)
            {
                Undo.RecordObject(grid, "调整建筑网格");

                if (rebuildRequested && autoFitToIsland.boolValue)
                {
                    grid.FitToIsland();
                }
                else
                {
                    grid.Rebuild();
                }

                EditorUtility.SetDirty(grid);

                if (grid.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
                }

                SceneView.RepaintAll();
                serializedObject.Update();
                Repaint();
            }

            if (useAreaMask.boolValue && areaMask.objectReferenceValue != null)
            {
                if (!grid.HasCompatibleAreaMask)
                {
                    EditorGUILayout.HelpBox(
                        "手绘掩码的签名和当前网格不一致（格数 / 单格 / 中心变了）。" +
                        "打开画笔点「按当前网格重建」，不然会回退成海岛台地判定。",
                        MessageType.Warning);

                    if (GUILayout.Button("打开可建面积画笔…"))
                    {
                        BuildingAreaPainterWindow.Open();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"掩码签名匹配（{grid.CellCounts.x}×{grid.CellCounts.y} @ {grid.CellSize:0.##}）。",
                        MessageType.None);
                }
            }
        }
    }
}
