using System.IO;
using System.Linq;
using UnityEditor;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 场景路径的唯一来源。
    ///
    /// 2026-11-25 场景从 `SampleScene.unity` 改名为 `GameScene.unity`，
    /// 当时 6 个向导各自写死了旧路径，批处理模式下会直接报「找不到场景」。
    /// 现在统一从这里取：先认 GameScene，再认 SampleScene（兼容旧工程），
    /// 最后退回 Assets/Scenes 下第一个 .unity —— 以后再改名不用再改 6 个文件。
    /// </summary>
    public static class EditorScenePaths
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string PreferredScene = "Assets/Scenes/GameScene.unity";
        private const string LegacyScene = "Assets/Scenes/SampleScene.unity";

        /// <summary>主场景路径。找不到任何场景时返回首选路径（让调用方自己报错）。</summary>
        public static string MainScenePath
        {
            get
            {
                if (File.Exists(PreferredScene))
                {
                    return PreferredScene;
                }

                if (File.Exists(LegacyScene))
                {
                    return LegacyScene;
                }

                if (Directory.Exists(ScenesFolder))
                {
                    string candidate = Directory
                        .GetFiles(ScenesFolder, "*.unity", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path)
                        .FirstOrDefault();

                    if (!string.IsNullOrEmpty(candidate))
                    {
                        return candidate.Replace('\\', '/');
                    }
                }

                return PreferredScene;
            }
        }

        /// <summary>主场景是否真的存在。批处理向导在开场景前先问这个。</summary>
        public static bool MainSceneExists => File.Exists(MainScenePath);
    }
}
