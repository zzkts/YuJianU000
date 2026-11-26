using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Core;
using Yujian.Levels;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 一键配置垃圾背包系统（策划案 2.1.6，2026-11-25）。
    ///
    /// 做五件事，全部幂等：
    ///   1. 建 / 补 等级表资产 `Assets/Data/Mining/GarbageBagTable.asset`（占位公式）；
    ///   2. 给 PlayerGarbage 所在物体挂 GarbageBag 并接线；
    ///   3. 让 PlayerGarbage 认识这个背包（关卡内垃圾才只进背包）；
    ///   4. 在 Canvas 下建 `BagPanel`（只放一个空面板 + GarbageBagUI，子物体运行时自动生成）；
    ///   5. 把「清洁 / 建筑」两个按钮挪到屏幕两侧，并接上背包存档参与者。
    ///
    /// 跑完记得 Ctrl+S —— 向导只标脏，不替你保存场景。
    /// </summary>
    public static class GarbageBagSetupWizard
    {
        private const string TablePath = "Assets/Data/Mining/GarbageBagTable.asset";
        private const string PanelName = "BagPanel";

        [MenuItem("屿见/垃圾背包/配置垃圾背包系统", false, 60)]
        public static void Setup()
        {
            GarbageBagTable table = EnsureTable();

            if (table == null)
            {
                EditorUtility.DisplayDialog("垃圾背包", "等级表创建失败，详见 Console。", "好");
                return;
            }

            PlayerGarbage garbage = Object.FindFirstObjectByType<PlayerGarbage>(FindObjectsInactive.Include);

            if (garbage == null)
            {
                EditorUtility.DisplayDialog("垃圾背包",
                    "场景里找不到 PlayerGarbage，无法挂背包。\n先跑「屿见/场景/配置清洁系统」把玩家接出来。", "好");
                return;
            }

            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            RecyclingHudUI resourceHud = Object.FindFirstObjectByType<RecyclingHudUI>(FindObjectsInactive.Include);

            GarbageBag bag = EnsureBag(garbage, table, currency, areas);
            EnsureGarbageKnowsBag(garbage, bag);

            // 2026-11-26：关卡暂存池。背包满时掉在关卡里的垃圾记在这里，下次进关再灌回背包。
            LevelManager levels = Object.FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include);
            GarbageOverflow overflow = EnsureOverflow(garbage, bag, areas, levels);
            EnsureGarbageKnowsOverflow(garbage, overflow);

            EnsurePanel(bag, garbage, resourceHud, areas);
            EnsureNavOnSides();

            SaveSetupWizard.EnsureEconomyParticipants();

            EditorSceneManager.MarkSceneDirty(garbage.gameObject.scene);
            EditorUtility.SetDirty(garbage);
            EditorUtility.SetDirty(bag);

            Selection.activeGameObject = bag.gameObject;
            Debug.Log("[垃圾背包] 配置完成：背包 + 关卡暂存池挂在 " + garbage.name + " 上，UI 为 Canvas/" + PanelName +
                      "。请 Ctrl+S 保存场景。");
        }

        private static GarbageBagTable EnsureTable()
        {
            GarbageBagTable table = AssetDatabase.LoadAssetAtPath<GarbageBagTable>(TablePath);

            if (table != null)
            {
                return table;
            }

            table = ScriptableObject.CreateInstance<GarbageBagTable>();
            table.ConfigureForSetup(
                newBaseCapacity: 200,
                newCapacityGrowth: 1.15f,
                newMaxLevel: 50,
                newUpgradeCostBase: 40,
                newUpgradeCostGrowth: 1.18f,
                newTransferPerSecond: 10f,
                newLongPressMaxLevel: 19,
                newClickMaxLevel: 49);

            AssetDatabase.CreateAsset(table, TablePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[垃圾背包] 已创建等级表 {TablePath}（占位公式，数值可直接在 Inspector 改）。");
            return table;
        }

        private static GarbageBag EnsureBag(PlayerGarbage garbage, GarbageBagTable table,
            PlayerCurrency currency, GameAreaController areas)
        {
            GarbageBag bag = garbage.GetComponent<GarbageBag>();

            if (bag == null)
            {
                bag = Undo.AddComponent<GarbageBag>(garbage.gameObject);
                Debug.Log($"[垃圾背包] 已给「{garbage.name}」添加 GarbageBag。");
            }

            bag.ConfigureForSetup(table, currency, areas, 1);
            EditorUtility.SetDirty(bag);
            return bag;
        }

        private static void EnsureGarbageKnowsBag(PlayerGarbage garbage, GarbageBag bag)
        {
            SerializedObject serialized = new SerializedObject(garbage);
            SerializedProperty property = serialized.FindProperty("bag");

            if (property == null)
            {
                Debug.LogError("[垃圾背包] PlayerGarbage 上找不到 bag 字段，脚本是否已编译？");
                return;
            }

            if (property.objectReferenceValue != bag)
            {
                property.objectReferenceValue = bag;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>关卡暂存池：挂在玩家身上，跟着 PlayerGarbage 走。</summary>
        private static GarbageOverflow EnsureOverflow(PlayerGarbage garbage, GarbageBag bag,
            GameAreaController areas, LevelManager levels)
        {
            GarbageOverflow overflow = garbage.GetComponent<GarbageOverflow>();

            if (overflow == null)
            {
                overflow = Undo.AddComponent<GarbageOverflow>(garbage.gameObject);
                Debug.Log($"[垃圾背包] 已给「{garbage.name}」添加 GarbageOverflow（关卡暂存池）。");
            }

            overflow.ConfigureForSetup(bag, areas, levels);
            EditorUtility.SetDirty(overflow);
            return overflow;
        }

        private static void EnsureGarbageKnowsOverflow(PlayerGarbage garbage, GarbageOverflow overflow)
        {
            SerializedObject serialized = new SerializedObject(garbage);
            SerializedProperty property = serialized.FindProperty("overflow");

            if (property == null)
            {
                Debug.LogError("[垃圾背包] PlayerGarbage 上找不到 overflow 字段，脚本是否已编译？");
                return;
            }

            if (property.objectReferenceValue != overflow)
            {
                property.objectReferenceValue = overflow;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsurePanel(GarbageBag bag, PlayerGarbage storage,
            RecyclingHudUI resourceHud, GameAreaController areas)
        {
            Canvas canvas = FindPersistentCanvas();

            if (canvas == null)
            {
                Debug.LogError("[垃圾背包] 场景里找不到常驻 Canvas，UI 建不出来。");
                return;
            }

            // 先看场景里有没有已经建过的背包面板（可能在错的画布下，比如「清洁界面/MiningHUDRuntime」）
            GarbageBagUI existingUi = Object.FindFirstObjectByType<GarbageBagUI>(FindObjectsInactive.Include);
            GameObject panel;

            if (existingUi != null)
            {
                panel = existingUi.gameObject;

                if (panel.transform.parent != canvas.transform)
                {
                    Debug.LogWarning($"[垃圾背包] 背包面板原来挂在「{panel.transform.parent?.name}」下，" +
                                     $"会跟着那个画布一起显隐。已迁到常驻画布「{canvas.name}」。");
                    panel.transform.SetParent(canvas.transform, false);
                }
            }
            else
            {
                panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(panel, "创建垃圾背包面板");
                panel.transform.SetParent(canvas.transform, false);
                panel.layer = canvas.gameObject.layer;
            }

            // 放到最后：盖在底部导航之上，不会被栏位挡住
            panel.transform.SetAsLastSibling();

            RectTransform rect = panel.GetComponent<RectTransform>();

            if (rect == null)
            {
                rect = panel.AddComponent<RectTransform>();
            }

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(340f, 150f);
            rect.anchoredPosition = new Vector2(0f, -80f);

            Image image = panel.GetComponent<Image>();

            if (image == null)
            {
                image = panel.AddComponent<Image>();
            }

            image.color = new Color(0.07f, 0.09f, 0.12f, 0.86f);

            GarbageBagUI ui = panel.GetComponent<GarbageBagUI>();

            if (ui == null)
            {
                ui = panel.AddComponent<GarbageBagUI>();
            }

            SerializedObject serialized = new SerializedObject(ui);
            SetRef(serialized, "bag", bag);
            SetRef(serialized, "storage", storage);
            SetRef(serialized, "resourceHud", resourceHud);
            SetRef(serialized, "areas", areas);
            SetRef(serialized, "overflow",
                Object.FindFirstObjectByType<GarbageOverflow>(FindObjectsInactive.Include));
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(ui);
            EditorUtility.SetDirty(panel);
        }

        /// <summary>
        /// 找「常驻」画布。场景里有两个根级 Canvas：主 Canvas，以及
        /// 「清洁界面/MiningHUDRuntime」（只在关卡里激活）。
        /// 直接 FindFirstObjectByType&lt;Canvas&gt; 会随机抓到后者，
        /// 背包就会跟着只在关卡里显示——2026-11-25 用户报的就是这个。
        ///
        /// 判据：底部导航（BottomNavUI）一定挂在常驻画布上，跟着它走最可靠。
        /// </summary>
        public static Canvas FindPersistentCanvas()
        {
            BottomNavUI nav = Object.FindFirstObjectByType<BottomNavUI>(FindObjectsInactive.Include);

            if (nav != null)
            {
                Canvas navCanvas = nav.GetComponentInParent<Canvas>();

                if (navCanvas != null)
                {
                    return navCanvas;
                }
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];

                if (candidate != null && candidate.transform.parent == null)
                {
                    return candidate;
                }
            }

            return canvases.Length > 0 ? canvases[0] : null;
        }

        private static void EnsureNavOnSides()
        {
            BottomNavUI nav = Object.FindFirstObjectByType<BottomNavUI>(FindObjectsInactive.Include);

            if (nav == null)
            {
                Debug.LogWarning("[垃圾背包] 场景里没有 BottomNavUI，跳过按钮挪两侧。");
                return;
            }

            SerializedObject serialized = new SerializedObject(nav);
            SerializedProperty property = serialized.FindProperty("placeButtonsOnSides");

            if (property != null && !property.boolValue)
            {
                property.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            nav.ApplySideLayout();
            EditorUtility.SetDirty(nav);
        }

        private static void SetRef(SerializedObject serialized, string fieldName, Object value)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[垃圾背包] {serialized.targetObject.GetType().Name} 上找不到字段 {fieldName}。",
                    serialized.targetObject);
                return;
            }

            property.objectReferenceValue = value;
        }
    }
}
