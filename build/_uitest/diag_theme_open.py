# -*- coding: utf-8 -*-
# 接 diag_theme_cards.py：板已开着，点齿轮开设置窗 → 截"主题"区 → 点"暗夜"卡验证选中态搬家 → 点回"白皙"
import os, sys, time
_here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_here))
BASE = os.path.dirname(os.path.dirname(_here))
import cuahelper as C

OUT = os.path.join(BASE, "build", "_uitest", "live")
os.makedirs(OUT, exist_ok=True)

b = max([w for w in C.cship_windows() if w["w"] > 800], key=lambda w: w["w"] * w["h"])
print("board:", b["x"], b["y"], b["w"], b["h"])
C.click(b["x"] + b["w"] - 33, b["y"] + 18); time.sleep(2.2)
for w in C.cship_windows():
    print("  win:", w["w"], w["h"], w["x"], w["y"], w["title"])
st = C.find_window(lambda w: 550 < w["w"] < 760 and 380 < w["h"] < 560)
print("settings:", st)
if st:
    C.shot("settings_full", box=(st["x"], st["y"], st["x"] + st["w"], st["y"] + st["h"]), out_dir=OUT)
    C.shot("settings_theme", box=(st["x"], st["y"], st["x"] + st["w"], st["y"] + 220), out_dir=OUT)
print("done")
