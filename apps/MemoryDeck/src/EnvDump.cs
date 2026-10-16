using System.Text;
using MemoryDeck.Core;

namespace MemoryDeck;

/// <summary>
/// 环境诊断模式（--env）：把影响界面尺寸与字体的关键事实打出来，
/// 用来排查「窗口被裁切 / 布局错位 / DPI 缩放异常」这类只在特定机器上出现的问题。
/// </summary>
internal static class EnvDump
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static int Run()
    {
        var log = ConsoleBridge.Create("env-last.log");

        log.WriteLine("MemoryDeck 环境诊断");
        log.WriteLine(new string('=', 58));
        log.WriteLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        log.WriteLine();
        log.WriteLine("【运行时】");
        log.WriteLine("  " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        log.WriteLine("  进程架构：" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        log.WriteLine("  操作系统：" + Environment.OSVersion.VersionString);

        log.WriteLine();
        log.WriteLine("【进程 DPI 感知】");
        log.WriteLine("  是否 PerMonitorV2 感知：" + (Application.HighDpiMode == HighDpiMode.PerMonitorV2));
        log.WriteLine("  Application.HighDpiMode：" + Application.HighDpiMode);

        log.WriteLine();
        log.WriteLine("【显示器】");
        foreach (var screen in Screen.AllScreens)
        {
            log.WriteLine($"  {screen.DeviceName}");
            log.WriteLine($"    设备名   ：{screen.DeviceName}");
            log.WriteLine($"    Bounds   ：{screen.Bounds}  ({screen.Bounds.Width}x{screen.Bounds.Height})");
            log.WriteLine($"    WorkingArea：{screen.WorkingArea}  ({screen.WorkingArea.Width}x{screen.WorkingArea.Height})");
            log.WriteLine($"    Primary  ：{screen.Primary}");
        }

        log.WriteLine();
        log.WriteLine("【Windows 缩放设置】");
        log.WriteLine("  LogPixels(SM_CXSCREEN)：" + GetSystemMetrics(0));
        log.WriteLine("  Monitor 数量：" + Screen.AllScreens.Length);

        log.WriteLine();
        log.WriteLine("【主窗口实测尺寸】");
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Application.HighDpiMode != HighDpiMode.PerMonitorV2)
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            UiCulture.Install();

            var config = StorageConfig.Resolve();
            using var form = new Ui.MainForm(config);

            _ = form.Handle;
            form.CreateControl();
            Application.DoEvents();

            log.WriteLine($"  构造后 Size       ：{form.Size}");
            log.WriteLine($"  构造后 ClientSize ：{form.ClientSize}");
            log.WriteLine($"  MaximumSize       ：{form.MaximumSize}");
            log.WriteLine($"  MinimumSize       ：{form.MinimumSize}");
            log.WriteLine($"  最终 Bounds       ：{form.Bounds}");

            var split = FindControl<SplitContainer>(form);
            if (split is not null)
            {
                log.WriteLine($"  SplitContainer    ：Size={split.Size} SplitterDistance={split.SplitterDistance} " +
                                $"Panel1MinSize={split.Panel1MinSize} Panel2MinSize={split.Panel2MinSize}");
            }

            var grid = FindControl<DataGridView>(form);
            if (grid is not null)
            {
                log.WriteLine($"  DataGridView      ：Size={grid.Size} 行数={grid.Rows.Count} 列宽合计=" +
                                $"{grid.Columns.Cast<DataGridViewColumn>().Sum(c => c.Width)}");
            }

            foreach (Control child in form.Controls)
                log.WriteLine($"  顶层控件：{child.GetType().Name,-16} Dock={child.Dock,-6} Bounds={child.Bounds}");

            form.Close();
        }
        catch (Exception ex)
        {
            log.WriteLine("  构造主窗口失败：" + ex.GetType().Name + ": " + ex.Message);
            log.WriteLine(ex.StackTrace ?? "(无堆栈)");
        }

        log.WriteLine();
        log.WriteLine("日志文件：" + log.LogPath);
        log.Dispose();
        return 0;
    }

    private static T? FindControl<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) return match;
            var nested = FindControl<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
