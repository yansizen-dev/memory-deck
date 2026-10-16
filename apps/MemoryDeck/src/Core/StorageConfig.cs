using System.Text;
using System.Text.Json;

namespace MemoryDeck.Core;

/// <summary>
/// 数据目录配置。启动时按以下顺序推断，任何一步成功即停止：
///   1) 环境变量 MEMORYDECK_DATA
///   2) exe 同级目录下的 memorydeck.config.json
///   3) exe 在 &lt;root&gt;\apps\MemoryDeck\publish 形态下，向上回溯仓库根
///   4) 当前工作目录
/// </summary>
public sealed class StorageConfig
{
    public const string EnvVar = "MEMORYDECK_DATA";
    public const string ConfigFileName = "memorydeck.config.json";
    public const string MemoryFileName = "MEMORY.md";
    public const string AgentsFileName = "AGENTS.md";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string DataDirectory { get; set; } = "";
    public string MemoryFileNameOverride { get; set; } = MemoryFileName;
    public string AgentsFileNameOverride { get; set; } = AgentsFileName;

    public string MemoryPath => Path.Combine(DataDirectory, MemoryFileNameOverride);
    public string AgentsPath => Path.Combine(DataDirectory, AgentsFileNameOverride);
    public string ConfigPath => Path.Combine(ExeDirectory, ConfigFileName);
    public string AutoBackupDirectory => Path.Combine(DataDirectory, ".memory-deck", "backups");

    public static string ExeDirectory
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) return dir;
            }

            return AppContext.BaseDirectory;
        }
    }

    public static StorageConfig Resolve(string? explicitDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            var cfg = new StorageConfig { DataDirectory = Normalize(explicitDirectory) };
            cfg.LoadOverridesIfPresent();
            return cfg;
        }

        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            var cfg = new StorageConfig { DataDirectory = Normalize(env) };
            cfg.LoadOverridesIfPresent();
            return cfg;
        }

        var fromFile = TryReadConfigFile(Path.Combine(ExeDirectory, ConfigFileName));
        if (fromFile is not null) return fromFile;

        var inferred = InferFromAncestors();
        if (inferred is not null) return inferred;

        return new StorageConfig { DataDirectory = Normalize(Environment.CurrentDirectory) };
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json, Utf8.NoBom);
        }
        catch (Exception)
        {
            // 配置写失败不影响主流程；下次启动回落到推断逻辑。
        }
    }

    private void LoadOverridesIfPresent()
    {
        var fromFile = TryReadConfigFile(Path.Combine(ExeDirectory, ConfigFileName));
        if (fromFile is null) return;
        MemoryFileNameOverride = fromFile.MemoryFileNameOverride;
        AgentsFileNameOverride = fromFile.AgentsFileNameOverride;
    }

    private static StorageConfig? TryReadConfigFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<StorageConfig>(json, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.DataDirectory)) return null;
            parsed.DataDirectory = Normalize(parsed.DataDirectory);
            if (string.IsNullOrWhiteSpace(parsed.MemoryFileNameOverride))
                parsed.MemoryFileNameOverride = MemoryFileName;
            if (string.IsNullOrWhiteSpace(parsed.AgentsFileNameOverride))
                parsed.AgentsFileNameOverride = AgentsFileName;
            return parsed;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static StorageConfig? InferFromAncestors()
    {
        // exe 位于 ...\<root>\apps\MemoryDeck\publish\MemoryDeck.exe 时，回溯到 <root>
        var probe = new DirectoryInfo(ExeDirectory);
        for (var depth = 0; probe is not null && depth < 6; depth++, probe = probe.Parent)
        {
            var hasMarker = File.Exists(Path.Combine(probe.FullName, MemoryFileName))
                            || File.Exists(Path.Combine(probe.FullName, AgentsFileName));
            var hasRepoShape = Directory.Exists(Path.Combine(probe.FullName, "apps"));
            if (hasMarker || hasRepoShape)
            {
                return new StorageConfig { DataDirectory = Normalize(probe.FullName) };
            }
        }

        return null;
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

/// <summary>统一的 UTF-8 无 BOM 编码/读写入口。</summary>
public static class Utf8
{
    public static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        // 原子写：先写同目录临时文件，再替换，避免半截文件。
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, NoBom))
        {
            writer.Write(content);
        }

        if (File.Exists(path))
        {
            File.Replace(temp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
