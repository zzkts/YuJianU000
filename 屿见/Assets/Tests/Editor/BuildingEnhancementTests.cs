using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.EditorTools;
using Yujian.Player;
using Yujian.Save;
using Yujian.UI;

namespace Yujian.Tests
{
    /// <summary>
    /// 建筑升级节点（策划案 3.4）：
    /// 节点查表、待选判定、必须选择才能继续升级、全局 / 个体加成汇总、存档字段。
    /// </summary>
    public class BuildingEnhancementTests : WhiteBoxTestBase
    {
        // ---------- 测试数据 ----------

        private static BuildingEnhancementOption MakeOption(
            string id, string name, BuildingEnhancementScope scope,
            BuildingEnhancementStat stat, float magnitude, bool stackable = false)
        {
            BuildingEnhancementOption option = new BuildingEnhancementOption();
            TestReflect.Set(option, "optionId", id);
            TestReflect.Set(option, "displayName", name);
            TestReflect.Set(option, "description", $"{name}的描述");
            TestReflect.Set(option, "scope", scope);
            TestReflect.Set(option, "stat", stat);
            TestReflect.Set(option, "magnitude", magnitude);
            TestReflect.Set(option, "stackable", stackable);
            return option;
        }

        private static BuildingEnhancementNode MakeNode(string id, int triggerLevel, params string[] optionIds)
        {
            BuildingEnhancementNode node = new BuildingEnhancementNode();
            TestReflect.Set(node, "nodeId", id);
            TestReflect.Set(node, "triggerLevel", triggerLevel);
            TestReflect.Set(node, "optionIds", optionIds);
            return node;
        }

        private BuildingEnhancementNodeTable MakeTable(
            BuildingEnhancementOption[] options, BuildingEnhancementNode[] nodes)
        {
            BuildingEnhancementNodeTable table =
                Track(ScriptableObject.CreateInstance<BuildingEnhancementNodeTable>());
            table.name = "Enhancement_Test";
            TestReflect.Set(table, "tableId", "Enhancement_Test");
            TestReflect.Set(table, "options", options);
            TestReflect.Set(table, "nodes", nodes);
            return table;
        }

        private BuildingEnhancementNodeTable MakeStandardTable()
        {
            return MakeTable(
                new[]
                {
                    MakeOption("Opt_10_Damage", "承重加固",
                        BuildingEnhancementScope.Individual, BuildingEnhancementStat.BuildingDamage, 0.15f),
                    MakeOption("Opt_10_Speed", "快速装填",
                        BuildingEnhancementScope.Individual, BuildingEnhancementStat.AttackSpeed, 0.10f),
                    MakeOption("Opt_20_GlobalBuilding", "全岛协作",
                        BuildingEnhancementScope.Global, BuildingEnhancementStat.BuildingDamage, 0.08f),
                    MakeOption("Opt_20_GlobalPlayer", "观光热潮",
                        BuildingEnhancementScope.Global, BuildingEnhancementStat.PlayerDamage, 0.05f),
                },
                new[]
                {
                    MakeNode("Node_10", 10, "Opt_10_Damage", "Opt_10_Speed"),
                    MakeNode("Node_20", 20, "Opt_20_GlobalBuilding", "Opt_20_GlobalPlayer"),
                });
        }

        private BuildingUpgradeTable MakeUpgradeTable(BuildingEnhancementNodeTable nodes)
        {
            BuildingUpgradeTable table = Track(ScriptableObject.CreateInstance<BuildingUpgradeTable>());
            TestReflect.Set(table, "maxLevel", 50);
            TestReflect.Set(table, "interval", 10);
            TestReflect.Set(table, "majorInterval", 50);
            TestReflect.Set(table, "baseCost", 1);
            TestReflect.Set(table, "costGrowthPerLevel", 1f);
            TestReflect.Set(table, "enhancementNodeTable", nodes);
            return table;
        }

        private BuildingData MakeBuildingWith(BuildingUpgradeTable upgradeTable)
        {
            BuildingData data = Track(TestFactory.MakeBuilding(
                "风琴博物馆", "b_museum", new Vector2Int(2, 2)));
            TestReflect.Set(data, "upgradeTable", upgradeTable);
            return data;
        }

        private PlacedBuildingInfo MakeInfo(BuildingData data, int level)
        {
            GameObject host = NewGameObject("建筑实例");
            PlacedBuildingInfo info = AddComponent<PlacedBuildingInfo>(host);
            info.Initialize(data, new RectInt(0, 0, 2, 2), null, level);
            return info;
        }

        // ---------- 节点查表 ----------

        [Test]
        public void NextTriggerLevel_FindsNextConfiguredNode()
        {
            BuildingEnhancementNodeTable table = MakeStandardTable();

            Assert.That(table.GetNextTriggerLevel(1), Is.EqualTo(10));
            Assert.That(table.GetNextTriggerLevel(9), Is.EqualTo(10));
            Assert.That(table.GetNextTriggerLevel(10), Is.EqualTo(20));
            Assert.That(table.GetNextTriggerLevel(20), Is.EqualTo(0));
            Assert.That(table.GetNodeAtLevel(10).NodeId, Is.EqualTo("Node_10"));
            Assert.That(table.GetNodeAtLevel(11), Is.Null);
        }

        [Test]
        public void LowestUnresolvedNode_SkipsResolvedAndUnreachableNodes()
        {
            BuildingEnhancementNodeTable table = MakeStandardTable();

            Assert.That(table.GetLowestUnresolvedNode(9, null), Is.Null, "还没到节点等级");

            BuildingEnhancementNode node10 = table.GetLowestUnresolvedNode(10, null);
            Assert.That(node10, Is.Not.Null);
            Assert.That(node10.NodeId, Is.EqualTo("Node_10"));

            BuildingEnhancementNode resolved10 = table.GetLowestUnresolvedNode(
                10, new List<string> { "Opt_10_Damage" });
            Assert.That(resolved10, Is.Null, "10 级节点已选过");

            BuildingEnhancementNode node20 = table.GetLowestUnresolvedNode(
                20, new List<string> { "Opt_10_Speed" });
            Assert.That(node20, Is.Not.Null);
            Assert.That(node20.NodeId, Is.EqualTo("Node_20"));

            BuildingEnhancementNode unknownSelection = table.GetLowestUnresolvedNode(
                10, new List<string> { "不存在的选项" });
            Assert.That(unknownSelection.NodeId, Is.EqualTo("Node_10"), "脏 ID 不能把节点误判成已解决");
        }

        [Test]
        public void BrokenNodeWithoutOptions_IsSkippedInsteadOfSoftLocking()
        {
            BuildingEnhancementNodeTable table = MakeTable(
                new BuildingEnhancementOption[0],
                new[] { MakeNode("Node_10", 10, "Missing") });

            Assert.That(table.GetLowestUnresolvedNode(10, null), Is.Null);
            Assert.That(table.GetNextTriggerLevel(1), Is.EqualTo(0));
        }

        // ---------- 建筑实例上的待选与加成 ----------

        [Test]
        public void PlacedInfo_PendingNodeClearsAfterSelection()
        {
            BuildingUpgradeTable upgradeTable = MakeUpgradeTable(MakeStandardTable());
            PlacedBuildingInfo info = MakeInfo(MakeBuildingWith(upgradeTable), 9);

            Assert.That(info.PendingEnhancementNode, Is.Null);

            info.SetLevel(10);
            Assert.That(info.PendingEnhancementNode, Is.Not.Null);
            Assert.That(info.PendingEnhancementNode.NodeId, Is.EqualTo("Node_10"));

            info.AddEnhancementOption("Opt_10_Damage");
            Assert.That(info.PendingEnhancementNode, Is.Null, "选完后不应继续锁着");
            Assert.That(info.HasEnhancementOption("Opt_10_Damage"), Is.True);
        }

        [Test]
        public void EnhancementBonus_SeparatesIndividualAndGlobalStats()
        {
            BuildingUpgradeTable upgradeTable = MakeUpgradeTable(MakeStandardTable());
            PlacedBuildingInfo info = MakeInfo(MakeBuildingWith(upgradeTable), 20);

            info.SetEnhancementOptionIds(new[]
            {
                "Opt_10_Damage", "Opt_10_Speed", "Opt_20_GlobalBuilding", "Opt_20_GlobalPlayer",
            });

            Assert.That(info.GetEnhancementBonus(
                BuildingEnhancementScope.Individual, BuildingEnhancementStat.BuildingDamage),
                Is.EqualTo(0.15f).Within(1e-4f));
            Assert.That(info.GetEnhancementBonus(
                BuildingEnhancementScope.Individual, BuildingEnhancementStat.AttackSpeed),
                Is.EqualTo(0.10f).Within(1e-4f));
            Assert.That(info.GetEnhancementBonus(
                BuildingEnhancementScope.Global, BuildingEnhancementStat.BuildingDamage),
                Is.EqualTo(0.08f).Within(1e-4f));
            Assert.That(info.GetEnhancementBonus(
                BuildingEnhancementScope.Global, BuildingEnhancementStat.PlayerDamage),
                Is.EqualTo(0.05f).Within(1e-4f));
        }

        [Test]
        public void EnhancementBonus_NonStackableCountsOnce_StackableCountsEveryTime()
        {
            BuildingEnhancementNodeTable table = MakeTable(
                new[]
                {
                    MakeOption("Opt_Once", "一次",
                        BuildingEnhancementScope.Individual, BuildingEnhancementStat.BuildingDamage,
                        0.1f, stackable: false),
                    MakeOption("Opt_Stack", "叠加",
                        BuildingEnhancementScope.Individual, BuildingEnhancementStat.BuildingDamage,
                        0.2f, stackable: true),
                },
                new[]
                {
                    MakeNode("Node_10", 10, "Opt_Once", "Opt_Stack"),
                });

            PlacedBuildingInfo info = MakeInfo(
                MakeBuildingWith(MakeUpgradeTable(table)), 10);
            info.SetEnhancementOptionIds(new[] { "Opt_Once", "Opt_Once", "Opt_Stack", "Opt_Stack" });

            Assert.That(info.GetEnhancementBonus(
                BuildingEnhancementScope.Individual, BuildingEnhancementStat.BuildingDamage),
                Is.EqualTo(0.5f).Within(1e-4f));
        }

        // ---------- Max 模式不能跨过节点 ----------

        [Test]
        public void MaxModeTarget_StopsAtNextNodeLevel()
        {
            BuildingEnhancementNodeTable table = MakeTable(
                new[]
                {
                    MakeOption("Opt_A", "甲",
                        BuildingEnhancementScope.Individual, BuildingEnhancementStat.BuildingDamage, 0.1f),
                },
                new[] { MakeNode("Node_25", 25, "Opt_A") });

            BuildingUpgradeTable upgradeTable = MakeUpgradeTable(table);

            Assert.That(upgradeTable.GetMaxModeTargetLevel(1), Is.EqualTo(10),
                "最近的间隔边界仍然有效");
            Assert.That(upgradeTable.GetMaxModeTargetLevel(20), Is.EqualTo(25),
                "25 级节点比 30 级间隔更近，必须停在节点");
        }

        // ---------- 存档字段 ----------

        [Test]
        public void SaveData_Normalize_RepairsMissingEnhancementList()
        {
            SaveData data = new SaveData();
            data.buildings.Add(new BuildingSave { buildingId = "b1", enhancementOptionIds = null });

            data.Normalize();

            Assert.That(data.buildings[0].enhancementOptionIds, Is.Not.Null);
            Assert.That(data.buildings[0].enhancementOptionIds, Is.Empty);
        }

        [Test]
        public void BuildingSave_EnhancementIds_SurviveJsonRoundTrip()
        {
            BuildingSave save = new BuildingSave
            {
                buildingId = "b1",
                level = 20,
                enhancementOptionIds = new List<string> { "Opt_10_Damage", "Opt_20_GlobalBuilding" },
            };

            BuildingSave restored = JsonUtility.FromJson<BuildingSave>(JsonUtility.ToJson(save));

            Assert.That(restored.enhancementOptionIds, Is.EqualTo(save.enhancementOptionIds));
        }

        // ---------- 升级规则层：强制选择 ----------

        [Test]
        public void BuildingUpgrade_StopsAtNode_AndBlocksFurtherUpgradeUntilChoice()
        {
            BuildingEnhancementNodeTable nodeTable = MakeStandardTable();
            BuildingUpgradeTable upgradeTable = MakeUpgradeTable(nodeTable);
            BuildingData data = MakeBuildingWith(upgradeTable);
            PlacedBuildingInfo info = MakeInfo(data, 9);

            GameObject managerHost = NewGameObject("管理器");
            managerHost.SetActive(false);
            BuildingManager manager = AddComponent<BuildingManager>(managerHost);

            GameObject currencyHost = NewGameObject("货币");
            PlayerCurrency currency = TestFactory.MakeCurrency(currencyHost, 100);

            GameObject upgradeHost = NewGameObject("升级");
            upgradeHost.SetActive(false);
            BuildingUpgrade upgrade = AddComponent<BuildingUpgrade>(upgradeHost);
            TestReflect.Set(upgrade, "buildingManager", manager);
            TestReflect.Set(upgrade, "currency", currency);
            upgradeHost.SetActive(true);

            Assert.That(upgrade.Select(info.gameObject), Is.True);

            Assert.That(upgrade.TryUpgrade(BuildingUpgradeMode.OneLevel, out string message), Is.True);
            Assert.That(info.Level, Is.EqualTo(10));
            Assert.That(upgrade.HasPendingNode, Is.True);
            Assert.That(message, Does.Contain("强化节点"));

            Assert.That(upgrade.TryUpgrade(BuildingUpgradeMode.OneLevel, out string blocked), Is.False);
            Assert.That(blocked, Does.Contain("请先选择强化节点"));
            Assert.That(info.Level, Is.EqualTo(10), "没选强化前不能再升级");

            Assert.That(upgrade.Select(null), Is.False, "没选强化前不能点空地退出");

            int moneyBeforeChoice = currency.CurrentMoney;
            Assert.That(upgrade.TrySelectEnhancement("Opt_10_Damage", out string choiceMessage), Is.True);
            Assert.That(choiceMessage, Does.Contain("承重加固"));
            Assert.That(currency.CurrentMoney, Is.EqualTo(moneyBeforeChoice), "选择强化不额外收费");
            Assert.That(upgrade.HasPendingNode, Is.False);

            Assert.That(upgrade.Select(null), Is.True, "选完后可以正常退出");
        }

        [Test]
        public void BuildingUpgrade_RejectsOptionFromAnotherNode()
        {
            BuildingEnhancementNodeTable nodeTable = MakeStandardTable();
            BuildingUpgradeTable upgradeTable = MakeUpgradeTable(nodeTable);
            PlacedBuildingInfo info = MakeInfo(MakeBuildingWith(upgradeTable), 10);

            GameObject managerHost = NewGameObject("管理器");
            managerHost.SetActive(false);
            BuildingManager manager = AddComponent<BuildingManager>(managerHost);

            GameObject currencyHost = NewGameObject("货币");
            PlayerCurrency currency = TestFactory.MakeCurrency(currencyHost, 10);

            GameObject upgradeHost = NewGameObject("升级");
            upgradeHost.SetActive(false);
            BuildingUpgrade upgrade = AddComponent<BuildingUpgrade>(upgradeHost);
            TestReflect.Set(upgrade, "buildingManager", manager);
            TestReflect.Set(upgrade, "currency", currency);
            upgradeHost.SetActive(true);

            upgrade.Select(info.gameObject);

            Assert.That(upgrade.TrySelectEnhancement("Opt_20_GlobalBuilding", out string message), Is.False);
            Assert.That(message, Does.Contain("不属于当前节点"));
            Assert.That(info.EnhancementOptionIds, Is.Empty);
        }

        // ---------- 选项模板 / 克隆体 ----------

        [Test]
        public void OptionTemplate_BindUpdatesCloneTexts_NotTemplate()
        {
            GameObject container = NewGameObject("选项列表");
            GameObject host = NewGameObject("选项模板");
            host.transform.SetParent(container.transform, false);

            Image image = AddComponent<Image>(host);
            Button button = AddComponent<Button>(host);
            button.targetGraphic = image;

            GameObject nameObject = NewGameObject("名称");
            nameObject.transform.SetParent(host.transform, false);
            Text nameText = AddComponent<Text>(nameObject);
            nameText.text = "模板占位名称";

            GameObject descriptionObject = NewGameObject("描述");
            descriptionObject.transform.SetParent(host.transform, false);
            Text descriptionText = AddComponent<Text>(descriptionObject);
            descriptionText.text = "模板占位描述";

            BuildingUpgradeOptionUI template = AddComponent<BuildingUpgradeOptionUI>(host);
            template.ConfigureForSetup(button, nameText, descriptionText);

            BuildingEnhancementOption option = MakeOption(
                "Opt_Bind", "承重加固",
                BuildingEnhancementScope.Individual, BuildingEnhancementStat.BuildingDamage, 0.15f);

            BuildingUpgradeOptionUI clone = Track(
                Object.Instantiate(template, container.transform));
            clone.Bind(option, null);

            Text[] cloneTexts = clone.GetComponentsInChildren<Text>(true);
            Assert.That(cloneTexts[0].text, Does.Contain("承重加固"));
            Assert.That(cloneTexts[0].text, Does.Contain("个体"));
            Assert.That(cloneTexts[1].text, Does.Contain("+15%"));
            Assert.That(nameText.text, Is.EqualTo("模板占位名称"), "克隆体绑定不能写到模板上");
        }

        // ---------- 场景 UI 生成器 ----------

        [Test]
        public void Wizard_BuildsEditableSceneUI()
        {
            GameObject canvasHost = NewGameObject("Canvas");
            Canvas canvas = AddComponent<Canvas>(canvasHost);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            AddComponent<GraphicRaycaster>(canvasHost);

            GameObject upgradeHost = NewGameObject("升级");
            upgradeHost.SetActive(false);
            BuildingUpgrade upgrade = AddComponent<BuildingUpgrade>(upgradeHost);

            MethodInfo build = typeof(BuildingEnhancementSetupWizard).GetMethod(
                "Build", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(build, Is.Not.Null, "向导的 Build 入口应存在");

            BuildingUpgradeNodeUI nodeUI = (BuildingUpgradeNodeUI)build.Invoke(
                null, new object[] { canvas, upgrade });
            Assert.That(nodeUI, Is.Not.Null);

            GameObject root = TestReflect.Get<GameObject>(nodeUI, "root");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.activeSelf, Is.False, "生成的遮罩默认收起");
            Image overlay = root.GetComponent<Image>();
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.color.a, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(overlay.raycastTarget, Is.True, "遮罩必须挡住后面的点击");

            RectTransform optionContainer = TestReflect.Get<RectTransform>(nodeUI, "optionContainer");
            Assert.That(optionContainer, Is.Not.Null);

            GridLayoutGroup grid = optionContainer.GetComponent<GridLayoutGroup>();
            Assert.That(grid, Is.Not.Null, "选项按分栏排布");
            Assert.That(grid.constraint, Is.EqualTo(GridLayoutGroup.Constraint.FixedColumnCount));
            Assert.That(grid.constraintCount, Is.EqualTo(2), "默认 2 列；可在 Inspector 改列数");

            BuildingUpgradeOptionUI template = TestReflect.Get<BuildingUpgradeOptionUI>(
                nodeUI, "optionTemplate");
            Assert.That(template, Is.Not.Null);
            Assert.That(template.gameObject.activeSelf, Is.False, "模板本身不能显示");
            Assert.That(template.GetComponent<Button>(), Is.Not.Null);
            Assert.That(template.GetComponentsInChildren<Text>(true).Length, Is.EqualTo(2),
                "模板应有名称与描述两个文本");

            // 场景里被误开也要在运行时强制隐藏，否则占位文字会被当成一张真实选项
            template.gameObject.SetActive(true);
            TestReflect.Invoke(nodeUI, "HideOptionTemplate");
            Assert.That(template.gameObject.activeSelf, Is.False, "运行时必须强制隐藏选项模板");

            GameObject confirmPanel = TestReflect.Get<GameObject>(nodeUI, "confirmPanel");
            Assert.That(confirmPanel, Is.Not.Null);
            Assert.That(confirmPanel.activeSelf, Is.False);
            Assert.That(TestReflect.Get<BuildingUpgrade>(nodeUI, "upgrade"), Is.SameAs(upgrade));
        }

        // ---------- 实际占位资产 ----------

        [Test]
        public void PlaceholderAsset_IsWiredIntoLevelUP01()
        {
            BuildingEnhancementNodeTable nodes = AssetDatabase.LoadAssetAtPath<BuildingEnhancementNodeTable>(
                "Assets/Data/Buildings/Enhancement_Normal_01.asset");
            BuildingUpgradeTable upgrade = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTable>(
                "Assets/Data/Buildings/LevelUP_01.asset");

            Assert.That(nodes, Is.Not.Null);
            Assert.That(upgrade, Is.Not.Null);
            Assert.That(upgrade.EnhancementNodeTable, Is.SameAs(nodes));

            // 2026-11-23 第 135 节按最新《屿见-建筑系统.docx》改成「两阶强化 + 4 个普通选项」，
            // 这里跟着数据走；节点等级待 Demo 清单拍板后再前移（第 138.10 节）。
            Assert.That(nodes.Nodes.Count, Is.EqualTo(2));
            Assert.That(nodes.Options.Count, Is.EqualTo(4));

            int[] expectedLevels = { 25, 50 };

            for (int i = 0; i < expectedLevels.Length; i++)
            {
                Assert.That(nodes.Nodes[i].TriggerLevel, Is.EqualTo(expectedLevels[i]));
                Assert.That(nodes.ResolveOptions(nodes.Nodes[i]).Count, Is.EqualTo(2));
            }
        }
    }
}
