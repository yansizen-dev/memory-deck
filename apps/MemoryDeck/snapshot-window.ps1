# DPI 感知的窗口截图工具（可指定窗口标题，用于截取对话框）
# 非 DPI 感知的进程拿到的窗口坐标会被系统虚拟化（缩小），导致截出来的图被裁切。
# 这里先调用 SetProcessDpiAwarenessContext 声明 PerMonitorV2 感知，再截图。
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Out,
    [string[]]$ArgumentList = @(),
    [string]$WindowTitleLike = "",
    [int]$WaitSeconds = 8,
    [switch]$NoKill,
    [switch]$NoEscape
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class DpiShot2 {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static void EnablePerMonitorV2() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }

    public static IntPtr FindByTitle(uint targetPid, string like) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid) return true;
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            var t = sb.ToString();
            if (t.Length == 0) return true;
            if (like.Length == 0 || t.Contains(like)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

[DpiShot2]::EnablePerMonitorV2()

$proc = Start-Process -FilePath $Exe -ArgumentList $ArgumentList -PassThru
Start-Sleep -Seconds $WaitSeconds
$proc.Refresh()
if ($proc.HasExited) { throw "进程已退出，退出码 $($proc.ExitCode)" }

$hwnd = [DpiShot2]::FindByTitle([uint32]$proc.Id, $WindowTitleLike)
if ($hwnd -eq [IntPtr]::Zero) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "找不到窗口（标题包含：'$WindowTitleLike'）"
}

[DpiShot2]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 1200

# 关掉可能被残留按键带出来的弹窗（无弹窗时 Esc 无副作用）。
# 注意：如果目标本身就是对话框，Esc 会把它关掉 —— 那种情况加 -NoEscape。
if (-not $NoEscape) {
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 600
    [DpiShot2]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 600
}

$rect = New-Object DpiShot2+RECT
[DpiShot2]::GetWindowRect($hwnd, [ref]$rect) | Out-Null

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
Write-Output "窗口物理尺寸: ${width}x${height}  位置: ($($rect.Left),$($rect.Top))"

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Output "已保存: $Out"

if (-not $NoKill) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Wait-Process -Id $proc.Id -Timeout 8 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
    Write-Output "程序已关闭"
}
