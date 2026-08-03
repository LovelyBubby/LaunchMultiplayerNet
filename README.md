# LaunchMultiplayerNet 扩展版

未转变者（Unturned）的 BepInEx 插件 —— **多插件网络通道复用框架**（扩展版）。

本仓库基于原版 `LaunchMultiplayerNet` 扩展而来，在保留原通道的基础上**新增 `SleepDawn = 103` 网络通道**，
供作者的其他插件（如 [SleepToDawn](https://github.com/LovelyBubby/SleepToDawn)）使用。向后兼容，原插件（如 LaunchInventoryTidy / LaunchInPlaceReload）不受影响。

## 扩展点

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

## 编译

需要 [.NET SDK](https://dotnet.microsoft.com/download)（支持 net472）。

默认游戏在 `E:\SteamLibrary\steamapps\common\Unturned`，
路径不同时用 `-p:UnturnedDir=你的游戏目录` 覆盖：

```bash
dotnet build LaunchMultiplayerNet.csproj -c Release
```

产物在 `artifacts/LaunchMultiplayerNet.dll`。

## 安装

客户端与服务器都要安装：

```
Unturned/BepInEx/plugins/LaunchMultiplayerNet.dll
```

## License

[MIT](LICENSE)
