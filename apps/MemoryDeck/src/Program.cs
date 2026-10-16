using System.Globalization;
using MemoryDeck.Core;
using MemoryDeck.Ui;

namespace MemoryDeck;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(CommandLineOptions.HelpText);
            return 0;
        }

        if (options.RunSelfTest)
        {
            // 控制台模式下不初始化 WinForms
            return SelfTest.Run(options.SelfTestVerbose);
        }

        if (options.RunRepair)
        {
            return Repair.Run(StorageConfig.Resolve(options.DataDirectory));
        }

        if (options.DumpEnvironment)
        {
            return EnvDump.Run();
        }

        if (options.RunExport)
        {
            return ExportCommand.Run(StorageConfig.Resolve(options.DataDirectory), options.Export);
        }

        if (options.OpenDirectory)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = options.DataDirectory ?? StorageConfig.Resolve().DataDirectory,
                    UseShellExecute = true,
                });
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开目录失败：" + ex.Message, "MemoryDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        UiCulture.Install();

        try
        {
            var config = StorageConfig.Resolve(options.DataDirectory);
            using var form = new MainForm(config) { AutoOpenExportOnLoad = options.ShowExportUi };
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"MemoryDeck 启动失败：{Environment.NewLine}{Environment.NewLine}{ex}",
                "MemoryDeck",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }
    }
}

/// <summary>命令行参数解析。</summary>
internal sealed class CommandLineOptions
{
    public bool RunSelfTest { get; private set; }
    public bool SelfTestVerbose { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool OpenDirectory { get; private set; }
    public bool RunRepair { get; private set; }
    public bool DumpEnvironment { get; private set; }
    public bool RunExport { get; private set; }
    public bool ShowExportUi { get; private set; }
    public ExportCliOptions Export { get; } = new();
    public string? DataDirectory { get; private set; }

    public const string HelpText = """
        MemoryDeck — 本地记忆卡片管理器

        用法：
          MemoryDeck.exe                              启动图形界面
          MemoryDeck.exe --data <目录>                指定数据目录（含 MEMORY.md / AGENTS.md）
          MemoryDeck.exe --open                       用资源管理器打开数据目录
          MemoryDeck.exe --selftest [--verbose]       运行数据层自检并返回退出码
          MemoryDeck.exe --repair                     规范化重写 MEMORY.md 并重建 AGENTS.md 摘要
          MemoryDeck.exe --env                        打印屏幕 / DPI / 窗口尺寸诊断信息
          MemoryDeck.exe --help                       显示本帮助

        导出（给网页版 AI 用的纯文本）：
          MemoryDeck.exe --export [-o 文件]           导出可粘贴的纯文本；不指定 -o 则打到 stdout
          MemoryDeck.exe --export -o out.txt          写入文件（UTF-8 无 BOM）
          MemoryDeck.exe --export --no-preferences    不带 AGENTS.md 里的协作偏好
          MemoryDeck.exe --export --no-header         不带说明头
          MemoryDeck.exe --export --category 身份 --category 项目
                                                      只导出指定类别（可重复）

        环境变量：
          MEMORYDECK_DATA                             等同 --data，优先级低于命令行
        """;

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i].Trim();
            var token = raw.ToLowerInvariant();

            switch (token)
            {
                case "--selftest":
                case "-t":
                    options.RunSelfTest = true;
                    break;

                case "--verbose":
                case "-v":
                    options.SelfTestVerbose = true;
                    break;

                case "--repair":
                    options.RunRepair = true;
                    break;

                case "--env":
                    options.DumpEnvironment = true;
                    break;

                case "--export":
                    options.RunExport = true;
                    break;
                case "--no-preferences":
                    options.Export.IncludePreferences = false;
                    break;

                case "--no-header":
                    options.Export.IncludeHeader = false;
                    break;

                case "-o":
                case "--out":
                    if (i + 1 < args.Length) options.Export.OutputPath = args[++i];
                    break;

                case "--category":
                case "-c":
                    if (i + 1 < args.Length)
                    {
                        options.Export.Categories = (options.Export.Categories ?? new List<string>())
                            .Append(args[++i])
                            .ToList();
                    }
                    break;

                case "--export-ui":
                    // 供截图与自检使用：启动后直接弹出导出对话框
                    options.ShowExportUi = true;
                    break;

                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;

                case "--open":
                    options.OpenDirectory = true;
                    break;

                case "--data":
                    if (i + 1 < args.Length) options.DataDirectory = args[++i];
                    break;

                default:
                    if (token.StartsWith("--data=", StringComparison.Ordinal))
                        options.DataDirectory = raw[7..];
                    break;
            }
        }

        return options;
    }
}

/// <summary>统一界面语言设置，保证日期/数字格式稳定。</summary>
internal static class UiCulture
{
    /// <summary>安装 zh-CN 文化；即便运行在 globalization-invariant 模式下也不允许抛异常。</summary>
    public static bool Install()
    {
        try
        {
            var culture = new CultureInfo("zh-CN");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Application.CurrentCulture = culture;
            return true;
        }
        catch (Exception)
        {
            // 退化到系统当前文化：程序照常可用，只是日期/数字格式跟随系统。
            return false;
        }
    }
}
