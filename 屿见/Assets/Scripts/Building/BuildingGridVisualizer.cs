using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑网格的可视化。职责：
    /// 1. 进入建造模式时，把可建区域画成一整片格线；
    /// 2. 蓝图移动时，把当前占用的格子用绿/红半透明高亮。
    ///
    /// 纯视图：不读输入、不判断能不能放、不碰蓝图与建筑。所有判定都来自 BuildingGrid。
    ///
    /// 网格线用程序化 Mesh 一次生成（不是上百个 LineRenderer）：
    /// 相邻格共享的边只画一次，整片网格一个 Mesh、一个材质、一个 Draw Call。
    /// </summary>
    public class BuildingGridVisualizer : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑网格。格线范围、可建区域与格子坐标都从它读取。留空会报错")]
        [SerializeField] private BuildingGrid grid;

        [Tooltip("建筑系统入口。只用来判断当前是否处于放置模式，没有蓝图时隐藏格线")]
        [SerializeField] private BuildingManager buildingManager;

        [Header("材质")]
        [Tooltip("格线材质。建议用半透明 URP 材质，颜色浅、不要抢蓝图的视线")]
        [SerializeField] private Material lineMaterial;

        [Tooltip("当前占地合法时的材质。可直接用 Assets/Materials/Valid.mat")]
        [SerializeField] private Material validMaterial;

        [Tooltip("当前占地非法时的材质。可直接用 Assets/Materials/InValid.mat")]
        [SerializeField] private Material invalidMaterial;

        [Header("渲染对象（由向导创建）")]
        [SerializeField] private MeshFilter gridMeshFilter;
        [SerializeField] private MeshRenderer gridMeshRenderer;
        [SerializeField] private MeshFilter highlightMeshFilter;
        [SerializeField] private MeshRenderer highlightMeshRenderer;

        [Header("外观")]
        [Tooltip("格线宽度（世界单位）")]
        [SerializeField] private float lineWidth = 0.03f;

        [Tooltip("格线离地面的高度，避免与地面 Z-fighting")]
        [SerializeField] private float lineYOffset = 0.01f;

        [Tooltip("占地高亮相对格子边缘内缩的距离，让相邻格子之间有细缝")]
        [SerializeField] private float highlightInset = 0.04f;

        [Tooltip("占地高亮离地面的高度。应略高于格线")]
        [SerializeField] private float highlightYOffset = 0.03f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印格线生成日志")]
        [SerializeField] private bool logVisualizerFlow = true;

        private Mesh gridMesh;
        private Mesh highlightMesh;

        private RectInt lastPreviewRect;
        private bool lastPreviewValid;
        private bool previewVisible;

        private bool warnedAboutMissingMaterials;
        private bool warnedAboutMissingRenderers;

        private void Awake()
        {
            if (grid == null)
            {
                Debug.LogError("[BuildingGridVisualizer] BuildingGrid 未配置，格线无法生成。", this);
            }

            if (buildingManager == null)
            {
                Debug.LogWarning("[BuildingGridVisualizer] BuildingManager 未配置，" +
                                 "格线会一直显示，不会随蓝图开关。", this);
            }

            if (gridMeshFilter == null || gridMeshRenderer == null ||
                highlightMeshFilter == null || highlightMeshRenderer == null)
            {
                if (!warnedAboutMissingRenderers)
                {
                    warnedAboutMissingRenderers = true;
                    Debug.LogError("[BuildingGridVisualizer] Grid Mesh / Grid Highlight 渲染对象未配置，" +
                                   "网格可视化不会工作。请重新运行菜单「屿见/建筑/配置建筑网格与建造相机（阶段 7）」。", this);
                }
            }
        }

        private void Start()
        {
            RebuildGridMesh();
        }

        private void Update()
        {
            if (gridMeshRenderer == null)
            {
                return;
            }

            bool shouldShow = grid != null && grid.IsConfigured &&
                              buildingManager != null && buildingManager.IsPlacing;

            if (gridMeshRenderer.enabled != shouldShow)
            {
                gridMeshRenderer.enabled = shouldShow;
            }

            if (!shouldShow)
            {
                HidePreview();
            }
        }

        /// <summary>
        /// 重新生成可建区域的格线。只在启动、改参数或右键菜单时调用，不是每帧。
        /// </summary>
        [ContextMenu("重建网格可视化")]
        public void RebuildGridMesh()
        {
            if (grid == null || !grid.IsConfigured)
            {
                return;
            }

            if (gridMeshFilter == null || gridMeshRenderer == null)
            {
                return;
            }

            if (lineMaterial == null)
            {
                if (!warnedAboutMissingMaterials)
                {
                    warnedAboutMissingMaterials = true;
                    Debug.LogError("[BuildingGridVisualizer] 格线材质未配置，网格不可见。", this);
                }

                return;
            }

            Vector2Int counts = grid.CellCounts;
            float cellSize = grid.CellSize;
            float y = grid.GridPlaneY + lineYOffset;

            List<Vector3> vertices = new List<Vector3>();
            List<int> indices = new List<int>();
            HashSet<long> drawnEdges = new HashSet<long>();

            for (int z = 0; z < counts.y; z++)
            {
                for (int x = 0; x < counts.x; x++)
                {
                    if (!grid.IsCellBuildable(new Vector2Int(x, z)))
                    {
                        continue;
                    }

                    Vector3 min = grid.CellMinCorner(new Vector2Int(x, z));
                    min.y = y;

                    Vector3 p00 = min;
                    Vector3 p10 = new Vector3(min.x + cellSize, y, min.z);
                    Vector3 p01 = new Vector3(min.x, y, min.z + cellSize);
                    Vector3 p11 = new Vector3(min.x + cellSize, y, min.z + cellSize);

                    AddEdge(p00, p10, x, z, 0, drawnEdges, vertices, indices);
                    AddEdge(p00, p01, x, z, 1, drawnEdges, vertices, indices);
                    AddEdge(p10, p11, x + 1, z, 1, drawnEdges, vertices, indices);
                    AddEdge(p01, p11, x, z + 1, 0, drawnEdges, vertices, indices);
                }
            }

            if (vertices.Count == 0)
            {
                Debug.LogWarning("[BuildingGridVisualizer] 没有任何可建格子，格线为空。" +
                                 "请检查 BuildingGrid 的 IslandSettings 与可建判定。", this);
                return;
            }

            ConvertToLocal(vertices, gridMeshFilter.transform);

            if (gridMesh == null)
            {
                gridMesh = new Mesh { name = "BuildingGridMesh" };
                gridMesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                gridMesh.Clear();
            }

            gridMesh.SetVertices(vertices);
            gridMesh.SetNormals(BuildUpNormals(vertices.Count));
            gridMesh.SetTriangles(indices, 0);
            gridMesh.RecalculateBounds();

            gridMeshFilter.sharedMesh = gridMesh;
            gridMeshRenderer.sharedMaterial = lineMaterial;
            gridMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            gridMeshRenderer.receiveShadows = false;

            if (logVisualizerFlow)
            {
                Debug.Log($"[BuildingGridVisualizer] 格线已生成：{counts.x}×{counts.y} 格，" +
                          $"{drawnEdges.Count} 条边，{vertices.Count} 顶点。");
            }
        }

        /// <summary>
        /// 显示当前占地高亮。rect 与 valid 都没变时直接返回，避免每帧重建小网格。
        /// </summary>
        public void ShowPreview(RectInt cellRect, bool valid)
        {
            if (grid == null || highlightMeshFilter == null || highlightMeshRenderer == null)
            {
                return;
            }

            if (previewVisible && lastPreviewValid == valid && lastPreviewRect.Equals(cellRect))
            {
                return;
            }

            Material material = valid ? validMaterial : invalidMaterial;

            if (material == null)
            {
                if (!warnedAboutMissingMaterials)
                {
                    warnedAboutMissingMaterials = true;
                    Debug.LogError("[BuildingGridVisualizer] Valid/Invalid 高亮材质未配置，" +
                                   "占地高亮不可见。", this);
                }

                HidePreview();
                return;
            }

            List<Vector3> vertices = BuildHighlightVertices(cellRect);

            if (vertices.Count == 0)
            {
                HidePreview();
                return;
            }

            ConvertToLocal(vertices, highlightMeshFilter.transform);

            if (highlightMesh == null)
            {
                highlightMesh = new Mesh { name = "BuildingGridHighlight" };
                highlightMesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                highlightMesh.Clear();
            }

            List<int> indices = BuildQuadIndices(vertices.Count);

            highlightMesh.SetVertices(vertices);
            highlightMesh.SetNormals(BuildUpNormals(vertices.Count));
            highlightMesh.SetTriangles(indices, 0);
            highlightMesh.RecalculateBounds();

            highlightMeshFilter.sharedMesh = highlightMesh;
            highlightMeshRenderer.sharedMaterial = material;
            highlightMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            highlightMeshRenderer.receiveShadows = false;
            highlightMeshRenderer.enabled = true;

            lastPreviewRect = cellRect;
            lastPreviewValid = valid;
            previewVisible = true;
        }

        /// <summary>隐藏占地高亮。没有高亮时不做任何事。</summary>
        public void HidePreview()
        {
            if (highlightMeshRenderer != null && highlightMeshRenderer.enabled)
            {
                highlightMeshRenderer.enabled = false;
            }

            previewVisible = false;
        }

        private List<Vector3> BuildHighlightVertices(RectInt cellRect)
        {
            List<Vector3> vertices = new List<Vector3>();

            float size = grid.CellSize;
            float inset = Mathf.Clamp(highlightInset, 0f, size * 0.5f - 0.001f);
            float y = grid.GridPlaneY + highlightYOffset;

            for (int z = cellRect.yMin; z < cellRect.yMax; z++)
            {
                for (int x = cellRect.xMin; x < cellRect.xMax; x++)
                {
                    Vector2Int cell = new Vector2Int(x, z);

                    // 只画落在网格内的格子；越界部分由蓝图变红表达
                    if (!grid.IsCellInsideGrid(cell))
                    {
                        continue;
                    }

                    Vector3 min = grid.CellMinCorner(cell);
                    float x0 = min.x + inset;
                    float x1 = min.x + size - inset;
                    float z0 = min.z + inset;
                    float z1 = min.z + size - inset;

                    // 正面朝上：顺序 0,2,1 / 0,3,2（见 BuildQuadIndices）
                    vertices.Add(new Vector3(x0, y, z0));
                    vertices.Add(new Vector3(x0, y, z1));
                    vertices.Add(new Vector3(x1, y, z1));
                    vertices.Add(new Vector3(x1, y, z0));
                }
            }

            return vertices;
        }

        /// <summary>
        /// 所有顶点法线朝上。URP Lit 需要法线参与光照；
        /// 不提供法线时 Unity 会按零向量处理，半透明面会渲染成黑色。
        /// </summary>
        private static List<Vector3> BuildUpNormals(int vertexCount)
        {
            List<Vector3> normals = new List<Vector3>(vertexCount);

            for (int i = 0; i < vertexCount; i++)
            {
                normals.Add(Vector3.up);
            }

            return normals;
        }

        private static List<int> BuildQuadIndices(int vertexCount)
        {
            List<int> indices = new List<int>(vertexCount / 4 * 6);

            for (int i = 0; i < vertexCount; i += 4)
            {
                // 顶点顺序：0 = min/min，1 = min/max，2 = max/max，3 = max/min
                // 从上方看是逆时针，法线朝 +Y
                indices.Add(i);
                indices.Add(i + 2);
                indices.Add(i + 1);

                indices.Add(i);
                indices.Add(i + 3);
                indices.Add(i + 2);
            }

            return indices;
        }

        /// <summary>
        /// 为一条格线生成一个细长四边形。相邻格共享的边用 HashSet 去重，只画一次。
        /// orientation：0 = 沿 X，1 = 沿 Z。
        /// </summary>
        private void AddEdge(
            Vector3 start, Vector3 end, int cornerX, int cornerZ, int orientation,
            HashSet<long> drawnEdges, List<Vector3> vertices, List<int> indices)
        {
            long key = ((long)cornerX << 33) ^ ((long)cornerZ << 1) ^ (long)orientation;

            if (!drawnEdges.Add(key))
            {
                return;
            }

            Vector3 direction = end - start;
            Vector3 perpendicular = Vector3.Cross(Vector3.up, direction).normalized
                                    * (lineWidth * 0.5f);

            int baseIndex = vertices.Count;
            vertices.Add(start - perpendicular);
            vertices.Add(start + perpendicular);
            vertices.Add(end + perpendicular);
            vertices.Add(end - perpendicular);

            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);

            indices.Add(baseIndex);
            indices.Add(baseIndex + 3);
            indices.Add(baseIndex + 2);
        }

        /// <summary>
        /// 把世界坐标顶点转换到渲染对象本地空间，这样 BuildingSystem 移动时网格仍然贴合地面。
        /// </summary>
        private static void ConvertToLocal(List<Vector3> vertices, Transform target)
        {
            if (target == null)
            {
                return;
            }

            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = target.InverseTransformPoint(vertices[i]);
            }
        }

        private void OnDestroy()
        {
            DestroyMesh(gridMesh);
            DestroyMesh(highlightMesh);
            gridMesh = null;
            highlightMesh = null;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }
    }
}
