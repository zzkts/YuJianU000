using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;
using Yujian.Core;
using Yujian.Player;
using Yujian.Shop;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 建筑栏位（策划案 3.7）的场景配置向导。
    ///
    /// 栏位本身的层级是 BuildingBarUI 在运行时用代码搭的，所以这里只需要：
    ///   · 在 Canvas 下建一个带 RectTransform 的「BuildingBar」；
    ///   · 挂上 BuildingBarUI，并把依赖接好（接不到也没关系，运行时会自己找）；
    ///   · 顺手把 BuildingUI 的库存 / 等级引用补上；
    ///   · 把旧的竖排建筑按钮藏起来（栏位替代它们），进度文字保留。
    ///
    /// 幂等：重复运行不会重复建物体。
    ///
    /// 运行入口：菜单栏 → 屿见 → 建筑 → 配置建筑栏位（3.7）
    /// </summary>
    public static class BuildingBarSetupWizard
    {
        private const string BarName = "BuildingBar";
        private const string LegacyPanelName = "BuildingPanel";
        private const string LegacyButtonPrefix = "Button_";

        private const float LegacyPanelCollapsedHeight = 150f;
        private const float LegacyPanelExpandedHeight = 584f;

        [MenuItem("屿见/建筑/配置建筑栏位（3.7）", false, 320)]
        public static void Configure()
        {
            Canvas canvas = FindMainCanvas();

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法创建建筑栏位。");
                return;
            }

            BuildingUI buildingUI = Object.FindFirstObjectByType<BuildingUI>();
            BuildingManager buildingManager = Object.FindFirstObjectByType<BuildingManager>();
            MaterialInventory inventory = Object.FindFirstObjectByType<MaterialInventory>();
            PlayerLevel playerLevel = Object.FindFirstObjectByType<PlayerLevel>();
            MilestoneProgress milestones = Object.FindFirstObjectByType<MilestoneProgress>();
            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>();

            if (buildingUI == null)
            {
                Fail("场景里找不到 BuildingUI，栏位里不会有建筑。请先确认建筑系统已经配好。");
                return;
            }

            Transform existing = canvas.transform.Find(BarName);
            GameObject barObject;

            if (existing != null)
            {
                barObject = existing.gameObject;
            }
            else
            {
                barObject = new GameObject(BarName, typeof(RectTransform));
                barObject.layer = 5;
                barObject.transform.SetParent(canvas.transform, false);
                Undo.RegisterCreatedObjectUndo(barObject, "创建建筑栏位");
            }

            barObject.transform.SetAsLastSibling();

            BuildingBarUI bar = barObject.GetComponent<BuildingBarUI>();

            if (bar == null)
            {
                bar = Undo.AddComponent<BuildingBarUI>(barObject);
            }

            SerializedObject barSerialized = new SerializedObject(bar);
            SetObject(barSerialized, "buildingUI", buildingUI);
            SetObject(barSerialized, "buildingManager", buildingManager);
            SetObject(barSerialized, "inventory", inventory);
            SetObject(barSerialized, "playerLevel", playerLevel);
            SetObject(barSerialized, "milestones", milestones);
            SetObject(barSerialized, "areas", areas);
            barSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject uiSerialized = new SerializedObject(buildingUI);
            SetObject(uiSerialized, "inventory", inventory);
            SetObject(uiSerialized, "playerLevel", playerLevel);
            uiSerialized.ApplyModifiedPropertiesWithoutUndo();

            // 上一次运行如果按旧规则误藏了「取消蓝图 / 退出建造」，这里先全部放回来
            RestoreLegacyButtons();
            HideLegacyButtons(true);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = barObject;

            Debug.Log("[BuildingBarSetupWizard] 建筑栏位已创建并接线。按 Ctrl+S 保存场景，然后 Play 测试。" +
                      "旧的竖排列表已隐藏，可用菜单「屿见/建筑/3.7 恢复旧的建筑列表按钮」找回。");

            EditorUtility.DisplayDialog(
                "建筑栏位已就绪",
                "已在 Canvas 下创建 BuildingBar，并接好建筑 / 库存 / 等级 / 区域引用。\n\n" +
                "旧的竖排建筑按钮已隐藏，进度文字保留。\n\n" +
                "下一步：Ctrl+S 保存场景 → 点 Play → 进入建筑区看底部栏位。",
                "知道了");
        }

        [MenuItem("屿见/建筑/3.7 隐藏旧的建筑列表按钮", false, 321)]
        public static void HideLegacy()
        {
            HideLegacyButtons(true);
        }

        [MenuItem("屿见/建筑/3.7 恢复旧的建筑列表按钮", false, 322)]
        public static void ShowLegacy()
        {
            HideLegacyButtons(false);
        }

        /// <summary>
        /// 只动「Button_建筑名」这 4 个按钮。
        ///
        /// 不能用「名字以 Button_ 开头」来认：建筑面板里还挂着「Button_取消蓝图」
        /// 「Button_退出建造」，按前缀过滤会把它们一起藏掉，玩家就没法退出建造了。
        /// </summary>
        private static void HideLegacyButtons(bool hide)
        {
            GameObject panelObject = GameObject.Find(LegacyPanelName);

            if (panelObject == null)
            {
                Debug.LogWarning($"[BuildingBarSetupWizard] 场景里找不到「{LegacyPanelName}」，跳过旧按钮显隐。");
                return;
            }

            HashSet<string> buildingButtonNames = CollectBuildingButtonNames();

            if (buildingButtonNames.Count == 0)
            {
                Debug.LogWarning("[BuildingBarSetupWizard] 读不到建筑清单，无法确定要隐藏哪几个旧按钮，" +
                                 "这次什么都不改（避免误藏「取消蓝图 / 退出建造」）。");
                return;
            }

            int changed = 0;
            Transform[] all = panelObject.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
            {
                if (!buildingButtonNames.Contains(all[i].name))
                {
                    continue;
                }

                Undo.RecordObject(all[i].gameObject, hide ? "隐藏旧建筑按钮" : "恢复旧建筑按钮");

                if (all[i].gameObject.activeSelf != !hide)
                {
                    all[i].gameObject.SetActive(!hide);
                    changed++;
                }
            }

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();

            if (panelRect != null)
            {
                Undo.RecordObject(panelRect, hide ? "收起建筑面板" : "展开建筑面板");
                panelRect.sizeDelta = new Vector2(
                    panelRect.sizeDelta.x,
                    hide ? LegacyPanelCollapsedHeight : LegacyPanelExpandedHeight);
            }

            EditorSceneManager.MarkSceneDirty(panelObject.scene);
            Debug.Log($"[BuildingBarSetupWizard] 旧建筑按钮{(hide ? "隐藏" : "恢复")}完成，改动 {changed} 个。" +
                      "记得 Ctrl+S 保存场景。");
        }

        /// <summary>
        /// 上一版向导按 Button_ 前缀隐藏，误藏了「取消蓝图 / 退出建造」。
        /// 每次配置前先把它们放回来，重跑一次就能自愈。
        /// </summary>
        private static void RestoreLegacyButtons()
        {
            GameObject panelObject = GameObject.Find(LegacyPanelName);

            if (panelObject == null)
            {
                return;
            }

            Transform[] all = panelObject.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].name.StartsWith(LegacyButtonPrefix) || all[i].gameObject.activeSelf)
                {
                    continue;
                }

                Undo.RecordObject(all[i].gameObject, "恢复旧建筑按钮");
                all[i].gameObject.SetActive(true);
            }
        }

        /// <summary>按建筑清单拼出「Button_建筑名」的集合。</summary>
        private static HashSet<string> CollectBuildingButtonNames()
        {
            HashSet<string> names = new HashSet<string>();
            BuildingUI buildingUI = Object.FindFirstObjectByType<BuildingUI>();

            if (buildingUI == null)
            {
                return names;
            }

            IReadOnlyList<BuildingData> list = buildingUI.AvailableBuildings;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    names.Add(LegacyButtonPrefix + list[i].BuildingName);
                }
            }

            return names;
        }

        private static Canvas FindMainCanvas()
        {
            GameObject named = GameObject.Find("Canvas");

            if (named != null)
            {
                Canvas canvas = named.GetComponent<Canvas>();

                if (canvas != null)
                {
                    return canvas;
                }
            }

            return Object.FindFirstObjectByType<Canvas>();
        }

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[BuildingBarSetupWizard] {so.targetObject.GetType().Name} 上找不到字段「{fieldName}」，" +
                                 "请检查字段名与脚本是否一致。");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑栏位未配置", message, "知道了");
            Debug.LogError("[BuildingBarSetupWizard] " + message);
        }
    }
}
