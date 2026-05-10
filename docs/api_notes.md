# STS2 API Notes

本文件用于记录从 how_to_custom_relic/how_to_custom_relic.md 和反编译工程中整理出来的关键 API。

## Mod entry

入口文件：

src/KaoyanEnglishEntry.cs

当前入口：

```csharp
[ModInitializer(nameof(ModLoaded))]
public static void ModLoaded()
Custom relic tutorial source

教程文件：

how_to_custom_relic/how_to_custom_relic.md

请优先参考该教程中的：

RelicModel 示例
ModelDb 注册方式
ModHelper.AddModelToPool 用法
StartingRelics patch 用法
relics.json 本地化格式
遗物图标资源路径
Relic target

遗物 ID：

KaoyanLexicon

英文名：

Kaoyan Lexicon

中文名：

考研词典

遗物效果第一版描述：

战斗开始时选择是否挑战考研英语单词。不挑战时本场战斗获得 1 点力量和 1 点敏捷；挑战时每回合多抽 1 张牌并进行单词答题。

注意：

第二阶段只注册遗物，不实现效果。


保存后提交：

```powershell id="z9i8bo"
git add docs/api_notes.md
git commit -m "Add STS2 API notes template"