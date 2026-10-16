using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MemoryDeck.Core;

/// <summary>
/// 记忆仓库：MEMORY.md 是唯一数据源（single source of truth）；
/// AGENTS.md 的摘要标记区由本类在每次写入后自动重建，
/// 保证 AI 侧在新会话加载到的摘要永远是最新的。
/// </summary>
public sealed class MemoryRepository
{
    private const string SummaryBegin = "<!-- MEMORY-DECK:SUMMARY:BEGIN -->";
    private const string SummaryEnd = "<!-- MEMORY-DECK:SUMMARY:END -->";
    private const int MaxAutoBackups = 30;

    private static readonly string[] DefaultColumns = { "ID", "类别", "内容", "来源", "记录时间" };

    private static readonly Regex IdPattern = new(@"^\s*([A-Za-z]{1,4})-(\d{4,6})\s*$", RegexOptions.Compiled);
    private static readonly object FileGate = new();

    public StorageConfig Config { get; }

    public MemoryRepository(StorageConfig config) => Config = config;

    // ================================================================ 读取

    public RepoSnapshot Load()
    {
        var warnings = new List<string>();
        var entries = new List<MemoryEntry>();
        var memoryExists = File.Exists(Config.MemoryPath);
        var agentsExists = File.Exists(Config.AgentsPath);

        if (memoryExists)
        {
            try
            {
                entries = ParseEntries(SplitLines(Utf8.ReadAllText(Config.MemoryPath)));
            }
            catch (Exception ex)
            {
                warnings.Add("解析 MEMORY.md 失败：" + ex.Message);
            }
        }

        entries.Sort(CompareEntries);

        var fingerprint = "";
        if (memoryExists) fingerprint += SafeHash(Config.MemoryPath) + "|";
        if (agentsExists) fingerprint += SafeHash(Config.AgentsPath);

        return new RepoSnapshot
        {
            MemoryPath = Config.MemoryPath,
            AgentsPath = Config.AgentsPath,
            MemoryExists = memoryExists,
            AgentsExists = agentsExists,
            Fingerprint = fingerprint,
            Entries = entries,
            Warnings = warnings,
        };
    }

    public string NextId(IEnumerable<MemoryEntry> existing)
    {
        var prefix = "M";
        var max = 0;
        foreach (var entry in existing)
        {
            var match = IdPattern.Match(entry.Id ?? "");
            if (!match.Success) continue;
            prefix = match.Groups[1].Value.ToUpperInvariant();
            if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > max)
                max = value;
        }

        return $"{prefix}-{max + 1:0000}";
    }

    // ================================================================ 写入

    public WriteResult Add(string? category, string? content, string? source, out string newId)
    {
        newId = "";
        var cleanContent = Sanitize(content);
        if (cleanContent.Length == 0) return WriteResult.Failure("内容不能为空。");
        if (cleanContent.Length > 2000) return WriteResult.Failure("内容过长（上限 2000 字）。");

        var cleanCategory = Sanitize(category);
        if (cleanCategory.Length == 0) cleanCategory = "未分类";
        var cleanSource = Sanitize(source);
        if (cleanSource.Length == 0) cleanSource = "用户记录";

        lock (FileGate)
        {
            var document = ReadDocument();
            newId = NextId(document.Entries);
            document.Entries.Add(new MemoryEntry
            {
                Id = newId,
                Category = cleanCategory,
                Content = cleanContent,
                Source = cleanSource,
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            });

            return Persist(document, $"已新增记忆 {newId}。");
        }
    }

    public WriteResult Update(MemoryEntry edited)
    {
        if (string.IsNullOrWhiteSpace(edited.Id)) return WriteResult.Failure("缺少条目 ID。");
        var cleanContent = Sanitize(edited.Content);
        if (cleanContent.Length == 0) return WriteResult.Failure("内容不能为空。");
        if (cleanContent.Length > 2000) return WriteResult.Failure("内容过长（上限 2000 字）。");

        lock (FileGate)
        {
            var document = ReadDocument();
            var index = document.Entries.FindIndex(e => SameId(e.Id, edited.Id));
            if (index < 0)
                return WriteResult.Failure($"未找到条目 {edited.Id}（文件可能已被外部修改，请先刷新）。");

            var category = Sanitize(edited.Category);
            document.Entries[index] = new MemoryEntry
            {
                Id = document.Entries[index].Id,
                Category = category.Length == 0 ? "未分类" : category,
                Content = cleanContent,
                Source = Sanitize(edited.Source),
                Time = document.Entries[index].Time,
            };

            return Persist(document, $"已更新记忆 {edited.Id}。");
        }
    }

    public WriteResult Remove(MemoryEntry target)
    {
        if (string.IsNullOrWhiteSpace(target.Id)) return WriteResult.Failure("缺少条目 ID。");

        lock (FileGate)
        {
            var document = ReadDocument();
            var removed = document.Entries.RemoveAll(e => SameId(e.Id, target.Id));
            if (removed == 0)
                return WriteResult.Failure($"未找到条目 {target.Id}（文件可能已被外部修改，请先刷新）。");

            return Persist(document, $"已删除记忆 {target.Id}。");
        }
    }

    private static bool SameId(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    // ================================================================ 文档模型

    private sealed class MarkdownDocument
    {
        public string Preamble { get; set; } = "";
        public string HeaderLine { get; set; } = "";
        public string Tail { get; set; } = "";
        public List<MemoryEntry> Entries { get; } = new();
    }

    private MarkdownDocument ReadDocument()
    {
        var doc = new MarkdownDocument();
        if (!File.Exists(Config.MemoryPath))
        {
            doc.Preamble = DefaultPreamble();
            doc.HeaderLine = DefaultHeader();
            return doc;
        }

        List<string> lines;
        try { lines = SplitLines(Utf8.ReadAllText(Config.MemoryPath)); }
        catch { lines = new List<string>(); }

        if (lines.Count == 0)
        {
            doc.Preamble = DefaultPreamble();
            doc.HeaderLine = DefaultHeader();
            return doc;
        }

        var table = LocateTable(lines);
        if (table is null)
        {
            // 文件存在但没有识别到表格：保留全文，仅追加表格。
            var body = string.Join(Environment.NewLine, lines).TrimEnd();
            doc.Preamble = body.Length == 0 ? DefaultPreamble() : body + Environment.NewLine + Environment.NewLine;
            doc.HeaderLine = DefaultHeader();
            return doc;
        }

        var preambleLines = lines.GetRange(0, table.Value.Header);
        while (preambleLines.Count > 0 && preambleLines[^1].Trim().Length == 0) preambleLines.RemoveAt(preambleLines.Count - 1);
        doc.Preamble = preambleLines.Count == 0
            ? DefaultPreamble()
            : string.Join(Environment.NewLine, preambleLines) + Environment.NewLine + Environment.NewLine;

        doc.HeaderLine = lines[table.Value.Header].Trim();

        var tailStart = table.Value.Last + 1;
        if (tailStart < lines.Count)
        {
            var tailLines = lines.GetRange(tailStart, lines.Count - tailStart);
            var tailText = string.Join(Environment.NewLine, tailLines).TrimEnd();
            doc.Tail = tailText.Length == 0 ? "" : Environment.NewLine + tailText;
        }

        doc.Entries.AddRange(ParseEntries(lines));
        return doc;
    }

    private WriteResult Persist(MarkdownDocument doc, string message)
    {
        doc.Entries.Sort(CompareEntries);
        var columns = ResolveColumns(doc.HeaderLine);

        string? backupWarning = null;
        try { AutoBackup(); }
        catch (Exception ex) { backupWarning = "自动备份失败：" + ex.Message; }

        var text = new StringBuilder();
        text.Append(doc.Preamble);
        text.Append("| ").Append(string.Join(" | ", columns.Select(DisplayHeader))).Append(" |").Append(Nl);
        text.Append('|').Append(string.Join('|', columns.Select(_ => " --- "))).Append('|').Append(Nl);

        foreach (var entry in doc.Entries)
        {
            text.Append("| ")
                .Append(string.Join(" | ", columns.Select(c => Escape(ValueOf(entry, c)))))
                .Append(" |").Append(Nl);
        }

        if (doc.Tail.Length > 0) text.Append(doc.Tail).Append(Nl);

        try
        {
            Utf8.WriteAllText(Config.MemoryPath, text.ToString());
        }
        catch (Exception ex)
        {
            return WriteResult.Failure("写入 MEMORY.md 失败：" + ex.Message);
        }

        var agentsSynced = false;
        string? agentsWarning = null;
        try
        {
            agentsSynced = SyncAgentsSummary(doc.Entries);
            if (!agentsSynced) agentsWarning = "AGENTS.md 中未找到摘要标记区，已跳过同步。";
        }
        catch (Exception ex)
        {
            agentsWarning = "同步 AGENTS.md 失败：" + ex.Message;
        }

        var final = message;
        if (backupWarning is not null) final += "（" + backupWarning + "）";
        return WriteResult.Success(final, agentsSynced, agentsWarning);
    }

    // ================================================================ 表格定位

    private readonly record struct TableSpan(int Header, int Separator, int Last);

    private static TableSpan? LocateTable(IReadOnlyList<string> lines)
    {
        for (var i = 0; i + 1 < lines.Count; i++)
        {
            if (!LooksLikeRow(lines[i])) continue;
            if (!LooksLikeSeparator(lines[i + 1])) continue;

            var cells = SplitCells(lines[i]);
            if (!HasHeaderKeyword(cells)) continue;

            var last = i + 1;
            for (var j = i + 2; j < lines.Count; j++)
            {
                if (!LooksLikeRow(lines[j])) continue;
                if (LooksLikeSeparator(lines[j])) continue;
                last = j;
            }

            return new TableSpan(i, i + 1, last);
        }

        return null;
    }

    private static List<MemoryEntry> ParseEntries(IReadOnlyList<string> lines)
    {
        var result = new List<MemoryEntry>();
        var span = LocateTable(lines);
        if (span is null) return result;

        var columns = ResolveColumns(lines[span.Value.Header]);

        for (var i = span.Value.Separator + 1; i <= span.Value.Last; i++)
        {
            if (!LooksLikeRow(lines[i]) || LooksLikeSeparator(lines[i])) continue;

            var cells = SplitCells(lines[i]);
            var entry = new MemoryEntry();
            for (var c = 0; c < columns.Length; c++)
            {
                var value = c < cells.Count ? cells[c].Trim() : "";
                switch (columns[c])
                {
                    case "ID": entry.Id = value; break;
                    case "类别": entry.Category = value; break;
                    case "内容": entry.Content = value; break;
                    case "来源": entry.Source = value; break;
                    case "记录时间": entry.Time = value; break;
                }
            }

            var isPlaceholder = entry.Id is "—" or "-" || entry.Content.StartsWith("暂无", StringComparison.Ordinal);
            if (isPlaceholder) continue;
            if (entry.Id.Length == 0 && entry.Content.Length == 0) continue;

            result.Add(entry);
        }

        return result;
    }

    private static string[] ResolveColumns(string headerLine)
    {
        var cells = SplitCells(headerLine);
        if (!HasHeaderKeyword(cells)) return DefaultColumns;

        var columns = new string[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var key = NormalizeHeader(cells[i]);
            columns[i] = key.Length == 0 ? "忽略" : key;
        }

        // 补齐缺失的标准列，避免写入时丢字段
        var missing = DefaultColumns.Where(d => !columns.Contains(d)).ToList();
        if (missing.Count == 0) return columns;

        var extended = columns.ToList();
        extended.AddRange(missing);
        return extended.ToArray();
    }

    private static string DisplayHeader(string key) => key switch
    {
        "ID" => "ID",
        "类别" => "类别",
        "内容" => "内容",
        "来源" => "来源",
        "记录时间" => "记录时间",
        _ => key,
    };

    private static string ValueOf(MemoryEntry entry, string key) => key switch
    {
        "ID" => entry.Id,
        "类别" => entry.Category,
        "内容" => entry.Content,
        "来源" => entry.Source,
        "记录时间" => entry.Time,
        _ => "",
    };

    private static bool HasHeaderKeyword(IReadOnlyList<string> cells)
    {
        var hasId = false;
        var hasContent = false;
        foreach (var cell in cells)
        {
            var key = NormalizeHeader(cell);
            if (key == "ID") hasId = true;
            else if (key == "内容") hasContent = true;
        }

        return hasId && hasContent;
    }

    private static string NormalizeHeader(string cell)
    {
        var text = (cell ?? "").Trim().Trim('*', '`', ' ');
        return text switch
        {
            "ID" or "id" or "Id" => "ID",
            "类别" or "分类" or "类型" => "类别",
            "内容" or "记忆内容" => "内容",
            "来源" or "出处" => "来源",
            "记录时间" or "日期" or "时间" => "记录时间",
            _ => text,
        };
    }

    /// <summary>形如 | a | b | 的一行。</summary>
    private static bool LooksLikeRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '|' && trimmed[^1] == '|';
    }

    /// <summary>形如 | --- | :--: | 的分隔行。</summary>
    private static bool LooksLikeSeparator(string line)
    {
        if (!LooksLikeRow(line)) return false;
        var inner = line.Trim().Trim('|');
        if (inner.Length == 0) return false;

        foreach (var chunk in inner.Split('|'))
        {
            var piece = chunk.Trim().Trim(':').Trim();
            if (piece.Length < 3) return false;
            if (piece.Any(ch => ch != '-')) return false;
        }

        return true;
    }

    /// <summary>按 | 切分表格行；支持 \| 转义与 \\ 还原，往返无损。</summary>
    private static List<string> SplitCells(string line)
    {
        var cells = new List<string>();
        var buffer = new StringBuilder();
        var trimmed = (line ?? "").Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];

        var escaped = false;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (escaped)
            {
                buffer.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\') { escaped = true; continue; }
            if (ch == '|')
            {
                cells.Add(buffer.ToString());
                buffer.Clear();
                continue;
            }

            buffer.Append(ch);
        }

        if (escaped) buffer.Append('\\');
        cells.Add(buffer.ToString());
        return cells;
    }

    // ================================================================ 转义

    /// <summary>
    /// 写入 Markdown 表格前的转义（无损）：
    ///   · 反斜杠 -> \\            （避免与 \| 转义冲突）
    ///   · 全角｜与半角 | -> \|    （渲染时都显示为 |，不会破表）
    /// 读取侧由 SplitCells 反向还原，往返完全无损。
    /// </summary>
    public static string Escape(string? value)
    {
        var text = (value ?? "").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        while (text.Contains("  ")) text = text.Replace("  ", " ");
        text = text.Trim();

        var builder = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '|':
                case '｜':
                    builder.Append("\\|");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>用户输入清洗：压平换行与连续空格，不做有损替换。</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        while (text.Contains("  ")) text = text.Replace("  ", " ");
        return text.Trim();
    }

    // ================================================================ AGENTS.md 同步

    public bool SyncAgentsSummary(IReadOnlyList<MemoryEntry> entries)
    {
        List<string> lines;
        if (File.Exists(Config.AgentsPath))
        {
            lines = SplitLines(Utf8.ReadAllText(Config.AgentsPath));
        }
        else
        {
            lines = SplitLines(DefaultAgents());
        }

        var begin = lines.FindIndex(l => l.Trim() == SummaryBegin);
        var end = lines.FindIndex(l => l.Trim() == SummaryEnd);

        if (begin >= 0 && end > begin)
        {
            var merged = new List<string>();
            merged.AddRange(lines.GetRange(0, begin));
            merged.Add(SummaryBegin);
            merged.Add("");
            merged.AddRange(BuildSummaryMarkdown(entries));
            merged.Add(SummaryEnd);
            if (end + 1 < lines.Count) merged.AddRange(lines.GetRange(end + 1, lines.Count - end - 1));

            Utf8.WriteAllText(Config.AgentsPath, JoinLines(merged));
            return true;
        }

        // 标记区缺失（含残缺情况）：在文件末尾补一个完整标记区，原有内容一律保留。
        var appended = new List<string>(lines);
        if (begin >= 0) appended.RemoveRange(begin, appended.Count - begin); // 丢弃残缺的 BEGIN
        while (appended.Count > 0 && appended[^1].Trim().Length == 0) appended.RemoveAt(appended.Count - 1);
        appended.Add("");
        appended.Add(SummaryBegin);
        appended.Add("");
        appended.AddRange(BuildSummaryMarkdown(entries));
        appended.Add(SummaryEnd);
        Utf8.WriteAllText(Config.AgentsPath, JoinLines(appended));
        return true;
    }

    public static IReadOnlyList<string> BuildSummaryMarkdown(IReadOnlyList<MemoryEntry> entries)
    {
        var list = new List<string>
        {
            "## 用户画像（长期记忆）",
            "",
            $"最后更新：{DateTime.Now:yyyy-MM-dd HH:mm} ｜ 共 {entries.Count} 条 ｜ 由 MemoryDeck 自动维护",
            "",
            "| ID | 类别 | 内容 | 记录时间 |",
            "| --- | --- | --- | --- |",
        };

        foreach (var entry in entries)
            list.Add($"| {Escape(entry.Id)} | {Escape(entry.Category)} | {Escape(entry.Content)} | {Escape(entry.Time)} |");

        if (entries.Count == 0)
            list.Add("| — | — | 暂无记忆条目 | — |");

        list.Add("");
        list.Add("> 完整条目见同目录 `MEMORY.md`。图形界面：`apps/MemoryDeck`（MemoryDeck.exe）。");
        list.Add("");
        return list;
    }

    private static string DefaultHeader() => "| ID | 类别 | 内容 | 来源 | 记录时间 |";

    private static string DefaultPreamble() =>
        "# MEMORY.md — 长期记忆条目" + Environment.NewLine + Environment.NewLine +
        "记忆按时间顺序累积。新增条目追加到表格末尾，不覆盖历史。" + Environment.NewLine + Environment.NewLine +
        "## 记忆条目" + Environment.NewLine + Environment.NewLine;

    private static string DefaultAgents() =>
        "# AGENTS.md — 工作区长期记忆" + Environment.NewLine + Environment.NewLine +
        SummaryBegin + Environment.NewLine +
        SummaryEnd + Environment.NewLine;

    // ================================================================ 备份与工具

    public string AutoBackup()
    {
        if (!File.Exists(Config.MemoryPath)) return "";
        var dir = Config.AutoBackupDirectory;
        Directory.CreateDirectory(dir);

        var name = $"{Path.GetFileNameWithoutExtension(Config.MemoryPath)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.md";
        var target = Path.Combine(dir, name);
        File.Copy(Config.MemoryPath, target, overwrite: false);

        var stale = new DirectoryInfo(dir).GetFiles("*.md")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxAutoBackups)
            .ToList();
        foreach (var file in stale)
        {
            try { file.Delete(); } catch { }
        }

        return target;
    }

    private static int CompareEntries(MemoryEntry a, MemoryEntry b)
    {
        var ma = IdPattern.Match(a.Id ?? "");
        var mb = IdPattern.Match(b.Id ?? "");
        if (ma.Success && mb.Success)
        {
            var prefixCompare = string.CompareOrdinal(ma.Groups[1].Value.ToUpperInvariant(), mb.Groups[1].Value.ToUpperInvariant());
            if (prefixCompare != 0) return prefixCompare;

            var va = long.Parse(ma.Groups[2].Value, CultureInfo.InvariantCulture);
            var vb = long.Parse(mb.Groups[2].Value, CultureInfo.InvariantCulture);
            var cmp = va.CompareTo(vb);
            if (cmp != 0) return cmp;
        }

        return string.CompareOrdinal(a.Id, b.Id);
    }

    public static List<string> SplitLines(string text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    private static string Nl => Environment.NewLine;

    private static string JoinLines(IEnumerable<string> lines) =>
        string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;

    private static string SafeHash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(stream), 0, 8);
        }
        catch
        {
            return "ERR";
        }
    }
}
