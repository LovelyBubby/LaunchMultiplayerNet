# LaunchMultiplayerNet 扩展版

> ### 🔗 原作者（原版）
> 本仓库是基于 **@YU80Rice** 的 [LaunchMultiplayerNet](https://github.com/YU80Rice/LaunchMultiplayerNet)
> **原版**扩展而来的分支，所有核心功能归原作者所有。
> 在此向原作者致敬。请同时访问并支持原版项目。

## 与原版的区别

| 项目 | 原版 | 本扩展版 |
|---|---|---|
| 网络通道 | 原版既有通道 | **新增 `SleepDawn = 103`** |
| 消息类型 | 原版既有消息 | **新增 `RequestSleep = 30`、`SleepResult = 31`** |
| 兼容性 | — | **向后兼容**：原插件（LaunchInventoryTidy / LaunchInPlaceReload 等）不受影响 |

> 扩展目的：为作者的 [SleepToDawn](https://github.com/LovelyBubby/SleepToDawn)（睡觉到日出）插件提供
> `SleepDawn = 103` 网络通道，实现客户端 → 服务器的睡觉请求收发。

## 扩展点详情

- `ModChannels.cs`：新增 `SleepDawn = 103`
- `EModMessage`：新增 `RequestSleep = 30`、`SleepResult = 31`

## 目录结构

```
├── LaunchMultiplayerNet.csproj
├── LaunchMultiplayerNetPlugin.cs
├── ModTransport.cs
├── ModChannels.cs
├── ModP2PTransport.cs
├── ModRouter.cs
├── NetReflectionHelper.cs
├── IModTransport.cs
├── Patches/           # 收发消息补丁
├── Properties/
├── artifacts/         # 编译产物（git 忽略）
└── README.md
```

## 依赖

- 未转变者（Unturned）Steam 版
- [BepInEx](https://github.com/BepInEx/BepInEx) 5.x
- [Harmony](https://github.com/pardeike/Harmony)(BepInEx 自带)

## 安装

客户端与服务器都要安装：

```
Unturned/BepInEx/plugins/LaunchMultiplayerNet.dll
```

## License

本仓库同样遵循原作者项目的许可条款；文件版权归原作者 [YU80Rice](https://github.com/YU80Rice) 与扩展作者 [LovelyBubby](https://github.com/LovelyBubby) 所有。
