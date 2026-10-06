# 08 步移植性测试：中文+空格路径 / 跨分区 / 改名目录 / 二次运行 config 不重置
# 用法: powershell -NoProfile -File build\_step8_port.ps1
$ErrorActionPreference = 'Stop'
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'

$targets = @(
    @{ Label = '中文+空格'; Dir = 'C:\Temp\Test 1\测试' },
    @{ Label = '跨分区';    Dir = 'D:\PortTest' },
    @{ Label = '改名目录';  Dir = 'D:\PortTest\我的收纳工具' }
)

function Test-One {
    param([string]$Label, [string]$Dir, [switch]$SecondRun)
    $exe = Join-Path $Dir 'Cship.exe'
    $cfg = Join-Path $Dir 'dependencies\config'
    $settings = Join-Path $cfg 'settings.json'
    $log = Join-Path $cfg ("logs\cship_{0}.log" -f (Get-Date -Format 'yyyyMMdd'))

    if (-not (Test-Path -LiteralPath $exe)) { Write-Host "  缺 exe：$exe" -ForegroundColor Red; return }
    Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700

    $beforeCreate = if (Test-Path -LiteralPath $settings) { (Get-Item -LiteralPath $settings).CreationTimeUtc } else { $null }
    $beforeLog = 0
    if (Test-Path -LiteralPath $log) {
        try { $beforeLog = @(Get-Content -LiteralPath $log -Encoding UTF8).Count } catch { $beforeLog = 0 }
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $exe -PassThru
    $vis = -1
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if ([int](& $pwsh -NoProfile -File $probe win $p.Id) -gt 0) { $vis = $sw.ElapsedMilliseconds; break }
        Start-Sleep -Milliseconds 25
    }
    $sw.Stop()
    Start-Sleep -Seconds 2

    $alive = $null -ne (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)
    $cfgOk = Test-Path -LiteralPath $settings
    $afterCreate = if (Test-Path -LiteralPath $settings) { (Get-Item -LiteralPath $settings).CreationTimeUtc } else { $null }
    $newLines = 0; $warns = 0; $errs = 0; $logNote = ''
    if (Test-Path -LiteralPath $log) {
        try {
            $lines = @(Get-Content -LiteralPath $log -Encoding UTF8)
            $newLines = $lines.Count - $beforeLog
            $errs = @($lines | Where-Object { $_ -match '\[ERROR\]' }).Count
            $warns = @($lines | Where-Object { $_ -match '\[WARN\]' }).Count
        } catch {
            $logNote = '（日志不可读：' + $_.Exception.GetType().Name + '）'
        }
    }
    $kept = ($null -ne $beforeCreate -and $null -ne $afterCreate -and $beforeCreate -eq $afterCreate)
    Write-Host ("  [{0}] {1}" -f $Label, $Dir)
    Write-Host ("      启动→悬浮窗 {0} ms | 存活={1} | settings 存在={2} | 日志新增 {3} 行 | WARN={4} ERROR={5} {6}" -f `
            $vis, $alive, $cfgOk, $newLines, $warns, $errs, $logNote)
    if ($SecondRun) {
        Write-Host ("      二次运行：settings.json 创建时间不变={0}（True 即沿用原 config，未被重置）" -f $kept)
    }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
    return $vis
}

Write-Host '===== 移植性测试 =====' -ForegroundColor Cyan
foreach ($t in $targets) {
    Write-Host ("--- {0} ---" -f $t.Label) -ForegroundColor Yellow
    # 首启：先清 config，验证"结构在各自路径自建"
    $cfg = Join-Path $t.Dir 'dependencies\config'
    if (Test-Path -LiteralPath $cfg) { Remove-Item -LiteralPath $cfg -Recurse -Force }
    Test-One -Label '首启' -Dir $t.Dir | Out-Null
    Test-One -Label '二次' -Dir $t.Dir -SecondRun | Out-Null
}

Write-Host ''
Write-Host '===== 只读/受限目录降级 =====' -ForegroundColor Cyan
$roDir = 'D:\PortTest\rotest'
# icacls 的账户必须带机器名前缀：只写 "YuHeng:" 会被解析成机器名 "YUHENG\"（空账户），deny 静默失效
$who = "$env:COMPUTERNAME\$env:USERNAME"
if (Test-Path -LiteralPath $roDir) {
    & "$env:SystemRoot\System32\icacls.exe" $roDir /remove:d $who /T 2>&1 | Out-Null
    Remove-Item -LiteralPath $roDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $roDir | Out-Null
Copy-Item -Path 'D:\PortTest\Cship.exe' -Destination $roDir -Force
Copy-Item -Path 'D:\PortTest\dependencies' -Destination $roDir -Recurse -Force
# 拒绝当前用户写入 config（模拟只读介质 / 受限权限目录下的"写 config·写日志失败"）
# 注意：若对整个目录 deny (W)，Windows 会直接拒绝从该目录启动进程（Start-Process 拒绝访问）——
#       那是 OS 行为而非程序缺陷，故这里只收紧 config 子树，专测"写入失败不崩溃"。
$cfgRo = Join-Path $roDir 'dependencies\config'
New-Item -ItemType Directory -Force -Path $cfgRo | Out-Null
& "$env:SystemRoot\System32\icacls.exe" $cfgRo /deny "$($who):(OI)(CI)(W)" 2>&1 | Out-Null
Write-Host ("  已对 {0} 施加写拒绝（icacls deny W）" -f $cfgRo)
Test-One -Label '只读降级' -Dir $roDir | Out-Null
Write-Host '  预期：不崩溃（进程存活），config 写不进去时不抛未捕获异常'
& "$env:SystemRoot\System32\icacls.exe" $cfgRo /remove:d $who 2>&1 | Out-Null
Write-Host '  已移除写拒绝'
