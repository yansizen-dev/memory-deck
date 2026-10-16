using System.Text;
using MemoryDeck.Core;

namespace MemoryDeck;

/// <summary>
/// 命令行导出模式（--export）：
/// 不开界面，直接把「可粘进网页版 AI 的纯文本」打到 stdout 或写入文件。
/// 同一个 MemoryExporter 供界面与命令行共用，保证两条路出来的东西一模一样。
/// </summary>
internal static class ExportCommand
{
    public static int Run(StorageConfig config, ExportCliOptions options)
    {
        try
        {
            var repo = new MemoryRepository(config);
            var snapshot = repo.Load();

            string? agentsText = null;
            if (File.Exists(config.AgentsPath))
            {
                try { agentsText = Utf8.ReadAllText(config.AgentsPath); }
                catch { agentsText = null; }
            }

            var bundle = MemoryExporter.Build(
                snapshot.Entries,
                agentsText,
                new MemoryExporter.Options
                {
                    // 记录时间固定不导出（对 AI 判断帮助小，且明显拉长体量）
                IncludeTime = false,
                    IncludePreferences = options.IncludePreferences,
                    IncludeHeader = options.IncludeHeader,
                    OnlyCategories = options.Categories,
                });

            if (options.OutputPath is { Length: > 0 } path)
            {
                var full = Path.GetFullPath(path);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, bundle.Text, new UTF8Encoding(false));

                var log = ConsoleBridge.Create("export-last.log");
                log.WriteLine($"已导出 {bundle.EntryCount} 条 / {bundle.CategoryCount} 个类别");
                log.WriteLine("体量：" + bundle.SizeSummary);
                log.WriteLine("文件：" + full);
                log.Dispose();
                return 0;
            }

            // 没有指定文件就打到 stdout —— 便于管道消费
            Console.Write(bundle.Text);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("导出失败：" + ex.Message);
            return 1;
        }
    }
}

/// <summary>--export 的参数。</summary>
internal sealed class ExportCliOptions
{
    public bool IncludeTime { get; set; }
    public bool IncludePreferences { get; set; } = true;
    public bool IncludeHeader { get; set; } = true;
    public IReadOnlyCollection<string>? Categories { get; set; }
    public string? OutputPath { get; set; }
}
