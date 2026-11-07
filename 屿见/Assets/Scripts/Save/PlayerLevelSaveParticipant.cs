using UnityEngine;
using Yujian.Player;

namespace Yujian.Save
{
    /// <summary>
    /// 玩家历史最高等级存档（策划案 4.2：等级只升不降）。
    ///
    /// 建筑可以拆除，场上建筑数会变少，但等级不能回退；
    /// 所以「当前等级」不能只按建筑数重算，必须把历史最高值单独存下来。
    /// 旧存档没有该字段时为 0，恢复时忽略，读档后仍按建筑数重算。
    /// </summary>
    public class PlayerLevelSaveParticipant : SaveParticipant
    {
        [Tooltip("玩家等级组件")]
        [SerializeField] private PlayerLevel playerLevel;

        public override void Capture(SaveData data)
        {
            if (data == null || playerLevel == null)
            {
                return;
            }

            data.playerHighestLevel = playerLevel.HighestLevel;
        }

        public override void Restore(SaveData data)
        {
            if (data == null || playerLevel == null)
            {
                return;
            }

            playerLevel.RestoreHighestLevel(data.playerHighestLevel);
        }
    }
}
