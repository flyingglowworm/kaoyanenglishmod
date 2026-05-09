# Build notes

项目路径：

E:\slay_spire_mods\kaoyanenglishmod

构建命令：

```powershell
dotnet restore
dotnet build

注释：
当前构建状态：

dotnet build 已成功。

输出 DLL：

.godot\mono\temp\bin\Debug\KaoyanEnglishMod.dll

依赖文件：

deps/0Harmony.dll
deps/GodotSharp.dll
deps/sts2.dll

要求：

每次修改 C# 代码后必须运行 dotnet build。
如果 build 失败，只做最小修改修复。
不要删除 deps、project.godot、KaoyanEnglishMod.csproj。
不要修改游戏本体文件。