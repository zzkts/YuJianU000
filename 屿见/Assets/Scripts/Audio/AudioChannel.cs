namespace Yujian.Audio
{
    /// <summary>
    /// 音频分组。音量按组独立存储（PlayerPrefs），主音量再乘一次。
    /// 新增分组时追加到末尾，不要改动已有数值——PlayerPrefs 键与枚举名绑定的是名字，但日志对照需要稳定值。
    /// </summary>
    public enum AudioChannel
    {
        /// <summary>主音量。只用于设置界面，音频条目不应选它。</summary>
        Master = 0,

        /// <summary>背景音乐。同一时刻只播一条。</summary>
        Music = 1,

        /// <summary>世界音效。矿石、建造、金币等。</summary>
        Sfx = 2,

        /// <summary>界面音效。按钮点击、面板开关。</summary>
        Ui = 3,
    }
}
