using UnityEngine;

namespace Yujian.Island
{
    /// <summary>
    /// 挂在海面物体上的低模海面状态。由编辑器工具写入，运行时不做任何事。
    /// 保存原海面的网格 / 材质 / 缩放，确保“还原原海面”在任何时候都能恢复；
    /// 同时记录当前网格参数，窗口重开或重新生成网格时不用手填。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class LowPolyOceanState : MonoBehaviour
    {
        [HideInInspector] public bool originalCaptured;
        [HideInInspector] public Mesh originalMesh;
        [HideInInspector] public Material originalMaterial;
        [HideInInspector] public Vector3 originalScale = Vector3.one;

        [HideInInspector] public Mesh lowPolyMesh;
        [HideInInspector] public Material lowPolyMaterial;

        [HideInInspector] public LowPolyOceanTopology topology = LowPolyOceanTopology.ShaderSmooth;
        [HideInInspector] public float meshSize = 2000f;
        [HideInInspector] public float cellSize = 10f;
        [HideInInspector] public float jitter = 0.32f;
        [HideInInspector] public int seed = 20261114;
        [HideInInspector] public float waveHeight = 2f;
        [HideInInspector] public float shoreFadeDepth = 2.5f;
        [HideInInspector] public Vector2 islandOffset;
        [HideInInspector] public int radialSegments = 256;
        [HideInInspector] public float shoreRingSpacing = 2.5f;
        [HideInInspector] public float shoreInset = 1.5f;
        [HideInInspector] public float refineCellSize = 2.5f;
        [HideInInspector] public float refineHalfSize = 110f;
        [HideInInspector] public float coarseGrowth = 1.28f;
        [HideInInspector] public float delaunayCellSize = 5f;
        [HideInInspector] public float delaunayCoarseCellSize = 100f;
        [HideInInspector] public int preset;
    }
}
