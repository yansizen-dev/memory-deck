## MemoryDeck v1.0.0

**用 C# / WinForms 手写的本地记忆卡片管理器。**
以 Markdown 为唯一数据源，与工作区的 AI 长期记忆双向同步。

---

### 这个版本有什么

| | |
| --- | --- |
| **单文件 264 KB** | 框架依赖发布，双击即用，不需要安装程序 |
| **零第三方依赖** | 只用 .NET 10 自带的 WinForms，没有 NuGet 包 |
| **85 项自检** | 含界面冒烟测试与崩溃回归，`--selftest` 一条命令跑完 |
| **Markdown 即数据源** | 读写的就是你手写的那张表格，可手改、可进 Git |

### 核心能力

- **手写区域零侵入** —— 表格之前的说明、之后的维护约定，程序一律原样保留
- **AI 侧自动同步** —— 每次写入后重建 `AGENTS.md` 的摘要标记区，AI 下一轮就能读到
- **外部变更跟随** —— 每 1.5 秒检查文件指纹，你在编辑器保存后界面 2 秒内自动刷新
- **自动备份** —— 每次写入前备份，最多保留 30 份，超出自动清理
- **原子写入** —— 先写同目录临时文件再替换，崩溃不会留下半截文件
- **无损转义** —— 竖线、反斜杠、emoji、全角字符全部原样往返，不破表
- **幂等写入** —— 对同一份文件反复读写，结果字节级一致

### 快捷键

| 操作 | 快捷键 |
| --- | --- |
| 新增记忆 | `Ctrl+N` |
| 编辑选中条目 | `Ctrl+E` |
| 保存 | `Ctrl+S` |
| 搜索 | `Ctrl+F` |
| 删除 | `Delete` |
| 重新加载 | `F5` |

---

### 下载哪个

**`MemoryDeck-v1.0.0-win-x64.zip`**（130 KB）—— 解压后用 `MemoryDeck.exe`。

需要 **.NET 10 桌面运行时**。已经在开发机/装了 .NET SDK 的机器上直接跑；
不想装运行时的话，把 `MemoryDeck.csproj` 里的 `SelfContained` 改成 `true` 重新发布（产物约 150 MB）。

---

### 命令行

```powershell
MemoryDeck.exe                      # 启动图形界面
MemoryDeck.exe --data <目录>         # 指定数据目录
MemoryDeck.exe --open               # 打开数据目录
MemoryDeck.exe --selftest --verbose # 跑 85 项自检
MemoryDeck.exe --repair             # 规范化重写 + 校验数据无损
MemoryDeck.exe --env                # 打印显示器 / DPI / 窗口实测尺寸
MemoryDeck.exe --help               # 帮助
```

---

### 开发过程中修掉的三个崩溃

这三条都进了回归用例，因为它们是同一类问题：
**"数据层全绿"和"程序能跑"是两件事。**

1. **`InvariantGlobalization` 废掉中文文化** —— Release 配置里开了它，
   `new CultureInfo("zh-CN")` 直接抛异常，程序一启动就死。
   自检没发现是因为 `--selftest` 入口根本不初始化界面。

2. **`Panel2MinSize` 的 setter 会顺手重算 `SplitterDistance`** ——
   容器还没有尺寸时设一个较大的最小尺寸，内部 `ApplyPanel2MinSize` 会抛
   `InvalidOperationException`。构造期只能用安全值，精确位置推迟到 `OnLoad`。

3. **外部修改文件触发重载时直接杀进程** —— 重载时表格先被清空，
   `SelectionChanged` 回调在此时访问越界行；异常从 WinForms 事件回调逃逸，
   未捕获的 UI 事件异常会终止进程。
   修法：统一异常护栏 + 界检查 + 选中条目改按 ID 查找而非行索引。

第 3 条有 5 个边界用例守着：追加 / 清空 / 恢复 / 文件被删 / 文件被写坏。

---

### 已知限制

- **仅 Windows** —— 代码用了 WinForms，没有跨平台计划
- **需要 .NET 10 桌面运行时** —— 下载的是框架依赖版本
- **无并发保护** —— 同一份数据目录不建议开多个实例同时写
- **无加密** —— 记忆以明文 Markdown 存储，敏感信息请自行评估

---

### 环境踩坑记录

开发过程中踩到的坑写在
[`docs/env-troubleshooting.md`](https://github.com/yansizen-dev/memory-deck/blob/main/docs/env-troubleshooting.md)，
包含三条实测结论：

1. **NuGet** —— Visual Studio 残留的 `fallbackPackageFolders` 指向已删目录时，
   `.csproj` 属性无效，必须项目级 `nuget.config` 用 `<clear />`
2. **代理** —— `gh` 和 `git` 需求**相反**：`gh` 走系统代理最稳（6/6），
   额外设 `HTTPS_PROXY` 反而降到 1/4；`git` 则**必须**显式配 `http.proxy`（3/3 vs 0/3）
3. **DPI** —— 非 DPI 感知的进程读窗口尺寸会被系统虚拟化（1120×720 被读成 747×480）

方法论：**不要用 A 工具的现象去推断 B 工具的行为。**
先让目标程序自己带 debug 说话，再做受控对照实验。

---

**完整变更**：[`ad6de77...v1.0.0`](https://github.com/yansizen-dev/memory-deck/compare/ad6de77...v1.0.0)
