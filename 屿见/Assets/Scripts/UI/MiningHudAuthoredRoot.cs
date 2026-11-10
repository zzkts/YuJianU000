using UnityEngine;

namespace Yujian.UI
{
    /// <summary>
    /// 标记由编辑器生成的清洁界面根节点。
    /// 运行时 <see cref="MiningHudUI"/> 优先绑定这个节点，不再重复生成一份 UI；
    /// 用组件而不是名字判断，用户在 Hierarchy 里改名也不会失效。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MiningHudAuthoredRoot : MonoBehaviour
    {
    }
}
