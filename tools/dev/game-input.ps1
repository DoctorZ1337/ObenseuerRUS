# Отладка перевода в запущенной игре: показать окно и отправить ему клавиши или клики.
#   tools\dev\game-input.ps1 -Restore
#   tools\dev\game-input.ps1 -Keys Escape,F9        (имена из System.Windows.Forms.Keys)
#   tools\dev\game-input.ps1 -Click 1280,720        (координаты окна игры)
#   tools\dev\game-input.ps1 -Move 50,500          (только навести курсор)
#   tools\dev\game-input.ps1 -Turn -300,0          (повернуть камеру: относительное движение мыши)
# Ввод уходит только если окно игры на переднем плане, иначе скрипт прерывается.
# Координаты строкой "x,y": через powershell -File массив int[] не передаётся.
param([switch]$Restore, [string[]]$Keys = @(), [string]$Click = '', [string]$Move = '', [string]$Turn = '', [int]$HoldMs = 60)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class GameInput {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] public static extern void mouse_rel(uint flags, int dx, int dy, uint data, UIntPtr extra);
}
'@
$window = (Get-Process Obenseuer).MainWindowHandle
if ($Restore -or [GameInput]::IsIconic($window)) { [GameInput]::ShowWindow($window, 9) | Out-Null }
[GameInput]::SetForegroundWindow($window) | Out-Null
Start-Sleep -Milliseconds 300
if ([GameInput]::GetForegroundWindow() -ne $window) { throw 'Окно игры не на переднем плане — ввод не отправлен' }
foreach ($name in $Keys) {
    $vk = [byte][System.Windows.Forms.Keys]$name
    $scan = [byte][GameInput]::MapVirtualKey($vk, 0)
    [GameInput]::keybd_event($vk, $scan, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $HoldMs
    [GameInput]::keybd_event($vk, $scan, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}
# Движение — настоящим событием мыши (абсолютные координаты 0..65535): игра не видит SetCursorPos.
function Move-Mouse([int]$x, [int]$y) {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $ax = [uint32]([math]::Round($x * 65535 / ($screen.Width - 1)))
    $ay = [uint32]([math]::Round($y * 65535 / ($screen.Height - 1)))
    [GameInput]::SetCursorPos($x, $y) | Out-Null
    [GameInput]::mouse_event(0x8001, $ax, $ay, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
    $at = [System.Windows.Forms.Cursor]::Position
    "курсор: $($at.X),$($at.Y)"
}
if ($Turn) { $d = [int[]]($Turn -split ','); [GameInput]::mouse_rel(1, $d[0], $d[1], 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200 }
if ($Move) { $m = [int[]]($Move -split ','); Move-Mouse $m[0] $m[1] }
if ($Click) {
    $c = [int[]]($Click -split ','); Move-Mouse $c[0] $c[1]
    [GameInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $HoldMs
    [GameInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
'ok'
