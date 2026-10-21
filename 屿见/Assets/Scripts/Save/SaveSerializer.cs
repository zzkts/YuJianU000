using System;
using UnityEngine;

namespace Yujian.Save
{
    /// <summary>
    /// 存档序列化接口。把“对象 ↔ 文本”关在这一层后面。
    ///
    /// 第一版用 Unity 自带的 JsonUtility：零依赖、可读、足够小。
    /// 以后要换 Newtonsoft、加密或二进制格式，只换实现，各系统的 Capture / Restore 一行都不用改。
    /// </summary>
    public interface ISaveSerializer
    {
        string Serialize(SaveData data);
        SaveData Deserialize(string json);
    }

    /// <summary>基于 JsonUtility 的存档序列化。不引入任何第三方包。</summary>
    public sealed class JsonSaveSerializer : ISaveSerializer
    {
        public string Serialize(SaveData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return JsonUtility.ToJson(data, true);
        }

        public SaveData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError("[JsonSaveSerializer] 存档文本为空，无法解析。");
                return null;
            }

            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(json);

                if (data == null)
                {
                    Debug.LogError("[JsonSaveSerializer] 存档解析结果为空（根对象不是 SaveData 结构）。");
                    return null;
                }

                data.Normalize();
                return data;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[JsonSaveSerializer] 存档解析失败：{exception.Message}");
                return null;
            }
        }
    }
}
