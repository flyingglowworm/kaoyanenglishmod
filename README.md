# KaoyanEnglishMod

KaoyanEnglishMod 是一个面向 **Slay the Spire 2** 的 Godot C# Mod。它添加了一个“考研词典”遗物，让玩家在战斗中通过考研英语词汇题获得奖励，同时承担答错惩罚。

## 功能

- 战斗开始时选择是否接受考研英语挑战。
- 接受挑战后，每个玩家回合会出现一道词义选择题。
- 答对后可以选择一张手牌获得 Replay 1，或使其本回合免费。
- 答错后随机消耗一张手牌。
- 支持按词频 rank 和动态难度权重抽题。
- 记录本局答题表现、错词和掌握情况，并在 Run 结束时展示错词回顾。
- 已修复与官方开局选牌流程、满手牌、禁抽和小提琴等场景的流程冲突。
- 当前联机同步方案会通过 `NetKaoyan*Action` 和官方 Action Queue 同步影响战斗状态的结果。

## 安装

从 GitHub Releases 下载玩家安装包，例如：

```text
KaoyanEnglishMod-v0.1.2.zip
```

解压后应得到：

```text
KaoyanEnglishMod/
  KaoyanEnglishMod.dll
  KaoyanEnglishMod.pck
  mod_manifest.json
```

将整个 `KaoyanEnglishMod` 文件夹复制到 Slay the Spire 2 的 `mods` 目录：

```text
...\SteamLibrary\steamapps\common\Slay the Spire 2\mods\KaoyanEnglishMod
```

## 从源码构建

本仓库只保存源码、Godot 工程文件、词库和 Mod 必要资源。构建依赖 DLL 不随仓库分发。

本地开发前，请自行从你的 Slay the Spire 2 安装目录或合法 SDK 来源准备依赖，并放入：

```text
deps/
  0Harmony.dll
  GodotSharp.dll
  sts2.dll
```

然后运行：

```powershell
dotnet build
```

构建输出通常位于：

```text
.godot/mono/temp/bin/Debug/KaoyanEnglishMod.dll
```

## 仓库内容

应该提交到源码仓库的内容包括：

- `src/`：C# 源码。
- `KaoyanEnglishMod/data/`：词库数据。
- `KaoyanEnglishMod/images/`：Mod 运行必要图片资源。
- `KaoyanEnglishMod/localization/`：本地化文本。
- `*.csproj`、`*.sln`、`project.godot`、`mod_manifest.json` 等工程文件。
- `docs/`：开发记录和参考文档。

不应该提交的内容包括：

- `.godot/`、`bin/`、`obj/`、`build/`。
- 本地测试日志。
- `photos_for_UI/` 等设计参考素材。
- 从游戏本体复制出来的依赖 DLL。

## 版本发布

源码通过 Git 分支保存。玩家下载包请通过 GitHub Releases 发布，建议命名为：

```text
KaoyanEnglishMod-v0.1.2.zip
```

Release 包应包含运行 Mod 必需的 DLL、PCK、manifest、词库和图片资源，但不应包含源码仓库里的临时素材或本地日志。

## 许可证

本项目使用 MIT License。详见 [LICENSE](LICENSE)。
