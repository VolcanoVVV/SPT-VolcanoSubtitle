# 发布步骤

确定发布版本后，同步插件声明、README、CHANGELOG 和 Git 标签。按对应版本记录构建与实机验证结果。

1. 在源码仓库运行 `dotnet run --project Tests/Regression.csproj`。
2. 按 [BUILDING.md](../BUILDING.md) 分别构建目标 Mono 版本和 5.0 BE。Mono 打包时明确传入 `DeployToGame=false`。
3. 检查 `ReleasePackages/<配置>/BepInEx/plugins/subtitle` 的 DLL 与该配置输出一致，且含语言、预设、过滤文件；确认包根目录带 README、更新日志和许可证。
4. 按 [Tests/README.md](../Tests/README.md) 完成 Unity/Fika 实机回归，记录实际测试的 SPT 与 Fika 版本。5.0 BE 基于干净安装验证加载和真实台词，记录验证用的 BE 和 EFT 构建号。
5. 每个 Release 配置单独压缩，使压缩包顶层直接包含 `BepInEx`。建议命名 `VolcanoSubtitle-<版本>-SPT-<SPT版本>.zip`，不要混放不同版本 DLL。
6. 发布说明列出变更、安装/备份要求、验证范围和已知问题。由维护者决定上传渠道并执行发布。

不要把开发者配置、游戏日志、导出音频、本机绝对路径或 Debug 包混入正式包。源仓库的 `ReleasePackages` 是忽略目录；生成包后应另行保管所选发布产物。

已知待实测项：预览迁移到战斗 UI、连续进出局、长时间高密度语音、实验室组合广播、Fika 主客机。角色台词翻译覆盖差异继续保留，不列为此次已解决问题。
