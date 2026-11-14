using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yujian.EditorTools;
using Yujian.Island;

namespace Yujian.Tests
{
    public class LowPolyOceanMeshTests : WhiteBoxTestBase
    {
        private static LowPolyOceanMeshSettings Settings(
            float size = 120f, float cell = 10f, float jitter = 0.3f)
        {
            LowPolyOceanMeshSettings settings = LowPolyOceanMeshSettings.Default;
            // 这些用例验证的是原矩形网格（方案 D）的拓扑。
            settings.topology = LowPolyOceanTopology.ShaderSmooth;
            settings.size = size;
            settings.cellSize = cell;
            settings.jitter = jitter;
            return settings;
        }

        [Test]
        public void Generate_DefaultTopology_UsesSplitTriangleVertices()
        {
            Mesh mesh = Track(LowPolyOceanMeshGenerator.Generate(Settings()));

            const int columns = 12;
            const int expectedTriangles = columns * columns * 2;
            Assert.That(mesh.GetIndexCount(0) / 3u, Is.EqualTo(expectedTriangles));
            Assert.That(mesh.vertexCount, Is.EqualTo(expectedTriangles * 3));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.uv2.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount));
        }

        [Test]
        public void Generate_EachTriangle_HasBarycentricAndSharedFaceColor()
        {
            Mesh mesh = Track(LowPolyOceanMeshGenerator.Generate(Settings()));
            Vector2[] uv2 = mesh.uv2;
            Color[] colors = mesh.colors;

            int triangleCount = mesh.vertexCount / 3;
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int start = triangle * 3;
                Assert.That(uv2[start + 0], Is.EqualTo(new Vector2(1f, 0f)));
                Assert.That(uv2[start + 1], Is.EqualTo(new Vector2(0f, 1f)));
                Assert.That(uv2[start + 2], Is.EqualTo(new Vector2(0f, 0f)));

                Assert.That(colors[start + 0].r, Is.EqualTo(colors[start + 1].r));
                Assert.That(colors[start + 1].r, Is.EqualTo(colors[start + 2].r));
            }
        }

        [Test]
        public void Generate_IsDeterministic()
        {
            LowPolyOceanMeshSettings settings = Settings();
            Mesh first = Track(LowPolyOceanMeshGenerator.Generate(settings));
            Mesh second = Track(LowPolyOceanMeshGenerator.Generate(settings));

            Assert.That(second.vertexCount, Is.EqualTo(first.vertexCount));
            CollectionAssert.AreEqual(first.vertices, second.vertices);
            CollectionAssert.AreEqual(first.uv2, second.uv2);
            CollectionAssert.AreEqual(first.colors, second.colors);
        }

        [Test]
        public void Generate_WithJitter_MovesInteriorVertices()
        {
            Mesh regular = Track(LowPolyOceanMeshGenerator.Generate(
                Settings(jitter: 0f)));
            Mesh jittered = Track(LowPolyOceanMeshGenerator.Generate(
                Settings(jitter: 0.4f)));

            Assert.That(jittered.vertexCount, Is.EqualTo(regular.vertexCount));
            CollectionAssert.AreNotEqual(regular.vertices, jittered.vertices);
        }

        [Test]
        public void Generate_ExtendsBoundsForWaveDisplacement()
        {
            LowPolyOceanMeshSettings settings = Settings(size: 100f, cell: 10f);
            settings.waveHeight = 2.5f;
            Mesh mesh = Track(LowPolyOceanMeshGenerator.Generate(settings));

            Assert.That(mesh.bounds.size.x, Is.GreaterThanOrEqualTo(100f));
            Assert.That(mesh.bounds.size.z, Is.GreaterThanOrEqualTo(100f));
            Assert.That(mesh.bounds.extents.y, Is.GreaterThanOrEqualTo(2.5f));
        }

        [Test]
        public void Generate_SanitizesInvalidSettings()
        {
            LowPolyOceanMeshSettings settings = Settings(size: 0f, cell: 0f, jitter: 9f);
            settings.waveHeight = -5f;

            Mesh mesh = Track(LowPolyOceanMeshGenerator.Generate(settings));
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));

            foreach (Vector3 vertex in mesh.vertices)
            {
                Assert.That(float.IsNaN(vertex.x), Is.False);
                Assert.That(float.IsNaN(vertex.y), Is.False);
                Assert.That(float.IsNaN(vertex.z), Is.False);
            }
        }

        [Test]
        public void CountTriangles_MatchesGeneratedMesh()
        {
            (float size, float cell)[] cases =
            {
                (100f, 10f),
                (120f, 10f),
                (100f, 7f),
                (1200f, 10f),
            };

            foreach ((float size, float cell) in cases)
            {
                Mesh mesh = Track(LowPolyOceanMeshGenerator.Generate(
                    Settings(size: size, cell: cell)));
                Assert.That(LowPolyOceanMeshGenerator.CountTriangles(size, cell),
                    Is.EqualTo((long)(mesh.GetIndexCount(0) / 3u)));
            }
        }

        [Test]
        public void Fill_RejectsTooManyTriangles()
        {
            Mesh mesh = Track(new Mesh());
            LowPolyOceanMeshSettings settings = Settings(size: 4000f, cell: 0.1f);

            Assert.Throws<ArgumentException>(
                () => LowPolyOceanMeshGenerator.Fill(mesh, settings));
        }

        [Test]
        public void Fill_ReusesExistingMeshAsset()
        {
            Mesh mesh = Track(new Mesh());
            LowPolyOceanMeshGenerator.Fill(mesh, Settings());
            int firstCount = mesh.vertexCount;

            LowPolyOceanMeshGenerator.Fill(mesh, Settings(cell: 5f));
            Assert.That(mesh.vertexCount, Is.GreaterThan(firstCount));
        }
    }

    public class LowPolyOceanToolTests : WhiteBoxTestBase
    {
        private Material _originalMaterial;
        private Material _lowPolyMaterial;

        [SetUp]
        public void CreateMaterials()
        {
            Shader shader = Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);
            _originalMaterial = Track(new Material(shader) { name = "Original" });
            _lowPolyMaterial = Track(new Material(shader) { name = "LowPoly" });
        }

        private GameObject CreateOcean(out MeshFilter filter, out MeshRenderer renderer,
            out Mesh originalMesh)
        {
            GameObject ocean = NewGameObject("Ocean");
            filter = ocean.AddComponent<MeshFilter>();
            renderer = ocean.AddComponent<MeshRenderer>();
            originalMesh = Track(new Mesh { name = "OriginalOcean" });
            filter.sharedMesh = originalMesh;
            renderer.sharedMaterial = _originalMaterial;
            ocean.transform.localScale = new Vector3(200f, 1f, 200f);
            return ocean;
        }

        private Mesh CreateLowPolyMesh(float cell = 10f)
        {
            LowPolyOceanMeshSettings settings = LowPolyOceanMeshSettings.Default;
            settings.topology = LowPolyOceanTopology.ShaderSmooth;
            settings.size = 120f;
            settings.cellSize = cell;
            return Track(LowPolyOceanMeshGenerator.Generate(settings));
        }

        [Test]
        public void ApplyToOcean_CapturesOriginalAndSwapsAssets()
        {
            GameObject ocean = CreateOcean(out MeshFilter filter, out MeshRenderer renderer,
                out Mesh originalMesh);
            Mesh lowPolyMesh = CreateLowPolyMesh();
            LowPolyOceanMeshSettings settings = LowPolyOceanMeshSettings.Default;

            LowPolyOceanState state = LowPolyOceanTool.ApplyToOcean(
                ocean, lowPolyMesh, _lowPolyMaterial, settings, 2, false);

            Assert.That(filter.sharedMesh, Is.SameAs(lowPolyMesh));
            Assert.That(renderer.sharedMaterial, Is.SameAs(_lowPolyMaterial));
            Assert.That(ocean.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(state.originalCaptured, Is.True);
            Assert.That(state.originalMesh, Is.SameAs(originalMesh));
            Assert.That(state.originalMaterial, Is.SameAs(_originalMaterial));
            Assert.That(state.originalScale, Is.EqualTo(new Vector3(200f, 1f, 200f)));
            Assert.That(state.preset, Is.EqualTo(2));
        }

        [Test]
        public void ApplyToOcean_Twice_KeepsFirstOriginal()
        {
            GameObject ocean = CreateOcean(out MeshFilter filter, out _,
                out Mesh originalMesh);
            Mesh firstLowPoly = CreateLowPolyMesh();
            Mesh secondLowPoly = CreateLowPolyMesh(5f);
            LowPolyOceanMeshSettings settings = LowPolyOceanMeshSettings.Default;

            LowPolyOceanState state = LowPolyOceanTool.ApplyToOcean(
                ocean, firstLowPoly, _lowPolyMaterial, settings, 2, false);
            LowPolyOceanTool.ApplyToOcean(
                ocean, secondLowPoly, _lowPolyMaterial, settings, 2, false);

            Assert.That(filter.sharedMesh, Is.SameAs(secondLowPoly));
            Assert.That(state.originalMesh, Is.SameAs(originalMesh));
        }

        [Test]
        public void RestoreOriginal_RestoresMeshMaterialScaleAndRemovesState()
        {
            GameObject ocean = CreateOcean(out MeshFilter filter, out MeshRenderer renderer,
                out Mesh originalMesh);
            Mesh lowPolyMesh = CreateLowPolyMesh();
            LowPolyOceanTool.ApplyToOcean(
                ocean, lowPolyMesh, _lowPolyMaterial, LowPolyOceanMeshSettings.Default, 2, false);

            bool restored = LowPolyOceanTool.RestoreOriginal(ocean, null, null, false);

            Assert.That(restored, Is.True);
            Assert.That(filter.sharedMesh, Is.SameAs(originalMesh));
            Assert.That(renderer.sharedMaterial, Is.SameAs(_originalMaterial));
            Assert.That(ocean.transform.localScale, Is.EqualTo(new Vector3(200f, 1f, 200f)));
            Assert.That(ocean.GetComponent<LowPolyOceanState>(), Is.Null);
        }

        [Test]
        public void RestoreOriginal_WithoutState_UsesFallback()
        {
            GameObject ocean = CreateOcean(out MeshFilter filter, out MeshRenderer renderer,
                out _);
            Mesh fallback = Track(new Mesh { name = "Fallback" });
            Material fallbackMaterial = Track(new Material(Shader.Find("Sprites/Default")));

            bool restored = LowPolyOceanTool.RestoreOriginal(
                ocean, fallback, fallbackMaterial, false);

            Assert.That(restored, Is.True);
            Assert.That(filter.sharedMesh, Is.SameAs(fallback));
            Assert.That(renderer.sharedMaterial, Is.SameAs(fallbackMaterial));
            Assert.That(ocean.transform.localScale, Is.EqualTo(new Vector3(200f, 1f, 200f)));
        }

        [Test]
        public void RestoreOriginal_WithoutStateOrFallback_ReturnsFalse()
        {
            GameObject ocean = CreateOcean(out _, out _, out _);

            Assert.That(LowPolyOceanTool.RestoreOriginal(ocean, null, null, false), Is.False);
        }

        [Test]
        public void Presets_ProduceDistinctLooks()
        {
            LowPolyOceanLook flat = LowPolyOceanLook.FromPreset(LowPolyOceanPreset.FlatColor);
            LowPolyOceanLook rolling = LowPolyOceanLook.FromPreset(LowPolyOceanPreset.RollingWaves);
            LowPolyOceanLook wire = LowPolyOceanLook.FromPreset(LowPolyOceanPreset.FacetWireframe);

            Assert.That(flat.facetShade, Is.EqualTo(0f));
            Assert.That(rolling.waveHeight, Is.GreaterThan(flat.waveHeight));
            Assert.That(wire.edgeStrength, Is.GreaterThan(0f));
            Assert.That(wire.edgeStrength, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void DefaultLook_DisablesResidualNoiseAndStrengthensFacets()
        {
            LowPolyOceanLook look = LowPolyOceanLook.Default;

            Assert.That(look.noiseStrength, Is.EqualTo(0f));
            Assert.That(look.facetTint, Is.GreaterThanOrEqualTo(0.2f));
            Assert.That(look.facetShade, Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(look.shallowAlpha, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(look.deepAlpha, Is.GreaterThanOrEqualTo(0.9f));
        }

        [Test]
        public void ApplyLook_WritesCleanDefaultsToMaterial()
        {
            Shader shader = Shader.Find(LowPolyOceanTool.LowPolyShaderName);
            Assert.That(shader, Is.Not.Null);
            Material material = Track(new Material(shader));

            LowPolyOceanTool.ApplyLook(material, LowPolyOceanLook.Default, false);

            Assert.That(material.GetFloat("_NoiseStrength"), Is.EqualTo(0f));
            Assert.That(material.GetFloat("_FacetShade"), Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(material.GetFloat("_FacetTint"), Is.GreaterThanOrEqualTo(0.2f));
            Assert.That(material.GetFloat("_ShallowAlpha"), Is.GreaterThanOrEqualTo(0.5f));
        }

        [Test]
        public void LowPolyShader_IsImportedWithoutErrors()
        {
            Shader shader = Shader.Find(LowPolyOceanTool.LowPolyShaderName);
            Assert.That(shader, Is.Not.Null, "低模海面 Shader 没有导入。");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False,
                "低模海面 Shader 编译失败，请看 Console。");

            string[] required =
            {
                "_LowPolyWaveHeight",
                "_LowPolyWaveSpeed",
                "_LowPolyWaveScale",
                "_FacetTint",
                "_FacetShade",
                "_EdgeStrength",
                "_EdgeWidth",
                "_NoiseStrength",
                "_ShallowAlpha",
                "_DeepAlpha",
                "_ShoreMask",
                "_ShoreMaskCenter",
                "_ShoreMaskSize",
                "_ShoreSmooth",
            };

            foreach (string property in required)
            {
                Assert.That(shader.GetPropertyCount(), Is.GreaterThan(0));
                bool found = false;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if (shader.GetPropertyName(i) == property)
                    {
                        found = true;
                        break;
                    }
                }

                Assert.That(found, Is.True, "Shader 缺少属性 " + property);
            }
        }
    }

    public class LowPolyOceanShoreMaskTests : WhiteBoxTestBase
    {
        [Test]
        public void ComputeHalfExtent_MatchesIslandSamplingRange()
        {
            IslandSettings settings = new IslandSettings();
            float coastWidth = Mathf.Max(
                settings.beachWidth + 0.01f, settings.coastWidth);
            float expected = Mathf.Max(
                settings.halfSizeX + coastWidth
                    + settings.coastNoiseLarge + settings.coastNoiseSmall + 2f,
                settings.halfSizeZ + coastWidth
                    + settings.coastNoiseLarge + settings.coastNoiseSmall + 2f);

            Assert.That(LowPolyOceanShoreMask.ComputeHalfExtent(settings),
                Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void Generate_LandCenterIsClippedAndDeepCornerIsClear()
        {
            IslandSettings settings = new IslandSettings();
            Texture2D mask = Track(LowPolyOceanShoreMask.Generate(settings, 64, 2.5f));

            Assert.That(mask.width, Is.EqualTo(64));
            Assert.That(mask.height, Is.EqualTo(64));
            Assert.That(mask.name, Is.EqualTo("OceanShoreMask"));

            Color center = mask.GetPixel(32, 32);
            Assert.That(center.g, Is.GreaterThan(0.9f), "岛中心应该是陆地");
            Assert.That(center.r, Is.LessThan(0.1f), "岛中心浪高遮罩应该为 0");

            Color corner = mask.GetPixel(0, 0);
            Assert.That(corner.r, Is.GreaterThan(0.9f), "远海应该保持完整浪高");
            Assert.That(corner.g, Is.LessThan(0.1f), "远海不应该是陆地");
        }

        [Test]
        public void Generate_IsDeterministic()
        {
            IslandSettings settings = new IslandSettings();
            Texture2D first = Track(LowPolyOceanShoreMask.Generate(settings, 32, 2f));
            Texture2D second = Track(LowPolyOceanShoreMask.Generate(settings, 32, 2f));

            CollectionAssert.AreEqual(first.GetPixels32(), second.GetPixels32());
        }

        [Test]
        public void FillFromHeights_MapsLandWaterAndNoHit()
        {
            Texture2D texture = Track(new Texture2D(
                3, 1, TextureFormat.RGBA32, false, true));
            float[] heights =
            {
                0f,                     // 陆地
                -4f,                    // 深水
                float.NegativeInfinity, // 没碰到地形 = 深水
            };

            LowPolyOceanShoreMask.FillFromHeights(texture, heights, -1.4f, 2.5f);

            Color land = texture.GetPixel(0, 0);
            Assert.That(land.g, Is.GreaterThan(0.9f));
            Assert.That(land.r, Is.LessThan(0.1f));

            Color deep = texture.GetPixel(1, 0);
            Assert.That(deep.r, Is.GreaterThan(0.9f));
            Assert.That(deep.g, Is.LessThan(0.1f));

            Color noHit = texture.GetPixel(2, 0);
            Assert.That(noHit.r, Is.GreaterThan(0.9f));
            Assert.That(noHit.g, Is.LessThan(0.1f));
        }

        [Test]
        public void Generate_WaterlinePixel_HasLandThresholdAndNoWave()
        {
            IslandSettings settings = new IslandSettings();
            Texture2D mask = Track(LowPolyOceanShoreMask.Generate(settings, 128, 2.5f));

            bool foundWaterline = false;
            foreach (Color32 pixel in mask.GetPixels32())
            {
                // G 在 0.5 附近 = 水线；此时 R 应该已经接近 0。
                if (pixel.g >= 120 && pixel.g <= 140)
                {
                    Assert.That(pixel.r, Is.LessThan(30));
                    foundWaterline = true;
                    break;
                }
            }

            Assert.That(foundWaterline, Is.True, "没有找到水线附近的遮罩像素");
        }
    }

    public class LowPolyOceanTopologyTests : WhiteBoxTestBase
    {
        private static LowPolyOceanMeshSettings ModeSettings(LowPolyOceanTopology mode)
        {
            LowPolyOceanMeshSettings settings = LowPolyOceanMeshSettings.Default;
            settings.topology = mode;
            settings.size = 240f;
            settings.refineHalfSize = 90f;
            settings.refineCellSize = 3f;
            settings.delaunayCellSize = 4f;
            settings.delaunayCoarseCellSize = 30f;
            settings.radialSegments = 96;
            settings.shoreRingSpacing = 3f;
            return settings;
        }

        [Test]
        public void DefaultTopology_IsFullWaterSurface()
        {
            Assert.That(LowPolyOceanMeshSettings.Default.topology,
                Is.EqualTo(LowPolyOceanTopology.ShaderSmooth));
        }

        [Test]
        public void AllModes_GenerateNonEmptyMeshes()
        {
            foreach (LowPolyOceanTopology mode in Enum.GetValues(
                typeof(LowPolyOceanTopology)))
            {
                LowPolyOceanMeshSettings settings = ModeSettings(mode);
                Mesh mesh = Track(LowPolyOceanMeshGenerator.Generate(
                    settings, new IslandSettings()));

                Assert.That(mesh.vertexCount, Is.GreaterThan(0), mode + " 顶点为空");
                Assert.That(mesh.vertexCount % 3, Is.EqualTo(0), mode + " 顶点数不是 3 的倍数");
                Assert.That(mesh.GetIndexCount(0) / 3u, Is.GreaterThan(0),
                    mode + " 没有三角形");
                Assert.That(mesh.bounds.size.x, Is.GreaterThan(0f));
                Assert.That(LowPolyOceanMeshGenerator.CountTriangles(settings),
                    Is.GreaterThan(0L), mode + " 预计数量为 0");
            }
        }

        [Test]
        public void RadialMesh_DoesNotCoverIslandCenter()
        {
            LowPolyOceanMeshSettings settings = ModeSettings(LowPolyOceanTopology.Radial);
            Mesh mesh = Track(LowPolyOceanMeshGenerator.Generate(
                settings, new IslandSettings()));

            foreach (Vector3 vertex in mesh.vertices)
            {
                float distance = new Vector2(vertex.x, vertex.z).magnitude;
                Assert.That(distance, Is.GreaterThan(35f),
                    "径向网格不应该铺到岛中心");
            }
        }

        [Test]
        public void RefinedGrid_IsDenserThanUniformAtSameSize()
        {
            LowPolyOceanMeshSettings refined = ModeSettings(LowPolyOceanTopology.RefinedGrid);
            LowPolyOceanMeshSettings uniform = ModeSettings(LowPolyOceanTopology.ShaderSmooth);
            uniform.cellSize = refined.cellSize;

            Assert.That(LowPolyOceanMeshGenerator.CountTriangles(refined),
                Is.GreaterThan(LowPolyOceanMeshGenerator.CountTriangles(uniform)));
        }
    }

    public class ColliderShoreMaskBakerTests : WhiteBoxTestBase
    {
        [Test]
        public void SampleHeights_HitsTerrainCollider()
        {
            GameObject terrain = NewGameObject("MaskTestTerrain");
            BoxCollider box = terrain.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(20f, 1f, 20f);
            Physics.SyncTransforms();

            List<Collider> colliders = new List<Collider> { box };
            float[] heights = ColliderShoreMaskBaker.SampleHeights(
                colliders, 8, 0f, 0f, out Vector2 center, out float size);

            Assert.That(center.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(center.y, Is.EqualTo(0f).Within(0.01f));
            Assert.That(size, Is.EqualTo(20f).Within(0.01f));

            foreach (float height in heights)
            {
                Assert.That(height, Is.EqualTo(0.5f).Within(0.01f),
                    "盒子顶面应该被射线命中");
            }
        }

        [Test]
        public void SampleHeights_NoColliders_ReturnsDeepWater()
        {
            float[] heights = ColliderShoreMaskBaker.SampleHeights(
                new List<Collider>(), 8, -1.4f, 10f,
                out _, out float size);

            Assert.That(size, Is.GreaterThan(0f));
            foreach (float height in heights)
            {
                Assert.That(float.IsNegativeInfinity(height), Is.True);
            }
        }
    }

    public class LowPolyOceanDelaunayTests
    {
        [Test]
        public void Triangulate_TooFewPoints_ReturnsEmpty()
        {
            Assert.That(LowPolyOceanDelaunay.Triangulate(null).Count, Is.EqualTo(0));
            Assert.That(LowPolyOceanDelaunay.Triangulate(
                new List<Vector2> { Vector2.zero }).Count, Is.EqualTo(0));
            Assert.That(LowPolyOceanDelaunay.Triangulate(
                new List<Vector2> { Vector2.zero, Vector2.right }).Count, Is.EqualTo(0));
        }

        [Test]
        public void Triangulate_SquareWithCenter_ProducesFourTriangles()
        {
            List<Vector2> points = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                new Vector2(10f, 10f),
                new Vector2(0f, 10f),
                new Vector2(5f, 5f),
            };

            List<int> triangles = LowPolyOceanDelaunay.Triangulate(points);
            Assert.That(triangles.Count / 3, Is.EqualTo(4));

            for (int i = 0; i < triangles.Count; i += 3)
            {
                Assert.That(triangles[i], Is.InRange(0, 4));
                Assert.That(triangles[i + 1], Is.InRange(0, 4));
                Assert.That(triangles[i + 2], Is.InRange(0, 4));
                Assert.That(triangles[i], Is.Not.EqualTo(triangles[i + 1]));
                Assert.That(triangles[i + 1], Is.Not.EqualTo(triangles[i + 2]));
                Assert.That(triangles[i], Is.Not.EqualTo(triangles[i + 2]));
            }
        }

        [Test]
        public void Triangulate_DeterministicAndNonDegenerate()
        {
            List<Vector2> points = new List<Vector2>();
            for (int i = 0; i < 24; i++)
            {
                points.Add(new Vector2((i % 6) * 3f, (i / 6) * 3f));
            }

            List<int> first = LowPolyOceanDelaunay.Triangulate(points);
            List<int> second = LowPolyOceanDelaunay.Triangulate(points);
            CollectionAssert.AreEqual(first, second);
            Assert.That(first.Count, Is.GreaterThan(0));

            for (int i = 0; i < first.Count; i += 3)
            {
                Vector2 a = points[first[i]];
                Vector2 b = points[first[i + 1]];
                Vector2 c = points[first[i + 2]];
                float area = Mathf.Abs((b.x - a.x) * (c.y - a.y)
                    - (b.y - a.y) * (c.x - a.x));
                Assert.That(area, Is.GreaterThan(0.001f));
            }
        }
    }
}
