using System;
using System.Reflection;
using SDG.NetTransport;
using SDG.Unturned;

namespace LaunchMultiplayerNet
{
    /// <summary>
    /// vanilla internal 字段反射访问缓存（v3.2）。
    ///
    /// 用途：mod 程序集无 InternalsVisibleTo，无法编译期访问 vanilla internal 成员。
    /// 启动时反射拿一次，缓存 FieldInfo，后续零反射开销。
    ///
    /// 缓存项：
    /// - Provider.clientTransport（internal static IClientTransport）：客户端发送链路
    ///
    /// 注意：SteamPlayer.transportConnection 是 public 属性（定义在父类 SteamConnectedClientBase，
    /// SteamPending.cs:73），mod 可直接编译期访问，无需反射。
    /// </summary>
    internal static class NetReflectionHelper
    {
        private static FieldInfo _clientTransportField;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                _clientTransportField = typeof(Provider).GetField(
                    "clientTransport",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (_clientTransportField == null)
                {
                    ModTransport.Log.LogError("[NetReflection] Provider.clientTransport field not found");
                }

                _initialized = true;
                ModTransport.Log.LogInfo(
                    $"[NetReflection] initialized (clientTransport={(_clientTransportField != null ? "ok" : "MISSING")}, " +
                    $"steamPlayerTransport=public-property-direct-access)");
            }
            catch (Exception e)
            {
                ModTransport.Log.LogError($"[NetReflection] initialize crash: {e}");
            }
        }

        /// <summary>
        /// 获取 Provider.clientTransport（客户端发送链路）。
        /// 返回 null 表示未连接 / SP 模式 / 反射失败。
        /// </summary>
        public static IClientTransport GetClientTransport()
        {
            if (_clientTransportField == null) return null;
            try
            {
                return _clientTransportField.GetValue(null) as IClientTransport;
            }
            catch (Exception e)
            {
                ModTransport.Log.LogError($"[NetReflection] GetClientTransport crash: {e}");
                return null;
            }
        }

        /// <summary>
        /// 获取 SteamPlayer 持有的 ITransportConnection（服务器端发送到指定客户端）。
        /// transportConnection 是 public 属性（SteamPending.cs:73，定义在 SteamConnectedClientBase 父类），
        /// mod 直接编译期访问，无需反射。
        /// </summary>
        public static ITransportConnection GetSteamPlayerTransport(SteamPlayer sp)
        {
            if (sp == null) return null;
            try
            {
                return sp.transportConnection;
            }
            catch (Exception e)
            {
                ModTransport.Log.LogError($"[NetReflection] GetSteamPlayerTransport crash: {e}");
                return null;
            }
        }
    }
}
