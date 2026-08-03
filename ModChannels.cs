namespace LaunchMultiplayerNet
{
    /// <summary>
    /// 模组虚拟通道分配。
    ///
    /// v3.1 架构（2026-07-15 重构）：
    /// - 所有模组业务走 SteamNetworking P2P（单 channel=0）
    /// - 100/101/102 是"虚拟通道"标识符，编码在 P2P payload 头部第一字节
    /// - 由 ModTransport.HandleIncomingPacket 根据 virtual_channel 字节派发到对应 handler
    ///
    /// v2.0/v3.0 历史：
    /// - 曾走 vanilla SteamChannel(id=200) + [SteamCall] RPC
    /// - v3.0 SendToServer 在 dedicated server 模式下包被 vanilla 静默丢弃，v3.1 弃用
    ///
    /// v1.x 历史：
    /// - 100/101/102 曾是 SteamNetworking.SendP2PPacket 的 nChannel 参数
    /// - v3.1 重新使用 SteamNetworking P2P，但 virtual_channel 字节移到 payload 内部
    /// </summary>
    public static class ModChannels
    {
        /// <summary>LaunchInventoryTidy: 客户端 -> 服务器 的"请帮我整理背包"请求通道。</summary>
        public const int TidyPage = 100;

        /// <summary>LaunchInPlaceReload (AmmoRepacker): 客户端 -> 服务器 的"请帮我压弹"请求通道。</summary>
        public const int RepackAmmo = 101;

        /// <summary>LaunchHordeTracker: 服务器 -> 所有客户端 的尸潮状态广播通道。</summary>
        public const int HordeStatus = 102;

        /// <summary>SleepToDawn: 客户端 -> 服务器 的"请帮我睡觉到日出"请求通道。</summary>
        public const int SleepDawn = 103;
    }

    /// <summary>
    /// 每个虚拟通道内可承载的子消息类型。1 字节标识，便于在单通道上扩展多种消息。
    /// </summary>
    public enum EModMessage : byte
    {
        // Channel 100: InventoryTidy
        RequestTidyPage = 1,

        // Channel 101: AmmoRepacker
        RequestRepackAmmo = 10,

        // Channel 102: HordeTracker
        HordeStatusUpdate = 20,
        HordeStatusClear = 21,

        // Channel 103: SleepToDawn
        RequestSleep = 30,
        SleepResult = 31,
    }
}
