using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 10 一键配置：给场景里的 DamageSystem 开启暴击。
    /// 只改组件参数并让 Unity 保存场景，不直接改 .unity 文件（项目规则 13）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 配置暴击（阶段 10）
    /// </summary>
    public static class CritSetupWizard
    {
        private const float DefaultDamageMultiplier = 2f;

        [MenuItem("屿见/挖矿/配置暴击（阶段 10）", false, 335)]
        public static void Configure()
        {
            DamageSystem[] systems = FindDamageSystems();

            if (systems.Length == 0)
            {
                Debug.LogWarning("[暴击] 当前打开的场景里没有 DamageSystem。" +
                                 "请先打开 SampleScene 再执行本命令。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("配置挖矿暴击");

            for (int i = 0; i < systems.Length; i++)
            {
                DamageSystem system = systems[i];
                Undo.RecordObject(system, "配置挖矿暴击");

                // D3=A 起没有随机暴击概率，只保留暴击点伤害倍数。
                // 旧场景里新增字段可能反序列化成 1，视为「还没配置」，补成默认值。
                if (system.CriticalDamageMultiplier <= 1f)
                {
                    system.ConfigureCriticalHit(DefaultDamageMultiplier);
                }

                EditorUtility.SetDirty(system);

                Debug.Log($"[暴击] {system.name}：暴击点伤害 ×{system.CriticalDamageMultiplier:0.##}" +
                          "（随机暴击已移除）。", system);
            }

            Undo.CollapseUndoOperations(undoGroup);

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
            }

            EditorSceneManager.SaveOpenScenes();
        }

        private static DamageSystem[] FindDamageSystems()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<DamageSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<DamageSystem>(true);
#endif
        }
    }
}