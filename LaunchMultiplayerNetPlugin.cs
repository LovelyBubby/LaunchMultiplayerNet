using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LaunchMultiplayerNet
{
    /// <summary>
    /// LaunchMultiplayerNet 独立 BepInEx 插件入口（v3.2+）。
    ///
    /// 设计目标：放进任何 Unturned BepInEx 环境的 plugins/ 文件夹都能独立正常运行，
    /// 无需任何前置拉起逻辑或外部协调。适配三种部署场景：
    ///   - 场景 A：玩家客户端 Unturned + BepInEx（客机）
    ///   - 场景 B：单机本地模式（SP，clientTransport==null，本地派发）
    ///   - 场景 C：独立启动的 U3DS Dedicated Server + BepInEx（纯服务器）
    ///
    /// v3.2 架构：vanilla ITransportConnection + "MOD" 魔数路由
    /// - 客户端 -> 服务器：反射 Provider.clientTransport.Send（vanilla SteamNetworkingSockets）
    /// - 服务器 -> 客户端：SteamPlayer.transportConnection.Send（vanilla SteamGameServerNetworkingSockets）
    /// - 接收：Harmony Prefix 拦截 NetMessages.ReceiveMessageFromClient/Server，魔数识别后切片派发
    /// - SP 本地模式：Provider.isServer 直接本地派发（不走网络）
    ///
    /// 消费方插件（LaunchInventoryTidy / LaunchInPlaceReload / LaunchHordeTracker 等）
    /// 通过 [BepInDependency(GUID)] 声明依赖，BepInEx 保证本插件先加载。
    /// 消费方无需再调 ModTransport.Initialize()（但调用也安全，幂等）。
    ///
    /// 后续新插件开发模板：
    /// <code>
    /// [BepInPlugin("com.you.yourmod", "YourMod", "1.0.0")]
    /// [BepInDependency(LaunchMultiplayerNetPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    /// public class YourModPlugin : BaseUnityPlugin {
    ///     private void Awake() {
    ///         ModTransport.RegisterServerHandler(ModChannels.YourChannel, HandleServerSide);
    ///         ModTransport.RegisterClientHandler(ModChannels.YourChannel, HandleClientSide);
    ///     }
    ///     private static void HandleServerSide(CSteamID sender, BinaryReader reader) { ... }
    ///     private static void HandleClientSide(BinaryReader reader) { ... }
    /// }
    /// </code>
    /// </summary>
    [BepInPlugin(Guid, "LaunchMultiplayerNet", Version)]
    public class LaunchMultiplayerNetPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.yu80rice.launchmultiplayernet";
        public const string Version = "3.2.0.0";

        internal static ManualLogSource LogSource;
        private Harmony _harmony;

        private void Awake()
        {
            // 自我保护 Manager GameObject（FACT.md 教训：BepInEx 5.4.22 在 Unturned Unity 2022.3.62 上
            // Manager GameObject 默认不被 DontDestroyOnLoad 保护，启动场景->主菜单场景切换时会被销毁，
            // 挂在上面的插件 MonoBehaviour OnDestroy 被调用表现为"插件自动卸载"）
            DontDestroyOnLoad(this.gameObject);
            this.gameObject.hideFlags = HideFlags.HideAndDontSave;

            LogSource = Logger;
            ModTransport.Initialize();

            _harmony = new Harmony("com.yu80rice.launchmultiplayernet");
            // NetMessages 是 internal 类，用 AccessTools 反射 patch（无法用 [HarmonyPatch(typeof())] 编译期绑定）
            Patches.NetMessagesReceiveClientPatch.Apply(_harmony);
            Patches.NetMessagesReceiveServerPatch.Apply(_harmony);
            LogSource.LogInfo($"[LaunchMultiplayerNet] v{Version} loaded (ITransportConnection + MOD magic, Harmony patches applied)");
        }

        private void Update()
        {
            // Plugin.Update 直驱 Poll（FACT.md 教训：避免 MonoBehaviour + AddComponent 调度不确定性）
            ModTransport.Poll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            ModTransport.Shutdown();
        }
    }
}
