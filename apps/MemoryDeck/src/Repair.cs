using System.Text;
using MemoryDeck.Core;

namespace MemoryDeck;

/// <summary>
/// 自愈 / 规范化模式（--repair）：
/// 读取当前数据目录的 MEMORY.md，按统一格式重写一遍，并重建 AGENTS.md 的摘要区。
/// 用途：
///   · 外部手改过文件、缩进或列顺序乱了，跑一次恢复标准格式；
///   · 写入前后对比条目数，确认没有丢数据；
///   · 是「确认渲染器/写入器行为」最直接的端到端验收手段。
/// 写入前会自动备份原文件。
/// </summary>
internal static class Repair
{
    public static int Run(StorageConfig config)
    {
        var log = ConsoleBridge.Create("repair-last.log");
        var repo = new MemoryRepository(config);

        log.WriteLine("MemoryDeck 数据目录自愈");
        log.WriteLine(new string('=', 58));
        log.WriteLine("数据目录：" + config.DataDirectory);
        log.WriteLine("MEMORY.md：" + config.MemoryPath);
        log.WriteLine("AGENTS.md：" + config.AgentsPath);
        log.WriteLine();

        try
        {
            var before = repo.Load();
            log.WriteLine($"读取到 {before.Entries.Count} 条记忆，告警 {before.Warnings.Count} 条。");
            foreach (var warning in before.Warnings) log.WriteLine("  ! " + warning);

            foreach (var entry in before.Entries)
                log.WriteLine($"  · {entry.Id} ｜ {entry.Category} ｜ {entry.Content}");

            log.WriteLine();

            if (!before.MemoryExists)
            {
                log.WriteLine("MEMORY.md 不存在，创建一份空的骨架。");
            }
            else
            {
                var backup = repo.AutoBackup();
                log.WriteLine("已备份到：" + backup);
            }

            log.WriteLine();

            // 用一条空转写入触发完整格式化流程：先加一条哨兵再删掉，
            // 这样能真实跑一遍「新增 -> 写盘 -> 读取 -> 删除 -> 写盘」而不改变数据。
            var sentinel = repo.Add("自愈", "临时哨兵条目（正常情况下会被立即删除）", "MemoryDeck --repair", out var sentinelId);
            if (!sentinel.Ok)
            {
                log.WriteLine("写入失败：" + sentinel.Message);
                log.Dispose();
                return 1;
            }

            log.WriteLine($"哨兵条目 {sentinelId} 已写入并回读校验。");

            var mid = repo.Load();
            var found = mid.Entries.FirstOrDefault(e => e.Id == sentinelId);
            if (found is null)
            {
                log.WriteLine("回读失败：哨兵条目没有出现在重新解析的结果里。");
                log.Dispose();
                return 1;
            }

            log.WriteLine($"回读内容一致：{found.Content == "临时哨兵条目（正常情况下会被立即删除）"}");

            var removed = repo.Remove(found);
            log.WriteLine("删除哨兵：" + removed.Message);

            log.WriteLine();

            var after = repo.Load();
            log.WriteLine($"规范化后 {after.Entries.Count} 条记忆。");

            if (after.Entries.Count != before.Entries.Count)
            {
                log.WriteLine($"条目数发生变化：{before.Entries.Count} -> {after.Entries.Count}");
                log.Dispose();
                return 1;
            }

            log.WriteLine("条目数一致，数据无损。");
            log.WriteLine();
            log.WriteLine("MEMORY.md 现状：");
            log.WriteLine(new string('-', 58));
            foreach (var line in MemoryRepository.SplitLines(Utf8.ReadAllText(config.MemoryPath)))
                log.WriteLine(line);
            log.WriteLine(new string('-', 58));

            log.WriteLine();
            log.WriteLine("结果：OK");
            log.WriteLine("日志文件：" + log.LogPath);
            log.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            log.WriteLine("自愈失败：" + ex);
            log.Dispose();
            return 2;
        }
    }
}
