using UnityEngine;
using Yujian.Player;

namespace Yujian.Save
{
    /// <summary>金币的存档适配器。只负责把 PlayerCurrency.CurrentMoney 写进 / 读出 SaveData。</summary>
    public class CurrencySaveParticipant : SaveParticipant
    {
        [Tooltip("玩家金币。留空会明确报错，金币不会进入存档")]
        [SerializeField] private PlayerCurrency currency;

        public override void Capture(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (currency == null)
            {
                Debug.LogError("[CurrencySaveParticipant] PlayerCurrency 未配置，金币不会写入存档。", this);
                data.currency = 0;
                return;
            }

            data.currency = currency.CurrentMoney;
        }

        public override void Restore(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            if (currency == null)
            {
                Debug.LogError("[CurrencySaveParticipant] PlayerCurrency 未配置，金币无法恢复。", this);
                return;
            }

            currency.RestoreMoney(data.currency);
        }
    }
}
