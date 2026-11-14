using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Building;
using Yujian.Shop;

namespace Yujian.Tests
{
    /// <summary>
    /// 楼层堆叠：按材料数克隆/排布楼层、均分总高、刷色与透明度、隐藏多余层。
    /// 用两个 4×1×4 的 Cube 当样板层。
    /// </summary>
    public class BuildingLayerStackTests : WhiteBoxTestBase
    {
        private GameObject host;
        private BuildingLayerStack stack;

        [SetUp]
        public void CreateStack()
        {
            host = NewGameObject("TestBuilding");
            CreateSampleLayer(host.transform, "层0");
            CreateSampleLayer(host.transform, "层1");

            stack = AddComponent<BuildingLayerStack>(host);
        }

        private void CreateSampleLayer(Transform parent, string name)
        {
            GameObject cube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = new Vector3(4f, 1f, 4f);
        }

        private static Renderer LayerRenderer(GameObject root, int index)
        {
            return root.transform.GetChild(index).GetComponent<Renderer>();
        }

        private static Color GetLayerColor(GameObject root, int index)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            LayerRenderer(root, index).GetPropertyBlock(block);
            return block.GetColor(Shader.PropertyToID("_BaseColor"));
        }

        [Test]
        public void EnsureLayout_DiscoversSampleLayers()
        {
            // EditMode 下 Awake 不一定执行，EnsureLayout 内部会懒加载样板层
            stack.EnsureLayout(1);

            Assert.That(stack.SlotCount, Is.EqualTo(2));
            Assert.That(stack.LayerRoot, Is.SameAs(host.transform));
        }

        [Test]
        public void EnsureLayout_ClonesUpToRequestedCount()
        {
            stack.EnsureLayout(3);

            Assert.That(stack.ActiveLayerCount, Is.EqualTo(3));
            Assert.That(stack.SlotCount, Is.EqualTo(3));
            Assert.That(host.transform.childCount, Is.EqualTo(3));
        }

        [Test]
        public void EnsureLayout_SplitsTotalHeightEvenly()
        {
            stack.EnsureLayout(3);

            const float layerHeight = 4f / 3f;

            for (int i = 0; i < 3; i++)
            {
                Transform layer = host.transform.GetChild(i);

                Assert.That(layer.localScale.y, Is.EqualTo(layerHeight).Within(1e-4f));
                Assert.That(layer.localScale.x, Is.EqualTo(4f).Within(1e-4f),
                    "克隆层要照抄样板的宽度");
                Assert.That(layer.localScale.z, Is.EqualTo(4f).Within(1e-4f),
                    "克隆层要照抄样板的深度");
                Assert.That(layer.localPosition.y,
                    Is.EqualTo((i + 0.5f) * layerHeight).Within(1e-4f));
                Assert.That(layer.localPosition.x, Is.EqualTo(0f));
                Assert.That(layer.localPosition.z, Is.EqualTo(0f));
                Assert.That(layer.localRotation, Is.EqualTo(Quaternion.identity));
            }
        }

        [Test]
        public void EnsureLayout_FewerLayers_KeepsSamplesForLaterReuse()
        {
            stack.EnsureLayout(3);
            stack.EnsureLayout(1);

            Assert.That(stack.ActiveLayerCount, Is.EqualTo(1));
            Assert.That(stack.SlotCount, Is.EqualTo(3), "多余的样板层保留，不销毁");
        }

        [Test]
        public void EnsureLayout_NonPositive_WarnsAndDoesNothing()
        {
            LogAssert.ignoreFailingMessages = true;

            stack.EnsureLayout(0);

            Assert.That(stack.ActiveLayerCount, Is.EqualTo(0));
            Assert.That(stack.SlotCount, Is.EqualTo(2));
        }

        [Test]
        public void EnsureLayout_NoSamples_LogsError()
        {
            LogAssert.ignoreFailingMessages = true;

            GameObject empty = NewGameObject("EmptyBuilding");
            BuildingLayerStack emptyStack = AddComponent<BuildingLayerStack>(empty);

            emptyStack.EnsureLayout(2);

            Assert.That(emptyStack.SlotCount, Is.EqualTo(0));
            Assert.That(emptyStack.ActiveLayerCount, Is.EqualTo(0));
        }

        [Test]
        public void Apply_OneMaterialPerLayer()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick",
                color: new Color(0.9f, 0.1f, 0.1f, 1f)));
            MaterialData wood = Track(TestFactory.MakeMaterial("木板", "mat_wood",
                color: new Color(0.1f, 0.7f, 0.2f, 1f)));

            stack.Apply(new List<MaterialData> { brick, wood });

            Assert.That(stack.ActiveLayerCount, Is.EqualTo(2));

            Color first = GetLayerColor(host, 0);
            Assert.That(first.r, Is.EqualTo(0.9f).Within(1e-3f));
            Assert.That(first.g, Is.EqualTo(0.1f).Within(1e-3f));
            Assert.That(first.a, Is.EqualTo(1f).Within(1e-3f));

            Color second = GetLayerColor(host, 1);
            Assert.That(second.g, Is.EqualTo(0.7f).Within(1e-3f));
        }

        [Test]
        public void Apply_HidesLayersBeyondMaterialCount()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));

            stack.Apply(new List<MaterialData> { brick });

            Assert.That(LayerRenderer(host, 0).enabled, Is.True);
            Assert.That(LayerRenderer(host, 1).enabled, Is.False);
        }

        [Test]
        public void Apply_EmptyOrNull_UsesOneFallbackLayer()
        {
            stack.Apply(null);

            Assert.That(stack.ActiveLayerCount, Is.EqualTo(1));
            Assert.That(LayerRenderer(host, 0).enabled, Is.True);
            Assert.That(LayerRenderer(host, 1).enabled, Is.False);

            Color fallback = GetLayerColor(host, 0);
            Assert.That(fallback.r, Is.EqualTo(0.78f).Within(1e-3f));
            Assert.That(fallback.g, Is.EqualTo(0.76f).Within(1e-3f));
            Assert.That(fallback.b, Is.EqualTo(0.70f).Within(1e-3f));
        }

        [Test]
        public void Apply_NullMaterialEntry_FallsBackToFallbackColor()
        {
            stack.Apply(new List<MaterialData> { null });

            Color color = GetLayerColor(host, 0);
            Assert.That(color.r, Is.EqualTo(0.78f).Within(1e-3f));
        }

        [Test]
        public void SetLayerAlpha_OverridesColorAlphaAndClamps()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick",
                color: new Color(0.9f, 0.1f, 0.1f, 1f)));

            stack.Apply(new List<MaterialData> { brick });
            stack.SetLayerAlpha(0.5f);

            Assert.That(stack.CurrentLayerAlpha, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(GetLayerColor(host, 0).a, Is.EqualTo(0.5f).Within(1e-3f));

            // RGB 不受透明度调整影响
            Assert.That(GetLayerColor(host, 0).r, Is.EqualTo(0.9f).Within(1e-3f));

            stack.SetLayerAlpha(-1f);
            Assert.That(stack.CurrentLayerAlpha, Is.EqualTo(0f));

            stack.SetLayerAlpha(2f);
            Assert.That(stack.CurrentLayerAlpha, Is.EqualTo(1f));
        }

        [Test]
        public void SetLayerAlpha_SkipsUncoloredLayers()
        {
            // 还没 Apply 过：楼层没有 property block，不能被刷黑
            stack.SetLayerAlpha(0.3f);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            LayerRenderer(host, 0).GetPropertyBlock(block);

            Assert.That(block.isEmpty, Is.True);
        }

        [Test]
        public void SetLayerColor_OutOfRange_DoesNothing()
        {
            stack.SetLayerColor(-1, Color.red);
            stack.SetLayerColor(99, Color.red);

            Assert.That(stack.SlotCount, Is.EqualTo(2));
        }

        [Test]
        public void SetLayerVisible_TogglesRenderer()
        {
            stack.SetLayerVisible(0, false);
            Assert.That(LayerRenderer(host, 0).enabled, Is.False);

            stack.SetLayerVisible(0, true);
            Assert.That(LayerRenderer(host, 0).enabled, Is.True);
        }
    }
}
