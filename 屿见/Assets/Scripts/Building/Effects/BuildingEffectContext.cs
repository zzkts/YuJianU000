using UnityEngine;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.Building.Effects
{
    /// <summary>
    /// 建筑效果初始化时拿到的「场景服务」集合。
    ///
    /// 为什么需要它：效果挂在**建筑预制体**上，而 DamageSystem / PlayerCurrency 住在场景里。
    /// 预制体资产不可能持有场景引用（Unity 存下来只会变成 Missing），所以这些引用只能在运行时注入。
    /// 效果不要自己去 FindObjectOfType，一律通过本结构拿依赖——将来服务来源要换
    /// （例如接入存档后换成"从存档恢复出来的实例"），只改这一处，效果代码一行不动。
    ///
    /// 只装**跨效果共用的**依赖。效果自己的数值（攻击间隔、攻击范围、图层）仍然写在
    /// 效果自身的 Inspector 上，不进这里。
    /// </summary>
    public readonly struct BuildingEffectContext
    {
        public BuildingEffectContext(GameObject building, DamageSystem damageSystem, PlayerCurrency playerCurrency)
        {
            Building = building;
            DamageSystem = damageSystem;
            PlayerCurrency = playerCurrency;
        }

        /// <summary>效果所属的建筑实例根物体。建筑被销毁时它一起消失。</summary>
        public GameObject Building { get; }

        /// <summary>
        /// 伤害计算系统。建筑攻击**必须**走它，才能和玩家手动攻击共用同一套公式，
        /// 从而实现"玩家成长 → 建筑自动攻击同步成长"。
        /// </summary>
        public DamageSystem DamageSystem { get; }

        /// <summary>玩家货币。建筑击碎矿石时按与玩家挖矿相同的规则发放奖励。</summary>
        public PlayerCurrency PlayerCurrency { get; }

        /// <summary>依赖是否齐全。缺任何一个，效果都无法正常工作。</summary>
        public bool IsValid => Building != null && DamageSystem != null && PlayerCurrency != null;

        public override string ToString()
        {
            return $"建筑={(Building != null ? Building.name : "缺失")}, " +
                   $"DamageSystem={(DamageSystem != null ? "有" : "缺失")}, " +
                   $"PlayerCurrency={(PlayerCurrency != null ? "有" : "缺失")}";
        }
    }
}
