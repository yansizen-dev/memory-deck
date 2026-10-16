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
| 收起 / 展开详情区 | `Ctrl+D` |
| 删除选中条目 | `Delete` |
| 重新加载文件 | `F5` |
| 查看完整信息 / 复制 / 编辑 / 删除 | **右键表格行** |

程序每 1.5 秒检查一次文件指纹：你或 AI 在外部改了 `MEMORY.md` / `AGENTS.md`，
界面会自动重新加载（正在编辑时不会打扰）。

## 界面约定

**表格只常显三列：`ID / 类别 / 内容`。**

「来源」与「记录时间」不再常显，改从右键菜单的「查看完整信息」按需查看——
这两项日常扫视用不到，各占一百多像素，是视觉密度和观感的主要来源。

> **「来源」字段的处理**：界面已完全下线该字段的显示与编辑，
> 但**文件里已有的这一列不会被删除**。程序会继续读它、写它，历史数据原样保留。
> 想彻底移除，直接在 `MEMORY.md` 里删掉这一列表头即可，程序会跟着收敛。
> 同理，你自己加的自定义列也会被原样保留。

**类别下拉是「建议」不是「限制」。**

预设类别（见 `src/Core/CategoryCatalog.cs`）：

| 类别 | 含义 |
| --- | --- |
| 身份 | 我是谁：背景、角色、当前状态 |
| 偏好 | 我喜欢怎样被对待、怎样协作 |
| 项目 | 在做的事、里程碑、决定 |
| 技术栈 | 在用的语言、工具、框架 |
| 目标 | 想达成什么、下一步计划 |
| 约束 | 不能做什么、硬性边界 |
| 技能 | 会什么、正在学什么 |
| 连接 | 人脉、协作方、联系方式线索 |
| 资料 | 外部材料的位置与要点 |
| 随手记 | 暂时不知道归哪类，先记下来 |

下拉永远包含「预设 + 数据里实际出现过的类别」，
即使一条记忆都没有也有可选项；预设之外可以直接手输新类别。

**表格为空时详情区自动收起**，把空间还给列表。
也可以随时用 `Ctrl+D` 或右键菜单手动折叠。

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

## 验收 / 对账

`acceptance.ps1` 用来在手动点界面之前、之后对账：

```powershell
cd F:\DSH\仓库\apps\MemoryDeck

.\acceptance.ps1              # 完整检查：安装状态、数据文件、自检、当前数据
.\acceptance.ps1 -Baseline    # 记录当前数据状态为基线
.\acceptance.ps1 -Compare     # 与基线比对，报告新增/删除了哪些条目、有无新备份
```

先在点界面**之前**跑 `-Baseline`，操作完再跑 `-Compare`，就能确认每次点击是否真的落盘。

> 脚本带 UTF-8 BOM，同 `build.ps1`，**别另存为无 BOM**。

## 手动验收清单

1. 双击桌面「记忆卡片管理器」→ 窗口应正常出现
2. `Ctrl+N` 新增 → 填内容 → `Ctrl+S` → 表格立刻多一行
3. 双击条目进入编辑 → 改内容 → `Ctrl+S`
4. 搜索框输入关键词 → 只显示匹配行
5. 类别下拉 → 按类别过滤
6. `Delete` 删除选中条目（会弹确认，删前自动备份）
7. **用记事本改 `MEMORY.md` 加一行 → 等 2 秒 → 界面应自动刷新**

第 7 步曾经会让程序直接崩掉（重载时表格清空、行索引越界）。
现已修复，并有 `Test12` 的 5 个子场景守着：追加、清空、恢复、文件被删、文件被写坏。

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
