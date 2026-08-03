using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace LaunchMultiplayerNet
{
    /// <summary>
    /// 基于 vanilla ITransportConnection + 魔数路由的双端网络传输层（v3.2）。
    ///
    /// v3.2 架构（2026-07-15 修复 v3.1 双缺陷）：
    /// - 缺陷 1：U3DS 端 Steamworks 未初始化（dedicated server 不初始化 Steamworks 客户端 API）
    /// - 缺陷 2：客户端 P2P 到 SteamGameServer ID 不可达（error=4 TargetNotRunningGame）
    /// - 修复：弃用 SteamNetworking P2P，改用 vanilla ITransportConnection.Send
    ///   - 客户端 -> 服务器：反射 Provider.clientTransport（internal static IClientTransport）
    ///   - 服务器 -> 客户端：SteamPlayer.transportConnection（instance field）
    ///   - 接收：Harmony Prefix 拦截 NetMessages.ReceiveMessageFromClient/Server
    ///   - 路由：3 字节魔数 "MOD" + virtual_channel 字节
    ///
    /// v3.0/v3.1 保留部分：
    /// - 独立 BepInEx 插件模式（LaunchMultiplayerNetPlugin）
    /// - 未匹配请求暂存队列（handler 注册竞争保护）
    /// - 消息格式：[virtual_channel:1byte][EModMessage:1byte][business_payload]
    ///
    /// SP 本地模式：
    /// - Provider.singleplayer() 下 clientTransport==null（永不初始化）
    /// - SendToServer 检测 Provider.isServer 直接本地派发到 ServerHandlers（loopback）
    ///
    /// 关键陷阱（FACT.md 教训）：
    /// - Provider.buffer（65535 字节）在 vanilla while 循环中每包覆盖
    /// - ModRouter.TryHandle* 内部必须立即 Array.Copy 复制 payload
    /// </summary>
    public static class ModTransport
    {
        internal static ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("LaunchMultiplayerNet");

        private static bool _initialized;

        /// <summary>服务器端虚拟通道处理器表：virtual_channel -> (sender, reader) callback。</summary>
        internal static readonly Dictionary<int, Action<CSteamID, BinaryReader>> ServerHandlers =
            new Dictionary<int, Action<CSteamID, BinaryReader>>();

        /// <summary>客户端虚拟通道处理器表：virtual_channel -> reader callback。</summary>
        internal static readonly Dictionary<int, Action<BinaryReader>> ClientHandlers =
            new Dictionary<int, Action<BinaryReader>>();

        /// <summary>未匹配请求暂存队列（上限 64 条/端，过期 120s）。</summary>
        private static readonly List<PendingServerRequest> _pendingServerRequests =
            new List<PendingServerRequest>();
        private static readonly List<PendingClientRequest> _pendingClientRequests =
            new List<PendingClientRequest>();

        private const int MaxPendingPerSide = 64;
        private const float PendingExpireSeconds = 120f;

        // ─────────────────────────────────────────────────────────────
        // 生命周期
        // ─────────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized)
            {
                Log.LogInfo("[ModTransport] Initialize called but already initialized (idempotent no-op)");
                return;
            }

            NetReflectionHelper.Initialize();
            _initialized = true;
            Log.LogInfo($"[ModTransport] v3.2 initialized (ITransportConnection + MOD magic, isServer={Provider.isServer})");
        }

        public static void Shutdown()
        {
            if (!_initialized) return;

            ServerHandlers.Clear();
            ClientHandlers.Clear();
            lock (_pendingServerRequests) _pendingServerRequests.Clear();
            lock (_pendingClientRequests) _pendingClientRequests.Clear();
            _initialized = false;
            Log.LogInfo("[ModTransport] shutdown");
        }

        // ─────────────────────────────────────────────────────────────
        // 处理器注册（注册时自动回放暂存队列）
        // ─────────────────────────────────────────────────────────────

        public static void RegisterServerHandler(int virtualChannel, Action<CSteamID, BinaryReader> handler)
        {
            if (handler == null) return;
            ServerHandlers[virtualChannel] = handler;
            Log.LogInfo($"[ModTransport] server handler registered: channel={virtualChannel}, total={ServerHandlers.Count}");
            ReplayPendingServerRequests(virtualChannel, handler);
        }

        public static void RegisterClientHandler(int virtualChannel, Action<BinaryReader> handler)
        {
            if (handler == null) return;
            ClientHandlers[virtualChannel] = handler;
            Log.LogInfo($"[ModTransport] client handler registered: channel={virtualChannel}, total={ClientHandlers.Count}");
            ReplayPendingClientRequests(virtualChannel, handler);
        }

        // ─────────────────────────────────────────────────────────────
        // 主循环驱动（保留 Poll 接口供 Plugin.Update 调用，v3.2 主要做暂存队列清理）
        // ─────────────────────────────────────────────────────────────

        public static void Poll()
        {
            if (!_initialized) return;
            EvictExpiredPending();
        }

        // ─────────────────────────────────────────────────────────────
        // 接收派发（由 ModRouter 调用）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 服务器端收到客户端包后派发到 ServerHandlers。
        /// 输入 payload 已是 mod 自有 buffer（ModRouter 已复制），格式 [virtual_channel][EModMessage][business]。
        /// </summary>
        internal static void HandleServerPacket(byte virtualChannel, CSteamID sender, byte[] payload, int offset, int length)
        {
            if (length < 1)
            {
                Log.LogWarning($"[ModTransport] server packet empty from {sender}");
                return;
            }

            if (!ServerHandlers.TryGetValue(virtualChannel, out var handler))
            {
                EnqueuePendingServer(virtualChannel, sender, payload, offset, length);
                return;
            }

            try
            {
                using (var ms = new MemoryStream(payload, offset, length))
                using (var reader = new BinaryReader(ms))
                {
                    handler(sender, reader);
                }
            }
            catch (Exception e)
            {
                Log.LogError($"[ModTransport] server handler (channel={virtualChannel}) crash: {e}");
            }
        }

        /// <summary>
        /// 客户端收到服务器包后派发到 ClientHandlers。
        /// 输入 payload 已是 mod 自有 buffer（ModRouter 已复制）。
        /// </summary>
        internal static void HandleClientPacket(byte virtualChannel, byte[] payload, int offset, int length)
        {
            if (length < 1)
            {
                Log.LogWarning("[ModTransport] client packet empty");
                return;
            }

            if (!ClientHandlers.TryGetValue(virtualChannel, out var handler))
            {
                EnqueuePendingClient(virtualChannel, payload, offset, length);
                return;
            }

            try
            {
                using (var ms = new MemoryStream(payload, offset, length))
                using (var reader = new BinaryReader(ms))
                {
                    handler(reader);
                }
            }
            catch (Exception e)
            {
                Log.LogError($"[ModTransport] client handler (channel={virtualChannel}) crash: {e}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 发送 API
        // ─────────────────────────────────────────────────────────────

        public static void SendToServer(int virtualChannel, byte[] payload, bool reliable = true)
        {
            if (!_initialized) return;
            if (payload == null || payload.Length == 0) return;

            // 服务器自身（dedicated server 自调或 SP 模式）-> 本地派发
            if (Provider.isServer)
            {
                LoopbackToServer(virtualChannel, payload);
                return;
            }

            // 客户端 -> 服务器：反射 Provider.clientTransport
            IClientTransport transport = NetReflectionHelper.GetClientTransport();
            if (transport == null)
            {
                Log.LogWarning($"[ModTransport] SendToServer: clientTransport null (not connected?), channel={virtualChannel}");
                return;
            }

            byte[] packet = ModRouter.BuildModPacket(virtualChannel, payload);
            ENetReliability reliability = reliable ? ENetReliability.Reliable : ENetReliability.Unreliable;
            try
            {
                transport.Send(packet, packet.Length, reliability);
            }
            catch (Exception e)
            {
                Log.LogError($"[ModTransport] SendToServer crash: {e}");
            }
        }

        public static void SendToClient(CSteamID client, int virtualChannel, byte[] payload, bool reliable = true)
        {
            if (!_initialized) return;
            if (!Provider.isServer) return;
            if (payload == null || payload.Length == 0) return;

            ITransportConnection transport = FindClientTransport(client);
            if (transport == null)
            {
                Log.LogWarning($"[ModTransport] SendToClient: transport not found for {client}, channel={virtualChannel}");
                return;
            }

            SendViaTransport(transport, virtualChannel, payload, reliable);
        }

        public static void BroadcastToAllClients(int virtualChannel, byte[] payload, bool reliable = true)
        {
            if (!_initialized) return;
            if (!Provider.isServer) return;
            if (payload == null || payload.Length == 0) return;

            var clients = Provider.clients;
            if (clients == null) return;

            byte[] packet = null;
            ENetReliability reliability = reliable ? ENetReliability.Reliable : ENetReliability.Unreliable;

            for (int i = 0; i < clients.Count; i++)
            {
                SteamPlayer sp = clients[i];
                if (sp == null) continue;

                ITransportConnection transport = NetReflectionHelper.GetSteamPlayerTransport(sp);
                if (transport == null) continue;

                if (packet == null)
                {
                    packet = ModRouter.BuildModPacket(virtualChannel, payload);
                }

                try
                {
                    transport.Send(packet, packet.Length, reliability);
                }
                catch (Exception e)
                {
                    CSteamID sid = CSteamID.Nil;
                    if (!ReferenceEquals(sp.playerID, null)) sid = sp.playerID.steamID;
                    Log.LogError($"[ModTransport] Broadcast crash (target={sid}): {e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 内部发送辅助
        // ─────────────────────────────────────────────────────────────

        private static void SendViaTransport(ITransportConnection transport, int virtualChannel, byte[] payload, bool reliable)
        {
            byte[] packet = ModRouter.BuildModPacket(virtualChannel, payload);
            ENetReliability reliability = reliable ? ENetReliability.Reliable : ENetReliability.Unreliable;
            try
            {
                transport.Send(packet, packet.Length, reliability);
            }
            catch (Exception e)
            {
                Log.LogError($"[ModTransport] SendViaTransport crash: {e}");
            }
        }

        private static ITransportConnection FindClientTransport(CSteamID steamId)
        {
            var clients = Provider.clients;
            if (clients == null) return null;

            for (int i = 0; i < clients.Count; i++)
            {
                SteamPlayer sp = clients[i];
                if (sp == null || ReferenceEquals(sp.playerID, null)) continue;
                if (sp.playerID.steamID == steamId)
                {
                    return NetReflectionHelper.GetSteamPlayerTransport(sp);
                }
            }
            return null;
        }

        /// <summary>
        /// SP 本地模式 / 服务器自调：直接派发到 ServerHandlers（loopback）。
        /// 发送方 sender 设为 Provider.server（SP 下即玩家自身）。
        /// </summary>
        private static void LoopbackToServer(int virtualChannel, byte[] payload)
        {
            if (!ServerHandlers.TryGetValue(virtualChannel, out var handler))
            {
                EnqueuePendingServer(virtualChannel, Provider.server, payload, 0, payload.Length);
                return;
            }

            try
            {
                using (var ms = new MemoryStream(payload, 0, payload.Length))
                using (var reader = new BinaryReader(ms))
                {
                    handler(Provider.server, reader);
                }
            }
            catch (Exception e)
            {
                Log.LogError($"[ModTransport] loopback handler (channel={virtualChannel}) crash: {e}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 序列化辅助
        // ─────────────────────────────────────────────────────────────

        public static byte[] BuildMessage(EModMessage msg, Action<BinaryWriter> bodyWriter = null)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)msg);
                bodyWriter?.Invoke(w);
                return ms.ToArray();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 暂存队列内部 API
        // ─────────────────────────────────────────────────────────────

        internal static void EnqueuePendingServer(int virtualChannel, CSteamID sender, byte[] payload, int offset, int length)
        {
            lock (_pendingServerRequests)
            {
                if (_pendingServerRequests.Count >= MaxPendingPerSide)
                {
                    _pendingServerRequests.RemoveAt(0);
                    Log.LogWarning("[ModTransport] pending server queue full, dropped oldest");
                }
                byte[] copy = new byte[length];
                Buffer.BlockCopy(payload, offset, copy, 0, length);
                _pendingServerRequests.Add(new PendingServerRequest
                {
                    VirtualChannel = virtualChannel,
                    Sender = sender,
                    Payload = copy,
                    EnqueuedAt = Time.realtimeSinceStartup
                });
            }
            Log.LogWarning($"[ModTransport] pending server request enqueued: channel={virtualChannel}, sender={sender}");
        }

        internal static void EnqueuePendingClient(int virtualChannel, byte[] payload, int offset, int length)
        {
            lock (_pendingClientRequests)
            {
                if (_pendingClientRequests.Count >= MaxPendingPerSide)
                {
                    _pendingClientRequests.RemoveAt(0);
                    Log.LogWarning("[ModTransport] pending client queue full, dropped oldest");
                }
                byte[] copy = new byte[length];
                Buffer.BlockCopy(payload, offset, copy, 0, length);
                _pendingClientRequests.Add(new PendingClientRequest
                {
                    VirtualChannel = virtualChannel,
                    Payload = copy,
                    EnqueuedAt = Time.realtimeSinceStartup
                });
            }
            Log.LogWarning($"[ModTransport] pending client request enqueued: channel={virtualChannel}");
        }

        private static void ReplayPendingServerRequests(int virtualChannel, Action<CSteamID, BinaryReader> handler)
        {
            List<PendingServerRequest> toReplay;
            lock (_pendingServerRequests)
            {
                if (_pendingServerRequests.Count == 0) return;
                toReplay = _pendingServerRequests.FindAll(p => p.VirtualChannel == virtualChannel);
                _pendingServerRequests.RemoveAll(p => p.VirtualChannel == virtualChannel);
            }

            if (toReplay.Count == 0) return;

            Log.LogInfo($"[ModTransport] replaying {toReplay.Count} pending server request(s) for channel={virtualChannel}");
            foreach (var pending in toReplay)
            {
                try
                {
                    using (var ms = new MemoryStream(pending.Payload))
                    using (var reader = new BinaryReader(ms))
                    {
                        handler(pending.Sender, reader);
                    }
                }
                catch (Exception e)
                {
                    Log.LogError($"[ModTransport] replay pending server request crashed: {e}");
                }
            }
        }

        private static void ReplayPendingClientRequests(int virtualChannel, Action<BinaryReader> handler)
        {
            List<PendingClientRequest> toReplay;
            lock (_pendingClientRequests)
            {
                if (_pendingClientRequests.Count == 0) return;
                toReplay = _pendingClientRequests.FindAll(p => p.VirtualChannel == virtualChannel);
                _pendingClientRequests.RemoveAll(p => p.VirtualChannel == virtualChannel);
            }

            if (toReplay.Count == 0) return;

            Log.LogInfo($"[ModTransport] replaying {toReplay.Count} pending client request(s) for channel={virtualChannel}");
            foreach (var pending in toReplay)
            {
                try
                {
                    using (var ms = new MemoryStream(pending.Payload))
                    using (var reader = new BinaryReader(ms))
                    {
                        handler(reader);
                    }
                }
                catch (Exception e)
                {
                    Log.LogError($"[ModTransport] replay pending client request crashed: {e}");
                }
            }
        }

        private static void EvictExpiredPending()
        {
            float now = Time.realtimeSinceStartup;
            lock (_pendingServerRequests)
            {
                int before = _pendingServerRequests.Count;
                _pendingServerRequests.RemoveAll(p => now - p.EnqueuedAt > PendingExpireSeconds);
                if (_pendingServerRequests.Count < before)
                {
                    Log.LogWarning($"[ModTransport] evicted {before - _pendingServerRequests.Count} expired pending server request(s)");
                }
            }
            lock (_pendingClientRequests)
            {
                int before = _pendingClientRequests.Count;
                _pendingClientRequests.RemoveAll(p => now - p.EnqueuedAt > PendingExpireSeconds);
                if (_pendingClientRequests.Count < before)
                {
                    Log.LogWarning($"[ModTransport] evicted {before - _pendingClientRequests.Count} expired pending client request(s)");
                }
            }
        }

        private struct PendingServerRequest
        {
            public int VirtualChannel;
            public CSteamID Sender;
            public byte[] Payload;
            public float EnqueuedAt;
        }

        private struct PendingClientRequest
        {
            public int VirtualChannel;
            public byte[] Payload;
            public float EnqueuedAt;
        }
    }
}
