using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Core;
using Yujian.Lighting;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 矿洞系统总修复入口：入口、矿洞照明、手持提灯一次配置完。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 一键修复矿洞入口与照明
    /// </summary>
    public static class MiningSystemSetupWizard
    {
        [MenuItem("屿见/挖矿/一键修复矿洞入口与照明", false, 330)]
        public static void ConfigureAll()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("矿洞系统", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            MiningEntranceSetupWizard.Configure();
            MiningLightingSetupWizard.Configure();
            MiningHandheldSetupWizard.Configure();

            GameAreaController areaController =
                Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            MiningEntrance entrance =
                Object.FindFirstObjectByType<MiningEntrance>(FindObjectsInactive.Include);

            if (areaController == null || entrance == null)
            {
                Debug.LogError("[矿洞系统] 修复未完成：场景里找不到 GameAreaController 或 MiningEntrance。");
                return;
            }

            RepairEntrance(areaController, entrance);
            RepairLighting(areaController);

            EditorSceneManager.MarkSceneDirty(entrance.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = entrance.gameObject;
            EditorGUIUtility.PingObject(entrance);

            Debug.Log("[矿洞系统] 已修复并保存：清洁区入口可点击、照明参数已恢复、手持提灯保留。");
            EditorUtility.DisplayDialog("矿洞系统",
                "清洁区入口与照明已经修复并保存。\n\n现在可以进 Play，从岛上的入口进去检查。",
                "知道了");
        }

        private static void RepairEntrance(GameAreaController areaController, MiningEntrance entrance)
        {
            GameObject root = entrance.gameObject;

            if (!root.activeSelf)
            {
                Undo.RecordObject(root, "启用清洁区入口");
                root.SetActive(true);
            }

            if (!entrance.enabled)
            {
                Undo.RecordObject(entrance, "启用清洁区入口组件");
                entrance.enabled = true;
            }

            BoxCollider box = root.GetComponent<BoxCollider>();

            if (box == null)
            {
                box = Undo.AddComponent<BoxCollider>(root);
            }
            else
            {
                Undo.RecordObject(box, "适配清洁区入口碰撞框");
            }

            entrance.RebuildClickCollider();
            entrance.ConfigureForSetup(areaController);
            EditorUtility.SetDirty(entrance);

            MiningEntranceRouter router = areaController.GetComponent<MiningEntranceRouter>();

            if (router == null)
            {
                router = Undo.AddComponent<MiningEntranceRouter>(areaController.gameObject);
            }

            if (!router.enabled)
            {
                Undo.RecordObject(router, "启用清洁区入口点击");
                router.enabled = true;
            }

            SerializedObject routerSo = new SerializedObject(router);
            SetObjectReference(routerSo, "viewCamera", Camera.main);
            SetObjectReference(routerSo, "areaController", areaController);
            routerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(router);
        }

        private static void RepairLighting(GameAreaController areaController)
        {
            MiningLightingController lighting =
                Object.FindFirstObjectByType<MiningLightingController>(FindObjectsInactive.Include);

            if (lighting == null)
            {
                Debug.LogError("[矿洞系统] 场景里找不到 MiningLightingController，矿洞照明未修复。");
                return;
            }

            Light sun = FindSunLight();
            lighting.ConfigureForSetup(areaController, sun, Camera.main);
            EditorUtility.SetDirty(lighting);

            SetLightIntensity(lighting.transform.Find("Fill Light"), 1.5f);
            SetLightIntensity(lighting.transform.Find("Rim Light"), 2.5f);

            MiningHandheldRig rig =
                Object.FindFirstObjectByType<MiningHandheldRig>(FindObjectsInactive.Include);

            Light handLight = null;

            if (rig != null)
            {
                Transform lanternPivot = rig.transform.Find("LanternPivot");
                handLight = lanternPivot != null
                    ? lanternPivot.Find("Lantern Light")?.GetComponent<Light>()
                    : null;
            }

            if (handLight == null)
            {
                Camera camera = Camera.main;
                Transform lanternPivot = camera != null
                    ? camera.transform.Find("清洁手持道具/LanternPivot")
                    : null;
                handLight = lanternPivot != null
                    ? lanternPivot.Find("Lantern Light")?.GetComponent<Light>()
                    : null;
            }

            if (handLight == null)
            {
                Debug.LogWarning("[矿洞系统] 没找到手持提灯 Lantern Light，补光已恢复，但提灯亮度未调整。");
                return;
            }

            Undo.RecordObject(handLight, "恢复手持提灯亮度");
            handLight.intensity = 4.6f;
            handLight.range = 13f;
            EditorUtility.SetDirty(handLight);
        }

        private static Light FindSunLight()
        {
            Light[] lights = Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }

            return null;
        }

        private static void SetLightIntensity(Transform target, float intensity)
        {
            if (target == null)
            {
                Debug.LogWarning("[矿洞系统] 找不到 Fill Light / Rim Light，请先运行阶段 12。");
                return;
            }

            Light light = target.GetComponent<Light>();

            if (light == null)
            {
                return;
            }

            Undo.RecordObject(light, "恢复清洁区灯光亮度");
            light.intensity = intensity;
            EditorUtility.SetDirty(light);
        }

        private static void SetObjectReference(SerializedObject serializedObject, string fieldName,
            Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }
    }
}