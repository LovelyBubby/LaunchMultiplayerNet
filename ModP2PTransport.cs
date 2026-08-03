using System;
using System.IO;
using Steamworks;

namespace LaunchMultiplayerNet
{
    /// <summary>
    /// [已弃用] v1.x 基于 SteamNetworking P2P 的传输层。
    ///
    /// v3.1（2026-07-15）后，本类仅作为兼容别名，所有调用委托到 ModTransport。
    /// 新代码请直接使用 ModTransport。
    ///
    /// 弃用历史：
    /// - v1.x：原始 SteamNetworking P2P 实现
    /// - v2.0/v3.0：改为 vanilla SteamChannel（id=200）+ [SteamCall] RPC
    /// - v3.1：回到 SteamNetworking P2P（修复 v3.0 SendToServer 在 dedicated server 模式下包被丢弃）
    /// </summary>
    [Obsolete("Use ModTransport instead. v3.1 redirects to SteamNetworking P2P transport.")]
    public static class ModP2PTransport
    {
        public static void Initialize() => ModTransport.Initialize();
        public static void Shutdown() => ModTransport.Shutdown();

        public static void RegisterServerHandler(int virtualChannel, Action<CSteamID, BinaryReader> handler)
            => ModTransport.RegisterServerHandler(virtualChannel, handler);

        public static void RegisterClientHandler(int virtualChannel, Action<BinaryReader> handler)
            => ModTransport.RegisterClientHandler(virtualChannel, handler);

        public static void Poll() => ModTransport.Poll();

        public static void SendToServer(int virtualChannel, byte[] payload, bool reliable = true)
            => ModTransport.SendToServer(virtualChannel, payload, reliable);

        public static void SendToClient(CSteamID client, int virtualChannel, byte[] payload, bool reliable = true)
            => ModTransport.SendToClient(client, virtualChannel, payload, reliable);

        public static void BroadcastToAllClients(int virtualChannel, byte[] payload, bool reliable = true)
            => ModTransport.BroadcastToAllClients(virtualChannel, payload, reliable);

        public static byte[] BuildMessage(EModMessage msg, Action<BinaryWriter> bodyWriter = null)
            => ModTransport.BuildMessage(msg, bodyWriter);
    }
}
