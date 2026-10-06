# -*- coding: utf-8 -*-
# 真机自检：开收纳板 → 齿轮开设置窗 → 截"主题"区（四张预览卡的实际观感）
# 只读式操作（不点卡片、不改设置），点卡切换另见 diag_theme_click.py
import os, sys, time, subprocess
_here = os.path.dirname(os.path.abspath(__file__))          # build\_uitest
sys.path.insert(0, os.path.dirname(_here))                   # build\（cuahelper 所在）
BASE = os.path.dirname(os.path.dirname(_here))               # 仓库根
import cuahelper as C

EXE = os.path.join(BASE, r"src\Cship\bin\Debug\net8.0-windows\Cship.exe")
OUT = os.path.join(BASE, "build", "_uitest", "live")
LOG = os.path.join(BASE, r"src\Cship\bin\Debug\net8.0-windows\dependencies\config\logs", "cship_" + time.strftime("%Y%m%d") + ".log")
os.makedirs(OUT, exist_ok=True)
u32 = C.u32

log_before = os.path.getsize(LOG) if os.path.exists(LOG) else 0
C.kill_cship(); time.sleep(1.0)
subprocess.Popen([EXE]); time.sleep(4.0)

# Alt+Tab（强制快捷键）开板
u32.keybd_event(0x12, 0, 0, 0); time.sleep(0.05)
u32.keybd_event(0x09, 0, 0, 0); time.sleep(0.05)
u32.keybd_event(0x09, 0, 2, 0); u32.keybd_event(0x12, 0, 2, 0)
time.sleep(2.5)
b = max([w for w in C.cship_windows() if w["w"] > 800], key=lambda w: w["w"] * w["h"])
print("board:", b)
C.shot("board", box=(b["x"], b["y"], b["x"] + b["w"], b["y"] + b["h"]), out_dir=OUT)

# 齿轮 → 设置窗
C.click(b["x"] + b["w"] - 49, b["y"] + 18); time.sleep(2.0)
st = C.find_window(lambda w: 600 < w["w"] < 700 and 420 < w["h"] < 520)
print("settings:", st)
if st:
    C.shot("settings_full", box=(st["x"], st["y"], st["x"] + st["w"], st["y"] + st["h"]), out_dir=OUT)
    C.shot("settings_theme", box=(st["x"], st["y"], st["x"] + st["w"], st["y"] + 230), out_dir=OUT)
    # 悬停"暗夜"卡（只为看 hover/观感，不点击）
    C.move(st["x"] + 250, st["y"] + 120); time.sleep(0.6)
    C.shot("settings_theme_hover", box=(st["x"], st["y"], st["x"] + st["w"], st["y"] + 230), out_dir=OUT)

with open(LOG, "r", encoding="utf-8", errors="replace") as f:
    f.seek(log_before)
    tail = f.read()
errs = [l for l in tail.splitlines() if "[ERROR]" in l or "[WARN]" in l]
print("--- 日志 ERROR/WARN %d 条 ---" % len(errs))
for l in errs[:10]:
    print("   ", l)
print("done; settings window open, board open")
