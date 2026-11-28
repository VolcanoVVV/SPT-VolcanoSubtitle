# SPT 5.0 BE（IL2CPP）开发版

本工程使用主线的共享字幕源码。当前编译使用 SPT 5.0.0 BE、EFT 客户端 `1.1.5.0-47242-bca02170`、Unity 2022.3.43f2 及 BepInEx 6.0.0-be.788 的本机程序集。

## 构建和打包

在仓库根目录执行：

```powershell
dotnet build IL2CPP/Subtitle.IL2CPP.csproj -c Release
dotnet build IL2CPP/Subtitle.IL2CPP.csproj -c Release -t:Package
```

用实际安装目录覆盖工程中的本地默认路径。构建时可传入 `"-p:SptGamePath=D:\Games\SPT 5.0"`。先让相应游戏安装生成 `BepInEx/interop`。本工程引用安装中的 SPTushonka Reflection 和托管 Newtonsoft.Json 13.x。默认从 `BepInEx/plugins/Newtonsoft.Json.dll` 读取托管库，也可以传入 `"-p:ManagedJsonPath=完整路径"`。测试安装所用的 BepInEx 也须能加载这份托管库。互操作程序集里的同名 DLL 提供的是 IL2CPP 游戏类型。

打包输出在 `ReleasePackages/Release-5.0-IL2CPP/BepInEx/plugins/subtitle`，包含字幕 DLL、语言、预设和台词过滤文件。构建和打包只读取游戏目录。测试安装时，将打包输出里的 `BepInEx` 复制到目标 5.0 BE 安装，若目标已有自定义语言、预设和过滤文件，复制时保留这些文件。

## 实验包状态

2.0.2 的 5.0 BE 包已通过构建、打包和托管静态检查。干净安装的插件加载、真实台词显示和 Fika 联机仍待实机验证。

## 游戏内验证

1. 查看 `BepInEx/LogOutput.log`，确认加载 `Volcano-Subtitle`，且字幕语音、广播和战斗界面的补丁安装无报错。
2. 在主菜单按 F9 打开设置和字幕预览。进入离线战局，听一条 AI 台词并检查字幕、弹幕和 3D 气泡。验证藏身处以及连续进出两次战局。
3. 分别测试实验室广播、字体和预设切换、F8 调试面板与声线试听。联机环境可用时，再验证 `PlayDirect` 远端语音和去重。

## 共享源码与版本专用代码

字幕、弹幕、3D 气泡、设置和语言数据共用主线源码。5.0 BE 的构建配置、插件初始化和战斗界面补丁位于 `IL2CPP/`。共用功能在主线修改一次，提交前同时构建对应的 Mono 和 IL2CPP 工程。