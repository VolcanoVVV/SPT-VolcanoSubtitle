# Volcano Subtitle / 火山家的实时字幕

为 SPT 游戏内角色语音显示字幕，提供普通字幕、弹幕和头顶 3D 气泡，以及实验室广播字幕。文本通过语音事件、声线和剪辑编号匹配本地资源；本项目不包含语音识别或在线机器翻译。

当前插件版本为 **2.0.2**。更新内容见 [更新记录](CHANGELOG.md)，安装包见 [GitHub Releases](https://github.com/VolcanoVVV/SPT-VolcanoSubtitle/releases/tag/2.0.2)。

## 版本与依赖

| 项目 | 说明 |
| --- | --- |
| SPT | Mono 构建支持 3.11、4.0、4.1。另有 [5.0 BE（IL2CPP）实验构建](https://github.com/VolcanoVVV/SPT-VolcanoSubtitle/blob/2.0.2/IL2CPP/README.md)。请选择与本机版本对应的包 |
| BepInEx、SPT 客户端依赖 | 使用对应 SPT 安装自带版本 |
| FontReplace | 可选，字幕可读取其字体资源包；不使用字体包时可用系统/游戏字体回退 |
| Fika | 已有远端语音播放适配；仍需在实际主机、客户端环境验证 |
| 界面语言 | `ch` 中文、`en` 英文、`ru` 俄文；角色台词完整度因语言而异 |

实际适用范围以发布说明里的实测版本为准。

## 安装、更新与卸载

1. 退出游戏，选择匹配 SPT 版本的 Release 包，例如 `Release-4.1`。
2. 将包中的 `BepInEx` 目录合并到游戏根目录。
3. 确认文件位于 `BepInEx/plugins/subtitle/Subtitle.dll`，同目录应有 `locales/`、`presets/` 和 `PhraseFilter.jsonc`。
4. 启动游戏。日志应出现 Volcano-Subtitle 的加载信息。

更新前备份 `BepInEx/config/Volcano.Subtitle.cfg`，以及自行修改的 `locales/`、`presets/`、`PhraseFilter.jsonc`。新包会覆盖同名资源文件；不要同时安装多个版本的 `Subtitle.dll`。

卸载时移除 `BepInEx/plugins/subtitle`。配置文件可以保留，重新安装后继续使用。用户另行安装的 FontReplace 由用户单独管理。

## 使用

- **F9**：打开/关闭图形设置窗口；热键可配置。
- 在设置中分别启用字幕、弹幕、3D 气泡，调整角色、距离、字体、颜色、背景、位置和显示数量。
- 预设需要点击“应用”；保存预设时同名文件会覆盖。
- 台词过滤面板按频道、声线、事件和 NetId 控制显示规则。
- 开启开发者工具并进入藏身处/战局后，可用 **F8** 打开语音浏览面板。也可通过设置里的按钮打开语音浏览、导出和诊断工具。
- 随机字幕与弹幕测试可在菜单使用；3D 测试需要藏身处或战局。

安装了兼容的 ConfigurationManager 时，也可通过其配置界面进入图形设置；该工具的默认热键通常为 F12，以实际配置为准。

## 语言与字体

界面语言设置会同时切换界面、角色名、台词、广播和主播词表，并重载缓存。缺失角色文件优先使用当前语言的 `Default_Voice.jsonc`，必要时最终回退中文通用台词。

界面语言可用不代表每个角色都已有逐句翻译。当前中文角色资源较多，英文不完整，俄文主要使用通用台词；本轮更新不补全台词翻译。

出现方框或缺字时，先检查所选字体是否包含对应语言字形，再检查字体包路径与回退字体。配置键、声线名、事件名、文件名及自定义预设名保留原始标识，避免破坏已有配置。翻译人员说明见 [docs/LOCALIZATION.md](docs/LOCALIZATION.md)。

## 排错与反馈

字幕不显示时先检查频道总开关、距离、台词过滤、角色优先级和显示数量。实验室广播还需要开启对应的广播开关。

在图形设置的测试/调试分类打开“实时事件与资源诊断”：

- “实时事件”列出最近 200 条事件、匹配资源及频道处理结果。
- “资源检查”检查当前语言文件；点击该按钮可重新扫描。
- “复制”复制当前报告，便于提交问题。
- 修改资源后，点击“重新加载本地化资源”，包括此前缺失或损坏的文件。

日志策略、诊断状态及反馈所需信息见 [docs/DIAGNOSTICS.md](docs/DIAGNOSTICS.md)。构建方式见 [BUILDING.md](BUILDING.md)，开发回归方式见 [Tests/README.md](Tests/README.md)。

## 发布状态与许可

打包流程保留原目录结构，按版本和构建配置分别输出；发布流程见 [docs/RELEASING.md](docs/RELEASING.md)。Unity 场景切换、长时间运行及 Fika 主客机行为仍需实机回归，不应只凭编译结果宣布兼容。

许可证见 [LICENSE.txt](LICENSE.txt)。
