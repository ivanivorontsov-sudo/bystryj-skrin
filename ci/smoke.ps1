# Дымовой тест на настоящем Windows: запускает exe, делает скриншоты и проверяет PNG.
param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path $Exe).Path
$root = Join-Path ($env:RUNNER_TEMP ?? $env:TEMP) ("smoke-" + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$shots = Join-Path $root 'ui-shots'      # снимки интерфейса для просмотра (артефакт)
New-Item -ItemType Directory -Force $root, $shots | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class In {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr e);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  public static void Keys(params byte[] vks) {
    foreach (var k in vks) keybd_event(k, 0, 0, UIntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    for (int i = vks.Length - 1; i >= 0; i--) keybd_event(vks[i], 0, 2, UIntPtr.Zero);
  }
  public static void Down() { mouse_event(2, 0, 0, 0, UIntPtr.Zero); }
  public static void Up() { mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}
"@
$failures = New-Object System.Collections.Generic.List[string]
function Check($cond, $msg) { if ($cond) { Write-Host "  OK  $msg" } else { Write-Host "  FAIL $msg" -ForegroundColor Red; $failures.Add($msg) } }
function ImgSize($path) { $i = [System.Drawing.Image]::FromFile($path); try { return "$($i.Width)x$($i.Height)" } finally { $i.Dispose() } }
$namePattern = '^Скриншот \d{4}-\d\d-\d\d \d\d-\d\d-\d\d( \(\d+\))?\.(png|jpg)$'
function UiShot($label) {
  $w = [In]::GetSystemMetrics(78); $h = [In]::GetSystemMetrics(79)
  $p = Start-Process $exe -ArgumentList @('--capture-rect', "0,0,$w,$h", '--out', "`"$shots`"", '--config-dir', "`"$root\cfg-ui`"") -Wait -PassThru
  $f = Get-ChildItem $shots -Filter *.png | Sort-Object LastWriteTime | Select-Object -Last 1
  if ($f) { Rename-Item $f.FullName ("$label.png") -ErrorAction SilentlyContinue }
}
Write-Host "Экран: $([In]::GetSystemMetrics(78))x$([In]::GetSystemMetrics(79))"

Write-Host "1) --capture-rect (PNG)"
$out1 = Join-Path $root 'headless'
$p = Start-Process $exe -ArgumentList @('--capture-rect', '10,20,300,200', '--out', "`"$out1`"", '--config-dir', "`"$root\cfg1`"") -Wait -PassThru
Check ($p.ExitCode -eq 0) "код выхода 0 (получен $($p.ExitCode))"
$f = @(Get-ChildItem $out1 -Filter *.png -ErrorAction SilentlyContinue)
Check ($f.Count -eq 1) "создан ровно один PNG"
if ($f.Count -ge 1) {
  Check ($f[0].Name -match $namePattern) "имя файла: $($f[0].Name)"
  Check ((ImgSize $f[0].FullName) -eq '300x200') "размер 300x200 (получен $(ImgSize $f[0].FullName))"
}
$p = Start-Process $exe -ArgumentList @('--capture-rect', '10,20,300,200', '--out', "`"$out1`"", '--config-dir', "`"$root\cfg1`"") -Wait -PassThru
Check ((@(Get-ChildItem $out1 -Filter *.png).Count) -eq 2) "второй снимок не перезаписал первый"

Write-Host "2) --capture-rect (JPG)"
$p = Start-Process $exe -ArgumentList @('--capture-rect', '0,0,64,48', '--out', "`"$root\jpg`"", '--format', 'jpg', '--config-dir', "`"$root\cfg1`"") -Wait -PassThru
$j = @(Get-ChildItem "$root\jpg" -Filter *.jpg -ErrorAction SilentlyContinue)
Check ($p.ExitCode -eq 0 -and $j.Count -eq 1) "JPG создан"

Write-Host "3) недоступная папка → запасная, снимок не теряется"
$blocker = Join-Path $root 'not-a-folder'; Set-Content $blocker 'x'
$marker = Join-Path $root 'marker3.txt'
$p = Start-Process $exe -ArgumentList @('--capture-rect', '0,0,50,50', '--out', "`"$blocker\sub`"", '--saved-marker', "`"$marker`"", '--config-dir', "`"$root\cfg1`"") -Wait -PassThru
Check ($p.ExitCode -eq 5) "код 5 = сохранено в запасную папку (получен $($p.ExitCode))"
if (Test-Path $marker) { $fb = Get-Content $marker -Raw; Check (Test-Path $fb) "файл в запасной папке: $fb"; Remove-Item $fb -ErrorAction SilentlyContinue } else { Check $false "маркер запасного сохранения" }

Write-Host "4) неверные аргументы"
$p = Start-Process $exe -ArgumentList @('--capture-rect', 'abc') -Wait -PassThru
Check ($p.ExitCode -eq 2) "неверный прямоугольник → код 2"

Write-Host "5) трей + горячая клавиша + выделение мышью"
$cfg2 = Join-Path $root 'cfg2'; $out2 = Join-Path $root 'tray'
New-Item -ItemType Directory -Force $cfg2 | Out-Null
[IO.File]::WriteAllText((Join-Path $cfg2 'settings.ini'), "SaveFolder=$out2`nShowNotification=true`nCopyToClipboard=true`n", [Text.UTF8Encoding]::new($false))
$marker2 = Join-Path $root 'marker5.txt'
$app = Start-Process $exe -ArgumentList @('--config-dir', "`"$cfg2`"", '--saved-marker', "`"$marker2`"") -PassThru
Start-Sleep -Seconds 4
Check (-not $app.HasExited) "программа работает в трее"

# случайный щелчок (ошибка оператора) — не должен ничего сохранять
[In]::Keys(0x11, 0x10, 0x53)   # Ctrl+Shift+S
Start-Sleep -Milliseconds 1500
[In]::Keys(0x11, 0x10, 0x53)   # повторное нажатие при открытом выделении — игнорируется
Start-Sleep -Milliseconds 500
[In]::SetCursorPos(200, 200); [In]::Down(); Start-Sleep -Milliseconds 80; [In]::Up()
Start-Sleep -Milliseconds 800
UiShot '01-overlay-after-tiny-click'
Check (-not (Test-Path $out2) -or @(Get-ChildItem $out2).Count -eq 0) "щелчок без выделения ничего не сохранил"

# настоящее выделение
[In]::SetCursorPos(100, 100); [In]::Down()
for ($i = 1; $i -le 10; $i++) { [In]::SetCursorPos(100 + 30 * $i, 100 + 20 * $i); Start-Sleep -Milliseconds 40 }
Start-Sleep -Milliseconds 300
UiShot '02-overlay-dragging'
[In]::Up()
Start-Sleep -Seconds 2
UiShot '03-toast-after-save'
$saved = @(Get-ChildItem $out2 -Filter *.png -ErrorAction SilentlyContinue)
Check ($saved.Count -eq 1) "через выделение мышью сохранён ровно 1 PNG (найдено $($saved.Count))"
if ($saved.Count -ge 1) {
  Check ($saved[0].Name -match $namePattern) "имя: $($saved[0].Name)"
  Check ((ImgSize $saved[0].FullName) -eq '301x201') "размер выделения 301x201 (получен $(ImgSize $saved[0].FullName))"
  Copy-Item $saved[0].FullName (Join-Path $shots '04-saved-region.png')
}

# Esc отменяет
[In]::Keys(0x11, 0x10, 0x53); Start-Sleep -Milliseconds 1200
[In]::Keys(0x1B); Start-Sleep -Milliseconds 800
[In]::SetCursorPos(300, 300); [In]::Down(); [In]::SetCursorPos(500, 500); [In]::Up(); Start-Sleep -Milliseconds 800
Check (@(Get-ChildItem $out2 -Filter *.png).Count -eq 1) "Esc отменил выделение (новых файлов нет)"

# правая кнопка отменяет
[In]::Keys(0x11, 0x10, 0x53); Start-Sleep -Milliseconds 1200
[In]::SetCursorPos(300, 300); [In]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [In]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 800
[In]::SetCursorPos(300, 300); [In]::Down(); [In]::SetCursorPos(500, 500); [In]::Up(); Start-Sleep -Milliseconds 800
Check (@(Get-ChildItem $out2 -Filter *.png).Count -eq 1) "правая кнопка отменила выделение"

# Ctrl+Shift+Z — отмена последнего скриншота (в корзину)
[In]::Keys(0x11, 0x10, 0x5A); Start-Sleep -Seconds 2
Check (@(Get-ChildItem $out2 -Filter *.png -ErrorAction SilentlyContinue).Count -eq 0) "Ctrl+Shift+Z убрал последний скриншот в корзину"
UiShot '05-toast-after-undo'

Check (-not $app.HasExited) "программа не упала"
$p = Start-Process $exe -ArgumentList @('--quit') -Wait -PassThru
$app.WaitForExit(10000) | Out-Null
Check ($app.HasExited) "--quit закрыл программу"

Write-Host "6) окно настроек"
$app = Start-Process $exe -ArgumentList @('--config-dir', "`"$cfg2`"", '--settings') -PassThru
Start-Sleep -Seconds 4
UiShot '06-settings'
[In]::Keys(0x1B); Start-Sleep -Milliseconds 800   # Esc закрывает окно настроек
Check (-not $app.HasExited) "после закрытия настроек программа работает"
Start-Process $exe -ArgumentList @('--quit') -Wait | Out-Null
$app.WaitForExit(10000) | Out-Null
Check ($app.HasExited) "--quit закрыл программу (2)"

$log = Join-Path $cfg2 'log.txt'
if (Test-Path $log) { Write-Host "---- журнал ----"; Get-Content $log | Write-Host }
Copy-Item $shots -Destination (Join-Path ($env:GITHUB_WORKSPACE ?? '.') 'ui-shots') -Recurse -Force

if ($failures.Count -gt 0) { Write-Host "ПРОВАЛЕНО: $($failures.Count)" -ForegroundColor Red; $failures | ForEach-Object { Write-Host " - $_" }; exit 1 }
Write-Host "Все проверки пройдены" -ForegroundColor Green
