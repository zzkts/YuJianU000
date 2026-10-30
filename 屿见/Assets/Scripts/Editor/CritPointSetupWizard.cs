using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 配置暴击点系统（D3=A）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 配置暴击点系统
    ///
    /// 把 CritPointSystem 挂到 DamageSystem 所在物体上，接好矿石与照明工具，
    /// 再把引用写进 MiningInput。幂等：重复运行复用已有组件。
    /// </summary>
    public static class CritPointSetupWizard
    {
        [MenuItem("屿见/挖矿/配置暴击点系统", false, 337)]
        public static void Configure()
        {
            DamageSystem damageSystem = Object.FindFirstObjectByType<DamageSystem>(FindObjectsInactive.Include);
            Ore ore = Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);
            MiningLightTool lightTool = Object.FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            MiningInput miningInput = Object.FindFirstObjectByType<MiningInput>(FindObjectsInactive.Include);

            if (damageSystem == null)
            {
                Fail("场景里找不到 DamageSystem。");
                return;
            }

            if (ore == null)
            {
                Fail("场景里找不到 Ore（矿石）。请先运行矿石相关向导。");
                return;
            }

            if (lightTool == null)
            {
                Fail("场景里找不到 MiningLightTool。请先运行「屿见/挖矿/配置照明工具」。");
                return;
            }

            GameObject host = damageSystem.gameObject;
            CritPointSystem system = host.GetComponent<CritPointSystem>();

            if (system == null)
            {
                system = Undo.AddComponent<CritPointSystem>(host);
            }

            system.ConfigureForSetup(ore, lightTool);
            EditorUtility.SetDirty(system);

            bool inputWired = false;

            if (miningInput != null)
            {
                SerializedObject serializedInput = new SerializedObject(miningInput);
                SerializedProperty property = serializedInput.FindProperty("critPointSystem");

                if (property != null)
                {
                    property.objectReferenceValue = system;
                    serializedInput.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(miningInput);
                    inputWired = true;
                }
            }

            EditorSceneManager.MarkSceneDirty(host.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Debug.Log($"[CritPointSetupWizard] 暴击点系统已配置：" +
                      $"宿主 {host.name}，矿石 {ore.name}，照明工具 {(lightTool != null ? lightTool.name : "未配置")}，" +
                      $"MiningInput 接线 {(inputWired ? "成功" : "失败（场景里找不到 MiningInput）")}。");
        }

        private static void Fail(string message)
        {
            Debug.LogError("[CritPointSetupWizard] " + message);
            EditorUtility.DisplayDialog("暴击点系统未配置", message, "知道了");
        }
    }
}
