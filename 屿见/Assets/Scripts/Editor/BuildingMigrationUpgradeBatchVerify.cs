using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Player;
using Yujian.Save;
using Yujian.Shop;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 建筑迁移（3.3）与建筑升级（3.4）的临时批处理验证。
    /// 只在无头模式下跑一次，真实场景验证通过后删除本文件。
    /// </summary>
    public static class BuildingMigrationUpgradeBatchVerify
    {
        private static readonly List<string> Failures = new List<string>();
        private static int checks;

        public static void RunAll()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

            BuildingMigrationUpgradeSetupWizard.Configure();

            VerifySceneWiring();
            VerifyUpgradeTable();
            VerifyUpgradeLogic();
            VerifyMigration();
            VerifyBlueprintSmoothSnap();
            VerifySave();
            VerifyOldSaveLevelDefault();

            Debug.Log($"[BuildingVerify] 完成：{checks - Failures.Count}/{checks} 通过。");

            if (Failures.Count > 0)
            {
                for (int i = 0; i < Failures.Count; i++)
                {
                    Debug.LogError("[BuildingVerify] FAIL: " + Failures[i]);
                }

                Debug.LogError($"[BuildingVerify] 失败 {Failures.Count} 项。");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[BuildingVerify] 全部 PASS。");
            EditorApplication.Exit(0);
        }

        // ---------------- 1. 场景接线 ----------------

        private static void VerifySceneWiring()
        {
            BuildingManager manager = FindManager();
            Check(manager != null, "场景里应有 BuildingManager");

            if (manager == null)
            {
                return;
            }

            BuildingUpgrade upgrade = manager.GetComponent<BuildingUpgrade>();
            BuildingMigration migration = manager.GetComponent<BuildingMigration>();
            BuildingPointerInteraction interaction = manager.GetComponent<BuildingPointerInteraction>();

            Check(upgrade != null, "BuildingManager 上应有 BuildingUpgrade");
            Check(migration != null, "BuildingManager 上应有 BuildingMigration");
            Check(interaction != null, "BuildingManager 上应有 BuildingPointerInteraction");

            if (upgrade != null)
            {
                Check(Get<BuildingManager>(upgrade, "buildingManager") == manager, "BuildingUpgrade.buildingManager 已接线");
                Check(Get<PlayerCurrency>(upgrade, "currency") != null, "BuildingUpgrade.currency 已接线");
            }

            if (migration != null)
            {
                Check(Get<BuildingManager>(migration, "buildingManager") == manager, "BuildingMigration.buildingManager 已接线");
                Check(Get<BuildingGrid>(migration, "grid") != null, "BuildingMigration.grid 已接线");
                Check(Get<Camera>(migration, "viewCamera") != null, "BuildingMigration.viewCamera 已接线");
                Check(Get<GameAreaController>(migration, "areas") != null, "BuildingMigration.areas 已接线");
            }

            if (interaction != null)
            {
                Check(Get<BuildingMigration>(interaction, "migration") == migration, "Interaction.migration 已接线");
                Check(Get<BuildingUpgrade>(interaction, "upgrade") == upgrade, "Interaction.upgrade 已接线");
                Check(Get<GameAreaController>(interaction, "areas") != null, "Interaction.areas 已接线");
            }

            BuildingUpgradeUI upgradeUI = Object.FindFirstObjectByType<BuildingUpgradeUI>(FindObjectsInactive.Include);
            Check(upgradeUI != null, "Canvas 里应有 BuildingUpgradeUI");

            if (upgradeUI != null)
            {
                Check(Get<BuildingUpgrade>(upgradeUI, "upgrade") == upgrade, "升级界面的 upgrade 已接线");
                Check(Get<BuildingMigration>(upgradeUI, "migration") == migration, "升级界面的 migration 已接线");
                Check(Get<Button>(upgradeUI, "upgradeButton") != null, "升级按钮已接线");
                Check(Get<Text>(upgradeUI, "upgradeButtonText") != null, "升级按钮文字已接线");
                Check(Get<Text>(upgradeUI, "levelText") != null, "等级文字已接线");
                Check(Get<BuildingUpgradeModeToggle>(upgradeUI, "modeToggle") != null, "x1/Max 切换块已接线");
            }

            BuildingUpgradeModeToggle toggle =
                Object.FindFirstObjectByType<BuildingUpgradeModeToggle>(FindObjectsInactive.Include);
            Check(toggle != null, "场景里应有 BuildingUpgradeModeToggle");
            Check(toggle != null && Get<Button>(toggle, "button") != null, "切换块的按钮已接线");
            Check(toggle != null && Get<Text>(toggle, "tooltipText") != null, "切换块的悬停说明已接线");

            BuildingMigrationUI migrationUI =
                Object.FindFirstObjectByType<BuildingMigrationUI>(FindObjectsInactive.Include);
            Check(migrationUI != null, "Canvas 里应有 BuildingMigrationUI");

            if (migrationUI != null)
            {
                Check(Get<BuildingMigration>(migrationUI, "migration") == migration, "迁移界面的 migration 已接线");
                Check(Get<Button>(migrationUI, "confirmButton") != null, "迁移确认按钮已接线");
                Check(Get<Button>(migrationUI, "undoButton") != null, "迁移撤回按钮已接线");
                Check(Get<Text>(migrationUI, "statusText") != null, "迁移状态行已接线");
            }

            Check(Shader.Find("Yujian/Building Preview Tint") != null,
                "绿 / 红预览着色器已导入（Yujian/Building Preview Tint）");
            Check(AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Materials/BuildingMigrationPreview.mat") != null,
                "迁移预览材质已生成");

            if (migration != null)
            {
                Check(Get<Material>(migration, "ghostMaterialTemplate") != null,
                    "BuildingMigration.ghostMaterialTemplate 已接线");
            }
        }

        // ---------------- 2. 升级表公式 ----------------

        private static void VerifyUpgradeTable()
        {
            BuildingUpgradeTable table = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTable>(
                "Assets/Data/Buildings/LevelUP_01.asset");

            Check(table != null, "能加载升级表 LevelUP_01");

            if (table == null)
            {
                return;
            }

            Check(table.MaxLevel == 50, $"等级上限 = 50（实际 {table.MaxLevel}）");
            Check(table.Interval == 10, $"升级间隔 = 10（实际 {table.Interval}）");

            Check(Mathf.Abs(table.GetEffectMultiplier(1) - 1f) < 0.0001f,
                $"1 级倍率 = 1（实际 {table.GetEffectMultiplier(1):0.####}）");
            Check(Mathf.Abs(table.GetEffectMultiplier(10) - 1.978f) < 0.001f,
                $"10 级倍率 = 1.978（实际 {table.GetEffectMultiplier(10):0.####}）");
            Check(Mathf.Abs(table.GetEffectMultiplier(50) - 14.844f) < 0.01f,
                $"50 级倍率 = 14.844（实际 {table.GetEffectMultiplier(50):0.####}）");
            Check(table.GetEffectMultiplier(11) > table.GetEffectMultiplier(10),
                "11 级比 10 级更强");

            Check(table.GetUpgradeCost(1) == 15, $"1→2 级费用 = 15（实际 {table.GetUpgradeCost(1)}）");
            Check(table.GetUpgradeCost(9) == 60, $"9→10 级费用 = 60（实际 {table.GetUpgradeCost(9)}）");
            Check(table.GetUpgradeCost(49) == 2816, $"49→50 级费用 = 2816（实际 {table.GetUpgradeCost(49)}）");
            Check(table.GetUpgradeCost(50) == 0, "满级后费用 = 0");

            Check(table.GetMaxModeTargetLevel(7) == 10, "Max：7 级 → 10 级");
            Check(table.GetMaxModeTargetLevel(10) == 20, "Max：10 级 → 20 级");
            Check(table.GetMaxModeTargetLevel(50) == 50, "Max：满级 → 50 级");
            Check(table.GetMaxModeTargetLevel(45) == 50, "Max：45 级 → 50 级（不超过上限）");

            Check(table.GetAffordableLevelCount(1, 10, 0) == 0, "没钱时一级都升不了");
            Check(table.GetAffordableLevelCount(1, 10, 1000) == 9, "1000 金币从 1 级能连升到 10 级");

            string[] paths =
            {
                "Assets/Data/Buildings/Building_风琴博物馆.asset",
                "Assets/Data/Buildings/Building_八卦楼.asset",
                "Assets/Data/Buildings/Building_菽庄花园.asset",
                "Assets/Data/Buildings/Building_日光岩.asset",
            };

            for (int i = 0; i < paths.Length; i++)
            {
                BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(paths[i]);
                Check(data != null && data.UpgradeTable == table,
                    $"{System.IO.Path.GetFileNameWithoutExtension(paths[i])} 已挂上升级表");
            }
        }

        // ---------------- 3. 升级逻辑 ----------------

        private static void VerifyUpgradeLogic()
        {
            BuildingManager manager = FindManager();
            BuildingGrid grid = FindGrid();

            if (manager == null || grid == null)
            {
                Check(false, "升级逻辑验证缺少 BuildingManager / BuildingGrid");
                return;
            }

            grid.Rebuild();

            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(
                "Assets/Data/Buildings/Building_风琴博物馆.asset");
            BuildingUpgrade upgrade = manager.GetComponent<BuildingUpgrade>();
            BuildingUpgradeTable table = data != null ? data.UpgradeTable : null;
            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);

            Check(data != null && table != null && upgrade != null && currency != null,
                "升级逻辑依赖齐全");

            if (data == null || table == null || upgrade == null || currency == null)
            {
                return;
            }

            if (!TryFindFreeRect(grid, data.FootprintCells, default, out RectInt rect))
            {
                Check(false, "网格里找不到可建的空地，升级逻辑无法验证");
                return;
            }

            if (!manager.RestoreBuilding(data, rect, new List<MaterialColor>(), 3, out string error))
            {
                Check(false, "恢复一座 3 级建筑失败：" + error);
                return;
            }

            Check(manager.PlacedBuildings.Count == 1, "恢复后场上有 1 座建筑");

            GameObject instance = manager.PlacedBuildings[0];
            PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

            Check(info != null && info.Level == 3, $"恢复的建筑是 3 级（实际 {info?.Level}）");
            Check(upgrade.Select(instance), "能选中建筑进入升级");
            Check(upgrade.CurrentLevel == 3, "升级界面读到 3 级");
            Check(upgrade.MaxLevel == 50, "升级界面读到上限 50");
            Check(upgrade.NextCost == table.GetUpgradeCost(3), "下一级费用与升级表一致");

            currency.RestoreMoney(1000);
            int moneyBefore = currency.CurrentMoney;
            int cost = table.GetUpgradeCost(3);

            Check(upgrade.TryUpgrade(BuildingUpgradeMode.OneLevel, out string message),
                "x1 模式升级成功：" + message);
            Check(info.Level == 4, $"x1 后等级 3 → 4（实际 {info.Level}）");
            Check(currency.CurrentMoney == moneyBefore - cost,
                $"x1 扣了 {cost} 金币（实际 {moneyBefore - currency.CurrentMoney}）");

            int moneyBeforeMax = currency.CurrentMoney;
            int expectedMaxCost = table.GetTotalCost(4, 10);

            Check(upgrade.TryUpgrade(BuildingUpgradeMode.MaxToInterval, out string maxMessage),
                "Max 模式升级成功：" + maxMessage);
            Check(info.Level == 10, $"Max 后 4 → 10 级（实际 {info.Level}）");
            Check(currency.CurrentMoney == moneyBeforeMax - expectedMaxCost,
                $"Max 扣了 {expectedMaxCost} 金币（实际 {moneyBeforeMax - currency.CurrentMoney}）");

            // 钱足够时反复 Max，最终停在上限
            currency.RestoreMoney(1000000);

            for (int i = 0; i < 10 && !upgrade.IsMaxed; i++)
            {
                upgrade.TryUpgrade(BuildingUpgradeMode.MaxToInterval, out _);
            }

            Check(upgrade.IsMaxed, $"连升后到达上限（实际 {info.Level}）");
            Check(info.Level == 50, $"上限等级 = 50（实际 {info.Level}）");
            Check(!upgrade.TryUpgrade(BuildingUpgradeMode.OneLevel, out string maxedMessage),
                "满级后不能再升");
            Check(maxedMessage == "已经满级了", $"满级提示文案正确（实际「{maxedMessage}」）");
        }

        // ---------------- 4. 迁移 ----------------

        private static void VerifyMigration()
        {
            BuildingManager manager = FindManager();
            BuildingGrid grid = FindGrid();
            BuildingMigration migration = manager != null ? manager.GetComponent<BuildingMigration>() : null;

            if (manager == null || grid == null || migration == null || manager.PlacedBuildings.Count == 0)
            {
                Check(false, "迁移验证缺少建筑 / 网格 / 迁移组件");
                return;
            }

            GameObject instance = manager.PlacedBuildings[0];
            PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();
            RectInt startRect = info.FootprintRect;
            Vector3 startPosition = instance.transform.position;
            float cell = grid.CellSize;

            // ---------- 1. 绿 / 红外表：迁移开始换材质，撤回换回 ----------

            List<Renderer> meshRenderers = CollectMeshRenderers(instance);
            var originals = new Dictionary<Renderer, Material[]>();

            for (int i = 0; i < meshRenderers.Count; i++)
            {
                originals[meshRenderers[i]] = meshRenderers[i].sharedMaterials;
            }

            Check(migration.Begin(instance), "能进入迁移状态");
            Check(migration.State == BuildingMigrationState.Dragging, "迁移状态 = 拖动中");
            Check(migration.IsValid,
                $"原位置上时是绿色（实际 {(migration.IsValid ? "绿" : "红：" + migration.StatusReason)}）");
            Check(CountGhostRenderers(instance) == meshRenderers.Count,
                $"迁移中所有网格都换成了预览材质（{CountGhostRenderers(instance)}/{meshRenderers.Count}）");

            Color? ghostColor = GetGhostColor(instance);
            Check(ghostColor.HasValue && ghostColor.Value.g > ghostColor.Value.r,
                "可放置时预览色偏绿");

            migration.Revert();
            Check(migration.State == BuildingMigrationState.Idle, "撤回后退出迁移");
            Check((instance.transform.position - startPosition).sqrMagnitude < 0.0001f, "撤回后建筑回到原位");
            Check(info.FootprintRect.Equals(startRect), "撤回后占格记录不变");
            Check(OriginalMaterialsRestored(meshRenderers, originals), "撤回后恢复原材质");

            // ---------- 2. 自由跟手，确认时才吸附 ----------

            if (!TryFindFreeRect(grid, info.Data.FootprintCells, startRect, out RectInt freeRect))
            {
                Check(false, "找不到第二个空地，迁移无法验证");
                return;
            }

            Vector3 freePoint = grid.CellRectCenter(freeRect) + new Vector3(cell * 0.37f, 0f, cell * 0.29f);

            Check(migration.Begin(instance), "能再次进入迁移");

            Vector3 beforeDrag = instance.transform.position;

            Check(migration.DragToGroundPoint(freePoint), "拖到空地时判为可放置（绿）");
            Check(migration.IsValid, "拖到空地后 IsValid = true");
            Check(Vector3.Distance(instance.transform.position, beforeDrag) < 0.0001f,
                "设置目标时建筑不瞬移，交给 TickVisual 平滑过渡");

            RectInt expectedRect = migration.CurrentRect;
            Vector3 expectedCenter = migration.SnappedCenter;

            Check(Vector3.Distance(expectedCenter, freePoint) > 0.1f,
                "吸附点与指针自由位置不是同一个点（网格吸附确实生效）");

            migration.TickVisual(0.02f);

            Check(Vector3.Distance(instance.transform.position, beforeDrag) > 0.0001f,
                "TickVisual 让建筑开始朝吸附点移动");
            Check(Vector3.Distance(instance.transform.position, expectedCenter) > 0.1f,
                "一小段时间后还没到吸附点（不是硬跳）");

            migration.TickVisual(2f);

            Check(Vector3.Distance(instance.transform.position, expectedCenter) < 0.01f,
                "持续 TickVisual 后平滑收敛到吸附点（对齐网格）");
            Check(Vector3.Distance(instance.transform.position, freePoint) > 0.1f,
                "最终位置是格点，不是指针自由位置");

            migration.EndDrag();
            Check(migration.IsPendingConfirm, "松开后进入待确认");
            Check(Vector3.Distance(instance.transform.position, expectedCenter) < 0.01f,
                "松开后建筑收在吸附点上，但占格还没提交");
            Check(info.FootprintRect.Equals(startRect), "待确认阶段旧占格仍在（没有提前登记）");
            Check(migration.CanConfirm, "绿色时确认按钮可用");

            Check(migration.Confirm(out string confirmMessage), "确认迁移成功：" + confirmMessage);
            Check(info.FootprintRect.Equals(expectedRect),
                $"确认后占格 = 拖动时算出的 {expectedRect}（实际 {info.FootprintRect}）");
            Check((instance.transform.position - expectedCenter).sqrMagnitude < 0.0001f,
                "确认后建筑才吸附到格子中心");
            Check(grid.CanPlace(startRect, out _), "旧占格已释放");
            Check(!grid.CanPlace(expectedRect, out _), "新占格已登记");

            // ---------- 3. 红色：目标格被别的建筑占住 ----------

            Vector3 confirmedPosition = instance.transform.position;
            RectInt confirmedRect = info.FootprintRect;

            if (!TryFindFreeRect(grid, info.Data.FootprintCells, confirmedRect, out RectInt blockedRect))
            {
                Check(false, "找不到第三块空地做占格冲突测试");
                return;
            }

            grid.Occupy(blockedRect, 987654);
            Vector3 blockedCenter = grid.CellRectCenter(blockedRect);

            Check(migration.Begin(instance), "冲突测试：能进入迁移");
            Check(!migration.DragToGroundPoint(blockedCenter), "拖到被占的格子时判为不可放置（红）");
            Check(!migration.IsValid, "拖到被占的格子后 IsValid = false");
            Check(migration.StatusReason != null && migration.StatusReason.Contains("占用"),
                $"红色原因说明格子被占用（实际「{migration.StatusReason}」）");

            Color? invalidColor = GetGhostColor(instance);
            Check(invalidColor.HasValue && invalidColor.Value.r > invalidColor.Value.g,
                "不可放置时预览色偏红");

            migration.TickVisual(2f);
            Check(Vector2.Distance(
                      new Vector2(instance.transform.position.x, instance.transform.position.z),
                      new Vector2(blockedCenter.x, blockedCenter.z)) < 0.01f,
                "红色状态下建筑仍跟到指针位置，不会卡在最后一个合法位置");

            migration.EndDrag();
            Check(!migration.CanConfirm, "红色时确认按钮不可用");
            Check(!migration.Confirm(out string blockedMessage), "红色时确认被拒绝");
            Check(blockedMessage != null && blockedMessage.Contains("不可放置"),
                $"拒绝文案说明位置不可放置（实际「{blockedMessage}」）");
            Check((instance.transform.position - blockedCenter).sqrMagnitude < 0.0001f,
                "确认被拒绝后建筑停在松手位置，不吸附也不回原位");

            grid.Release(987654);

            migration.Revert();
            Check((instance.transform.position - confirmedPosition).sqrMagnitude < 0.0001f,
                "撤回后回到本次迁移之前的位置");
            Check(info.FootprintRect.Equals(confirmedRect), "撤回后占格记录不变");

            // ---------- 4. 待确认时按住建筑可以接着拖 ----------

            Check(migration.Begin(instance), "继续拖测试：能进入迁移");
            migration.EndDrag();
            Check(migration.IsPendingConfirm, "继续拖测试：松开后待确认");
            Check(migration.ResumeDrag(), "待确认时按建筑能继续拖，不用重新长按");
            Check(migration.IsDragging, "继续拖后状态 = 拖动中");
            Check(migration.DragToGroundPoint(grid.CellRectCenter(blockedRect)),
                "继续拖到空地后判为可放置");
            migration.TickVisual(2f);
            migration.EndDrag();
            Check(migration.Confirm(out string resumeMessage), "继续拖之后能确认：" + resumeMessage);
            Check(migration.State == BuildingMigrationState.Idle, "继续拖并确认后退出迁移");

            RectInt rectAfterResume = info.FootprintRect;

            // ---------- 5. BuildingManager 层的占格校验 ----------

            if (!TryFindFreeRect(grid, info.Data.FootprintCells, info.FootprintRect, out RectInt managerBlockedRect))
            {
                Check(false, "找不到空地做 BuildingManager 占格解冲突测试");
                return;
            }

            grid.Occupy(managerBlockedRect, 987654);
            bool rejected = !manager.TryMoveBuilding(
                instance, grid.CellRectCenter(managerBlockedRect), managerBlockedRect, out string managerError);
            grid.Release(987654);

            Check(rejected, "目标格被占时 TryMoveBuilding 拒绝");
            Check(managerError != null && managerError.Contains("格子已被占用"),
                $"拒绝原因说明占格冲突（实际「{managerError}」）");
            Check(info.FootprintRect.Equals(rectAfterResume), "被拒绝后占格记录没变");
        }

        // ---------------- 4. 蓝图吸附过渡（与迁移同一套） ----------------

        private static void VerifyBlueprintSmoothSnap()
        {
            BuildingManager manager = FindManager();
            BuildingGrid grid = FindGrid();
            BuildingPlacement placement =
                Object.FindFirstObjectByType<BuildingPlacement>(FindObjectsInactive.Include);

            Check(manager != null && grid != null, "蓝图吸附验证所需的 BuildingManager / BuildingGrid 都已就位");
            Check(placement != null, "场景里应有 BuildingPlacement");

            if (manager == null || grid == null || placement == null)
            {
                return;
            }

            Check(Get<BuildingManager>(placement, "buildingManager") == manager,
                "BuildingPlacement.buildingManager 已接线");

            BuildingData data = FindBlueprintData();

            if (data == null)
            {
                Check(false, "Assets/Data/Buildings 里没有配了 Blueprint Prefab 的 BuildingData");
                return;
            }

            // 直接实例化蓝图并临时挂到 manager 上：只验证 TickVisual，不走 SelectBuilding 的挖矿屏蔽
            Vector2 worldFootprint = grid.CellsToWorldFootprint(data.FootprintCells);
            GameObject blueprintObject = Object.Instantiate(data.BlueprintPrefab);
            BuildingBlueprint blueprint = blueprintObject.GetComponent<BuildingBlueprint>();

            if (blueprint == null)
            {
                Object.DestroyImmediate(blueprintObject);
                Check(false, "蓝图 Prefab 根物体上没有 BuildingBlueprint");
                return;
            }

            blueprint.Initialize(data, worldFootprint);

            FieldInfo blueprintField = typeof(BuildingManager).GetField(
                "currentBlueprint", BindingFlags.Instance | BindingFlags.NonPublic);

            if (blueprintField == null)
            {
                Object.DestroyImmediate(blueprintObject);
                Check(false, "BuildingManager.currentBlueprint 字段不存在");
                return;
            }

            object previousBlueprint = blueprintField.GetValue(manager);
            blueprintField.SetValue(manager, blueprint);

            SerializedObject placementSerialized = new SerializedObject(placement);
            SerializedProperty speedProperty = placementSerialized.FindProperty("snapMoveSpeed");
            float defaultSpeed = speedProperty != null ? speedProperty.floatValue : -1f;

            Check(speedProperty != null, "BuildingPlacement.snapMoveSpeed 已序列化");
            Check(defaultSpeed > 0f, $"蓝图吸附速度默认大于 0（实际 {defaultSpeed}）");

            Vector3 start = blueprint.transform.position;
            Vector3 targetPoint = start + new Vector3(grid.CellSize * 3f, 0f, grid.CellSize * 2f);
            SetPrivateField(placement, "visualTarget", targetPoint);

            placement.TickVisual(0f);
            Check((blueprint.transform.position - start).sqrMagnitude < 0.0001f,
                "dt = 0 时蓝图不动");

            placement.TickVisual(0.01f);
            Check(Vector3.Distance(blueprint.transform.position, start) > 0.001f,
                "蓝图开始朝吸附点移动");
            Check(Vector3.Distance(blueprint.transform.position, targetPoint) > 0.1f,
                "一小段时间后还没到吸附点（不是硬跳）");

            placement.TickVisual(2f);
            Check(Vector3.Distance(blueprint.transform.position, targetPoint) < 0.01f,
                "持续 TickVisual 后平滑收敛到吸附点");

            if (speedProperty != null)
            {
                speedProperty.floatValue = 0f;
                placementSerialized.ApplyModifiedPropertiesWithoutUndo();

                blueprint.transform.position = start;
                placement.TickVisual(0f);
                Check(Vector3.Distance(blueprint.transform.position, targetPoint) < 0.0001f,
                    "速度为 0 = 直接吸附（与迁移一致）");

                placementSerialized.Update();
                speedProperty.floatValue = defaultSpeed;
                placementSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            blueprintField.SetValue(manager, previousBlueprint);
            Object.DestroyImmediate(blueprintObject);
        }

        /// <summary>收集会被迁移换材质的那批 Renderer（与 BuildingMigration 的排除规则一致）。</summary>
        private static List<Renderer> CollectMeshRenderers(GameObject target)
        {
            var result = new List<Renderer>();
            Renderer[] all = target.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];

                if (renderer == null ||
                    renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                result.Add(renderer);
            }

            return result;
        }

        private static int CountGhostRenderers(GameObject target)
        {
            Renderer[] all = target.GetComponentsInChildren<Renderer>(true);
            int count = 0;

            for (int i = 0; i < all.Length; i++)
            {
                Material material = all[i] != null ? all[i].sharedMaterial : null;

                if (material != null && material.shader != null &&
                    material.shader.name == "Yujian/Building Preview Tint")
                {
                    count++;
                }
            }

            return count;
        }

        private static Color? GetGhostColor(GameObject target)
        {
            Renderer[] all = target.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < all.Length; i++)
            {
                Material material = all[i] != null ? all[i].sharedMaterial : null;

                if (material != null && material.shader != null &&
                    material.shader.name == "Yujian/Building Preview Tint" &&
                    material.HasProperty("_GhostColor"))
                {
                    return material.GetColor("_GhostColor");
                }
            }

            return null;
        }

        private static bool OriginalMaterialsRestored(
            List<Renderer> renderers, Dictionary<Renderer, Material[]> expected)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null || !expected.TryGetValue(renderer, out Material[] materials))
                {
                    return false;
                }

                Material[] current = renderer.sharedMaterials;

                if (current.Length != materials.Length)
                {
                    return false;
                }

                for (int j = 0; j < current.Length; j++)
                {
                    if (current[j] != materials[j])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // ---------------- 5. 存档 ----------------

        private static void VerifySave()
        {
            BuildingManager manager = FindManager();
            SaveSystem save = Object.FindFirstObjectByType<SaveSystem>(FindObjectsInactive.Include);
            BuildingSaveParticipant participant =
                save != null ? save.GetComponent<BuildingSaveParticipant>() : null;

            Check(participant != null, "SaveSystem 上应有 BuildingSaveParticipant");

            if (manager == null || participant == null || manager.PlacedBuildings.Count == 0)
            {
                return;
            }

            PlacedBuildingInfo info = manager.PlacedBuildings[0].GetComponent<PlacedBuildingInfo>();
            SaveData data = new SaveData();
            participant.Capture(data);

            Check(data.buildings.Count == 1, $"存档写入 1 座建筑（实际 {data.buildings.Count}）");

            if (data.buildings.Count == 0)
            {
                return;
            }

            BuildingSave entry = data.buildings[0];
            Check(entry.level == info.Level, $"存档等级 = {info.Level}（实际 {entry.level}）");
            Check(entry.x == info.FootprintRect.x && entry.y == info.FootprintRect.y &&
                  entry.width == info.FootprintRect.width && entry.height == info.FootprintRect.height,
                $"存档占格 = {info.FootprintRect}（迁移后的新位置）");
        }

        /// <summary>旧存档没有 level 字段（0）时应恢复成 1 级。</summary>
        private static void VerifyOldSaveLevelDefault()
        {
            BuildingManager manager = FindManager();
            SaveSystem save = Object.FindFirstObjectByType<SaveSystem>(FindObjectsInactive.Include);
            BuildingSaveParticipant participant =
                save != null ? save.GetComponent<BuildingSaveParticipant>() : null;
            BuildingGrid grid = FindGrid();

            if (participant == null || grid == null)
            {
                return;
            }

            SaveCatalog catalog = Get<SaveCatalog>(participant, "catalog");

            if (catalog == null)
            {
                Check(false, "BuildingSaveParticipant.catalog 未接线");
                return;
            }

            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(
                "Assets/Data/Buildings/Building_八卦楼.asset");
            string buildingId = catalog.GetBuildingId(data);

            if (string.IsNullOrEmpty(buildingId))
            {
                Check(false, "SaveCatalog 里找不到八卦楼的 ID");
                return;
            }

            if (!TryFindFreeRect(grid, data.FootprintCells, default, out RectInt rect))
            {
                Check(false, "找不到空地恢复旧存档建筑");
                return;
            }

            SaveData oldData = new SaveData();

            oldData.buildings.Add(new BuildingSave
            {
                buildingId = buildingId,
                x = rect.x,
                y = rect.y,
                width = rect.width,
                height = rect.height,
                level = 0,
            });

            participant.Restore(oldData);

            PlacedBuildingInfo restored = null;

            for (int i = 0; i < manager.PlacedBuildings.Count; i++)
            {
                PlacedBuildingInfo candidate = manager.PlacedBuildings[i].GetComponent<PlacedBuildingInfo>();

                if (candidate != null && candidate.Data == data)
                {
                    restored = candidate;
                    break;
                }
            }

            Check(restored != null, "旧存档里的建筑已恢复");

            if (restored != null)
            {
                Check(restored.Level == 1, $"旧存档缺等级时按 1 级恢复（实际 {restored.Level}）");
            }
        }

        // ---------------- 工具 ----------------

        private static BuildingManager FindManager()
        {
            return Object.FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
        }

        private static BuildingGrid FindGrid()
        {
            return Object.FindFirstObjectByType<BuildingGrid>(FindObjectsInactive.Include);
        }

        private static bool TryFindFreeRect(
            BuildingGrid grid, Vector2Int size, RectInt avoid, out RectInt rect)
        {
            Vector2Int counts = grid.CellCounts;

            for (int y = 0; y <= counts.y - size.y; y++)
            {
                for (int x = 0; x <= counts.x - size.x; x++)
                {
                    RectInt candidate = new RectInt(x, y, size.x, size.y);

                    if (avoid.width > 0 && candidate.Overlaps(avoid))
                    {
                        continue;
                    }

                    if (grid.CanPlace(candidate, out _))
                    {
                        rect = candidate;
                        return true;
                    }
                }
            }

            rect = default;
            return false;
        }

        /// <summary>找一个配了蓝图 Prefab 的建筑数据，供蓝图吸附验证用。</summary>
        private static BuildingData FindBlueprintData()
        {
            string[] guids = AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/Data/Buildings" });

            for (int i = 0; i < guids.Length; i++)
            {
                BuildingData data =
                    AssetDatabase.LoadAssetAtPath<BuildingData>(AssetDatabase.GUIDToAssetPath(guids[i]));

                if (data != null && data.BlueprintPrefab != null && data.HasValidFootprint)
                {
                    return data;
                }
            }

            return null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                Check(false, $"{target.GetType().Name}.{fieldName} 字段不存在");
                return;
            }

            field.SetValue(target, value);
        }

        private static T Get<T>(Object target, string fieldName) where T : Object
        {
            if (target == null)
            {
                return null;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static void Check(bool condition, string message)
        {
            checks++;

            if (condition)
            {
                Debug.Log("[BuildingVerify] PASS: " + message);
                return;
            }

            Failures.Add(message);
        }
    }
}
