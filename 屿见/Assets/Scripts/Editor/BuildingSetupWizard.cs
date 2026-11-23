using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Mining;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 3 建筑系统的一键配置工具。
    ///
    /// 为什么用编辑器脚本而不是直接改 .unity 文件：
    /// Unity 打开着场景时，从外部改动磁盘上的 .unity 文件会让编辑器整场重载，
    /// 编辑器里尚未保存的改动会一起丢失。让 Unity 自己写就没有这个问题。
    ///
    /// 本工具可重复运行：已存在的物体会被复用，按钮会先清空再重建，不会越堆越多。
    /// 运行入口：菜单栏 → 屿见 → 配置建筑系统（阶段 3）
    /// </summary>
    public static class BuildingSetupWizard
    {
        private const string BuildingFolder = "Assets/Data/Buildings";
        private const string SystemObjectName = "BuildingSystem";
        private const string PanelObjectName = "BuildingPanel";
        private const string BlueprintActionPanelName = "蓝图按钮";

        /// <summary>
        /// 建筑面板里的按钮顺序（也就是玩家看到的顺序）。
        /// **新增建筑时把文件名追加到这里**：面板、BuildingUI.availableBuildings 与按钮下标一次配齐。
        /// 回收站固定第一（启动入口），红砖厝第二；其余建筑保持原顺序。
        /// </summary>
        private static readonly string[] BuildingFileNames =
        {
            "Building_回收站.asset",
            "Building_红砖厝.asset",
            "Building_风琴博物馆.asset",
            "Building_八卦楼.asset",
            "Building_菽庄花园.asset",
            "Building_日光岩.asset",
            "Building_海天堂构.asset",
        };

        /// <summary>建筑面板顶部的里程碑进度行。名字固定，里程碑 UI 会按它找文本。</summary>
        private const string ProgressTextName = "MilestoneProgressText";
        private const float ProgressTextHeight = 44f;

        // 以下坐标全部读自 SampleScene.unity 的真实值，不是估的
        private static readonly Vector3 MapCenter = new Vector3(10.21735f, 0f, -9.49205f);
        private static readonly Vector3 BuildingCameraPosition = new Vector3(10.21735f, 49.15f, -43.9f);
        private static readonly Vector3 BuildingCameraEuler = new Vector3(55f, 0f, 0f);

        private const float ButtonHeight = 80f;
        private const float ButtonSpacing = 10f;

        // ---------- 滚动面板尺寸 ----------
        // 建筑数量会一直增加，面板不能无限长：中间一段做成 ScrollRect，
        // 顶部进度行与底部两个操作按钮固定，建筑按钮在滚动区里上下浏览（鼠标滚轮）。
        private const string ScrollViewName = "Scroll View";
        private const float PanelWidth = 340f;
        private const float PanelHeight = 640f;
        private const float PanelBottomMargin = 10f;
        private const float ScrollBarWidth = 8f;
        private const float ScrollBarGap = 4f;
        private const float ButtonHorizontalPadding = 16f;

        /// <summary>底部固定区：退出建造 + 间隔 + 取消蓝图 + 底边距 + 与滚动区的间隔。</summary>
        private const float ActionAreaHeight = PanelBottomMargin + ButtonHeight + ButtonSpacing + ButtonHeight + ButtonSpacing;

        [MenuItem("屿见/建筑/配置建筑系统（阶段 3）", false, 510)]
        public static void Configure()
        {
            // ---------- 前置检查：任何一项不满足就整体不执行，避免留下半配置状态 ----------

            List<BuildingData> buildings = LoadBuildings();

            if (buildings.Count == 0)
            {
                Fail($"在 {BuildingFolder} 下找不到任何建筑数据资产，无法创建建筑按钮。\n\n" +
                     "请确认 BuildingFileNames 里列出的资产存在且已导入。");
                return;
            }

            BuildingData data = buildings[0];

            int buildingLayer = LayerMask.NameToLayer("Building");

            if (buildingLayer < 0)
            {
                Fail("工程里没有名为 Building 的图层。\n\n" +
                     "请先到 Project Settings → Tags and Layers 添加一个 Building 层，再运行本命令。");
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Fail("场景里找不到带 MainCamera 标签的相机，无法配置视角与射线。");
                return;
            }

            Canvas canvas = ResolveBuildingCanvas(out Transform existingPanel);

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法创建建筑按钮。");
                return;
            }

            Transform templateButton = FindTemplateButton(canvas);

            if (templateButton == null)
            {
                Fail("Canvas 里没有任何现成的 Button 可以当模板复制。\n\n" +
                     "请先在 Canvas 下手动建一个 Button，再运行本命令。");
                return;
            }

            // ---------- 1. BuildingSystem 物体与组件 ----------

            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>();
            GameObject systemObject;

            if (manager == null)
            {
                systemObject = new GameObject(SystemObjectName);
                Undo.RegisterCreatedObjectUndo(systemObject, "创建 " + SystemObjectName);
                manager = systemObject.AddComponent<BuildingManager>();
            }
            else
            {
                systemObject = manager.gameObject;
            }

            Transform buildingRoot = FindOrCreateChild(systemObject.transform, "Building Root");
            Transform blueprintRoot = FindOrCreateChild(systemObject.transform, "Blueprint Root");
            BuildingPlacement placement = EnsureComponent<BuildingPlacement>(systemObject);
            BuildingCameraDirector director = EnsureComponent<BuildingCameraDirector>(systemObject);
            BuildingUI ui = EnsureComponent<BuildingUI>(systemObject);

            MiningInput miningInput = Object.FindFirstObjectByType<MiningInput>();

            // ---------- 2. BuildingManager ----------

            SerializedObject managerSo = new SerializedObject(manager);
            SetObject(managerSo, "miningInput", miningInput);
            SetInt(managerSo, "buildingLayers", 1 << buildingLayer);
            SetVector3(managerSo, "defaultSpawnPosition", MapCenter);
            SetObject(managerSo, "buildingRoot", buildingRoot);
            SetObject(managerSo, "blueprintRoot", blueprintRoot);
            SetObject(managerSo, "testBuilding", data);
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. BuildingPlacement ----------

            SerializedObject placementSo = new SerializedObject(placement);
            SetObject(placementSo, "buildingManager", manager);
            SetObject(placementSo, "viewCamera", mainCamera);
            SetFloat(placementSo, "groundHeight", 0f);
            SetFloat(placementSo, "overlapBoxHeight", 4f);
            SetBool(placementSo, "blockClicksOverUI", true);
            placementSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. BuildingCameraDirector ----------

            SerializedObject directorSo = new SerializedObject(director);
            SetObject(directorSo, "cameraTransform", mainCamera.transform);
            SetObject(directorSo, "buildingManager", manager);
            SetBool(directorSo, "enterAutomatically", true);
            SetVector3(directorSo, "buildingModePosition", BuildingCameraPosition);
            SetVector3(directorSo, "buildingModeEulerAngles", BuildingCameraEuler);
            SetFloat(directorSo, "transitionDuration", 0.6f);
            directorSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 5. BuildingUI 的引用与建筑清单 ----------

            SerializedObject uiSo = new SerializedObject(ui);
            SetObject(uiSo, "buildingManager", manager);
            SetObject(uiSo, "cameraDirector", director);

            SerializedProperty list = uiSo.FindProperty("availableBuildings");

            if (list != null)
            {
                list.arraySize = buildings.Count;

                for (int i = 0; i < buildings.Count; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = buildings[i];
                }
            }
            else
            {
                Debug.LogWarning("[BuildingSetupWizard] 找不到 BuildingUI.availableBuildings 字段，" +
                                 "建筑清单需要手动填写。");
            }

            uiSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 6. 按钮面板 ----------

            BuildButtonPanel(canvas, existingPanel, templateButton, ui, buildings);

            // ---------- 7. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            Selection.activeGameObject = systemObject;
            EditorGUIUtility.PingObject(systemObject);

            // 自动保存：脚本重编译/进入 Play 的域重载会丢弃未保存的场景改动，
            // 之前出现过「向导跑完没保存 → 域重载后回到旧面板 → 以为滚动没做」的情况。
            // 本向导是显式的场景配置命令，这里直接把场景落盘。
            if (!EditorApplication.isPlaying)
            {
                EditorSceneManager.SaveScene(systemObject.scene);
            }

            Debug.Log($"[BuildingSetupWizard] 配置完成并已保存场景：\n" +
                      $"  · {SystemObjectName}：BuildingManager / BuildingPlacement / BuildingCameraDirector / BuildingUI\n" +
                      $"  · Canvas/{PanelObjectName}：{buildings.Count} 个建筑按钮（滚动区，鼠标滚轮）+ " +
                      $"取消蓝图 / 退出建造，顶部进度行「主要建筑 0/3」");
        }

        /// <summary>
        /// 在 Canvas 右下角建按钮面板。
        /// 位置选右下是因为商店面板占的是左下（anchoredPosition 280,400，尺寸 520×760）。
        ///
        /// 2026-11-20 改成滚动菜单：建筑数量一直在加（现在 6 座），
        /// 面板不能再「每多一座就长高一截」，否则最后几座会被屏幕边缘吃掉。
        /// 结构：
        ///   BuildingPanel（固定 340×640）
        ///   ├─ 里程碑进度行（固定顶部）
        ///   ├─ Scroll View / Viewport / Content（建筑按钮，可上下滚动，鼠标滚轮）
        ///   ├─ Button_取消蓝图（固定底部）
        ///   └─ Button_退出建造（固定底部）
        /// </summary>
        private static void BuildButtonPanel(
            Canvas canvas, Transform existingPanel, Transform templateButton, BuildingUI ui,
            IReadOnlyList<BuildingData> buildings)
        {
            GameObject panel;

            if (existingPanel != null)
            {
                panel = existingPanel.gameObject;
            }
            else
            {
                Transform existing = canvas.transform.Find(PanelObjectName);

                if (existing != null)
                {
                    panel = existing.gameObject;
                }
                else
                {
                    panel = new GameObject(PanelObjectName, typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(panel, "创建 " + PanelObjectName);
                }
            }

            RectTransform panelRect = panel.GetComponent<RectTransform>();

            if (panelRect != null)
            {
                // 之前 FindFirstObjectByType<Canvas>() 可能命中 MiningHUDRuntime 那个子 Canvas，
                // 结果建筑面板被建到另一个 Canvas 下，和主 UI 里的旧面板同时显示（两层 UI）。
                // 这里强制把它放回解析出的目标 Canvas 下。
                if (panelRect.parent != canvas.transform)
                {
                    panelRect.SetParent(canvas.transform, false);
                }

                panelRect.anchorMin = new Vector2(1f, 0f);
                panelRect.anchorMax = new Vector2(1f, 0f);
                panelRect.pivot = new Vector2(1f, 0f);
                panelRect.anchoredPosition = new Vector2(-30f, 160f);
                panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            }

            // 全透明的射线接收面：没有它，滚轮落在面板空白处时既不会滚列表，
            // 还会被建造相机当成「指针不在 UI 上」而缩放镜头
            Image panelRaycastSurface = EnsureComponent<Image>(panel);

            if (panelRaycastSurface != null)
            {
                panelRaycastSurface.color = new Color(0f, 0f, 0f, 0f);
                panelRaycastSurface.raycastTarget = true;
            }

            // 重复运行时先清掉旧内容，否则每跑一次就多一批。
            // 进度行要留下：里程碑 UI 组件持有它的引用，重建会让引用变成 Missing。
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = panel.transform.GetChild(i);

                if (child.name == ProgressTextName)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(child.gameObject);
            }

            // ---------- 顶部进度行 ----------

            bool createdProgressText = panel.transform.Find(ProgressTextName) == null;
            Text progressText = FindOrCreateTextChild(panel.transform, ProgressTextName);
            RectTransform progressRect = progressText.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0f, 1f);
            progressRect.anchorMax = new Vector2(1f, 1f);
            progressRect.pivot = new Vector2(0.5f, 1f);
            progressRect.anchoredPosition = Vector2.zero;
            progressRect.sizeDelta = new Vector2(0f, ProgressTextHeight);

            progressText.font = BuiltinFont();
            progressText.fontSize = 26;
            progressText.alignment = TextAnchor.MiddleCenter;
            progressText.color = Color.white;
            progressText.raycastTarget = false;

            if (createdProgressText)
            {
                // 只是新建时的占位文案；真实数字由 MilestonePanelUI 写入。
                // 重跑本向导时不覆盖，避免把已刷新的进度打回 0/3。
                progressText.text = "主要建筑 0/3";
            }

            // ---------- 滚动区 ----------

            ScrollRect scrollRect = BuildScrollView(panel.transform, templateButton, ui, buildings);

            // ---------- 底部固定操作按钮 ----------

            CreateButton(panel.transform, templateButton, "Button_取消蓝图", "取消蓝图",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, PanelBottomMargin + ButtonHeight + ButtonSpacing),
                new Vector2(-ButtonHorizontalPadding, ButtonHeight),
                button => UnityEventTools.AddPersistentListener(button.onClick, ui.CancelSelection));

            CreateButton(panel.transform, templateButton, "Button_退出建造", "退出建造",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, PanelBottomMargin),
                new Vector2(-ButtonHorizontalPadding, ButtonHeight),
                button => UnityEventTools.AddPersistentListener(button.onClick, ui.ExitBuildMode));

            // ---------- 滚轮 ----------

            ScrollWheelRelay relay = EnsureComponent<ScrollWheelRelay>(panel);

            if (relay != null)
            {
                relay.Configure(scrollRect, 0.12f);
            }

            // ---------- 清理重复面板 ----------

            // 之前跑向导时 FindFirstObjectByType<Canvas>() 可能选中了 MiningHUDRuntime，
            // 于是又建了一个 BuildingPanel；旧面板还在主 Canvas 上，两层同时显示。
            // 这里保留当前这一块，删掉场景里其余的 BuildingPanel。
            CleanupDuplicatePanels(panel.transform);
            EnsureAreaPanelReference(panel);
        }

        /// <summary>
        /// 解析建筑面板应该挂在哪个 Canvas。
        /// 优先用 GameAreaController.buildingAreaPanels 里引用的那个面板所在的 Canvas：
        /// 区域系统靠这份引用显隐面板，必须原地重建，引用才不会丢。
        /// </summary>
        private static Canvas ResolveBuildingCanvas(out Transform existingPanel)
        {
            existingPanel = null;

            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);

            if (areas != null)
            {
                SerializedObject serialized = new SerializedObject(areas);
                SerializedProperty panels = serialized.FindProperty("buildingAreaPanels");

                if (panels != null)
                {
                    for (int i = 0; i < panels.arraySize; i++)
                    {
                        GameObject referenced = panels.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;

                        if (referenced == null || referenced.name != PanelObjectName)
                        {
                            continue;
                        }

                        Canvas parentCanvas = referenced.GetComponentInParent<Canvas>();

                        if (parentCanvas != null)
                        {
                            existingPanel = referenced.transform;
                            return parentCanvas;
                        }
                    }
                }
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Canvas fallback = null;

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];

                if (candidate == null)
                {
                    continue;
                }

                if (fallback == null && candidate.GetComponent<ShopWindowController>() != null)
                {
                    fallback = candidate;
                }

                Transform panel = candidate.transform.Find(PanelObjectName);

                if (panel != null)
                {
                    existingPanel = panel;
                    return candidate;
                }
            }

            if (fallback != null)
            {
                return fallback;
            }

            return canvases.Length > 0 ? canvases[0] : null;
        }

        /// <summary>删掉除 keep 之外的 BuildingPanel，避免同一份建筑入口出现两层。</summary>
        private static void CleanupDuplicatePanels(Transform keep)
        {
            Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                Transform candidate = all[i];

                if (candidate == null || candidate == keep || candidate.name != PanelObjectName)
                {
                    continue;
                }

                // 只删真正的建筑面板根，避免误删同名装饰物。
                // 注意：新建面板时不一定会挂 UIPanel / MilestonePanelUI（那些是旧面板已有的），
                // 所以还要认 ScrollWheelRelay / Scroll View / 进度行这些本面板独有的结构。
                bool looksLikeBuildingPanel =
                    candidate.GetComponent<UIPanel>() != null ||
                    candidate.GetComponent<MilestonePanelUI>() != null ||
                    candidate.GetComponent<SpecialBuildingAvailabilityUI>() != null ||
                    candidate.GetComponent<ScrollWheelRelay>() != null ||
                    candidate.Find(ScrollViewName) != null ||
                    candidate.Find(ProgressTextName) != null;

                if (!looksLikeBuildingPanel)
                {
                    continue;
                }

                Debug.Log($"[BuildingSetupWizard] 删除重复的建筑面板：{candidate.name}（父物体 " +
                          $"{(candidate.parent != null ? candidate.parent.name : "无")}）");
                Undo.DestroyObjectImmediate(candidate.gameObject);
            }
        }

        /// <summary>确保 GameAreaController 的 buildingAreaPanels 指向重建后的面板。</summary>
        private static void EnsureAreaPanelReference(GameObject panel)
        {
            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);

            if (areas == null)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(areas);
            SerializedProperty panels = serialized.FindProperty("buildingAreaPanels");

            if (panels == null)
            {
                return;
            }

            int emptySlot = -1;

            for (int i = 0; i < panels.arraySize; i++)
            {
                SerializedProperty item = panels.GetArrayElementAtIndex(i);

                if (item.objectReferenceValue == panel)
                {
                    return;
                }

                if (emptySlot < 0 && item.objectReferenceValue == null)
                {
                    emptySlot = i;
                }
            }

            if (emptySlot < 0)
            {
                emptySlot = panels.arraySize;
                panels.arraySize = emptySlot + 1;
            }

            panels.GetArrayElementAtIndex(emptySlot).objectReferenceValue = panel;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[BuildingSetupWizard] 已把「{panel.name}」重新接到 GameAreaController.buildingAreaPanels。");
        }

        /// <summary>建 Scroll View / Viewport / Content，并把建筑按钮按顺序摆进 Content。</summary>
        private static ScrollRect BuildScrollView(
            Transform panel, Transform templateButton, BuildingUI ui, IReadOnlyList<BuildingData> buildings)
        {
            GameObject scrollView = new GameObject(ScrollViewName, typeof(RectTransform), typeof(ScrollRect));
            scrollView.transform.SetParent(panel, false);

            RectTransform scrollViewRect = scrollView.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = Vector2.zero;
            scrollViewRect.anchorMax = Vector2.one;
            scrollViewRect.offsetMin = new Vector2(0f, ActionAreaHeight);
            scrollViewRect.offsetMax = new Vector2(0f, -(ProgressTextHeight + ButtonSpacing));

            // Viewport 必须有一张透明 Image：滚轮落在空白处时，事件需要有个 Graphic 可以命中，
            // 再沿层级向上冒泡到 ScrollRect。RectMask2D 只负责裁剪，不接收射线
            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scrollView.transform, false);

            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-(ScrollBarWidth + ScrollBarGap), 0f);

            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewportImage.raycastTarget = true;

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);

            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;

            float contentHeight = Mathf.Max(1, buildings.Count) * (ButtonHeight + ButtonSpacing);
            contentRect.sizeDelta = new Vector2(0f, contentHeight);

            // ---------- 建筑按钮 ----------

            Button[] buildingButtons = new Button[buildings.Count];

            for (int i = 0; i < buildings.Count; i++)
            {
                // 必须拷贝一份：for 循环变量在所有迭代里是同一个，闭包会全部抓到最后一个下标
                int index = i;
                float top = i * (ButtonHeight + ButtonSpacing);

                buildingButtons[i] = CreateButton(
                    content.transform, templateButton,
                    "Button_" + buildings[i].BuildingName, buildings[i].BuildingName,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -top),
                    new Vector2(-ButtonHorizontalPadding, ButtonHeight),
                    button => UnityEventTools.AddIntPersistentListener(button.onClick, ui.SelectBuilding, index));
            }

            // ---------- 滚动条 ----------

            Scrollbar scrollbar = BuildScrollbar(scrollView.transform);

            // ---------- ScrollRect ----------

            ScrollRect scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.decelerationRate = 0.135f;

            // 滚轮统一由面板根上的 ScrollWheelRelay 读输入处理（指针在面板内就生效），
            // ScrollRect 自己的滚轮灵敏度归零，避免一次滚轮被处理两次
            scrollRect.scrollSensitivity = 0f;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scrollRect.verticalScrollbarSpacing = 0f;

            // ---------- 特殊建筑按钮置灰 ----------

            // 向导每次都重建按钮，旧的引用会变成 Missing，这里重新接一遍
            SpecialBuildingAvailabilityUI special = panel.GetComponent<SpecialBuildingAvailabilityUI>();

            if (special != null)
            {
                SerializedObject specialSo = new SerializedObject(special);
                SerializedProperty buttons = specialSo.FindProperty("buildingButtons");

                if (buttons != null)
                {
                    buttons.arraySize = buildingButtons.Length;

                    for (int i = 0; i < buildingButtons.Length; i++)
                    {
                        buttons.GetArrayElementAtIndex(i).objectReferenceValue = buildingButtons[i];
                    }

                    specialSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            return scrollRect;
        }

        private static Scrollbar BuildScrollbar(Transform scrollView)
        {
            GameObject barObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(scrollView, false);

            RectTransform barRect = barObject.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(ScrollBarWidth, -8f);

            Image trackImage = barObject.GetComponent<Image>();
            trackImage.color = new Color(1f, 1f, 1f, 0.12f);

            GameObject slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
            slidingArea.transform.SetParent(barObject.transform, false);

            RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(0f, 2f);
            slidingRect.offsetMax = new Vector2(0f, -2f);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(slidingArea.transform, false);

            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(1f, 1f, 1f, 0.45f);

            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.value = 1f;
            scrollbar.size = 0.5f;

            return scrollbar;
        }

        private static Button CreateButton(
            Transform parent, Transform templateButton, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 sizeDelta, System.Action<Button> wire)
        {
            // 拿商店里已有的按钮当模板：字体、图片、配色都跟着复制过来，
            // 不必在代码里加载字体（那才是容易出错的地方）
            GameObject clone = Object.Instantiate(templateButton.gameObject, parent, false);
            clone.name = name;

            // 模板是商店面板里那个**隐藏**的 ButtonTemplate（m_IsActive: 0），克隆体会带着关闭状态。
            // 漏掉这一行的话：面板本身照样显示（它没有背景图），但里面一块空白、一个字都没有，
            // 而且 Console 一句报错都不给 —— 阶段 6 实测就是撞在这上面
            // （「建筑模块点开后没有蓝图选项」＝ 风琴博物馆 / 取消蓝图 / 退出建造 三个按钮全是关的）。
            // 本行是那个缺陷的源头：不补上，每重跑一次本命令就会重新把它造出来（踩坑 5）。
            clone.SetActive(true);

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect != null)
            {
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = sizeDelta;
            }

            Text text = clone.GetComponentInChildren<Text>(true);

            if (text != null)
            {
                text.text = label;
            }

            Button button = clone.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning($"[BuildingSetupWizard] 复制的按钮模板上没有 Button 组件，「{label}」按钮无法接线。");
                return null;
            }

            // 模板上可能带着别的持久监听，先清干净再接自己的
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            wire(button);
            return button;
        }

        /// <summary>在 Canvas 里找一个可以当模板的按钮。跳过自建面板里的按钮，避免重复运行时复制自己。</summary>
        private static Transform FindTemplateButton(Canvas canvas)
        {
            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
            Transform ownPanel = canvas.transform.Find(PanelObjectName);
            Transform blueprintPanel = canvas.transform.Find(BlueprintActionPanelName);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                // 跳过自建面板里的按钮。用 IsChildOf 而不是看直接父物体：
                // 建筑按钮现在住在 BuildingPanel/Scroll View/Viewport/Content 下面，
                // 重跑时若把它当成模板，会在重建面板时连模板一起删掉，后续克隆直接空引用
                if ((ownPanel != null && buttons[i].transform.IsChildOf(ownPanel)) ||
                    (blueprintPanel != null && buttons[i].transform.IsChildOf(blueprintPanel)))
                {
                    continue;
                }

                return buttons[i].transform;
            }

            return null;
        }

        /// <summary>按 BuildingFileNames 的顺序加载建筑资产；缺哪个就警告哪个，不中断。</summary>
        private static List<BuildingData> LoadBuildings()
        {
            List<BuildingData> result = new List<BuildingData>(BuildingFileNames.Length);

            for (int i = 0; i < BuildingFileNames.Length; i++)
            {
                string path = BuildingFolder + "/" + BuildingFileNames[i];
                BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(path);

                if (data == null)
                {
                    Debug.LogWarning($"[BuildingSetupWizard] 找不到建筑资产「{path}」，" +
                                     "这一项不会出现在建筑面板里。");
                    continue;
                }

                result.Add(data);
            }

            return result;
        }

        /// <summary>找到名字对应的子物体，没有就建一个（重跑向导时复用，不会越堆越多）。</summary>
        private static Text FindOrCreateTextChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            GameObject child;

            if (existing == null)
            {
                child = new GameObject(childName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(child, "创建 " + childName);
                child.transform.SetParent(parent, false);
            }
            else
            {
                child = existing.gameObject;
            }

            return EnsureComponent<Text>(child);
        }

        private static Font BuiltinFont()
        {
            // 项目规则：不引入 TextMeshPro，UI 文字用旧版 Text + 内置字体
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);

            if (existing != null)
            {
                return existing;
            }

            // 普通 Transform，不是 RectTransform：这两个是挂世界空间 3D 物体的容器
            GameObject child = new GameObject(childName);
            child.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(child, "创建 " + childName);
            return child.transform;
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        // ---- SerializedProperty 写入助手 ----
        // 这些字段都是 private [SerializeField]，只能走 SerializedObject 改。
        // 每个都判空：字段被改名时只跳过，不抛异常，免得整个配置中断在半路。

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
            }
            else
            {
                Debug.LogWarning($"[BuildingSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
            }
        }

        private static void SetInt(SerializedObject so, string fieldName, int value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetFloat(SerializedObject so, string fieldName, float value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetBool(SerializedObject so, string fieldName, bool value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetVector3(SerializedObject so, string fieldName, Vector3 value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.vector3Value = value;
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑系统配置未执行", message, "知道了");
            Debug.LogError("[BuildingSetupWizard] " + message);
        }
    }
}
