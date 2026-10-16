# MemoryDeck 安装与维护说明

## 这是什么

`MemoryDeck.exe` 是一个我（AI 助手）用 C# / WinForms 写的 **Windows 桌面程序**：
记忆卡片管理器。它把工作区的长期记忆（`MEMORY.md` 里的记忆表）做成了图形界面，
增删改查后直接写回 Markdown 文件——**AI 和你共用同一份记忆数据**。

## 文件位置

| 路径 | 说明 |
| --- | --- |
| `apps\MemoryDeck\publish\MemoryDeck.exe` | 程序本体（单文件，246 KB） |
| `apps\MemoryDeck\publish\memorydeck.config.json` | 数据目录配置 |
| `apps\MemoryDeck\src\` | 全部 C# 源码 |
| `apps\MemoryDeck\build.ps1` | 一键重新编译 / 自检 / 发布 |
| `F:\DSH\仓库\MEMORY.md` | **数据源**：全部记忆条目 |
| `F:\DSH\仓库\AGENTS.md` | AI 每轮自动加载的摘要（程序自动维护标记区） |
| `F:\DSH\仓库\.memory-deck\backups\` | 每次写入前的自动备份，最多保留 30 份 |

## 数据目录是怎么定的

程序按这个优先级推断数据目录：

1. 命令行 `--data <目录>`
2. 环境变量 `MEMORYDECK_DATA`
3. exe 同级的 `memorydeck.config.json`
4. 从 exe 所在路径向上回溯，找带 `MEMORY.md` / `AGENTS.md` / `apps` 的仓库根
5. 当前工作目录

本机安装时设了 `MEMORYDECK_DATA = F:\DSH\仓库`，所以程序直接读写仓库根目录的两个 Markdown 文件。
换目录：程序里「数据 → 数据目录」，或改环境变量，或改配置文件。

## 常用操作

| 操作 | 快捷键 |
| --- | --- |
| 新增记忆 | `Ctrl+N` |
| 编辑选中条目 | `Ctrl+E` 或双击行 |
| 保存 | `Ctrl+S` 或内容框里 `Ctrl+Enter` |
| 搜索框 | `Ctrl+F` |
| 删除选中条目 | `Delete` |
| 重新加载文件 | `F5` |

程序每 1.5 秒检查一次文件指纹：你或 AI 在外部改了 `MEMORY.md` / `AGENTS.md`，
界面会自动重新加载（正在编辑时不会打扰）。

## 命令行

```powershell
MemoryDeck.exe                      # 启动图形界面
MemoryDeck.exe --data <目录>         # 指定数据目录启动
MemoryDeck.exe --open               # 用资源管理器打开数据目录
MemoryDeck.exe --selftest --verbose # 数据层 + 界面自检，日志在 %TEMP%\MemoryDeck\
MemoryDeck.exe --repair             # 规范化重写 MEMORY.md 并重建 AGENTS.md 摘要（写入前自动备份）
MemoryDeck.exe --env                # 打印屏幕 / DPI / 窗口尺寸诊断信息
MemoryDeck.exe --help               # 帮助
```

### `--repair` 干什么

读一遍 `MEMORY.md` → 备份 → 写一条哨兵条目再删掉（真实跑完「新增→落盘→回读→删除→落盘」）
→ 对比前后条目数。表格会被重排成标准格式，**表格之前和之后的手写内容原样保留**。
外部手改乱过格式、或者想确认「程序读写不会吞数据」时用它。

### `--env` 干什么

打印显示器数量与分辨率、工作区、DPI 感知状态、窗口实测尺寸、各面板边界。
窗口显示异常（被裁切 / 布局错位 / 缩放不对）时，先跑这个拿事实。

## 重新编译

```powershell
cd F:\DSH\仓库\apps\MemoryDeck
.\build.ps1              # 编译 + 自检 + 发布
.\build.ps1 -TestOnly    # 只跑自检
.\build.ps1 -Install     # 发布后重建桌面快捷方式、设置环境变量
```

> `build.ps1` 带 UTF-8 BOM。**不要另存为无 BOM**，否则 Windows PowerShell 5.1
> 会按 ANSI 解析中文注释，直接报一堆语法错误。

## 已知的本机环境坑

这台机器的 NuGet 全局配置里有一条 Visual Studio 留下的 `fallbackPackageFolders`，
指向已被删除的 `F:\C++\工具\NuGetPackages`，会让 `dotnet build` 直接抛 `PackagingException`。
项目里的 `nuget.config` 用 `<clear />` 覆盖掉了它，**没有改动用户全局配置**。

## 技术栈

- .NET 10（`Microsoft.WindowsDesktop.App 10.0.8` 运行时，本机已装）
- WinForms，无任何第三方 NuGet 依赖
- 发布模式：单文件、框架依赖、`win-x64`
