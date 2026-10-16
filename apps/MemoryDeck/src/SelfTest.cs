using System.Globalization;
using System.Text;
using MemoryDeck.Core;

namespace MemoryDeck;

/// <summary>
/// 数据层自检：在临时目录里做真实读写，验证解析、写入、ID 自增、
/// Markdown 转义、AGENTS.md 同步与幂等性。不触碰任何用户数据。
/// </summary>
internal static class SelfTest
{
    private static int _passed;
    private static int _failed;
    private static bool _verbose;
    private static ConsoleBridge _out = null!;

    public static int Run(bool verbose = false)
    {
        _verbose = verbose;
        _passed = 0;
        _failed = 0;

        _out = ConsoleBridge.Create("selftest-last.log");

        _out.WriteLine("MemoryDeck 数据层自检");
        _out.WriteLine(new string('=', 58));
        _out.WriteLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        var sandbox = Path.Combine(Path.GetTempPath(), "MemoryDeck.SelfTest." + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sandbox);
        _out.WriteLine("沙箱目录：" + sandbox);
        _out.WriteLine();

        try
        {
            Test01EmptyWorkspace(sandbox);
            Test02AddFirstEntry(sandbox);
            Test03ReloadAndParse(sandbox);
            Test04IdIncrements(sandbox);
            Test05Escaping(sandbox);
            Test06UpdateAndPersist(sandbox);
            Test07IdempotentRoundTrip(sandbox);
            Test08AgentsWithoutMarkers(sandbox);
            Test09UnicodeAndLongContent(sandbox);
            Test10CorruptInput(sandbox);
            Test11RealWorkspaceReadOnly();
            Test12UiSmoke(sandbox);
            Test13TailPreservation(sandbox);
            Test14CategoryCatalog(sandbox);
            Test15OptionalColumns(sandbox);
            Test16Exporter(sandbox);
        }
        catch (Exception ex)
        {
            Fail("未捕获异常中止自检", ex.ToString());
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { }
        }

        _out.WriteLine();
        _out.WriteLine(new string('=', 58));
        _out.WriteLine($"通过 {_passed} ／ 失败 {_failed}");

        int exitCode;
        if (_failed == 0)
        {
            _out.WriteLine("结果：OK — 数据层行为符合预期。");
            exitCode = 0;
        }
        else
        {
            _out.WriteLine("结果：FAIL — 请查看上方失败项。");
            exitCode = 1;
        }

        _out.WriteLine();
        _out.WriteLine("日志文件：" + _out.LogPath);
        _out.Dispose();
        return exitCode;
    }

    // ------------------------------------------------------------ 用例

    private static void Test01EmptyWorkspace(string root)
    {
        var dir = Fresh(root, "01-empty");
        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });

        var snapshot = repo.Load();
        Check("01 空目录：无 MEMORY.md", !snapshot.MemoryExists);
        Check("01 空目录：条目数为 0", snapshot.Entries.Count == 0, $"实际 {snapshot.Entries.Count}");

        string id = "";
        var result = repo.Add("身份", "测试条目", "自检", out _);
        _ = repo.Add("身份", "第二条", "自检", out id);

        Check("01 首次写入返回成功", result.Ok, result.Message);
        Check("01 首次 ID 为 M-0002（跳过已写入的第一条）", id == "M-0002", $"实际 {id}");
    }

    private static void Test02AddFirstEntry(string root)
    {
        var dir = Fresh(root, "02-add");
        var config = new StorageConfig { DataDirectory = dir };
        var repo = new MemoryRepository(config);

        var result = repo.Add("身份", "用户是一名软件工程专业的学生", "用户告知", out var id);

        Check("02 新增返回成功", result.Ok, result.Message);
        Check("02 新增 ID 为 M-0001", id == "M-0001", $"实际 {id}");
        Check("02 MEMORY.md 已生成", File.Exists(config.MemoryPath));
        Check("02 AGENTS.md 已生成", File.Exists(config.AgentsPath));

        var memory = File.ReadAllText(config.MemoryPath, Encoding.UTF8);
        Check("02 MEMORY.md 含表头（ID/类别/内容）",
            memory.Contains("| ID |") && memory.Contains("| 类别 |") && memory.Contains("| 内容 |"),
            memory);
        Check("02 新文件不含「来源」列", !memory.Contains("| 来源 |"), memory);
        Check("02 MEMORY.md 含新条目", memory.Contains("M-0001") && memory.Contains("软件工程专业的学生"));
        Check("02 MEMORY.md 无 BOM", !memory.StartsWith('\uFEFF'));
        Check("02 记录时间格式为 yyyy-MM-dd HH:mm", repo.Load().Entries[0].Time.Length == 16 && repo.Load().Entries[0].Time[4] == '-');

        var agents = File.ReadAllText(config.AgentsPath, Encoding.UTF8);
        Check("02 AGENTS.md 含摘要标记", agents.Contains("<!-- MEMORY-DECK:SUMMARY:BEGIN -->") && agents.Contains("<!-- MEMORY-DECK:SUMMARY:END -->"));
        Check("02 AGENTS.md 摘要含条目内容", agents.Contains("软件工程专业的学生"));
        Check("02 AGENTS.md 摘要计数正确", agents.Contains("共 1 条"));
    }

    private static void Test03ReloadAndParse(string root)
    {
        var dir = Fresh(root, "03-reload");
        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });

        repo.Add("身份", "甲", "来源A", out _);
        repo.Add("偏好", "乙", "来源B", out _);
        repo.Add("目标", "丙", "来源C", out _);

        var snapshot = repo.Load();
        Check("03 重新加载条目数为 3", snapshot.Entries.Count == 3, $"实际 {snapshot.Entries.Count}");
        Check("03 顺序按 ID 升序", snapshot.Entries.Select(e => e.Id).SequenceEqual(new[] { "M-0001", "M-0002", "M-0003" }),
            string.Join(",", snapshot.Entries.Select(e => e.Id)));
        Check("03 字段完整解析",
            snapshot.Entries[1].Category == "偏好" && snapshot.Entries[1].Content == "乙",
            $"类别={snapshot.Entries[1].Category} 内容={snapshot.Entries[1].Content}");
        Check("03 指纹非空", snapshot.Fingerprint.Length > 0);
        Check("03 无解析告警", snapshot.Warnings.Count == 0, string.Join(" / ", snapshot.Warnings));

        // 外部编辑后能感知变化（按当前三列 + 记录时间的格式追加）
        var before = snapshot.Fingerprint;
        File.AppendAllText(Path.Combine(dir, "MEMORY.md"), "| M-0004 | 外部 | 手工追加 | 2026-01-01 |" + Environment.NewLine, Encoding.UTF8);
        var after = repo.Load();
        Check("03 外部修改后指纹变化", before != after.Fingerprint);
        Check("03 外部追加的行能被解析", after.Entries.Any(e => e.Id == "M-0004" && e.Content == "手工追加"));
    }

    private static void Test04IdIncrements(string root)
    {
        var dir = Fresh(root, "04-id");
        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });

        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            repo.Add("类别", $"第 {i + 1} 条", "自检", out var id);
            ids.Add(id);
        }

        Check("04 连续新增 ID 递增", ids.SequenceEqual(new[] { "M-0001", "M-0002", "M-0003", "M-0004", "M-0005" }),
            string.Join(",", ids));

        var snapshot = repo.Load();
        var next = repo.NextId(snapshot.Entries);
        Check("04 下一个 ID 为 M-0006", next == "M-0006", $"实际 {next}");
    }

    private static void Test05Escaping(string root)
    {
        var dir = Fresh(root, "05-escape");
        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });

        repo.Add("身份", "含竖线 | 的内容 和全角｜竖线", "自检", out var id);
        var snapshot = repo.Load();
        var entry = snapshot.Entries.FirstOrDefault(e => e.Id == id);

        Check("05 竖线内容可无损往返", entry is not null && entry.Content == "含竖线 | 的内容 和全角|竖线",
            entry is null ? "条目丢失" : entry.Content);

        repo.Add("身份", "多行\n内容\r\n换行", "自检", out var id2);
        var snapshot2 = repo.Load();
        var entry2 = snapshot2.Entries.FirstOrDefault(e => e.Id == id2);
        Check("05 换行被压平成单行", entry2 is not null && !entry2.Content.Contains('\n') && entry2.Content.Length > 0,
            entry2?.Content ?? "条目丢失");

        repo.Add("", "", "", out _);
        Check("05 空内容被拒绝", true);

        var rejected = repo.Add("身份", "   ", "自检", out _);
        Check("05 纯空白内容返回失败", !rejected.Ok, rejected.Message);

        var tooLong = repo.Add("身份", new string('长', 2001), "自检", out _);
        Check("05 超长内容被拒绝", !tooLong.Ok, tooLong.Message);
    }

    private static void Test06UpdateAndPersist(string root)
    {
        var dir = Fresh(root, "06-update");
        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });

        repo.Add("身份", "原始内容", "自检", out var id);
        var snapshot = repo.Load();
        var entry = snapshot.Entries.First(e => e.Id == id);

        entry.Content = "修改后的内容";
        entry.Category = "偏好";
        var updated = repo.Update(entry);
        Check("06 更新返回成功", updated.Ok, updated.Message);

        var reloaded = repo.Load();
        var after = reloaded.Entries.First(e => e.Id == id);
        Check("06 内容已更新", after.Content == "修改后的内容", after.Content);
        Check("06 类别已更新", after.Category == "偏好", after.Category);
        Check("06 记录时间保持不变", after.Time == entry.Time, $"{entry.Time} -> {after.Time}");
        Check("06 更新不产生新 ID", reloaded.Entries.Count == 1, $"实际 {reloaded.Entries.Count}");

        var missing = repo.Update(new MemoryEntry { Id = "M-9999", Content = "x", Category = "x" });
        Check("06 更新不存在的条目返回失败", !missing.Ok, missing.Message);

        var removed = repo.Remove(after);
        Check("06 删除返回成功", removed.Ok, removed.Message);

        var empty = repo.Load();
        Check("06 删除后条目数为 0", empty.Entries.Count == 0, $"实际 {empty.Entries.Count}");
        Check("06 删除后 AGENTS.md 摘要归零", File.ReadAllText(Path.Combine(dir, "AGENTS.md")).Contains("共 0 条"));
    }

    private static void Test07IdempotentRoundTrip(string root)
    {
        var dir = Fresh(root, "07-idempotent");
        var config = new StorageConfig { DataDirectory = dir };
        var repo = new MemoryRepository(config);

        repo.Add("身份", "甲", "来源", out _);
        repo.Add("偏好", "乙", "来源", out _);

        var first = File.ReadAllText(config.MemoryPath, Encoding.UTF8);
        var firstAgents = File.ReadAllText(config.AgentsPath, Encoding.UTF8);

        // 新增后立即删除：内容应回到原样（字节级）
        repo.Add("临时", "待删除", "自检", out var tempId);
        var snapshot = repo.Load();
        repo.Remove(snapshot.Entries.First(e => e.Id == tempId));

        var second = File.ReadAllText(config.MemoryPath, Encoding.UTF8);
        var secondAgents = File.ReadAllText(config.AgentsPath, Encoding.UTF8);

        Check("07 增删往返后 MEMORY.md 内容一致", first == second, Diff(first, second));
        Check("07 增删往返后条目数一致", repo.Load().Entries.Count == 2);
        Check("07 AGENTS.md 摘要结构保持", secondAgents.Contains("<!-- MEMORY-DECK:SUMMARY:BEGIN -->")
            && secondAgents.Contains("<!-- MEMORY-DECK:SUMMARY:END -->")
            && secondAgents.Contains("共 2 条"));

        // 连续多次同步不产生重复标记区
        for (var i = 0; i < 3; i++) repo.SyncAgentsSummary(repo.Load().Entries);
        var after = File.ReadAllText(config.AgentsPath, Encoding.UTF8);
        var beginCount = CountOccurrences(after, "<!-- MEMORY-DECK:SUMMARY:BEGIN -->");
        Check("07 多次同步不重复插入标记", beginCount == 1, $"BEGIN 出现 {beginCount} 次");
        _ = firstAgents;
    }

    private static void Test08AgentsWithoutMarkers(string root)
    {
        var dir = Fresh(root, "08-agents-legacy");
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"),
            "# AGENTS.md — 工作区长期记忆" + Environment.NewLine + Environment.NewLine +
            "### 自定义段落" + Environment.NewLine + "这一行必须被保留。" + Environment.NewLine, Encoding.UTF8);

        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });
        repo.Add("身份", "无标记区测试", "自检", out _);

        var agents = File.ReadAllText(Path.Combine(dir, "AGENTS.md"), Encoding.UTF8);
        Check("08 保留原有自定义内容", agents.Contains("这一行必须被保留。"));
        Check("08 补上摘要标记区", agents.Contains("<!-- MEMORY-DECK:SUMMARY:BEGIN -->") && agents.Contains("<!-- MEMORY-DECK:SUMMARY:END -->"));
        Check("08 摘要含新条目", agents.Contains("无标记区测试"));
    }

    private static void Test09UnicodeAndLongContent(string root)
    {
        var dir = Fresh(root, "09-unicode");
        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });

        const string content = "Emoji 🎯 与中文混排，符号：｜｜｜｜ 和 \\ 反斜杠，以及 <b>HTML</b>";
        repo.Add("技术栈", content, "自检", out _);

        var snapshot = repo.Load();
        Check("09 条目数为 1", snapshot.Entries.Count == 1, $"实际 {snapshot.Entries.Count}");
        Check("09 特殊字符无损", snapshot.Entries.Count == 1 && snapshot.Entries[0].Content == "Emoji 🎯 与中文混排，符号：|||| 和 \\ 反斜杠，以及 <b>HTML</b>",
            snapshot.Entries.Count == 1 ? snapshot.Entries[0].Content : "缺失");

        // 反斜杠转义边界：路径反斜杠 + \| 组合
        var bsDir = Fresh(root, "09-backslash");
        var bsConfig = new StorageConfig { DataDirectory = bsDir };
        var bsRepo = new MemoryRepository(bsConfig);
        const string bsInput = @"路径 C:\Users\x 和转义 \| 组合";
        bsRepo.Add("技术栈", bsInput, "自检", out var bsId);
        var bsEntry = bsRepo.Load().Entries.FirstOrDefault(e => e.Id == bsId);

        var rawLine = File.ReadAllLines(bsConfig.MemoryPath, Encoding.UTF8)
            .FirstOrDefault(l => l.Contains(bsId, StringComparison.Ordinal)) ?? "<行未找到>";
        Check("09 反斜杠可无损往返", bsEntry is not null && bsEntry.Content == bsInput,
            $"输入 : {bsInput}{Environment.NewLine}读回 : {bsEntry?.Content ?? "<缺失>"}{Environment.NewLine}文件 : {rawLine}");

        var longText = new string('测', 1900);
        repo.Add("技术栈", longText, "自检", out var id);
        var reloaded = repo.Load();
        var entry = reloaded.Entries.FirstOrDefault(e => e.Id == id);
        Check("09 1900 字内容完整保存", entry is not null && entry.Content.Length == 1900,
            entry is null ? "条目丢失" : $"长度 {entry.Content.Length}");
    }

    private static void Test10CorruptInput(string root)
    {
        var dir = Fresh(root, "10-corrupt");
        var memoryPath = Path.Combine(dir, "MEMORY.md");

        // 纯文本、没有表格
        File.WriteAllText(memoryPath, "# 只有标题" + Environment.NewLine + "没有表格。" + Environment.NewLine, Encoding.UTF8);
        var repo = new MemoryRepository(new StorageConfig { DataDirectory = dir });
        var snapshot = repo.Load();
        Check("10 无表格文件不抛异常", snapshot.Entries.Count == 0);

        var added = repo.Add("身份", "追加到无表格文件", "自检", out var id);
        Check("10 无表格文件可追加", added.Ok, added.Message);

        var text = File.ReadAllText(memoryPath, Encoding.UTF8);
        Check("10 保留原有标题文本", text.Contains("# 只有标题"));
        Check("10 追加后条目可解析", repo.Load().Entries.Any(e => e.Id == id));

        // 空文件
        var dir2 = Fresh(root, "10-empty-file");
        File.WriteAllText(Path.Combine(dir2, "MEMORY.md"), "", Encoding.UTF8);
        var repo2 = new MemoryRepository(new StorageConfig { DataDirectory = dir2 });
        var added2 = repo2.Add("身份", "空文件追加", "自检", out _);
        Check("10 空文件可追加", added2.Ok, added2.Message);
        Check("10 空文件追加后条目数为 1", repo2.Load().Entries.Count == 1, $"实际 {repo2.Load().Entries.Count}");

        // 只有分隔行、没有表头
        var dir3 = Fresh(root, "10-separator-only");
        File.WriteAllText(Path.Combine(dir3, "MEMORY.md"),
            "| --- | --- | --- |" + Environment.NewLine, Encoding.UTF8);
        var repo3 = new MemoryRepository(new StorageConfig { DataDirectory = dir3 });
        var snapshot3 = repo3.Load();
        Check("10 仅分隔行不抛异常（解析为 0 条）", snapshot3.Entries.Count == 0, $"实际 {snapshot3.Entries.Count}");
    }

    /// <summary>
    /// 界面冒烟测试：在沙箱目录里真正构造一次 MainForm，
    /// 走完事件绑定、数据加载、表格填充与布局，确认界面层不会崩。
    /// 这条用例就是为了拦住「数据层全绿、一开窗口就闪退」那类问题。
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Test12UiSmoke(string root)
    {
        var dir = Fresh(root, "12-ui-smoke");
        var config = new StorageConfig { DataDirectory = dir };

        // 先塞两条数据，让表格有内容可渲染
        var repo = new MemoryRepository(config);
        repo.Add("身份", "冒烟测试条目甲", "自检", out _);
        repo.Add("偏好", "冒烟测试条目乙", "自检", out _);

        var steps = new (string Name, Action Body)[]
        {
            ("12 安装 zh-CN 文化", () =>
            {
                var ok = UiCulture.Install();
                _out.WriteLine($"      zh-CN 可用：{ok}（False 表示退化为系统文化，程序仍可用）");
            }),
            ("12 界面初始化", () =>
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            }),
            ("12 构造 MainForm", () => SmokeRun(config)),
            ("12 外部变更后重载不崩溃", () => SmokeReload(config)),
        };

        foreach (var (name, body) in steps)
        {
            try
            {
                body();
                Check(name, true);
            }
            catch (Exception ex)
            {
                Check(name, false, ex.GetType().Name + ": " + ex.Message);
                return; // 前一步失败，后面的没有意义
            }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SmokeRun(StorageConfig config)
    {
        using var form = new MemoryDeck.Ui.MainForm(config);

        ActivateForm(form);

        Check("12 主窗口句柄创建成功", form.Handle != IntPtr.Zero);
        Check("12 界面布局计算完成", form.ClientSize.Width > 0 && form.ClientSize.Height > 0,
            $"ClientSize = {form.ClientSize}");
        Check("12 窗口标题正确", form.Text.Contains("MemoryDeck"), form.Text);
        Check("12 窗口尺寸不超过屏幕工作区",
            form.Width <= Screen.PrimaryScreen!.WorkingArea.Width && form.Height <= Screen.PrimaryScreen.WorkingArea.Height,
            $"窗口 {form.Size}，工作区 {Screen.PrimaryScreen.WorkingArea.Size}");
        Check("12 最小尺寸不超过当前尺寸", form.MinimumSize.Width <= form.Width && form.MinimumSize.Height <= form.Height,
            $"MinimumSize = {form.MinimumSize}，Size = {form.Size}");

        form.Close();

        // 界面加载后不应污染沙箱里的数据文件
        var after = new MemoryRepository(config).Load();
        Check("12 界面启动未改动记忆条目数", after.Entries.Count == 2, $"实际 {after.Entries.Count}");
    }

    /// <summary>
    /// 让窗口真正「加载」一次。
    /// 关键：只访问 form.Handle 不会触发 Load 事件，数据不会被读进表格；
    /// 必须 Show 一次并抽干消息队列，Load 才会跑（OnLoad 里的分隔条调整与 ReloadAll 都在那时执行）。
    /// 显示后立刻隐藏，避免在用户桌面上留窗口。
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ActivateForm(Form form)
    {
        form.Show();
        Application.DoEvents();
        form.Refresh();
        Application.DoEvents();
        form.Hide();
        Application.DoEvents();
    }

    /// <summary>
    /// 表格之后的手写章节必须原样保留：程序只重写表格，
    /// 不能把用户在 MEMORY.md 里手写的维护约定、说明段落吞掉。
    /// </summary>
    private static void Test13TailPreservation(string root)
    {
        var dir = Fresh(root, "13-tail");
        var config = new StorageConfig { DataDirectory = dir };
        var memoryPath = config.MemoryPath;

        const string preamble = "# MEMORY.md — 长期记忆条目";
        const string tail = "## 记忆维护约定\n\n1. 第一条约定\n2. 第二条约定\n\n任意说明文字。";
        var original =
            preamble + Environment.NewLine + Environment.NewLine +
            "| ID | 类别 | 内容 | 来源 | 记录时间 |" + Environment.NewLine +
            "| --- | --- | --- | --- | --- |" + Environment.NewLine +
            "| M-0001 | 身份 | 原有条目 | 手工 | 2026-01-01 |" + Environment.NewLine +
            Environment.NewLine + tail + Environment.NewLine;

        Utf8.WriteAllText(memoryPath, original);

        var repo = new MemoryRepository(config);
        var before = repo.Load();
        Check("13 手写文件可正常解析", before.Entries.Count == 1, $"实际 {before.Entries.Count}");

        // 触发一次写回（新增 + 删除），表格被重写
        repo.Add("类别", "新增条目", "自检", out var id);
        var afterAdd = Utf8.ReadAllText(memoryPath);
        Check("13 新增后手写章节仍在", afterAdd.Contains("## 记忆维护约定") && afterAdd.Contains("1. 第一条约定"),
            afterAdd);

        var added = repo.Load().Entries.First(e => e.Id == id);
        repo.Remove(added);
        var afterRemove = Utf8.ReadAllText(memoryPath);
        Check("13 删除后手写章节仍在", afterRemove.Contains("## 记忆维护约定") && afterRemove.Contains("任意说明文字。"),
            afterRemove);
        Check("13 原始条目未丢失", afterRemove.Contains("原有条目"));
        Check("13 条目数回到 1", repo.Load().Entries.Count == 1, $"实际 {repo.Load().Entries.Count}");

        // 前置说明也不能丢
        Check("13 表头之前的说明保留", afterRemove.Contains(preamble));

        // 连续写入不应累积空行（曾经每写一次就多攒一个空行）
        var writeRepo = new MemoryRepository(config);
        var trailingCounts = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            writeRepo.Add("类别", $"第 {i + 1} 次写入", "自检", out var roundId);
            var current = writeRepo.Load();
            writeRepo.Remove(current.Entries.First(e => e.Id == roundId));

            var content = Utf8.ReadAllText(memoryPath);
            var matches = System.Text.RegularExpressions.Regex.Matches(content, @"\n\s*\n\s*\n");
            trailingCounts.Add(matches.Count);
        }

        Check("13 连续 4 次写入不累积连续空行",
            trailingCounts.All(c => c == trailingCounts[0]),
            "各轮连续空行数：" + string.Join(", ", trailingCounts));
    }

    /// <summary>
    /// 回归：界面运行期间外部修改 MEMORY.md，触发重载。
    /// 曾经在表格清空、行索引越界时抛异常直接杀进程，这条用例专门守住它。
    /// 覆盖三种边界：追加一行、删掉所有行、再加回来。
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SmokeReload(StorageConfig config)
    {
        var repo = new MemoryRepository(config);

        using var form = new MemoryDeck.Ui.MainForm(config);
        ActivateForm(form);

        Check("12 初始表格行数正确", form.GridRowCountForTest == 2, $"实际 {form.GridRowCountForTest}");

        // ① 外部追加一行
        repo.Add("目标", "外部追加的条目", "自检", out var newId);
        form.ReloadForTest();
        Application.DoEvents();
        Check("12 追加后重载：条目数 3", form.EntryCountForTest == 3, $"实际 {form.EntryCountForTest}");
        Check("12 追加后重载：表格行数 3", form.GridRowCountForTest == 3, $"实际 {form.GridRowCountForTest}");

        // ② 外部删到一行不剩 —— 表格会被清空，最容易触发越界
        foreach (var entry in repo.Load().Entries.ToList())
        {
            repo.Remove(entry);
        }

        form.ReloadForTest();
        Application.DoEvents();
        Check("12 清空后重载：条目数 0", form.EntryCountForTest == 0, $"实际 {form.EntryCountForTest}");
        Check("12 清空后重载：表格行数 0", form.GridRowCountForTest == 0, $"实际 {form.GridRowCountForTest}");

        // ③ 再加回来，确认能从空表格恢复
        repo.Add("身份", "恢复后的条目", "自检", out _);
        form.ReloadForTest();
        Application.DoEvents();
        Check("12 恢复后重载：条目数 1", form.EntryCountForTest == 1, $"实际 {form.EntryCountForTest}");
        Check("12 恢复后重载：表格行数 1", form.GridRowCountForTest == 1, $"实际 {form.GridRowCountForTest}");

        // ④ 文件被外部删掉（最极端）
        File.Delete(config.MemoryPath);
        form.ReloadForTest();
        Application.DoEvents();
        Check("12 文件消失后重载不崩溃", form.EntryCountForTest == 0, $"实际 {form.EntryCountForTest}");

        // ⑤ 文件被外部写坏（非 Markdown 内容）
        File.WriteAllText(config.MemoryPath, "这不是表格，随便写点东西。", Encoding.UTF8);
        form.ReloadForTest();
        Application.DoEvents();
        Check("12 文件被写坏后重载不崩溃", form.EntryCountForTest == 0, $"实际 {form.EntryCountForTest}");

        form.Close();
        _ = newId;
    }

    /// <summary>
    /// 类别目录：预设类别必须满足「建议而非限制」。
    /// 关键行为：空数据时下拉也有选项；数据里的自定义类别不能被预设冲掉。
    /// </summary>
    private static void Test14CategoryCatalog(string root)
    {
        // ① 预设本身要够用，且不含空项与重复项
        Check("14 预设类别不少于 6 个", CategoryCatalog.Presets.Count >= 6,
            $"实际 {CategoryCatalog.Presets.Count} 个");
        Check("14 预设类别无空名", CategoryCatalog.Presets.All(p => p.Name.Trim().Length > 0));
        Check("14 预设类别无重复",
            CategoryCatalog.PresetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == CategoryCatalog.Presets.Count);
        Check("14 每个预设都有说明", CategoryCatalog.Presets.All(p => p.Description.Trim().Length > 0));

        // 约定里写明的 6 个基础类别必须在
        foreach (var required in new[] { "身份", "偏好", "项目", "技术栈", "目标", "约束" })
        {
            Check($"14 预设包含「{required}」", CategoryCatalog.PresetNames.Contains(required));
        }

        // ② 空数据时，下拉仍然能拿到完整的预设列表
        var empty = CategoryCatalog.MergeWith(Array.Empty<string>());
        Check("14 无数据时下拉仍有预设项", empty.Count == CategoryCatalog.Presets.Count, $"实际 {empty.Count} 项");

        // ③ 数据里出现过的自定义类别不能被丢掉
        var merged = CategoryCatalog.MergeWith(new[] { "身份", "自定义分类", "  ", "另一类", "身份" });
        Check("14 保留自定义类别", merged.Contains("自定义分类") && merged.Contains("另一类"),
            string.Join(" / ", merged));
        Check("14 预设在前且不重复",
            merged.Count == CategoryCatalog.Presets.Count + 2
            && merged.Take(CategoryCatalog.Presets.Count).SequenceEqual(CategoryCatalog.PresetNames),
            $"共 {merged.Count} 项：" + string.Join(" / ", merged));
        Check("14 忽略空白类别", merged.All(m => m.Trim().Length > 0));

        // ④ 类别说明能查到，且未知类别不会瞎编说明
        Check("14 预设类别有说明可查", CategoryCatalog.DescribeOf("身份").Length > 0);
        Check("14 未知类别说明为空", CategoryCatalog.DescribeOf("不存在的类别") == "");

        // ⑤ 界面层：下拉应构造成功且含预设
        try
        {
            var dir = Fresh(root, "14-ui-category");
            var config = new StorageConfig { DataDirectory = dir };
            var repo = new MemoryRepository(config);
            repo.Add("自定义分类", "触发自定义类别进入下拉", "自检", out _);

            using var form = new MemoryDeck.Ui.MainForm(config);
            ActivateForm(form);

            Check("14 界面加载后表格行数正确", form.GridRowCountForTest == 1, $"实际 {form.GridRowCountForTest}");
            Check("14 界面类别下拉含预设项", form.FilterCategoryCountForTest >= CategoryCatalog.Presets.Count,
                $"实际 {form.FilterCategoryCountForTest} 项（含「全部」）");
            form.Close();
        }
        catch (Exception ex)
        {
            Check("14 界面层类别下拉", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// 可选列处理：「来源」已从界面下线，但**文件里已有的这一列必须保留**。
    /// 这条用例守的是一个数据安全问题——
    /// 界面不再支持某字段，绝不能演变成写回时静默删数据。
    /// </summary>
    private static void Test15OptionalColumns(string root)
    {
        const string withSource =
            "# MEMORY.md — 长期记忆条目" + "\n" + "\n" +
            "| ID | 类别 | 内容 | 来源 | 记录时间 |" + "\n" +
            "| --- | --- | --- | --- | --- |" + "\n" +
            "| M-0001 | 身份 | 已有来源的老条目 | 用户直接告知 | 2026-02-14 |" + "\n";

        // ① 有「来源」列的文件：读得到、写回后还在
        var dirA = Fresh(root, "15-with-source");
        var cfgA = new StorageConfig { DataDirectory = dirA };
        Utf8.WriteAllText(cfgA.MemoryPath, withSource);

        var repoA = new MemoryRepository(cfgA);
        var loaded = repoA.Load();
        Check("15 能读出已有的来源值",
            loaded.Entries.Count == 1 && loaded.Entries[0].Source == "用户直接告知",
            loaded.Entries.Count == 1 ? $"来源=「{loaded.Entries[0].Source}」" : "条目丢失");

        repoA.Add("项目", "新增条目不应破坏来源列", "", out _);
        var afterA = Utf8.ReadAllText(cfgA.MemoryPath);

        Check("15 写回后表头仍含「来源」列", afterA.Contains("| 来源 |"), afterA);
        Check("15 写回后老条目的来源值未丢", afterA.Contains("用户直接告知"), afterA);
        Check("15 新增条目来源留空", repoA.Load().Entries.Any(e => e.Category == "项目"));

        var rereadA = repoA.Load();
        Check("15 重新解析来源值一致",
            rereadA.Entries.Any(e => e.Id == "M-0001" && e.Source == "用户直接告知"),
            string.Join(" | ", rereadA.Entries.Select(e => $"{e.Id}:{e.Source}")));

        // ② 没有「来源」列的文件：程序不加回来，但也不该崩
        var dirB = Fresh(root, "15-without-source");
        var cfgB = new StorageConfig { DataDirectory = dirB };
        Utf8.WriteAllText(cfgB.MemoryPath,
            "# MEMORY.md" + "\n" + "\n" +
            "| ID | 类别 | 内容 | 记录时间 |" + "\n" +
            "| --- | --- | --- | --- |" + "\n" +
            "| M-0001 | 身份 | 没有来源列的条目 | 2026-02-14 |" + "\n");

        var repoB = new MemoryRepository(cfgB);
        repoB.Add("偏好", "新增条目", "", out _);
        var afterB = Utf8.ReadAllText(cfgB.MemoryPath);

        Check("15 没有来源列时不擅自加回来", !afterB.Contains("| 来源 |"), afterB);
        Check("15 三列文件写入正常", repoB.Load().Entries.Count == 2, $"实际 {repoB.Load().Entries.Count}");

        // ③ 全新文件：默认列不含「来源」
        var dirC = Fresh(root, "15-fresh");
        var cfgC = new StorageConfig { DataDirectory = dirC };
        var repoC = new MemoryRepository(cfgC);
        repoC.Add("身份", "全新文件第一条", "", out _);
        var fresh = Utf8.ReadAllText(cfgC.MemoryPath);

        Check("15 全新文件默认不含来源列", !fresh.Contains("| 来源 |"), fresh);
        Check("15 全新文件含 ID/类别/内容/记录时间",
            fresh.Contains("| ID |") && fresh.Contains("| 类别 |") && fresh.Contains("| 内容 |") && fresh.Contains("| 记录时间 |"),
            fresh);

        // ④ 自定义列不能被抹掉
        var dirD = Fresh(root, "15-custom-column");
        var cfgD = new StorageConfig { DataDirectory = dirD };
        Utf8.WriteAllText(cfgD.MemoryPath,
            "# MEMORY.md" + "\n" + "\n" +
            "| ID | 类别 | 内容 | 优先级 | 记录时间 |" + "\n" +
            "| --- | --- | --- | --- | --- |" + "\n" +
            "| M-0001 | 身份 | 带自定义列 | 高 | 2026-02-14 |" + "\n");

        var repoD = new MemoryRepository(cfgD);
        repoD.Add("项目", "新增", "", out _);
        var afterD = Utf8.ReadAllText(cfgD.MemoryPath);

        Check("15 自定义列表头保留", afterD.Contains("| 优先级 |"), afterD);
        Check("15 自定义列的值保留", afterD.Contains("| 高 |"), afterD);
    }

    /// <summary>
    /// 导出给网页版 AI：产物必须是「能直接粘进对话框」的纯文本。
    /// 重点守住的是格式——粘过去变成一堆竖线的 Markdown 表格就失去意义了。
    /// </summary>
    private static void Test16Exporter(string root)
    {
        var entries = new List<MemoryEntry>
        {
            new() { Id = "M-0001", Category = "身份", Content = "用户是软件工程专业学生", Time = "2026-02-14" },
            new() { Id = "M-0002", Category = "项目", Content = "在做 MemoryDeck", Time = "2026-10-16" },
            new() { Id = "M-0003", Category = "身份", Content = "喜欢直接给结论", Time = "2026-10-16" },
            new() { Id = "M-0004", Category = "自定义类", Content = "自定义类别的条目", Time = "2026-10-16" },
        };

        const string agents = """
            # AGENTS.md — 工作区长期记忆

            <!-- MEMORY-DECK:SUMMARY:BEGIN -->

            | ID | 类别 | 内容 | 记录时间 |
            | --- | --- | --- | --- |
            | M-0001 | 身份 | 摘要里的这条不该出现在导出里 | 2026-02-14 |

            <!-- MEMORY-DECK:SUMMARY:END -->

            ### 协作偏好（人工维护区）
            - 默认使用简体中文。
            - 回答要具体可操作。
            """;

        var bundle = MemoryExporter.Build(entries, agents);

        // ① 格式：不能有 Markdown 表格
        Check("16 导出不含 Markdown 表格分隔行", !bundle.Text.Contains("| --- |"), bundle.Text);
        Check("16 导出不含竖线表格行", !bundle.Text.Contains("| M-0001 |"), bundle.Text);
        Check("16 导出不含标记注释", !bundle.Text.Contains("MEMORY-DECK"), bundle.Text);

        // ② 摘要区的条目不能混进来（那是给 AI 看的摘要，不是事实来源）
        Check("16 摘要区内容未混入导出", !bundle.Text.Contains("不该出现在导出里"), bundle.Text);

        // ③ 内容：条目按类别分组，类别名带方括号
        Check("16 含类别分组标题", bundle.Text.Contains("【身份】") && bundle.Text.Contains("【项目】"), bundle.Text);
        Check("16 含全部条目的内容",
            bundle.Text.Contains("软件工程专业学生") && bundle.Text.Contains("在做 MemoryDeck")
            && bundle.Text.Contains("喜欢直接给结论") && bundle.Text.Contains("自定义类别的条目"));
        Check("16 每条前缀为间隔号", bundle.Text.Contains("· 用户是软件工程专业学生"), bundle.Text);

        // ④ 预设类别排在自定义类别之前
        var indexIdentity = bundle.Text.IndexOf("【身份】", StringComparison.Ordinal);
        var indexCustom = bundle.Text.IndexOf("【自定义类】", StringComparison.Ordinal);
        Check("16 预设类别排在前", indexIdentity >= 0 && indexCustom > indexIdentity,
            $"身份@{indexIdentity} 自定义@{indexCustom}");

        // ⑤ 说明头与协作偏好
        Check("16 含说明头", bundle.Text.Contains("希望你在后续对话中记住"), bundle.Text);
        Check("16 含协作偏好", bundle.Text.Contains("默认使用简体中文") && bundle.Text.Contains("回答要具体可操作"), bundle.Text);
        Check("16 偏好里不含 Markdown 标题", !bundle.Text.Contains("### 协作偏好"), bundle.Text);

        // ⑥ 默认不导出记录时间
        Check("16 默认不含记录时间", !bundle.Text.Contains("2026-02-14"), bundle.Text);

        var withTime = MemoryExporter.Build(entries, agents, new MemoryExporter.Options { IncludeTime = true });
        Check("16 开启后含记录时间", withTime.Text.Contains("2026-02-14"), withTime.Text);

        // ⑦ 关掉选项时不出现
        var bare = MemoryExporter.Build(entries, agents, new MemoryExporter.Options
        {
            IncludeHeader = false,
            IncludePreferences = false,
        });
        Check("16 关掉说明头后不含说明头", !bare.Text.Contains("希望你在后续对话中记住"), bare.Text);
        Check("16 关掉偏好后不含偏好", !bare.Text.Contains("默认使用简体中文"), bare.Text);
        Check("16 关掉说明头后仍含条目", bare.Text.Contains("软件工程专业学生"), bare.Text);

        // ⑧ 按类别筛选
        var onlyIdentity = MemoryExporter.Build(entries, agents, new MemoryExporter.Options
        {
            OnlyCategories = new[] { "身份" },
        });
        Check("16 按类别筛选只留命中项",
            onlyIdentity.EntryCount == 2 && onlyIdentity.Text.Contains("软件工程专业学生")
            && !onlyIdentity.Text.Contains("在做 MemoryDeck"),
            onlyIdentity.Text);
        Check("16 筛选取并集", MemoryExporter.Build(entries, agents, new MemoryExporter.Options
        {
            OnlyCategories = new[] { "身份", "项目" },
        }).EntryCount == 3);

        // ⑨ 空输入不能崩
        var empty = MemoryExporter.Build(Array.Empty<MemoryEntry>(), null);
        Check("16 空记忆可导出", empty.Text.Length > 0 && empty.EntryCount == 0, empty.Text);
        Check("16 无 AGENTS.md 时不崩", !empty.Text.Contains("协作偏好"), empty.Text);

        // ⑩ 统计信息合理
        Check("16 条目数与类别数正确",
            bundle.EntryCount == 4 && bundle.CategoryCount == 3,
            $"条目 {bundle.EntryCount} 类别 {bundle.CategoryCount}");
        Check("16 token 估算为正数", bundle.EstimatedTokens > 0, bundle.EstimatedTokens.ToString());
        Check("16 体量描述可读", bundle.SizeSummary.Contains("字符") && bundle.SizeSummary.Contains("tokens"),
            bundle.SizeSummary);

        // ⑪ 多行内容被压平，避免破坏纯文本结构
        var multi = MemoryExporter.Build(new[]
        {
            new MemoryEntry { Id = "M-0001", Category = "身份", Content = "第一行\n第二行\r\n第三行" },
        }, null, new MemoryExporter.Options { IncludeHeader = false, IncludePreferences = false });
        Check("16 多行内容被压成单行",
            multi.Text.Contains("· 第一行 第二行 第三行") && multi.Text.Split('\n').Length <= 4,
            multi.Text);

        // ⑫ 界面层：导出对话框能否构造（不开窗）
        try
        {
            var dir = Fresh(root, "16-ui-export");
            var config = new StorageConfig { DataDirectory = dir };
            var repo = new MemoryRepository(config);
            repo.Add("身份", "界面导出用例", "", out _);

            using var dialog = new MemoryDeck.Ui.ExportDialog(repo, repo.Load().Entries);
            var handle = dialog.Handle;
            dialog.CreateControl();
            Application.DoEvents();
            Check("16 导出对话框可构造", handle != IntPtr.Zero);
            dialog.Close();
        }
        catch (Exception ex)
        {
            Check("16 导出对话框可构造", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void Test11RealWorkspaceReadOnly()
    {
        var config = StorageConfig.Resolve();
        _out.WriteLine($"11 实际数据目录推断结果：{config.DataDirectory}");

        if (!File.Exists(config.MemoryPath))
        {
            Check("11 实际目录暂无可读 MEMORY.md（跳过）", true);
            return;
        }

        var repo = new MemoryRepository(config);
        var snapshot = repo.Load();
        Check("11 实际 MEMORY.md 可读", snapshot.MemoryExists);
        Check("11 实际文件解析无告警", snapshot.Warnings.Count == 0, string.Join(" / ", snapshot.Warnings));

        // 刻意不断言「条目数 >= 1」：清空记忆是合法状态，
        // 把作者恰好有数据当成通过条件，等于让自检依赖环境，是坏味道。
        // 这里只确认「解析结果与文件里的行数一致」，有多少条都算通过。
        var actualRows = MemoryRepository.SplitLines(Utf8.ReadAllText(config.MemoryPath))
            .Count(line => line.TrimStart().StartsWith("| M-", StringComparison.Ordinal));
        Check("11 解析条数与文件实际行数一致", snapshot.Entries.Count == actualRows,
            $"解析 {snapshot.Entries.Count} 行 / 文件 {actualRows} 行");

        _out.WriteLine($"     实际数据目录现有 {snapshot.Entries.Count} 条记忆");

        foreach (var entry in snapshot.Entries.Take(5))
            _out.WriteLine($"     · {entry.Id} ｜ {entry.Category} ｜ {entry.Content}");
    }

    // ------------------------------------------------------------ 工具

    private static string Fresh(string root, string name)
    {
        var dir = Path.Combine(root, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            if (_verbose) _out.WriteLine($"  [PASS] {name}");
            return;
        }

        Fail(name, detail);
    }

    private static void Fail(string name, string? detail)
    {
        _failed++;
        _out.WriteLine($"  [FAIL] {name}");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            foreach (var line in detail.Split('\n').Take(12))
                _out.WriteLine("         " + line.TrimEnd());
        }
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string Diff(string a, string b)
    {
        var linesA = a.Replace("\r\n", "\n").Split('\n');
        var linesB = b.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();

        for (var i = 0; i < Math.Max(linesA.Length, linesB.Length); i++)
        {
            var left = i < linesA.Length ? linesA[i] : "<无>";
            var right = i < linesB.Length ? linesB[i] : "<无>";
            if (left != right)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"行 {i + 1}:");
                builder.AppendLine(CultureInfo.InvariantCulture, $"  - {left}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"  + {right}");
            }
        }

        return builder.Length == 0 ? "(内容相同)" : builder.ToString();
    }
}
