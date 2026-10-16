# DPI 感知的窗口截图工具
# 非 DPI 感知的进程拿到的窗口坐标会被系统虚拟化（缩小），导致截出来的图被裁切。
# 这里先调用 SetProcessDpiAwarenessContext 声明 PerMonitorV2 感知，再截图。
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$WaitSeconds = 8
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class DpiShot {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
    public static void EnablePerMonitorV2() {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
    }
}
'@

[DpiShot]::EnablePerMonitorV2()

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WaitSeconds
$proc.Refresh()
if ($proc.HasExited) { throw "进程已退出，退出码 $($proc.ExitCode)" }

[DpiShot]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 1500

# 关掉可能被残留按键带出来的弹窗（无弹窗时 Esc 无副作用）
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 700
[DpiShot]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 600

$rect = New-Object DpiShot+RECT
[DpiShot]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null

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

Stop-Process -Id $proc.Id -Force
Start-Sleep -Milliseconds 800
Write-Output "程序已关闭"
