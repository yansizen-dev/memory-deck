using System.Text;

namespace MemoryDeck.Core;

/// <summary>导出结果的文本 + 统计信息。</summary>
public sealed class ExportBundle
{
    public string Text { get; init; } = "";
    public int EntryCount { get; init; }
    public int CategoryCount { get; init; }

    public int Characters => Text.Length;

    /// <summary>粗略估算 token 数：中文约 1 字 1 token，英文约 4 字符 1 token。</summary>
    public int EstimatedTokens
    {
        get
        {
            var cjk = 0;
            var other = 0;
            foreach (var ch in Text)
            {
                if (ch >= 0x2E80 && ch <= 0x9FFF) cjk++;
                else other++;
            }

            return cjk + (other / 4);
        }
    }

    /// <summary>给用户看的体量描述。</summary>
    public string SizeSummary =>
        $"{Characters:N0} 字符 ／ 约 {EstimatedTokens:N0} tokens";
}

/// <summary>
/// 把记忆导出成「可以直接粘进网页版 AI 对话框」的纯文本块。
///
/// 设计要点：
///   · 不用 Markdown 表格——粘贴到多数聊天框里会变成一堆竖线，难读
///   · 带一个简短说明头，让接手这段话的 AI 知道这是什么、该怎么用
///   · 按类别分组，同类聚在一起
///   · 「来源」「记录时间」默认不导出：前者是内部记账字段，后者对 AI 判断帮助有限
///   · 同时读走 AGENTS.md 里的协作偏好（人工维护区），一并带上
/// </summary>
public static class MemoryExporter
{
    public sealed class Options
    {
        /// <summary>是否带上「记录时间」。</summary>
        public bool IncludeTime { get; set; }

        /// <summary>是否把 AGENTS.md 里的协作偏好也带上。</summary>
        public bool IncludePreferences { get; set; } = true;

        /// <summary>只导出这些类别；为空表示全部。</summary>
        public IReadOnlyCollection<string>? OnlyCategories { get; set; }

        /// <summary>是否包含这一段说明头。</summary>
        public bool IncludeHeader { get; set; } = true;
    }

    public static ExportBundle Build(
        IReadOnlyList<MemoryEntry> entries,
        string? preferencesText,
        Options? options = null)
    {
        var opt = options ?? new Options();
        var selected = entries.AsEnumerable();

        if (opt.OnlyCategories is { Count: > 0 })
        {
            var wanted = new HashSet<string>(opt.OnlyCategories, StringComparer.OrdinalIgnoreCase);
            selected = selected.Where(e => wanted.Contains(e.Category));
        }

        var list = selected.ToList();
        var groups = list
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "未分类" : e.Category)
            .OrderBy(g => CategorySortKey(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var text = new StringBuilder();

        if (opt.IncludeHeader)
        {
            text.Append("以下是我希望你在后续对话中记住的背景信息。");
            text.Append("不需要复述或确认，直接把它当作已知前提。");
            text.Append('\n');
            text.Append('\n');
        }

        var preferences = ExtractPreferences(preferencesText);
        var wantsPreferences = opt.IncludePreferences && preferences.Length > 0;
        var preferencesWritten = false;

        foreach (var group in groups)
        {
            text.Append("【").Append(group.Key).Append("】\n");

            foreach (var entry in group.OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                text.Append("· ").Append(Flatten(entry.Content));

                if (opt.IncludeTime && entry.Time.Length > 0)
                    text.Append("（").Append(entry.Time).Append("）");

                text.Append('\n');
            }

            text.Append('\n');

            // 偏好紧跟在「身份」之后：外部 AI 先知道你是谁，再知道该怎么跟你说话
            if (wantsPreferences && !preferencesWritten
                && string.Equals(group.Key, "身份", StringComparison.OrdinalIgnoreCase))
            {
                AppendPreferences(text, preferences);
                preferencesWritten = true;
            }
        }

        if (wantsPreferences && !preferencesWritten) AppendPreferences(text, preferences);

        if (list.Count == 0 && !wantsPreferences)
        {
            text.Append("（当前没有任何记忆条目）\n\n");
        }

        if (opt.IncludeHeader)
        {
            text.Append("以上背景信息请在本次对话中保持有效。");
        }

        return new ExportBundle
        {
            Text = text.ToString().TrimEnd() + "\n",
            EntryCount = list.Count,
            CategoryCount = groups.Count,
        };
    }

    /// <summary>
    /// 从 AGENTS.md 全文里取出「人工维护区」的协作偏好内容：
    /// 摘要是程序生成的，不需要给外部 AI 看；标记区之后的才是人写的、真正有用的偏好。
    /// </summary>
    public static string ExtractPreferences(string? agentsText)
    {
        if (string.IsNullOrWhiteSpace(agentsText)) return "";

        const string endMarker = "<!-- MEMORY-DECK:SUMMARY:END -->";
        var lines = MemoryRepository.SplitLines(agentsText);
        var endIndex = lines.FindIndex(l => l.Trim() == endMarker);

        var body = endIndex >= 0
            ? lines.Skip(endIndex + 1)
            : lines.AsEnumerable();

        var kept = new List<string>();
        foreach (var line in body)
        {
            var trimmed = line.Trim();

            // 丢掉标记注释与空标题，只留真正的说明文字
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal) && trimmed.EndsWith("-->", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

            kept.Add(line);
        }

        while (kept.Count > 0 && kept[0].Trim().Length == 0) kept.RemoveAt(0);
        while (kept.Count > 0 && kept[^1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);

        return kept.Count == 0 ? "" : string.Join(Environment.NewLine, kept).Trim();
    }

    /// <summary>把协作偏好追加成一段（统一补换行，避免调用处到处判断）。</summary>
    private static void AppendPreferences(StringBuilder text, string preferences)
    {
        text.Append("【协作偏好】\n");
        text.Append(preferences.Trim());
        text.Append('\n');
        text.Append('\n');
    }

    /// <summary>把条目内容压成单行，去掉换行与多余空白。</summary>
    private static string Flatten(string? content)
    {
        var text = (content ?? "")
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ")
            .Trim();

        while (text.Contains("  ")) text = text.Replace("  ", " ");
        return text;
    }

    /// <summary>
    /// 已知类别按「对外沟通时的重要性」排，而不是按目录顺序：
    /// 接手这段话的是外部 AI，先要知道你是谁、该怎么跟你协作，再谈具体项目。
    /// </summary>
    private static readonly string[] PreferredOrder =
    {
        "身份", "偏好", "约束", "目标", "项目", "技术栈", "技能", "连接", "资料", "随手记",
    };

    private static int CategorySortKey(string name)
    {
        for (var i = 0; i < PreferredOrder.Length; i++)
        {
            if (string.Equals(PreferredOrder[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // 预设目录里剩下的项排在已知项之后
        for (var i = 0; i < CategoryCatalog.Presets.Count; i++)
        {
            if (string.Equals(CategoryCatalog.Presets[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return PreferredOrder.Length + i;
        }

        return PreferredOrder.Length + CategoryCatalog.Presets.Count + 1;
    }
}
