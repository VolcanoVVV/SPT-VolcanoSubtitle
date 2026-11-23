# SPT 5.0 BE（IL2CPP）开发版

本项目以仓库的 SPT 4.1 字幕实现为基线。当前编译使用 SPT 5.0.0 BE、EFT 客户端 `1.1.5.0-47242-bca02170`、Unity 2022.3.43f2 及 BepInEx 6.0.0-be.788 的本机程序集。

## 构建和打包

在仓库根目录执行：

```powershell
dotnet build IL2CPP/Subtitle.IL2CPP.csproj -c Release
dotnet build IL2CPP/Subtitle.IL2CPP.csproj -c Release -t:Package
```

默认读 `V:\EFToffline\EFToffline - 5.0debug`。换安装目录时传入 `"-p:SptGamePath=D:\Games\SPT 5.0"`。先让相应游戏安装生成 `BepInEx/interop`。本项目还引用安装中的 SPTushonka Reflection，以及 `BepInEx/plugins/Newtonsoft.Json.dll` 中的托管 Newtonsoft.Json 13.x。若托管 DLL 在其他位置，构建时传入 `"-p:ManagedJsonPath=完整路径"`，游戏运行时也要能加载该托管程序集。

打包输出在 `ReleasePackages/Release-5.0-IL2CPP/BepInEx/plugins/subtitle`，包含字幕 DLL、语言、预设和台词过滤文件。构建和打包只读取游戏目录。测试安装时，将打包输出里的 `BepInEx` 复制到目标 5.0 BE 安装，若目标已有自定义语言、预设和过滤文件，复制时保留这些文件。

## 游戏内验证

1. 查看 `BepInEx/LogOutput.log`，确认加载 `Volcano-Subtitle`，且字幕语音、广播和战斗界面的补丁安装无报错。
2. 在主菜单按 F9 打开设置和字幕预览。进入离线战局，听一条 AI 台词并检查字幕、弹幕和 3D 气泡。验证藏身处以及连续进出两次战局。
3. 分别测试实验室广播、字体和预设切换、F8 调试面板与声线试听。联机环境可用时，再验证 `PlayDirect` 远端语音和去重。

## 两条分支并行开发

`master` 对应 Mono 版，`codex/il2cpp` 对应 5.0 BE 开发版。字幕、弹幕、3D 气泡、设置和语言数据继续引用仓库现有源码。5.0 BE 的构建项目和专用补丁位于 `IL2CPP/`。共用功能应尽量单独提交，方便把 Mono 提交合并或 cherry-pick 到 5.0 BE 分支。
