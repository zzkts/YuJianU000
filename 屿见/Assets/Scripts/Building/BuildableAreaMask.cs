using System;
using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 手绘可建面积掩码（2026-11-20）。
    ///
    /// 完全手绘模式的数据源：每格 true = 可建，false = 不可建。
    /// 由 <see cref="BuildingGrid"/> 引用；启用后它取代海岛台地的程序化判定。
    /// 网格签名（中心 / 单格 / 格数）与当前 BuildingGrid 不一致时，BuildingGrid 会回退到台地判定，
    /// 由编辑器工具「可建面积画笔」负责重建。
    ///
    /// 只是数据：不判断能否放置、不画东西、不碰关卡污染区与建筑占用。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/建筑/可建面积掩码", fileName = "BuildableAreaMask")]
    public class BuildableAreaMask : ScriptableObject
    {
        [Header("网格签名（由画笔写入，不要手改）")]
        [SerializeField] private Vector3 gridCenter;
        [SerializeField] private float cellSize = 2f;
        [SerializeField] private Vector2Int cellCounts = new Vector2Int(52, 52);

        [Tooltip("行优先：下标 = x + y * cellCounts.x。true = 可建")]
        [SerializeField] private bool[] cells = new bool[0];

        /// <summary>掩码对应的网格中心。</summary>
        public Vector3 GridCenter => gridCenter;

        /// <summary>掩码对应的单格边长。</summary>
        public float CellSize => cellSize;

        /// <summary>掩码对应的网格宽高格数。</summary>
        public Vector2Int CellCounts => cellCounts;

        /// <summary>总格数（宽 × 高）。</summary>
        public int CellCount => Mathf.Max(0, cellCounts.x) * Mathf.Max(0, cellCounts.y);

        /// <summary>数组长度是否与格数一致。</summary>
        public bool IsValid => cells != null && cells.Length == CellCount && CellCount > 0;

        /// <summary>
        /// 掩码签名是否与给定网格一致。中心允许 0.01 世界单位的误差。
        /// </summary>
        public bool IsCompatible(Vector3 center, float size, Vector2Int counts)
        {
            return cellCounts == counts
                && Mathf.Abs(cellSize - size) <= 0.0001f
                && (gridCenter - center).sqrMagnitude <= 0.0001f;
        }

        /// <summary>
        /// 按当前网格重建掩码。会丢弃旧内容，由调用方先弹确认。
        /// </summary>
        public void Initialize(Vector3 center, float size, Vector2Int counts, bool initialValue)
        {
            gridCenter = center;
            cellSize = Mathf.Max(0.01f, size);
            cellCounts = new Vector2Int(Mathf.Max(1, counts.x), Mathf.Max(1, counts.y));
            cells = new bool[CellCount];
            Fill(initialValue);
        }

        /// <summary>把所有格子设为同一个值。</summary>
        [ContextMenu("全部设为可建")]
        public void FillBuildable()
        {
            Fill(true);
        }

        /// <summary>把所有格子设为不可建。</summary>
        [ContextMenu("全部设为不可建")]
        public void ClearAll()
        {
            Fill(false);
        }

        /// <summary>把所有格子设为同一个值。</summary>
        public void Fill(bool value)
        {
            EnsureSize();

            if (cells == null)
            {
                return;
            }

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = value;
            }
        }

        /// <summary>读取一格。越界返回 false。</summary>
        public bool GetCell(Vector2Int cell)
        {
            return GetCell(cell.x, cell.y);
        }

        /// <summary>读取一格。越界返回 false。</summary>
        public bool GetCell(int x, int y)
        {
            if (!IsInside(x, y) || cells == null)
            {
                return false;
            }

            return cells[x + y * cellCounts.x];
        }

        /// <summary>写入一格。越界忽略。</summary>
        public void SetCell(Vector2Int cell, bool value)
        {
            SetCell(cell.x, cell.y, value);
        }

        /// <summary>写入一格。越界忽略。</summary>
        public void SetCell(int x, int y, bool value)
        {
            if (!IsInside(x, y))
            {
                return;
            }

            EnsureSize();

            if (cells != null)
            {
                cells[x + y * cellCounts.x] = value;
            }
        }

        /// <summary>坐标是否在掩码范围内。</summary>
        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < cellCounts.x && y >= 0 && y < cellCounts.y;
        }

        /// <summary>可建格数。</summary>
        public int CountBuildable()
        {
            if (!IsValid)
            {
                return 0;
            }

            int count = 0;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i])
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>把掩码复制到目标数组。长度不一致返回 false。</summary>
        public bool CopyTo(bool[] target)
        {
            if (!IsValid || target == null || target.Length != cells.Length)
            {
                return false;
            }

            Array.Copy(cells, target, cells.Length);
            return true;
        }

        /// <summary>从源数组覆盖掩码。长度不一致时只复制较小的一段。</summary>
        public void CopyFrom(bool[] source)
        {
            EnsureSize();

            if (source == null || cells == null)
            {
                return;
            }

            Array.Copy(source, cells, Mathf.Min(cells.Length, source.Length));
        }

        /// <summary>数组长度与格数不一致时按格数重建（不保留旧内容）。</summary>
        public void EnsureSize()
        {
            int expected = CellCount;

            if (cells == null || cells.Length != expected)
            {
                cells = new bool[expected];
            }
        }

        private void OnValidate()
        {
            cellSize = Mathf.Max(0.01f, cellSize);
            cellCounts = new Vector2Int(Mathf.Max(1, cellCounts.x), Mathf.Max(1, cellCounts.y));
            EnsureSize();
        }
    }
}
