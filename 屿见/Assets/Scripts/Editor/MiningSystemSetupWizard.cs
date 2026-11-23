using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Core;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 矿洞系统总修复入口：只修复矿洞入口（照明与手持道具已经移除）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 一键修复矿洞入口
    /// </summary>
    public static class MiningSystemSetupWizard
    {
        [MenuItem("屿见/挖矿/一键修复矿洞入口", false, 330)]
        public static void ConfigureAll()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("矿洞系统", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            MiningEntranceSetupWizard.Configure();

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

            EditorSceneManager.MarkSceneDirty(entrance.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = entrance.gameObject;
            EditorGUIUtility.PingObject(entrance);

            Debug.Log("[矿洞系统] 已修复并保存：清洁区入口可点击。");
            EditorUtility.DisplayDialog("矿洞系统",
                "清洁区入口已经修复并保存。\n\n现在可以进 Play，从岛上的入口进去检查。",
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