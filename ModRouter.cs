using System;
using System.IO;
using SDG.NetTransport;
using Steamworks;

namespace LaunchMultiplayerNet
{
    /// <summary>
    /// 模组包路由器（v3.2）。
    ///
    /// 设计：
    /// - 客户端/服务器双向包统一加 "MOD" 魔数前缀（0x4D 0x4F 0x44）
    /// - Harmony Prefix 在 vanilla NetMessages 接收入口检测魔数，命中则切片派发到 mod handler
    /// - 关键陷阱：Provider.buffer（65535 字节）在 vanilla while 循环中每包覆盖，
    ///   Prefix 必须立即 Array.Copy 复制 payload 到 mod 自有 buffer，再 return false
    ///
    /// 包格式：
    ///   [0x4D 0x4F 0x44 "MOD"][virtual_channel:1byte][EModMessage:1byte][business_payload]
    ///
    /// 派发方向：
    /// - 服务器端 Prefix 拦截 ReceiveMessageFromClient -> 派发到 ServerHandlers
    /// - 客户端 Prefix 拦截 ReceiveMessageFromServer -> 派发到 ClientHandlers
    /// </summary>
    internal static class ModRouter
    {
        public const byte Magic0 = 0x4D; // 'M'
        public const byte Magic1 = 0x4F; // 'O'
        public const byte Magic2 = 0x44; // 'D'
        public const int MagicSize = 3;

        /// <summary>
        /// 服务器端：检测入站包是否为 mod 包，命中则切片派发到 ServerHandlers。
        /// 由 NetMessagesReceiveClientPatch.Prefix 调用。
        /// </summary>
        /// <param name="transportConnection">发送方连接（用于识别 SteamID）</param>
        /// <param name="packet">vanilla Provider.buffer（会被下一包覆盖，必须立即复制）</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="size">包总长度</param>
        /// <returns>true=已处理（vanilla 应跳过）；false=非 mod 包（vanilla 应继续）</returns>
        public static bool TryHandleFromClient(ITransportConnection transportConnection, byte[] packet, int offset, int size)
        {
            if (size < MagicSize + 2) return false;
            if (packet[offset] != Magic0 || packet[offset + 1] != Magic1 || packet[offset + 2] != Magic2)
                return false;

            // 立即复制 payload 到 mod 自有 buffer（Provider.buffer 每包覆盖陷阱）
            int payloadLen = size - MagicSize;
            byte[] payloadCopy = new byte[payloadLen];
            Buffer.BlockCopy(packet, offset + MagicSize, payloadCopy, 0, payloadLen);

            byte virtualChannel = payloadCopy[0];
            CSteamID sender = CSteamID.Nil;
            if (transportConnection != null)
            {
                try
                {
                    if (transportConnection.TryGetSteamId(out ulong steamId))
                    {
                        sender = new CSteamID(steamId);
                    }
                }
                catch (Exception e)
                {
                    ModTransport.Log.LogWarning($"[ModRouter] TryGetSteamId crash: {e}");
                }
            }

            // 暂存队列逻辑由 ModTransport.HandleServerPacket 内部处理（handler 未注册时入队）
            ModTransport.HandleServerPacket(virtualChannel, sender, payloadCopy, 1, payloadLen - 1);
            return true;
        }

        /// <summary>
        /// 客户端：检测入站包是否为 mod 包，命中则切片派发到 ClientHandlers。
        /// 由 NetMessagesReceiveServerPatch.Prefix 调用。
        /// </summary>
        public static bool TryHandleFromServer(byte[] packet, int offset, int size)
        {
            if (size < MagicSize + 2) return false;
            if (packet[offset] != Magic0 || packet[offset + 1] != Magic1 || packet[offset + 2] != Magic2)
                return false;

            int payloadLen = size - MagicSize;
            byte[] payloadCopy = new byte[payloadLen];
            Buffer.BlockCopy(packet, offset + MagicSize, payloadCopy, 0, payloadLen);

            byte virtualChannel = payloadCopy[0];
            ModTransport.HandleClientPacket(virtualChannel, payloadCopy, 1, payloadLen - 1);
            return true;
        }

        /// <summary>
        /// 组包：[MOD 魔数 3 字节][virtual_channel 1 字节][payload...]。
        /// 调用方负责通过 ITransportConnection.Send / IClientTransport.Send 发出。
        /// </summary>
        public static byte[] BuildModPacket(int virtualChannel, byte[] payload)
        {
            byte[] full = new byte[MagicSize + 1 + payload.Length];
            full[0] = Magic0;
            full[1] = Magic1;
            full[2] = Magic2;
            full[3] = (byte)virtualChannel;
            Buffer.BlockCopy(payload, 0, full, MagicSize + 1, payload.Length);
            return full;
        }
    }
}
