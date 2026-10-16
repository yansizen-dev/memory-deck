using System.Security.Cryptography;
using System.Text;

namespace MemoryDeck.Core;

/// <summary>一条记忆记录，对应 MEMORY.md 记忆表里的一行。</summary>
public sealed class MemoryEntry
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Content { get; set; } = "";

    /// <summary>
    /// 界面已不再显示「来源」，但文件里若存在这一列，值会保留在这里并在写回时原样写出。
    /// 避免"不再支持某个字段"演变成静默删数据。
    /// </summary>
    public string Source { get; set; } = "";

    public string Time { get; set; } = "";

    /// <summary>
    /// 表格里除已知列之外的其他列（按出现顺序）。
    /// 用户在 MEMORY.md 里手工加了自定义列时，程序不会把它抹掉。
    /// </summary>
    public List<string> Extra { get; set; } = new();

    public MemoryEntry Clone() => new()
    {
        Id = Id,
        Category = Category,
        Content = Content,
        Source = Source,
        Time = Time,
        Extra = new List<string>(Extra),
    };

    public override string ToString() => $"【{Category}】{Content}";
}

/// <summary>仓库状态快照，用于界面展示。</summary>
public sealed class RepoSnapshot
{
    public string MemoryPath { get; init; } = "";
    public string AgentsPath { get; init; } = "";
    public bool MemoryExists { get; init; }
    public bool AgentsExists { get; init; }
    public string Fingerprint { get; init; } = "";
    public IReadOnlyList<MemoryEntry> Entries { get; init; } = Array.Empty<MemoryEntry>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>一次写入操作的结果。</summary>
public sealed class WriteResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public bool AgentsSynced { get; init; }
    public string? AgentsWarning { get; init; }

    public static WriteResult Success(string message, bool agentsSynced, string? agentsWarning)
        => new() { Ok = true, Message = message, AgentsSynced = agentsSynced, AgentsWarning = agentsWarning };

    public static WriteResult Failure(string message) => new() { Ok = false, Message = message };
}
