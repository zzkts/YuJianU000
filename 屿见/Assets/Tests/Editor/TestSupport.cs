using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Building;
using Yujian.Core.Events;
using Yujian.Mining;
using Yujian.Player;
using Yujian.Save;
using Yujian.Shop;

namespace Yujian.Tests
{
    /// <summary>
    /// 白盒测试公共基类：
    /// · 跟踪测试里创建的 Unity 对象，TearDown 统一销毁；
    /// · 每个用例结束后清空 EventBus，避免静态订阅串到下一个用例；
    /// · 默认让 LogAssert 严格（未预期的 Error 会让用例失败），
    ///   故意触发错误分支的用例自己把 ignoreFailingMessages 置 true。
    /// </summary>
    public abstract class WhiteBoxTestBase
    {
        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

        [SetUp]
        public void ResetLogAssert()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [TearDown]
        public void CleanUpWhiteBox()
        {
            EventBus.ClearAll();

            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
                }
            }

            createdObjects.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        protected T Track<T>(T value) where T : UnityEngine.Object
        {
            createdObjects.Add(value);
            return value;
        }

        protected GameObject NewGameObject(string name = "WhiteBoxTestObject")
        {
            return Track(new GameObject(name));
        }

        protected T AddComponent<T>(GameObject target) where T : Component
        {
            return target.AddComponent<T>();
        }

        protected static SaveData NewValidSaveData(int version = SaveData.CurrentVersion)
        {
            SaveData data = new SaveData
            {
                version = version,
                savedAtUtc = version >= 2 ? DateTime.UtcNow.Ticks : 0,
            };

            data.Normalize();
            return data;
        }
    }

    /// <summary>反射工具：白盒测试需要注入私有序列化字段、调用私有方法。</summary>
    public static class TestReflect
    {
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Set(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, Flags);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} 上找不到字段 {fieldName}");
            field.SetValue(target, value);
        }

        public static T Get<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, Flags);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} 上找不到字段 {fieldName}");
            return (T)field.GetValue(target);
        }

        public static object GetObject(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, Flags);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} 上找不到字段 {fieldName}");
            return field.GetValue(target);
        }

        public static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, Flags);
            Assert.That(method, Is.Not.Null, $"{target.GetType().Name} 上找不到方法 {methodName}");
            return method.Invoke(target, arguments);
        }
    }

    /// <summary>测试数据工厂：创建不落盘的 ScriptableObject 与常用组件。</summary>
    public static class TestFactory
    {
        public static MaterialData MakeMaterial(
            string name, string saveId, int price = 2,
            float damageBonus = 0.1f, float buildingBonus = 0f,
            Color? color = null, MaterialType type = MaterialType.Brick)
        {
            MaterialData material = ScriptableObject.CreateInstance<MaterialData>();
            material.name = name;

            TestReflect.Set(material, "materialName", name);
            TestReflect.Set(material, "materialType", type);
            TestReflect.Set(material, "price", price);
            TestReflect.Set(material, "damageMultiplierBonus", damageBonus);
            TestReflect.Set(material, "buildingDamageMultiplierBonus", buildingBonus);
            TestReflect.Set(material, "displayColor", color ?? new Color(0.8f, 0.22f, 0.2f, 1f));
            TestReflect.Set(material, "saveId", saveId);

            return material;
        }

        public static BuildingData MakeBuilding(
            string name, string saveId, Vector2Int footprint,
            params BuildingMaterialRequirement[] requirements)
        {
            BuildingData building = ScriptableObject.CreateInstance<BuildingData>();
            building.name = name;

            TestReflect.Set(building, "buildingName", name);
            TestReflect.Set(building, "saveId", saveId);
            TestReflect.Set(building, "footprintCells", footprint);
            TestReflect.Set(building, "requiredMaterials", requirements);
            return building;
        }

        public static SaveCatalog MakeCatalog(
            MaterialData[] materials = null, BuildingData[] buildings = null)
        {
            SaveCatalog catalog = ScriptableObject.CreateInstance<SaveCatalog>();
            catalog.name = "TestCatalog";

            TestReflect.Set(catalog, "materials",
                new List<MaterialData>(materials ?? Array.Empty<MaterialData>()));
            TestReflect.Set(catalog, "buildings",
                new List<BuildingData>(buildings ?? Array.Empty<BuildingData>()));
            return catalog;
        }

        /// <summary>
        /// 挖矿工具数据。默认走线性模式（1 级 10 攻击，每级 +5；升级 15 / 30），
        /// 数值小、可一眼算清，适合测升级逻辑。
        /// </summary>
        public static MiningToolData MakeMiningTool(
            string name = "石头", string itemId = "mine_01", float baseAttack = 10f,
            int attackPerLevel = 5, int goldCostPerLevel = 15,
            int goldCostIncreasePerLevel = 15, float attackGrowth = 1f,
            float costGrowth = 1f, int maxLevel = 3)
        {
            MiningToolData tool = ScriptableObject.CreateInstance<MiningToolData>();
            tool.name = name;
            tool.ConfigureForSetup(name, itemId, baseAttack, attackPerLevel, goldCostPerLevel,
                goldCostIncreasePerLevel, attackGrowth, costGrowth, maxLevel);
            return tool;
        }

        public static PlayerCurrency MakeCurrency(GameObject host, int money = 0)
        {
            PlayerCurrency currency = host.AddComponent<PlayerCurrency>();

            if (money > 0)
            {
                currency.AddMoney(money);
            }

            return currency;
        }

        public static MaterialInventory MakeInventory(
            GameObject host, params (MaterialData Material, int Count)[] stacks)
        {
            MaterialInventory inventory = host.AddComponent<MaterialInventory>();

            for (int i = 0; i < stacks.Length; i++)
            {
                inventory.AddMaterial(stacks[i].Material, stacks[i].Count);
            }

            return inventory;
        }
    }
}
