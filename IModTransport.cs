using System;
using System.IO;
using Steamworks;

namespace LaunchMultiplayerNet
{
    /// <summary>
    /// 双端自适应网络传输层接口。基于 vanilla SteamChannel（id=200）+ [SteamCall] 路由。
    ///
    /// 架构（2026-07-14 v2.0 重构）：
    /// - 弃用 listen server + SteamNetworking P2P 方案（GSLT 登录成功但 SDR 路由不通，详见 FACT.md）
    /// - 改用 vanilla SteamChannel（id=200）+ [SteamCall] RPC 框架
    /// - 单 vanilla 通道承载所有模组业务，virtual_channel 字节在 payload 头部区分路由
    /// - 既支持 U3DS dedicated server（Provider.isServer=true && Dedicator.IsDedicatedServer=true）
    ///   也支持 listen server（Provider.isServer=true && !Dedicator.IsDedicatedServer，向后兼容 v1.x）
    ///
    /// 9 个 API 签名与 v1.x ModP2PTransport 完全一致，消费方零改动。
    /// </summary>
    public interface IModTransport
    {
        // ─────────────────────────────────────────────────────────────
        // 生命周期
        // ─────────────────────────────────────────────────────────────

        /// <summary>创建 ModChannelHub GameObject 并注册 vanilla SteamChannel(id=200) 到 Provider.receivers。</summary>
        void Initialize();

        /// <summary>销毁 ModChannelHub GameObject，从 Provider.receivers 注销 SteamChannel。</summary>
        void Shutdown();

        // ─────────────────────────────────────────────────────────────
        // 处理器注册
        // ─────────────────────────────────────────────────────────────

        /// <summary>注册服务器端虚拟通道处理器。仅当 Provider.isServer=true 时被 [SteamCall] 路由调用。</summary>
        void RegisterServerHandler(int virtualChannel, Action<CSteamID, BinaryReader> handler);

        /// <summary>注册客户端虚拟通道处理器。在客机端被 [SteamCall] 路由调用，也在房主 loopback 时调用。</summary>
        void RegisterClientHandler(int virtualChannel, Action<BinaryReader> handler);

        // ─────────────────────────────────────────────────────────────
        // 主循环驱动（vanilla 模式下为 no-op，保留以兼容 v1.x 调用方）
        // ─────────────────────────────────────────────────────────────

        /// <summary>vanilla SteamChannel 自动路由，Poll 已无作用。保留以兼容 v1.x 调用方。</summary>
        void Poll();

        // ─────────────────────────────────────────────────────────────
        // 发送 API
        // ─────────────────────────────────────────────────────────────

        /// <summary>客机 -> 服务器。房主调用此 API 会被静默忽略（房主直接走本地路径）。</summary>
        void SendToServer(int virtualChannel, byte[] payload, bool reliable = true);

        /// <summary>服务器 -> 指定客户端。客机调用此 API 会被静默忽略。</summary>
        void SendToClient(CSteamID client, int virtualChannel, byte[] payload, bool reliable = true);

        /// <summary>服务器 -> 所有客户端。房主调用：vanilla send 自带 loopback 检测，会本地直投。</summary>
        void BroadcastToAllClients(int virtualChannel, byte[] payload, bool reliable = true);

        // ─────────────────────────────────────────────────────────────
        // 序列化辅助
        // ─────────────────────────────────────────────────────────────

        /// <summary>把消息类型 + 后续载荷打包成单字节数组。调用方负责追加业务字段。</summary>
        byte[] BuildMessage(EModMessage msg, Action<BinaryWriter> bodyWriter = null);
    }
}
