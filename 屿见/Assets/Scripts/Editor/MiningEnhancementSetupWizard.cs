using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 7 的一键配置：给场景里所有 Ore 挂上挖矿增强组件。
    ///
    /// 做四件事：
    ///   1. 加 OreTypeTag，并按矿石名字顺序轮换分配四种矿；
    ///   2. 加 OreHitFlash（受击闪白）；
    ///   3. 加 OreShatter（击碎飞块）；
    ///   4. 加 CulturalFragmentDropper（文化碎片彩蛋）。
    ///
    /// 幂等：已经存在的组件不会重复添加，也不会覆盖你手调过的参数。
    /// 不直接改 .unity / .prefab，由 Unity 自己写盘（项目规则 13）。
    ///
    /// 入口：菜单栏 → 屿见 → 配置挖矿增强（阶段 7）
    /// </summary>
    public static class MiningEnhancementSetupWizard
    {
        private static readonly OreType[] TypeRotation =
        {
            OreType.QingShi,
            OreType.HongZhuan,
            OreType.MuLiao,
            OreType.HuaGangYan,
            OreType.QingShi,
        };

        [MenuItem("屿见/挖矿/配置挖矿增强（阶段 7）", false, 310)]
        public static void ConfigureMining()
        {
            Ore[] ores = FindAllOres();

            if (ores.Length == 0)
            {
                Debug.LogWarning("[挖矿增强] 当前打开的场景里没有找到 Ore 组件。请先打开 SampleScene 再执行本命令。");
                return;
            }

            // 按名字排序，保证重复执行时分配到的矿种一致
            System.Array.Sort(ores, (a, b) => string.CompareOrdinal(a.name, b.name));

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("配置挖矿增强");

            for (int i = 0; i < ores.Length; i++)
            {
                GameObject target = ores[i].gameObject;

                OreTypeTag tag = target.GetComponent<OreTypeTag>();
                if (tag == null) tag = Undo.AddComponent<OreTypeTag>(target);
                tag.Configure(TypeRotation[i % TypeRotation.Length], true);

                if (target.GetComponent<OreHitFlash>() == null) Undo.AddComponent<OreHitFlash>(target);
                if (target.GetComponent<OreShatter>() == null) Undo.AddComponent<OreShatter>(target);
                if (target.GetComponent<CulturalFragmentDropper>() == null) Undo.AddComponent<CulturalFragmentDropper>(target);

                EditorUtility.SetDirty(target);
                if (target.scene.IsValid()) EditorSceneManager.MarkSceneDirty(target.scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[挖矿增强] 已配置 {ores.Length} 块矿石：种类标签、受击闪白、击碎飞块、文化碎片。进入 Play 即可看到染色与反馈。", ores[0]);
        }

        private static Ore[] FindAllOres()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<Ore>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<Ore>(true);
#endif
        }
    }
}