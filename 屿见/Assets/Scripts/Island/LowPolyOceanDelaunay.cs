using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Island
{
    /// <summary>
    /// 轻量 Bowyer-Watson Delaunay 三角化，只服务于编辑器里的海面网格生成。
    /// 输入点数量控制在几千级；返回扁平索引列表，每 3 个一组。
    /// </summary>
    public static class LowPolyOceanDelaunay
    {
        private struct Triangle
        {
            public int A;
            public int B;
            public int C;
        }

        public static List<int> Triangulate(IList<Vector2> points)
        {
            List<int> result = new List<int>();
            int pointCount = points == null ? 0 : points.Count;
            if (pointCount < 3)
            {
                return result;
            }

            List<Vector2> allPoints = new List<Vector2>(points);
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            for (int i = 0; i < pointCount; i++)
            {
                Vector2 p = points[i];
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.y);
                maxZ = Mathf.Max(maxZ, p.y);
            }

            float delta = Mathf.Max(maxX - minX, maxZ - minZ) * 20f + 10f;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            int superA = pointCount;
            int superB = pointCount + 1;
            int superC = pointCount + 2;
            allPoints.Add(new Vector2(centerX - 20f * delta, centerZ - delta));
            allPoints.Add(new Vector2(centerX, centerZ + 20f * delta));
            allPoints.Add(new Vector2(centerX + 20f * delta, centerZ - delta));

            List<Triangle> triangles = new List<Triangle>
            {
                new Triangle { A = superA, B = superB, C = superC },
            };
            List<int> badTriangles = new List<int>();
            Dictionary<long, int> edgeCounts = new Dictionary<long, int>();

            for (int point = 0; point < pointCount; point++)
            {
                Vector2 p = allPoints[point];
                badTriangles.Clear();
                for (int t = 0; t < triangles.Count; t++)
                {
                    Triangle triangle = triangles[t];
                    if (InCircumcircle(p,
                        allPoints[triangle.A],
                        allPoints[triangle.B],
                        allPoints[triangle.C]))
                    {
                        badTriangles.Add(t);
                    }
                }

                edgeCounts.Clear();
                for (int i = 0; i < badTriangles.Count; i++)
                {
                    Triangle triangle = triangles[badTriangles[i]];
                    AddEdge(edgeCounts, triangle.A, triangle.B);
                    AddEdge(edgeCounts, triangle.B, triangle.C);
                    AddEdge(edgeCounts, triangle.C, triangle.A);
                }

                for (int i = badTriangles.Count - 1; i >= 0; i--)
                {
                    triangles.RemoveAt(badTriangles[i]);
                }

                foreach (KeyValuePair<long, int> pair in edgeCounts)
                {
                    if (pair.Value != 1)
                    {
                        continue;
                    }

                    int a = (int)(pair.Key >> 32);
                    int b = (int)(pair.Key & 0xffffffffL);
                    triangles.Add(new Triangle { A = a, B = b, C = point });
                }
            }

            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle triangle = triangles[i];
                if (triangle.A >= pointCount
                    || triangle.B >= pointCount
                    || triangle.C >= pointCount)
                {
                    continue;
                }

                result.Add(triangle.A);
                result.Add(triangle.B);
                result.Add(triangle.C);
            }

            return result;
        }

        private static void AddEdge(Dictionary<long, int> edges, int a, int b)
        {
            long key = a < b
                ? ((long)a << 32) | (uint)b
                : ((long)b << 32) | (uint)a;
            int count;
            edges.TryGetValue(key, out count);
            edges[key] = count + 1;
        }

        private static bool InCircumcircle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            double ax = a.x;
            double ay = a.y;
            double bx = b.x;
            double by = b.y;
            double cx = c.x;
            double cy = c.y;
            double px = p.x;
            double py = p.y;

            double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (System.Math.Abs(d) < 1e-12)
            {
                return false;
            }

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            double radius2 = (ux - ax) * (ux - ax) + (uy - ay) * (uy - ay);
            double distance2 = (px - ux) * (px - ux) + (py - uy) * (py - uy);
            return distance2 < radius2 - 1e-7;
        }
    }
}
