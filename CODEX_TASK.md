# KaoyanEnglishMod 开发说明

这是一个 Slay the Spire 2 的 Godot C# 独立 mod。

目标：
1. 添加一个开局必得遗物“考研词典”。
2. 战斗开始时询问玩家是否挑战考研英语单词。
3. 不挑战：本场战斗获得 1 力量和 1 敏捷。
4. 挑战：每回合多抽 1 张牌；每回合开始显示一个英文单词和 A/B/C/D 中文选项。
5. 答对：二选一奖励：
   - 选择一张手牌，本场战斗添加重放 1。
   - 选择一张手牌，本回合免费打出。
6. 答错：随机消耗一张手牌。

技术约束：
- 这是独立 mod，不修改游戏本体。
- 不要把 sts2.dll、GodotSharp.dll、0Harmony.dll 复制到发布包。
- 词库文件路径：res://KaoyanEnglishMod/data/kaoyan_words_mod.json
- C# 目标框架：net9.0。
- 使用 Harmony 做必要 patch。
- 每一步都要保证 dotnet build 通过。
- 先做最小可运行版本，不要一次性实现所有功能。