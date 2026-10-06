# 临时验收夹具运行脚本（不入交付）：编译主产物 → 编译夹具 → 组装 run\ → 执行
# 用法： powershell -NoProfile -ExecutionPolicy Bypass -File build\_uitest\run.ps1 [config|render|click|dock|dockbind|pers|all]
# ⚠ 2026-10-06 踩坑：夹具项目对 Cship.dll 的 CopyLocal **不会**因主产物重建而刷新（obj 缓存），
#   曾因此跑出"旧代码"的假失败。故组装阶段强制用 _verify_fix 的新产物覆盖 run\ 里的副本。
param([string]$Mode = "all")
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # 仓库根
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$verify = Join-Path $root "build\_verify_fix"
$proj = Join-Path $root "build\_uitest"
$run = Join-Path $proj "run"

Write-Host "== 1/4 主产物（出到 build\_verify_fix，不动正在运行的程序） =="
& $dotnet build (Join-Path $root "src\Cship\Cship.csproj") -c Debug -o $verify -v q --nologo

Write-Host "== 2/4 夹具 =="
& $dotnet build (Join-Path $proj "_uitest.csproj") -c Debug -v q --nologo

Write-Host "== 3/4 组装 run\ =="
if (Test-Path $run) { Remove-Item $run -Recurse -Force }
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item (Join-Path $proj "bin\Debug\*") $run -Recurse -Force
Copy-Item (Join-Path $verify "Cship.dll") $run -Force          # 强制用刚构建的产物（见文件头踩坑）
New-Item -ItemType Directory -Path (Join-Path $run "dependencies") -Force | Out-Null
Copy-Item (Join-Path $verify "dependencies\resources") (Join-Path $run "dependencies") -Recurse -Force

Write-Host "== 4/4 执行（mode=$Mode） =="
& (Join-Path $run "cshipcheck.exe") $Mode
exit $LASTEXITCODE
