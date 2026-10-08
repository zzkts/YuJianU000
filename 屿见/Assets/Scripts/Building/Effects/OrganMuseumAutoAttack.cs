using UnityEngine;
using Yujian.Mining;

namespace Yujian.Building.Effects
{
    /// <summary>
    /// 风琴博物馆的效果：自动攻击矿石。
    ///
    /// 规则（阶段 5，2026-10-08 用户拍板后的版本）：
    ///   · **不按距离**：建筑建在矿区之外，所以"从建筑量 N 米"这种判据一律不用。
    ///     目标就是**场景里现存的矿石**（`Ore` 层上挂了 <see cref="Ore"/> 的物体），
    ///     用现在这套矿石系统，不引入矿区组件、不引入刷新器（用户 2026-10-08 明确要求）。
    ///   · 只打矿石：玩家、其他建筑、NPC、地形都不打（靠 `oreLayers` 图层过滤）。
    ///   · 每次伤害 = **玩家当前的单次攻击伤害**，攻击瞬间调
    ///     <see cref="DamageSystem.CalculatePlayerDamage"/> 现算——
    ///     玩家基础攻击力 / 伤害倍率一涨，建筑下一发立刻同步，这是本阶段最核心的一条规则。
    ///   · 每次攻击都重新找一遍目标，多个矿石时打**最近**的那个（矿区离建筑远，中间不会有东西挡着，
    ///     最近优先只是让多座建筑的分工看起来合理，不是玩法规则）。
    ///   · 击碎矿石时按与玩家手动挖矿完全相同的规则发奖励（用户已确认：和手动挖一样发）。
    ///
    /// 不做的事：不做寻路、不做仇恨值、不做复杂特效、不保存任何状态（未来读档时建筑重新生成即可）。
    ///
    /// ⚠️ 与阶段 5 原始 Prompt 的差异：Prompt 要求 `AttackRange = 5` + Gizmo 画攻击范围。
    /// 用户 2026-10-08 澄清"建筑在矿区之外、不按距离打矿区里的矿石"之后，射程这个概念失去了含义，
    /// 该字段已删除，Gizmo 改成"把当前能打到的矿石逐个标出来"。见 开发进度.md 第七节第 14 条。
    /// </summary>
    public class OrganMuseumAutoAttack : BuildingEffect
    {
        [Header("攻击")]
        [Tooltip("两次攻击之间的间隔（秒）。策划案未给数值，MVP 默认 1；建好后立刻打第一发，之后按间隔循环")]
        [SerializeField] private float attackInterval = 1f;

        [Tooltip("可攻击目标所在图层。**只勾 Ore**——勾了别的层就会打到不该打的东西。" +
                 "没有射程限制，这一层就是唯一的目标范围")]
        [SerializeField] private LayerMask oreLayers;

        [Tooltip("出射点。留空则用建筑根物体（地面上的占地中心）。只影响表现，不影响能不能打到")]
        [SerializeField] private Transform attackOrigin;

        [Header("结算")]
        [Tooltip("击碎矿石时是否按玩家挖矿的规则发放奖励。用户已确认：和手动挖一样发，不要关")]
        [SerializeField] private bool grantRewardOnKill = true;

        [Header("表现（MVP：只画一条线）")]
        [Tooltip("攻击时从出射点到目标画一条线。Scene 视图一定能看到；" +
                 "Game 视图需要在右上角 Gizmos 菜单里打开。正式版再来做真正的特效")]
        [SerializeField] private bool drawAttackLine = true;

        [SerializeField] private Color attackLineColor = new Color(1f, 0.85f, 0.3f, 1f);

        [Tooltip("攻击线停留时长（秒）")]
        [SerializeField] private float attackLineDuration = 0.15f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印每次自动攻击")]
        [SerializeField] private bool logAttackFlow = true;

        /// <summary>每帧查一次所有矿石太浪费；攻击间隔最短也是帧级，这里每次攻击查一次即可。</summary>
        private float timer;
        private Ore currentTarget;

        /// <summary>配置是否可用。图层没勾时置 false，效果整个停摆——不每秒刷屏、也不空跑。</summary>
        private bool canAttack;

        /// <summary>"场景里没有可打的矿石"是否已经提示过，避免矿区被打空之后每秒刷一条。</summary>
        private bool loggedNoTarget;

        /// <summary>实际出射点。</summary>
        private Transform Origin => attackOrigin != null ? attackOrigin : transform;

        protected override void OnInitialized()
        {
            if (attackInterval <= 0f)
            {
                Debug.LogWarning($"[OrganMuseumAutoAttack] 攻击间隔配置为非正数({attackInterval})，已按 1 秒处理。" +
                                 "间隔为 0 会变成每帧都打一次。", this);
                attackInterval = 1f;
            }

            if (oreLayers.value == 0)
            {
                canAttack = false;
                Debug.LogError("[OrganMuseumAutoAttack] Ore Layers 没有勾选任何图层，" +
                               "风琴博物馆永远找不到目标，效果不会启动（空跑只会刷屏）。" +
                               "请把该字段勾成 Ore 层，或运行菜单「屿见/配置建筑效果（阶段 5）」自动勾好。", this);
                return;
            }

            canAttack = true;

            // 建成立刻打第一发：策划案"每 1 秒攻击一次"，同时也让验收一眼就能看到效果
            timer = attackInterval;

            if (logAttackFlow)
            {
                Debug.Log($"[OrganMuseumAutoAttack] {name} 已启动：每 {attackInterval} 秒自动攻击一次，" +
                          "目标是场景里的矿石（不受距离限制），伤害取玩家当前单次攻击伤害。", this);
            }
        }

        protected override void OnShutdown()
        {
            timer = 0f;
            currentTarget = null;

            if (logAttackFlow)
            {
                Debug.Log($"[OrganMuseumAutoAttack] {name} 已销毁，自动攻击停止（无协程、无计时器、无事件残留）。", this);
            }
        }

        /// <summary>
        /// 建筑被停用时（例如整座建筑 SetActive(false)）把计时清掉，
        /// 免得重新启用时攒下的时间一次放出来。本组件不做任何跨帧的注册，所以停用即静默。
        /// </summary>
        private void OnDisable()
        {
            timer = 0f;
            currentTarget = null;
        }

        private void Update()
        {
            if (!IsInitialized || !canAttack)
            {
                return;
            }

            timer += Time.deltaTime;

            if (timer < attackInterval)
            {
                return;
            }

            // 归零而不是累减：攻击节奏严格等于 attackInterval；
            // 万一某一帧卡顿得比间隔还长，也只会补打一发，不会在一帧里连打好几发
            timer = 0f;
            AttackOnce();
        }

        /// <summary>一次完整的攻击：找目标 → 现算玩家伤害 → 造成伤害 → 击碎则发奖励。</summary>
        private void AttackOnce()
        {
            Ore target = FindNearestOre();

            if (target == null)
            {
                currentTarget = null;

                // 只提示第一次：矿区被打空之后每秒一条日志会把 Console 刷满
                if (logAttackFlow && !loggedNoTarget)
                {
                    loggedNoTarget = true;
                    Debug.Log($"[OrganMuseumAutoAttack] {name} 找不到可攻击的矿石" +
                              "（场景里没有存活的 Ore 层矿石）。此提示只出现一次，" +
                              "矿石重新出现后会自动恢复攻击。", this);
                }

                return;
            }

            loggedNoTarget = false;
            currentTarget = target;

            // 每发都现算：玩家升级后，建筑的下一发立刻跟着变强（本阶段的核心验收点）
            float damage = Context.DamageSystem.CalculatePlayerDamage();

            if (damage <= 0f)
            {
                // DamageSystem 已经打印过明确错误，这里不重复刷屏
                return;
            }

            // 先取出名字、奖励与位置：TakeDamage 可能击碎并销毁矿石（与 MiningInput 相同的顺序）
            string targetName = target.name;
            int reward = target.Reward;
            Vector3 targetPosition = target.transform.position;

            bool destroyed = target.TakeDamage(damage);

            if (drawAttackLine)
            {
                Debug.DrawLine(Origin.position, targetPosition, attackLineColor, attackLineDuration);
            }

            if (logAttackFlow)
            {
                Debug.Log($"[OrganMuseumAutoAttack] {name} 自动攻击 {targetName}，" +
                          $"造成 {damage} 点伤害{(destroyed ? "，已击碎" : string.Empty)}。" +
                          $"（伤害来自玩家当前值：基础攻击力 × 伤害倍率）", this);
            }

            if (!destroyed || !grantRewardOnKill || reward <= 0)
            {
                return;
            }

            // 与 MiningInput 同一条规则：击碎才发奖励，奖励值取自矿石自己
            Context.PlayerCurrency.AddMoney(reward);

            if (logAttackFlow)
            {
                Debug.Log($"[OrganMuseumAutoAttack] 击碎 {targetName}，发放 {reward} 金币，" +
                          $"当前金币 {Context.PlayerCurrency.CurrentMoney}", this);
            }
        }

        /// <summary>
        /// 找出当前可攻击的矿石里最近的一个。
        ///
        /// 直接扫场景里的 <see cref="Ore"/> 组件——用的就是现在这套矿石系统，
        /// 不引入矿区组件、不引入刷新器（用户 2026-10-08 的决定）。
        /// 每秒最多查一次，场景里矿石数量是十位数量级，这点开销可以忽略；
        /// 将来矿区真的做到成百上千个矿石，这里再换成矿区维护的列表。
        /// </summary>
        private Ore FindNearestOre()
        {
            Vector3 origin = Origin.position;

            Ore[] ores = Object.FindObjectsByType<Ore>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            Ore nearest = null;
            float nearestSqrDistance = float.MaxValue;

            for (int i = 0; i < ores.Length; i++)
            {
                Ore ore = ores[i];

                if (ore == null || !ore.IsAlive)
                {
                    continue;
                }

                // 唯一的目标过滤：图层。与 MiningInput 用同一份 Ore 层约定
                if ((oreLayers.value & (1 << ore.gameObject.layer)) == 0)
                {
                    continue;
                }

                float sqrDistance = (ore.transform.position - origin).sqrMagnitude;

                if (sqrDistance >= nearestSqrDistance)
                {
                    continue;
                }

                nearestSqrDistance = sqrDistance;
                nearest = ore;
            }

            return nearest;
        }

        /// <summary>
        /// 在 Scene 视图里把"现在能打到哪些矿石"逐个标出来。
        ///
        /// ⚠️ 没有射程限制，就没有"攻击范围"可画——阶段 5 原始 Prompt 要求的范围圆圈在这里没有意义，
        /// 改成本方法的做法。要改回范围制的话，先把规则本身谈清楚（见 开发进度.md 第七节第 14 条）。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Ore[] ores = Object.FindObjectsByType<Ore>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (ores.Length == 0)
            {
                return;
            }

            Vector3 origin = transform.position;
            bool playing = Application.isPlaying;
            Transform focal = Origin;
            Vector3 lineFrom = focal != null ? focal.position : origin;

            for (int i = 0; i < ores.Length; i++)
            {
                Ore ore = ores[i];

                if (ore == null || !ore.IsAlive)
                {
                    continue;
                }

                bool attackable = (oreLayers.value & (1 << ore.gameObject.layer)) != 0;
                Vector3 position = ore.transform.position;

                if (!attackable)
                {
                    // 在 Ore 层之外：标出来但不能打，图层配错时一眼可见
                    Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                    Gizmos.DrawWireCube(position, Vector3.one * 0.6f);
                    continue;
                }

                bool isCurrent = playing && currentTarget != null && currentTarget == ore;

                Gizmos.color = isCurrent
                    ? Color.red
                    : new Color(1f, 0.85f, 0.3f, 0.6f);

                Gizmos.DrawLine(lineFrom, position);
                Gizmos.DrawWireCube(position, Vector3.one * 0.6f);
            }
        }
    }
}
