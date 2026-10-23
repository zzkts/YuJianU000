using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Audio
{
    /// <summary>
    /// 音频配置表：把 <see cref="AudioId"/> 映射到具体 AudioClip 与播放参数。
    ///
    /// 为什么用 ScriptableObject 而不是在 AudioManager 上摊一堆数组：
    ///   · 换一条音效只改资产，不碰场景、不碰代码；
    ///   · 策划可以直接在 Inspector 里调音量 / 音高 / 冷却；
    ///   · 同一个 ID 在代码里只有一处引用，改素材不会漏掉某个调用点。
    ///
    /// 资产位置：Assets/Data/Audio/AudioCatalog.asset（由菜单「屿见/通用模块/配置通用模块（事件·音频·UI）」创建）。
    /// 没有资产时游戏照常运行，只是所有 Play 都是空操作。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/音频配置表", fileName = "AudioCatalog")]
    public class AudioCatalog : ScriptableObject
    {
        /// <summary>一条「ID → 素材 + 参数」的映射。</summary>
        [Serializable]
        public class Entry
        {
            [Tooltip("音频 ID。代码里用这个枚举播放")]
            [SerializeField] private AudioId id = AudioId.None;

            [Tooltip("实际播放的音频文件。留空表示这条还没做，播放时静默跳过")]
            [SerializeField] private AudioClip clip;

            [Tooltip("音量分组。界面音走 Ui，世界音走 Sfx，背景音乐走 Music")]
            [SerializeField] private AudioChannel channel = AudioChannel.Sfx;

            [Tooltip("这条音频自身的音量，会再乘上分组音量与主音量")]
            [SerializeField, Range(0f, 1f)] private float volume = 1f;

            [Tooltip("音高随机范围。上下限相等表示不随机；矿石受击这类重复音效建议 0.95~1.05")]
            [SerializeField] private Vector2 pitchRange = new Vector2(1f, 1f);

            [Tooltip("同一 ID 的最小播放间隔（秒）。防止一次点击触发多个事件时叠成爆音")]
            [SerializeField, Min(0f)] private float cooldownSeconds = 0.05f;

            [Tooltip("勾选后按 3D 音源播放（有距离衰减）。界面音不要勾")]
            [SerializeField] private bool spatial;

            [SerializeField, Min(0f)] private float minDistance = 3f;
            [SerializeField, Min(0f)] private float maxDistance = 40f;

            public AudioId Id => id;
            public AudioClip Clip => clip;
            public AudioChannel Channel => channel;
            public float Volume => volume;
            public float CooldownSeconds => cooldownSeconds;
            public bool Spatial => spatial;
            public float MinDistance => minDistance;
            public float MaxDistance => maxDistance;

            /// <summary>在当前音高范围内随机一个值。</summary>
            public float RollPitch()
            {
                float low = Mathf.Min(pitchRange.x, pitchRange.y);
                float high = Mathf.Max(pitchRange.x, pitchRange.y);

                if (Mathf.Approximately(low, high))
                {
                    return low <= 0f ? 1f : low;
                }

                return UnityEngine.Random.Range(low, high);
            }
        }

        [Tooltip("全部音频条目。同一个 ID 不要出现两次")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<AudioId, Entry> lookup;

        /// <summary>按 ID 查条目。查得到且素材不为空才返回 true。</summary>
        public bool TryGet(AudioId id, out Entry entry)
        {
            EnsureLookup();
            return lookup.TryGetValue(id, out entry) && entry != null && entry.Clip != null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<AudioId, Entry>();

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];

                if (entry == null || entry.Id == AudioId.None)
                {
                    continue;
                }

                if (lookup.ContainsKey(entry.Id))
                {
                    Debug.LogError($"[AudioCatalog] 「{name}」里 {entry.Id} 出现了多次，" +
                                   "播放时会取第一条。请删掉重复项。", this);
                    continue;
                }

                lookup.Add(entry.Id, entry);
            }
        }

        /// <summary>配置表内容被 Inspector 修改后，缓存必须重建。</summary>
        private void OnValidate()
        {
            lookup = null;
        }

        private void OnEnable()
        {
            lookup = null;
        }
    }
}
