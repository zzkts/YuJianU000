using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Mining;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 6 的一键配置：把现有系统接成能完整体验的 MVP。
    ///
    /// 做九件事：
    ///   1. 建 / 复用场景物体 <c>GameAreaSystem</c>（GameAreaController + AreaCameraDirector）；
    ///   2. 接线：挖矿输入、相机、建筑相机调度、按区域显隐的面板数组；
    ///   3. 重建 <c>Canvas/BottomNav</c>：三个按钮 [挖矿] [建筑] [商店]；
    ///   4. 建 <c>Canvas/CoinHud</c>：常驻金币文本（事件驱动）；
    ///   5. 给建筑预制体挂上 BuildingSpawnPop（建造成功 0.8 → 1）；
    ///   6. 让位：把材料面板与建筑面板整体抬到导航条上方，并同步 SlidingPanel 的隐藏位移；
    ///   7. 打开两个面板里被摆成关闭的按钮：材料面板的 <c>[×]</c> / <c>[√]</c>、
    ///      建筑面板的 [风琴博物馆] [取消蓝图] [退出建造]（踩坑 5）；
    ///   8. 给 GameManager 接上 GameAreaController（需求十）；
    ///   9. 校验依赖，缺什么就在 Console 点名。
    ///
    /// **幂等，且不覆盖你手调过的数值**：三个按钮每次重建，其余都是「空才写」。
    /// 机位（挖矿 / 商店）**完全不写**——它们就是 AreaCameraDirector 的字段默认值，
    /// 你用右键菜单记录过的机位重跑本命令不会被冲掉。
    ///
    /// ⚠️ 顺序：阶段 3 / 阶段 4 的向导都会把面板按钮重新造成**关闭**状态、把材料面板放回屏幕底部 (0,30)，
    /// 所以**只要重跑了那两个向导，最后务必再跑一次本命令**（第 6、7 步会把这些都修回来）。
    ///
    /// 为什么用编辑器脚本而不是直接改 .unity：见 开发进度.md 踩坑 2 / 项目规则 13。
    ///
    /// 入口：菜单栏 → 屿见 → 配置 MVP 整合（阶段 6）
    /// </summary>
    public static class MvpSetupWizard
    {
        private const string AreaSystemObjectName = "GameAreaSystem";
        private const string NavObjectName = "BottomNav";
        private const string CoinHudObjectName = "CoinHud";

        private const string MaterialPanelName = "材料面板";
        private const string BuildingPanelName = "BuildingPanel";
        private const string ShopPanelName = "ShopPanel";

        private const string BuildingPrefabPath = "Assets/Prefabs/建筑/建筑.prefab";

        /// <summary>底部导航条占的高度（Canvas 参考分辨率下的像素）。面板要躲开这么多。</summary>
        private const float NavBarHeight = 130f;

        private const float NavButtonWidth = 300f;
        private const float NavButtonHeight = 90f;

        /// <summary>三个按钮相对导航条中心的横向位置。</summary>
        private static readonly float[] NavButtonOffsetsX = { -340f, 0f, 340f };

        private static readonly Color NavBarColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color NavLabelColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color CoinTextColor = new Color(1f, 0.9f, 0.6f, 1f);
        private static readonly Color CoinBackColor = new Color(0f, 0f, 0f, 0.45f);

        [MenuItem("屿见/配置 MVP 整合（阶段 6）")]
        public static void Configure()
        {
            // 一律用 FindObjectsInactive.Include：向导在**编辑器里**跑，场景里很多东西
            // 当时可能是关闭的（面板、模板、被停用的 Player），默认只找激活物体的话
            // 会「明明在场景里却找不到」，报出来的错还指错方向
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法建底部导航与金币 HUD。");
                return;
            }

            // ---------------- 0. 收集依赖并校验（规则 5：缺什么就点名，不许静默失败） ----------------

            MiningInput miningInput = Object.FindFirstObjectByType<MiningInput>(FindObjectsInactive.Include);
            BuildingCameraDirector buildingDirector =
                Object.FindFirstObjectByType<BuildingCameraDirector>(FindObjectsInactive.Include);
            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            GameManager manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            Transform cameraTransform = FindMainCameraTransform();

            bool ready = true;

            ready &= Require(miningInput, "场景里找不到 MiningInput（它挂在 Player 上）");
            ready &= Require(buildingDirector, "场景里找不到 BuildingCameraDirector（它挂在 BuildingSystem 上）");
            ready &= Require(currency, "场景里找不到 PlayerCurrency（它挂在 Player 上）");
            ready &= Require(manager, "场景里找不到 GameManager");

            if (cameraTransform == null)
            {
                Debug.LogError("[MvpSetupWizard] 找不到 Main Camera（带 MainCamera 标签的相机），" +
                               "区域机位切换不会生效。");
                ready = false;
            }

            if (!ready)
            {
                Fail("场景依赖不齐，配置未执行。具体缺哪一项见 Console 的报错。");
                return;
            }

            // ---------------- 1. GameAreaSystem ----------------

            GameAreaController areaController =
                Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            GameObject systemObject;

            if (areaController == null)
            {
                systemObject = new GameObject(AreaSystemObjectName);
                Undo.RegisterCreatedObjectUndo(systemObject, "创建 " + AreaSystemObjectName);
                areaController = systemObject.AddComponent<GameAreaController>();
            }
            else
            {
                systemObject = areaController.gameObject;
            }

            AreaCameraDirector areaCamera = EnsureComponent<AreaCameraDirector>(systemObject);

            // ---------------- 2. 接线（机位不写，见类注释） ----------------

            SerializedObject areaSo = new SerializedObject(areaController);
            WriteIfEmpty(areaSo, "miningInput", miningInput);
            WritePanelArray(areaSo, "buildingAreaPanels", BuildingPanelName, FindPanel(canvas, BuildingPanelName));
            WritePanelArray(areaSo, "shopAreaPanels", ShopPanelName, FindPanel(canvas, ShopPanelName));
            areaSo.ApplyModifiedPropertiesWithoutUndo();

            // 写完立刻回读：数组是空的（或整条是空引用）时，运行时切过去**什么都不会发生、
            // 也不会有任何报错** —— SetPanelsActive 对空数组是静默的。只能在这里当场拦下来（规则 5）
            VerifyPanelArray(areaSo, "buildingAreaPanels", BuildingPanelName);
            VerifyPanelArray(areaSo, "shopAreaPanels", ShopPanelName);

            SerializedObject cameraSo = new SerializedObject(areaCamera);
            WriteIfEmpty(cameraSo, "areas", areaController);
            WriteIfEmpty(cameraSo, "cameraTransform", cameraTransform);
            WriteIfEmpty(cameraSo, "buildingDirector", buildingDirector);
            cameraSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------------- 3. 底部导航 ----------------

            Button templateButton = FindTemplateButton(canvas.transform);

            if (templateButton == null)
            {
                Fail("Canvas 里找不到任何现成的 Button 可以当模板，无法创建底部导航。\n\n" +
                     "请先确认 Canvas 下（例如商店面板里隐藏的 ButtonTemplate）还有按钮。\n" +
                     "注意：材料面板与建筑面板的接线**已经完成**，只是导航没建出来，重跑本命令即可补上。");
                return;
            }

            GameObject navObject = BuildBottomNav(canvas, templateButton, areaController);

            // ---------------- 4. 金币 HUD ----------------

            Text coinText = BuildCoinHud(canvas, currency);

            // ---------------- 5. 建筑弹出表现 ----------------

            string prefabReport = ConfigureBuildingPrefab();

            // ---------------- 6. 给底部导航条让位 ----------------

            string panelReport = MakeRoomForNavBar(canvas);

            // ---------------- 7. 面板里被摆成关闭的按钮 ----------------

            string buttonReport = EnsurePanelButtonsActive(canvas);

            // ---------------- 8. GameManager 接上区域状态 ----------------

            SerializedObject managerSo = new SerializedObject(manager);
            string managerReport = WriteIfEmpty(managerSo, "areas", areaController)
                ? "已接上 GameAreaController"
                : "已经有引用（未改动）";
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------------- 8. 收尾 ----------------

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = navObject;
            EditorGUIUtility.PingObject(navObject);

            Debug.Log("[MvpSetupWizard] 配置完成：\n" +
                      $"  · 区域系统：{AreaSystemObjectName}（GameAreaController + AreaCameraDirector）\n" +
                      "  · 导航条：Canvas/BottomNav = [挖矿] [建筑] [商店]，已接 BottomNavUI\n" +
                      $"  · 金币 HUD：{coinText.name}（事件驱动，没有 Update）\n" +
                      $"  · 建筑预制体：{prefabReport}\n" +
                      $"  · 面板让位：{panelReport}\n" +
                      $"  · 面板按钮：{buttonReport}\n" +
                      $"  · GameManager.CurrentArea：{managerReport}\n" +
                      "\n请按 Ctrl+S 保存场景。\n" +
                      "接着：开 Play Mode，用底部的 [挖矿] [建筑] [商店] 切区域。\n" +
                      "机位是**推算值**：想换成你摆好的镜头，选中 GameAreaSystem 上的 AreaCameraDirector，" +
                      "摆好 Scene 视图后右键组件 →「把相机当前位置记录为挖矿机位 / 商店机位」。\n" +
                      "建筑区的机位仍归 BuildingCameraDirector 管（阶段 3 那套），本命令不碰它。");
        }

        // ---------------- 底部导航 ----------------

        private static GameObject BuildBottomNav(Canvas canvas, Button templateButton, GameAreaController areaController)
        {
            Transform existing = canvas.transform.Find(NavObjectName);
            GameObject navObject;

            if (existing == null)
            {
                navObject = new GameObject(NavObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(navObject, "创建 " + NavObjectName);
                navObject.transform.SetParent(canvas.transform, false);
            }
            else
            {
                navObject = existing.gameObject;
            }

            RectTransform navRect = navObject.GetComponent<RectTransform>();
            SetRect(navRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    Vector2.zero, new Vector2(0f, NavBarHeight));

            Image background = EnsureComponent<Image>(navObject);
            background.color = NavBarColor;
            background.sprite = null;

            // 导航条自己吃点击：点在条上的空白处不该穿到世界里去打矿石（需求十一）
            background.raycastTarget = true;

            BottomNavUI nav = EnsureComponent<BottomNavUI>(navObject);

            // 每次重建三个按钮：旧按钮留着会越跑越多，而且接的监听会指向旧组件
            for (int i = navObject.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(navObject.transform.GetChild(i).gameObject);
            }

            Button miningButton = CreateNavButton(navObject.transform, templateButton, "Button_挖矿", "挖矿", 0,
                button => UnityEventTools.AddPersistentListener(button.onClick, nav.ShowMiningArea));

            Button buildingButton = CreateNavButton(navObject.transform, templateButton, "Button_建筑", "建筑", 1,
                button => UnityEventTools.AddPersistentListener(button.onClick, nav.ShowBuildingArea));

            Button shopButton = CreateNavButton(navObject.transform, templateButton, "Button_商店", "商店", 2,
                button => UnityEventTools.AddPersistentListener(button.onClick, nav.ShowShopArea));

            SerializedObject navSo = new SerializedObject(nav);
            SetObject(navSo, "areas", areaController);
            SetObject(navSo, "miningButton", miningButton);
            SetObject(navSo, "buildingButton", buildingButton);
            SetObject(navSo, "shopButton", shopButton);
            navSo.ApplyModifiedPropertiesWithoutUndo();

            // 放在 Canvas 最末：阶段 3/4 的向导是拿 Canvas 下**第一个** Button 当模板的，
            // 导航按钮排在最后才不会被它们误当成按钮模板（那会把两个面板的按钮换成 300×90）
            navObject.transform.SetAsLastSibling();

            return navObject;
        }

        private static Button CreateNavButton(Transform parent, Button template, string name, string label, int index,
                                             System.Action<Button> wire)
        {
            GameObject clone = Object.Instantiate(template.gameObject, parent, false);
            clone.name = name;

            // 模板（商店面板里隐藏的 ButtonTemplate）是关闭状态，克隆体也带着关闭状态。
            // 不 SetActive(true) 的话按钮在场景里存在、屏幕上什么都没有 —— 阶段 4 踩过这个坑（踩坑 5）
            clone.SetActive(true);

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect != null)
            {
                SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(NavButtonOffsetsX[index], 0f), new Vector2(NavButtonWidth, NavButtonHeight));
            }

            Text text = clone.GetComponentInChildren<Text>(true);

            if (text != null)
            {
                text.text = label;
                text.font = BuiltinFont();
                text.fontSize = 26;
                text.fontStyle = FontStyle.Normal;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = NavLabelColor;
                text.raycastTarget = false;
            }
            else
            {
                Debug.LogWarning($"[MvpSetupWizard] 按钮模板上没有 Text 子物体，「{label}」按钮会没有文字。");
            }

            Button button = clone.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogError($"[MvpSetupWizard] 按钮模板上没有 Button 组件，「{label}」按钮点不动。");
                return null;
            }

            // 模板上可能带着别的持久监听（商店面板的模板就带），先清干净再接自己的
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            wire(button);
            return button;
        }

        /// <summary>
        /// 找按钮模板。优先用商店面板里那个隐藏的 <c>ButtonTemplate</c>（阶段 2 重构留下的，
        /// 尺寸与字体和另外两个面板一致），其次退化成 Canvas 下第一个不在导航条里的按钮。
        /// </summary>
        private static Button FindTemplateButton(Transform canvas)
        {
            Transform shopPanel = canvas.Find("ShopPanel");

            if (shopPanel != null)
            {
                Transform template = shopPanel.Find("ButtonTemplate");
                Button named = template != null ? template.GetComponent<Button>() : null;

                if (named != null)
                {
                    return named;
                }
            }

            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                if (buttons[i].transform.IsChildOf(canvas.Find(NavObjectName)))
                {
                    continue;
                }

                return buttons[i];
            }

            return null;
        }

        // ---------------- 金币 HUD ----------------

        private static Text BuildCoinHud(Canvas canvas, PlayerCurrency currency)
        {
            Transform existing = canvas.transform.Find(CoinHudObjectName);
            GameObject hudObject;

            if (existing == null)
            {
                hudObject = new GameObject(CoinHudObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(hudObject, "创建 " + CoinHudObjectName);
                hudObject.transform.SetParent(canvas.transform, false);
            }
            else
            {
                hudObject = existing.gameObject;
            }

            // 右上角：商店面板在左下、建筑面板在右下，右上角是唯一三个区域都不挡的位置
            SetRect(hudObject.GetComponent<RectTransform>(),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-30f, -30f), new Vector2(400f, 60f));

            Image background = EnsureComponent<Image>(hudObject);
            background.color = CoinBackColor;
            background.sprite = null;

            // 金币只是一个读数，不拦截点击：它不是面板，没有需要点它的东西
            background.raycastTarget = false;

            // 文字放在子物体上，不跟 Image 挤在同一个物体上：
            // 一个物体上挂两个 Graphic（Image + Text）虽然能画出东西，但两者的
            // 材质 / 层级 / 尺寸互相牵扯，是 uGUI 里典型的坑。标准层级是「底板 + Text 子物体」
            Text text = FindOrCreateTextChild(hudObject.transform, "Text");
            SetRect(text.GetComponent<RectTransform>(),
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(-20f, 0f));

            text.font = BuiltinFont();
            text.fontSize = 34;
            text.alignment = TextAnchor.MiddleRight;
            text.color = CoinTextColor;
            text.raycastTarget = false;
            text.text = "金币：0";

            CoinHudUI hud = EnsureComponent<CoinHudUI>(hudObject);

            SerializedObject hudSo = new SerializedObject(hud);
            SetObject(hudSo, "currency", currency);
            SetObject(hudSo, "coinText", text);
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            hudObject.transform.SetAsLastSibling();

            return text;
        }

        // ---------------- 建筑预制体 ----------------

        private static string ConfigureBuildingPrefab()
        {
            // 先确认资产真的在：LoadPrefabContents 碰到不存在的路径是"抛异常"而不是"返回 null"
            // （签名上写着 GameObject，但拿不到时它会当场炸），这里提前拦一道，
            // 免得把前面几步已经接好的线一起炸掉
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingPrefabPath);

            if (asset == null)
            {
                Debug.LogError($"[MvpSetupWizard] 找不到建筑预制体：{BuildingPrefabPath}，" +
                               "建筑建成时不会有弹出表现（0.8 → 1）。" +
                               "其余配置已经完成，把预制体补上再重跑本命令即可。");
                return "找不到预制体，未做改动";
            }

            GameObject root = PrefabUtility.LoadPrefabContents(BuildingPrefabPath);

            try
            {
                if (root.GetComponent<BuildingSpawnPop>() != null)
                {
                    return "已有 BuildingSpawnPop（未改动）";
                }

                root.AddComponent<BuildingSpawnPop>();
                PrefabUtility.SaveAsPrefabAsset(root, BuildingPrefabPath);

                return "新增 BuildingSpawnPop（建造成功 0.8 → 1）";
            }
            finally
            {
                // 不管成没成都得卸载，否则这个"预制体场景"会一直挂在那里，
                // 之后再对着同一个预制体做任何操作都可能出怪事
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------- 给导航条让位 ----------------

        /// <summary>
        /// 底部的导航条会压住材料面板与建筑面板，这里把这两个面板整体抬高 <see cref="NavBarHeight"/>。
        ///
        /// 判据是「面板底边低于导航条顶边就抬高」，所以：
        ///   · 阶段 4 向导把材料面板放回 (0,30) 后再跑本命令 → 会被重新抬起来；
        ///   · 已经抬过的、或你手动放在更高位置的面板 → 不动。
        ///
        /// 材料面板还要同步抬高 <c>SlidingPanel</c> 的隐藏位移，否则滑出屏幕以后
        /// 会按旧的位移停在屏幕上，露出小半截。
        /// </summary>
        private static string MakeRoomForNavBar(Canvas canvas)
        {
            int moved = 0;

            moved += LiftPanel(canvas, BuildingPanelName);
            moved += LiftPanel(canvas, MaterialPanelName);

            if (moved == 0)
            {
                return "两个面板都已在导航条上方（未改动）";
            }

            return $"抬高 {moved} 个面板各 {NavBarHeight} 像素（材料面板连同 SlidingPanel 的隐藏位移一起调整）";
        }

        private static int LiftPanel(Canvas canvas, string panelName)
        {
            Transform panel = canvas.transform.Find(panelName);

            if (panel == null)
            {
                Debug.LogWarning($"[MvpSetupWizard] Canvas 下找不到「{panelName}」，没有给它让出底部导航条的位置。");
                return 0;
            }

            RectTransform rect = panel.GetComponent<RectTransform>();

            if (rect == null)
            {
                Debug.LogWarning($"[MvpSetupWizard]「{panelName}」上没有 RectTransform，无法抬高。");
                return 0;
            }

            SerializedObject rectSo = new SerializedObject(rect);
            SerializedProperty position = rectSo.FindProperty("m_AnchoredPosition");

            if (position == null)
            {
                Debug.LogWarning($"[MvpSetupWizard] 读不到「{panelName}」的 m_AnchoredPosition，无法抬高。");
                return 0;
            }

            Vector2 anchored = position.vector2Value;

            if (anchored.y >= NavBarHeight)
            {
                return 0;
            }

            anchored.y += NavBarHeight;
            position.vector2Value = anchored;
            rectSo.ApplyModifiedPropertiesWithoutUndo();

            LiftSlideTarget(panel);

            Debug.Log($"[MvpSetupWizard]「{panelName}」底边从导航条下面抬到了 ({anchored.x}, {anchored.y})。");
            return 1;
        }

        /// <summary>
        /// 面板上有 SlidingPanel 的话，把它的隐藏位移也往负方向挪同样的距离。
        /// 面板整体上移多少，滑出屏幕后的落点就跟着上移多少，不改它就会露出小半截。
        /// </summary>
        private static void LiftSlideTarget(Transform panel)
        {
            SlidingPanel slide = panel.GetComponent<SlidingPanel>();

            if (slide == null)
            {
                return;
            }

            SerializedObject slideSo = new SerializedObject(slide);
            SerializedProperty offset = slideSo.FindProperty("hiddenOffsetY");

            if (offset == null)
            {
                Debug.LogWarning($"[MvpSetupWizard]「{panel.name}」上的 SlidingPanel 找不到 hiddenOffsetY，隐藏位移未同步。");
                return;
            }

            offset.floatValue -= NavBarHeight;
            slideSo.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[MvpSetupWizard]「{panel.name}」的 SlidingPanel 隐藏位移同步为 {offset.floatValue}。");
        }

        // ---------------- 面板按钮的激活状态 ----------------

        /// <summary>
        /// 两个面板的按钮都在场景里被摆成过**关闭**状态 —— 踩坑 5：向导从隐藏的模板按钮克隆时
        /// 忘了 <c>SetActive(true)</c>，克隆体就带着关闭状态。关着的按钮看不见、也收不到点击，
        /// 而面板本身没有背景图，于是表现是「切过去了，屏幕上一块空白，Console 一句报错都没有」。
        ///
        /// 阶段 6 实测就撞在这上面：建筑面板的「风琴博物馆 / 取消蓝图 / 退出建造」三个按钮全是关的，
        /// 所以「建筑模块点开后没有蓝图选项」。阶段 3 向导的漏点已经一并补掉了（源头），
        /// 这一步负责把**已经坏掉的场景**修回来，并挡住「先跑阶段 6、后跑阶段 3」这种顺序。
        ///
        /// 两处的判据不同，各有理由：
        ///   · 材料面板 —— 按 `BuildingMaterialPanel` 上**已经接好的引用**激活，不按名字猜
        ///     （那两个引用是阶段 4 向导自己接上去的，最可靠）；
        ///   · 建筑面板 —— 那三个按钮**没有任何脚本持有引用**，只能把面板下所有按钮一起打开
        ///     （面板里刚好只有那三个按钮，没有别的）。
        ///
        /// 为什么放在向导里而不是"提醒你在 Inspector 里勾一下"：手勾只能当临时验证，
        /// 下次重跑阶段 3 / 阶段 4 向导又会回到关闭状态。
        /// </summary>
        private static string EnsurePanelButtonsActive(Canvas canvas)
        {
            string material = ActivateMaterialPanelButtons();
            string building = ActivateAllButtonsUnder(canvas.transform.Find(BuildingPanelName), BuildingPanelName);

            if (string.IsNullOrEmpty(building))
            {
                return material;
            }

            return string.IsNullOrEmpty(material) ? building : material + "；" + building;
        }

        private static string ActivateMaterialPanelButtons()
        {
            BuildingMaterialPanel panel =
                Object.FindFirstObjectByType<BuildingMaterialPanel>(FindObjectsInactive.Include);

            if (panel == null)
            {
                Debug.LogWarning("[MvpSetupWizard] 场景里找不到 BuildingMaterialPanel（材料面板），" +
                                 "没法检查「撤回 / 确认建造」按钮的激活状态。");
                return string.Empty;
            }

            SerializedObject panelSo = new SerializedObject(panel);
            int activated = ActivateButton(panelSo, "undoButton", "撤回");
            activated += ActivateButton(panelSo, "confirmButton", "确认建造");

            return activated == 0 ? string.Empty : $"材料面板激活了 {activated} 个（撤回 / 确认建造）";
        }

        /// <summary>
        /// 把面板下所有按钮打开，返回一条摘要；本来就都开着、或面板不在时返回空串。
        /// 不按名字挑那三个按钮，是因为面板里没有脚本持有它们的引用，名字又随时可以改。
        /// （面板本身找不到时不必在这里再喊一次：第 2 步的数组回读校验已经会报红字。）
        /// </summary>
        private static string ActivateAllButtonsUnder(Transform panel, string panelName)
        {
            if (panel == null)
            {
                return string.Empty;
            }

            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            int activated = 0;

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || buttons[i].gameObject.activeSelf)
                {
                    continue;
                }

                Undo.RecordObject(buttons[i].gameObject, "激活按钮 " + buttons[i].name);
                buttons[i].gameObject.SetActive(true);
                activated++;
            }

            return activated == 0 ? string.Empty : $"{panelName}激活了 {activated}/{buttons.Length} 个按钮";
        }

        private static int ActivateButton(SerializedObject panelSo, string fieldName, string label)
        {
            SerializedProperty property = panelSo.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[MvpSetupWizard] BuildingMaterialPanel 上找不到字段「{fieldName}」，" +
                                 "请检查字段名与脚本是否一致。");
                return 0;
            }

            Button button = property.objectReferenceValue as Button;

            if (button == null)
            {
                Debug.LogError($"[MvpSetupWizard] 材料面板的「{label}」按钮引用是空的，" +
                               "这个按钮既不会出现也不会生效。请重跑菜单「屿见/配置建造系统（阶段 4）」。");
                return 0;
            }

            if (button.gameObject.activeSelf)
            {
                return 0;
            }

            Undo.RecordObject(button.gameObject, "激活按钮 " + label);
            button.gameObject.SetActive(true);

            Debug.Log($"[MvpSetupWizard] 激活了「{label}」按钮（{button.name}）" +
                      "—— 它在场景里是关闭的，关着的按钮看不见也点不到。");
            return 1;
        }

        // ---------------- 小工具 ----------------

        private static GameObject FindPanel(Canvas canvas, string panelName)
        {
            Transform panel = canvas.transform.Find(panelName);

            if (panel == null)
            {
                Debug.LogWarning($"[MvpSetupWizard] Canvas 下找不到「{panelName}」，它不会随区域显隐。" +
                                 "（如果你还没建这个面板，先跑对应的阶段向导）");
                return null;
            }

            return panel.gameObject;
        }

        private static Transform FindMainCameraTransform()
        {
            Camera mainCamera = Camera.main;

            if (mainCamera != null)
            {
                return mainCamera.transform;
            }

            Camera any = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

            return any != null ? any.transform : null;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                   Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            if (rect == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(rect);
            SetVector2(so, "m_AnchorMin", anchorMin);
            SetVector2(so, "m_AnchorMax", anchorMax);
            SetVector2(so, "m_Pivot", pivot);
            SetVector2(so, "m_AnchoredPosition", anchoredPosition);
            SetVector2(so, "m_SizeDelta", sizeDelta);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector2(SerializedObject so, string fieldName, Vector2 value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.vector2Value = value;
            }
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();

            if (existing != null)
            {
                return existing;
            }

            return target.AddComponent<T>();
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

        private static bool Require(Object target, string message)
        {
            if (target != null)
            {
                return true;
            }

            Debug.LogError("[MvpSetupWizard] " + message + "。");
            return false;
        }

        /// <summary>字段为空才写：这样重跑本命令不会覆盖你在 Inspector 里手接的引用。</summary>
        private static bool WriteIfEmpty(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[MvpSetupWizard] {so.targetObject.GetType().Name} 上找不到字段「{fieldName}」，" +
                                 "请检查字段名与脚本是否一致。");
                return false;
            }

            if (property.objectReferenceValue != null)
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        /// <summary>
        /// 写面板数组：**空数组、或整条都是空引用**才写，已经接了真实物体的不动（保留你手接的面板）。
        ///
        /// 比「只有空数组才写」多覆盖一种情况：长度是 1、但那一项是空的
        /// （面板被销毁重建过，或者上一次跑本命令时面板还没建出来）。
        /// 为什么非要覆盖它：这种状态在运行时**一声不吭** —— SetPanelsActive 对空数组是静默的，
        /// 切过去什么都不打开、也不报错，表现就是「相机切到建筑区了，建筑面板却没出来」。
        /// </summary>
        private static void WritePanelArray(SerializedObject so, string fieldName, string panelName, GameObject panel)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[MvpSetupWizard] {so.targetObject.GetType().Name} 上找不到字段「{fieldName}」。");
                return;
            }

            if (IsArrayUsable(property))
            {
                // 已经接了真实物体（包括你手加的）就不动它
                return;
            }

            if (panel == null)
            {
                // FindPanel 已经就「找不到面板」警告过一次，这里不重复刷屏：
                // 写不成的后果由后面的 VerifyPanelArray 点成红字
                return;
            }

            property.arraySize = 1;
            property.GetArrayElementAtIndex(0).objectReferenceValue = panel;
        }

        /// <summary>数组非空、且至少有一项指向真实物体，才算可用。</summary>
        private static bool IsArrayUsable(SerializedProperty property)
        {
            if (property.arraySize == 0)
            {
                return false;
            }

            for (int i = 0; i < property.arraySize; i++)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 写完回读一遍；数组仍是空的 / 整条是空引用时当场报红字。
        /// 运行时那个区域切过去不会打开任何面板，也不会有别的报错，只能在这里拦（规则 5）。
        /// </summary>
        private static void VerifyPanelArray(SerializedObject so, string fieldName, string panelName)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null || IsArrayUsable(property))
            {
                return;
            }

            Debug.LogError($"[MvpSetupWizard] {fieldName} 没能写成功（Canvas/{panelName} 找不到，" +
                           "或者它不是 GameObject）：运行时切到对应区域时**面板不会出现，而且不会有别的报错**。" +
                           "请先把这个面板建好，再重跑本命令。");
        }

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
                return;
            }

            Debug.LogWarning($"[MvpSetupWizard] {so.targetObject.GetType().Name} 上找不到字段「{fieldName}」，" +
                             "请检查字段名与脚本是否一致。");
        }

        private static Font BuiltinFont()
        {
            // 项目规则：不引入 TextMeshPro，UI 文字用旧版 Text + 内置字体
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("MVP 整合配置未执行", message, "知道了");
            Debug.LogError("[MvpSetupWizard] " + message);
        }
    }
}
