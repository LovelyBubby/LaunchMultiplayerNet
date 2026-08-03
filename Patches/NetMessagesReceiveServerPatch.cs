using System.Reflection;
using HarmonyLib;
using SDG.Unturned;

namespace LaunchMultiplayerNet.Patches
{
    /// <summary>
    /// 客户端：Harmony Prefix 拦截 NetMessages.ReceiveMessageFromServer（v3.2）。
    ///
    /// NetMessages 类是 internal，改用 AccessTools.TypeByName + AccessTools.Method 运行时反射绑定，
    /// LaunchMultiplayerNetPlugin.Awake 手动 Patch。
    ///
    /// 作用：
    /// - 在 vanilla 读取 EClientMessage 之前嗅探首 3 字节魔数 "MOD"
    /// - 命中则切片派发到 ModRouter -> ModTransport.ClientHandlers，return false 跳过 vanilla
    /// - 未命中则 return true，vanilla 原路派发
    ///
    /// 关键陷阱：
    /// - Provider.buffer（65535 字节）在 Provider.listenClient() while 循环中每包覆盖
    /// - ModRouter.TryHandleFromServer 内部立即 Array.Copy 复制 payload 到 mod 自有 buffer
    /// </summary>
    internal static class NetMessagesReceiveServerPatch
    {
        internal static void Apply(Harmony harmony)
        {
            System.Type netMessagesType = AccessTools.TypeByName("SDG.Unturned.NetMessages");
            if (netMessagesType == null)
            {
                ModTransport.Log.LogError("[Patch-RecvServer] SDG.Unturned.NetMessages type not found");
                return;
            }

            MethodInfo target = AccessTools.Method(netMessagesType, "ReceiveMessageFromServer");
            if (target == null)
            {
                ModTransport.Log.LogError("[Patch-RecvServer] ReceiveMessageFromServer method not found");
                return;
            }

            MethodInfo prefix = typeof(NetMessagesReceiveServerPatch).GetMethod(
                nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModTransport.Log.LogInfo("[Patch-RecvServer] patched NetMessages.ReceiveMessageFromServer");
        }

        private static bool Prefix(byte[] packet, int offset, int size)
        {
            if (Provider.isServer) return true;

            try
            {
                if (ModRouter.TryHandleFromServer(packet, offset, size))
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                ModTransport.Log.LogError($"[Patch-RecvServer] crash: {e}");
            }
            return true;
        }
    }
}
