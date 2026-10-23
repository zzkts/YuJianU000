using UnityEngine;

namespace Yujian.Save
{
    /// <summary>
    /// 一个系统在存档里的“适配器”。
    ///
    /// 每个参与者只负责把**自己负责的那部分状态**写进 SaveData / 从 SaveData 读回来，
    /// 不碰文件、不解析 JSON、不知道别的系统怎么存。
    ///
    /// 执行顺序 = SaveSystem 上 participants 数组的顺序。
    /// 读档时顺序很重要：先恢复基础数据（金币 / 库存），再恢复依赖它们的系统。
    /// </summary>
    public abstract class SaveParticipant : MonoBehaviour
    {
        /// <summary>把当前状态写入存档。只读游戏状态，不得修改它。</summary>
        public abstract void Capture(SaveData data);

        /// <summary>从存档恢复状态。调用方已经保证 data 非空且已 Normalize。</summary>
        public abstract void Restore(SaveData data);
    }
}
