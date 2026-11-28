# 构建、部署与打包

Mono 和 IL2CPP 由同一个 `master` 源码树维护。两个工程各自使用对应的游戏程序集，并引用同一套字幕、弹幕、3D 气泡和设置源码。

## Mono（SPT 3.11、4.0、4.1）

Mono 工程使用 .NET Framework 4.7.1 引用程序集，游戏路径在 Subtitle.csproj 中配置。

### 仅构建

```powershell
dotnet build Subtitle.csproj -c Release-4.1 -p:DeployToGame=false
```

可用版本配置为 `Debug-3.11`、`Debug-4.0`、`Debug-4.1` 及对应的 `Release-*`。普通 `Debug`、`Release` 使用 4.1 引用。

### 部署到本机游戏

```powershell
dotnet build Subtitle.csproj -c Debug-4.1
```

保留原有开发行为：默认部署到对应游戏的 `BepInEx/plugins/subtitle`，包含 DLL、语言、预设和台词过滤文件。需要保留玩家自定义数据时使用仅构建命令。

### 分版本打包

```powershell
dotnet msbuild Subtitle.csproj -t:Package -p:Configuration=Release-4.1 -p:DeployToGame=false
```

输出到 `ReleasePackages/Release-4.1/BepInEx/plugins/subtitle`。不同版本、Debug/Release 各用独立目录，不再复制到旧的共享打包目录。打包不生成压缩文件。

`build-all.bat` 会对六种字幕配置执行上述打包，然后沿用原有流程构建旁边的 FontReplace 项目。FontReplace 的部署和打包行为由它自己的工程配置控制。

## IL2CPP（SPT 5.0 BE）

```powershell
dotnet build IL2CPP/Subtitle.IL2CPP.csproj -c Release
dotnet build IL2CPP/Subtitle.IL2CPP.csproj -c Release -t:Package
```

工程使用 .NET 6 和对应 BE 安装的 BepInEx interop 程序集。可用 `SptGamePath` 指定游戏目录。打包输出在 `ReleasePackages/Release-5.0-IL2CPP`，详细的前置依赖和实机检查见 [IL2CPP/README.md](https://github.com/VolcanoVVV/SPT-VolcanoSubtitle/blob/2.0.2/IL2CPP/README.md)。

自动测试和实机检查步骤见 [Tests/README.md](Tests/README.md)。
