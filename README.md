# 杀戮尖塔2考研英语 KaoyanEnglishMod

Slay the Spire 2 的 Godot C# 独立 Mod。

下载请前往 Releases 页面。
下面是项目信息和安装卸载说明：
<img width="1670" height="812" alt="image" src="https://github.com/user-attachments/assets/2ae47e00-0cc6-4823-bbf4-8fb622771287" />


## Mod 内容

开局获得遗物：考研词典。
<img width="539" height="697" alt="image" src="https://github.com/user-attachments/assets/69008f8a-6276-40aa-94b0-863ce1b04df6" />

战斗开始后，玩家可以选择是否挑战考研英语单词。

不挑战：
- 本场战斗获得 1 点力量和 1 点敏捷。

挑战：
- 每个玩家回合开始后额外抽 1 张牌。
- 每回合回答一道考研英语词汇题。
- 答对后选择一个奖励：
  - 选择一张手牌，本场战斗获得 Replay 1。
  - 选择一张手牌，本回合免费打出。
- 答错后随机消耗 1 张手牌。
- 可以跳过题目，跳过没有奖励也没有惩罚。

## 当前开发进度

当前版本：v0.1.1 版

已实现：
- 五个当前角色开局获得考研词典。
- 考研词典可在图鉴中显示。
- 考研词库按 Rank 进行中频优先的动态权重抽题。
- 挑战 / 不挑战流程。
- 答题 UI。
- 答对奖励选择。
- Replay / FreeThisTurn 奖励。
- 答错随机消耗手牌。
- 修复了考研题与原版回合开始选牌流程冲突的问题。
- 修改和美化了UI
- 增加了错题本功能
- 大幅修改和优化了单词生成逻辑
  
已知限制：
- 目前只有简体中文文本。
- UI 和数值仍可能继续调整。
- 这是早期版本，目前只适用于游戏版本v0.103.2（2026年4月16日版本）
  可能与未来游戏更新或者beta测试版不兼容。

## 安装方法

**1. 下载 Release 页面中的 `KaoyanEnglishMod-v0.1.0.zip`。**
   
**2. 解压后应得到文件夹：**

```text
KaoyanEnglishMod/
  KaoyanEnglishMod.dll
  KaoyanEnglishMod.pck
  KaoyanEnglishMod.json
```

**将整个 KaoyanEnglishMod 文件夹复制到 Slay the Spire 2 的 mods 目录：**

（*注：如果没有这个文件夹，只需要自己在Slay the spire2的目录中添加 \mods即可* )

<img width="966" height="700" alt="image" src="https://github.com/user-attachments/assets/a840df85-80df-4177-bce5-85af4db00439" />
<center> 没装过mod的朋友只要新建图中这个文件夹，然后放到里面就可以了</center>

```
...\SteamLibrary\steamapps\common\Slay the Spire 2\mods\
```
**最终路径应类似：**
```
...\Slay the Spire 2\mods\KaoyanEnglishMod\KaoyanEnglishMod.dll
...\Slay the Spire 2\mods\KaoyanEnglishMod\KaoyanEnglishMod.pck
...\Slay the Spire 2\mods\KaoyanEnglishMod\KaoyanEnglishMod.json
```
## 卸载方法
删除下面路径的文件即可：

```
...\Slay the Spire 2\mods\KaoyanEnglishMod
```
## 开发说明
本项目是 Godot C# Mod。

**如何重新构建文件？**
1、修改完源文件后 dotnet build
2、修改 C# 后需要重新构建 DLL。
3、修改图片、JSON、.tres 或导出清单后，需要重新导出 PCK。
