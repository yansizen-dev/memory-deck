using System.Runtime.InteropServices;
using System.Text;

namespace MemoryDeck;

/// <summary>
/// WinExe 程序默认没有控制台，stdout 会被丢弃。
/// 这里尝试挂到父进程控制台，并把同一份输出同时写入日志文件，
/// 这样无论是从终端调用还是双击运行，诊断信息都不会丢。
/// </summary>
internal sealed class ConsoleBridge : IDisposable
{
    private const int AttachParentProcess = -1;

    private readonly StringBuilder _log = new();
    private StreamWriter? _file;
    private bool _hasConsole;

    public string? LogPath { get; private set; }

    public static ConsoleBridge Create(string logFileName)
    {
        var bridge = new ConsoleBridge();
        bridge.Attach();
        bridge.OpenLog(logFileName);
        return bridge;
    }

    private void Attach()
    {
        try
        {
            if (GetConsoleWindow() != IntPtr.Zero)
            {
                _hasConsole = true;
            }
            else if (AttachConsole(AttachParentProcess))
            {
                _hasConsole = true;
            }
            else if (AllocConsole())
            {
                _hasConsole = true;
            }
        }
        catch
        {
            _hasConsole = false;
        }
    }

    private void OpenLog(string fileName)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "MemoryDeck");
            Directory.CreateDirectory(dir);
            LogPath = Path.Combine(dir, fileName);
            _file = new StreamWriter(LogPath, append: false, Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            _file = null;
        }
    }

    public void WriteLine(string text = "")
    {
        _log.AppendLine(text);
        _file?.WriteLine(text);

        if (!_hasConsole) return;
        try { Console.WriteLine(text); } catch { }
    }

    public void Dispose()
    {
        if (_file is not null && LogPath is not null)
        {
            try
            {
                File.WriteAllText(LogPath, _log.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // ignore
            }

            _file.Dispose();
            _file = null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
