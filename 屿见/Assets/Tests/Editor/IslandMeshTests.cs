using NUnit.Framework;
using UnityEngine;
using Yujian.Island;

namespace Yujian.Tests
{
    public class IslandMeshTests : WhiteBoxTestBase
    {
        [Test]
        public void Generate_UnderwaterBase_UsesSandAndWetSandBand()
        {
            IslandSettings settings = new IslandSettings();
            Mesh mesh = Track(IslandMeshGenerator.Generate(settings));

            Assert.That(mesh.subMeshCount, Is.EqualTo(IslandMeshGenerator.SubmeshCount));
            Assert.That(mesh.GetIndexCount(IslandMeshGenerator.SandSubmesh),
                Is.GreaterThan(0u), "水下坡面应该并入沙滩基底");
            Assert.That(mesh.GetIndexCount(IslandMeshGenerator.WetSandSubmesh),
                Is.GreaterThan(0u), "水线附近应该生成湿沙带");
        }

        [Test]
        public void Generate_DefaultSettings_AddsBaseRocks()
        {
            IslandSettings plain = new IslandSettings
            {
                rockClusterCount = 0,
                rockDensity = 0f,
            };
            IslandSettings withRocks = new IslandSettings();

            Mesh plainMesh = Track(IslandMeshGenerator.Generate(plain));
            Mesh rockMesh = Track(IslandMeshGenerator.Generate(withRocks));

            Assert.That(
                rockMesh.GetIndexCount(IslandMeshGenerator.RockSubmesh),
                Is.GreaterThan(plainMesh.GetIndexCount(IslandMeshGenerator.RockSubmesh)),
                "默认岛体应该在 Rock submesh 里加入岛基岩石");
        }
    }
}
