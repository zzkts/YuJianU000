using UnityEngine;
using Yujian.Core;

namespace Yujian.Building.Effects
{
    /// <summary>
    /// 建筑效果基类。
    ///
    /// 架构（阶段 5 的核心约定）：建筑本体与建筑效果**互相不认识**。
    ///   BuildingData / BuildingManager / BuildingConstruction 里没有一行提到效果；
    ///   效果只是挂在建筑预制体上的一个组件，建成后自己初始化、自己按需驱动、随建筑销毁自动停止。
    ///
    ///   Building（预制体实例）
    ///       └─ BuildingEffect（本类，抽象）
    ///             ├─ OrganMuseumAutoAttack   风琴博物馆：自动攻击矿石
    ///             ├─ CoinMultiplierEffect    未来：金币倍率
    ///             ├─ OfflineIncomeEffect     未来：离线收益
    ///             └─ MiningSpeedEffect       未来：挖矿加速
    ///
    /// 加一个新效果 = 新写一个子类 + 挂到对应建筑的预制体上，其余代码零改动。
    ///
    /// 为什么用抽象基类而不是策划案里提到的 IBuildingEffect 接口：
    /// 效果首先得是 **Unity 组件**——要能挂到预制体上、要有 Inspector 数值、
    /// 要能被 GetComponentsInChildren 找到、要能随物体销毁自动停。接口给不了这些；
    /// 而"初始化 / 停用"这套生命周期又确实需要一份共享实现。所以取抽象基类，
    /// 保留策划案要的解耦，去掉接口在这个引擎里带不来的好处。
    ///
    /// 生命周期：
    ///   Initialize(context) —— 生效一次。谁都可以调；没人调时 Start() 会兜底从 GameManager 取服务。
    ///   Shutdown()          —— 停止效果，OnDestroy 自动调一次。
    ///
    /// ⚠ 子类只重写 OnInitialized / OnShutdown。**不要**自己写 Start / OnDestroy——
    /// 基类占用了这两个消息，写重了会让停用逻辑漏掉，建筑拆了效果还在跑。
    /// 需要在物体被禁用时清账的，写 OnDisable（基类不占用它）。
    /// </summary>
    public abstract class BuildingEffect : MonoBehaviour
    {
        /// <summary>效果拿到的场景服务。Initialize 成功之后才有值。</summary>
        public BuildingEffectContext Context { get; private set; }

        /// <summary>是否已初始化。未初始化的效果必须什么都不做。</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 初始化效果并开始工作。
        /// 重复调用只警告、不重新初始化；服务不齐时明确报错并保持未初始化状态（效果不会半启动）。
        /// </summary>
        public void Initialize(BuildingEffectContext context)
        {
            if (IsInitialized)
            {
                Debug.LogWarning($"[{GetType().Name}] 已经初始化过了，本次重复调用被忽略。", this);
                return;
            }

            if (!context.IsValid)
            {
                Debug.LogError($"[{GetType().Name}] 初始化失败，服务不完整：{context}。" +
                               "效果不会启动（避免带着空引用跑一半才崩）。", this);
                return;
            }

            Context = context;
            IsInitialized = true;
            OnInitialized();
        }

        /// <summary>停止效果，清掉一切正在进行的动作。OnDestroy 会自动调用；重复调用无副作用。</summary>
        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            // 先落标志再回调：OnShutdown 里若又触发一次 Shutdown（例如顺手 Destroy 了别的物体），
            // 不会重入第二次
            IsInitialized = false;
            OnShutdown();
        }

        /// <summary>初始化成功后的启动逻辑。</summary>
        protected abstract void OnInitialized();

        /// <summary>停用前的收尾逻辑。默认什么都不做。</summary>
        protected virtual void OnShutdown()
        {
        }

        /// <summary>
        /// 兜底初始化。
        /// 效果挂在预制体上，预制体拿不到场景引用，所以服务统一从 GameManager 这个全局入口取
        /// （GameManager 的存在意义之一就是"供没有场景引用可挂的系统访问"，见它的类注释）。
        /// 建筑是运行时生成物，本方法执行时场景里所有 Awake 都早已跑完，Instance 一定已经就位。
        /// </summary>
        protected void EnsureInitialized()
        {
            if (IsInitialized)
            {
                return;
            }

            GameManager manager = GameManager.Instance;

            if (manager == null)
            {
                Debug.LogError($"[{GetType().Name}] 场景里没有 GameManager，拿不到 DamageSystem / PlayerCurrency，" +
                               "效果不会启动。请确认场景里有挂着 GameManager 的物体、并且它引用了这三个系统。", this);
                return;
            }

            Initialize(new BuildingEffectContext(gameObject, manager.Damage, manager.Currency));
        }

        private void Start()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
