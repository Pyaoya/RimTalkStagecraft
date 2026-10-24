# RimTalk - Stagecraft

RimTalk 的附属模组（RimWorld 1.6、1.7）。功能和简介见 `游戏内文本.md` 第一、二节。MIT 协议，见 LICENSE。

创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3826324825

## 目录

```
About/About.xml          模组信息（description 由脚本生成，见下）
LoadFolders.xml          1.6 读 1.6/，1.7 读 1.7/
Languages/               语言文件。中文那份由脚本生成；英文直接改
Source/*.cs              源码，1.6 和 1.7 共用
Build/Stagecraft17.csproj  1.7 的编译工程（.NET 10）
Build/sync_text.py       把 游戏内文本.md 写进中文语言文件、About.xml、创意工坊简介
Build/About_en.txt       About 里英文那一段
Build/创意工坊简介.txt   上传时贴进创意工坊页面（脚本生成）
Build/Workshop_en.txt    创意工坊页面的英文版（手写，中文改了意思要跟着改）
Build/cover.py           画封面 About/Preview.png（RimTalk 系列的配色和原版像素小人，加一对幕布）
游戏内文本.md            所有中文文字的源头，以它为准
build.ps1                同步文字 → 编 1.6 → 编 1.7 → 装进两个游戏的 Mods 文件夹
```

## 构建

```
powershell -File build.ps1            # 编译并安装
powershell -File build.ps1 -NoInstall # 只编译
```

- 1.6：用 csc 对着游戏自己的 Mono 程序集编（.NET Framework）。
- 1.7：游戏跑在 .NET 10 上，用 `dotnet build` 对着 1.7 的 `Assembly-CSharp.dll`、`UnityStubs.dll` 和 Harmony 的 1.7 版编。
- 路径在 build.ps1 开头的参数里。
- 中文文字只改 `游戏内文本.md`。脚本会检查占位符（`{0}` 这些）和英文对不对得上，对不上就停下，不编译。

## 自测

启动参数加 `-quicktest -stagecraftselftest`：进测试小地图以后，把找到的 RimTalk 部件、所在地那一行、对殖民者／囚犯／有名字的狗的行动描述、寒流的补聊次数、话头能不能进 RimTalk 的队列写进日志，每行以 `[RimTalk Stagecraft] self-test` 开头（`Source/SelfTest.cs`）。

## 说明

- RimTalk 的东西全是按名字反射找的（只有“所在地”那一行用的是它公开的接口）。RimTalk 改了名字，用到它的那一项就关掉，日志里有一行，其余照常。
- 不往存档里写东西。设置存在游戏的 Config 里。

## 致谢

- RimTalk 的作者 Juicy。封面里的像素小人取自 RimTalk 的封面，照 RimTalk 系列附属 mod 的惯例沿用。
