using System.Reflection;
using HarmonyLib;
using SDG.NetTransport;
using SDG.Unturned;

namespace LaunchMultiplayerNet.Patches
{
    /// <summary>
    /// 服务器端：Harmony Prefix 拦截 NetMessages.ReceiveMessageFromClient（v3.2）。
    ///
    /// NetMessages 类是 internal，无法用 [HarmonyPatch(typeof(NetMessages))] 编译期绑定。
    /// 改用 AccessTools.TypeByName + AccessTools.Method 运行时反射绑定，LaunchMultiplayerNetPlugin.Awake 手动 Patch。
    ///
    /// 作用：
    /// - 在 vanilla 读取 EServerMessage 之前嗅探首 3 字节魔数 "MOD"
    /// - 命中则切片派发到 ModRouter -> ModTransport.ServerHandlers，return false 跳过 vanilla
    /// - 未命中则 return true，vanilla 原路派发
    ///
    /// 关键陷阱：
    /// - Provider.buffer（65535 字节）在 Provider.listenServer() while 循环中每包覆盖
    /// - ModRouter.TryHandleFromClient 内部立即 Array.Copy 复制 payload 到 mod 自有 buffer
    /// </summary>
    internal static class NetMessagesReceiveClientPatch
    {
        internal static void Apply(Harmony harmony)
        {
            System.Type netMessagesType = AccessTools.TypeByName("SDG.Unturned.NetMessages");
            if (netMessagesType == null)
            {
                ModTransport.Log.LogError("[Patch-RecvClient] SDG.Unturned.NetMessages type not found");
                return;
            }

            MethodInfo target = AccessTools.Method(netMessagesType, "ReceiveMessageFromClient");
            if (target == null)
            {
                ModTransport.Log.LogError("[Patch-RecvClient] ReceiveMessageFromClient method not found");
                return;
            }

            MethodInfo prefix = typeof(NetMessagesReceiveClientPatch).GetMethod(
                nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModTransport.Log.LogInfo("[Patch-RecvClient] patched NetMessages.ReceiveMessageFromClient");
        }

        private static bool Prefix(ITransportConnection transportConnection, byte[] packet, int offset, int size)
        {
            if (!Provider.isServer) return true;

            try
            {
                if (ModRouter.TryHandleFromClient(transportConnection, packet, offset, size))
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                ModTransport.Log.LogError($"[Patch-RecvClient] crash: {e}");
            }
            return true;
        }
    }
}
