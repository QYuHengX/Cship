# -*- coding: utf-8 -*-
"""把"当前设置"同步为默认配置（步骤 06 附加需求 · 一键同步缺键兜底字面量）。

SettingsStore.Defaults（默认表）另行手工维护；本脚本负责把散落在各处的
GetString/GetDouble/GetBool(key, 兜底字面量) 兜底值统一到当前默认值，
避免"默认表改了、代码兜底还是旧值"的双口径。
只做整串精确替换（含 key），不做正则，跑完请用 rg 核对。
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src", "Cship")

REPL = [
    ('GetString("theme.mode", "system")', 'GetString("theme.mode", "light")'),
    ('GetString("advanced.dockPriority", "high")', 'GetString("advanced.dockPriority", "low")'),
    ('GetString("personal.boardAnim", "expand")', 'GetString("personal.boardAnim", "slideEdge")'),
    # 2026-10-06：收纳板材质默认改"无"、悬浮窗底面材质默认改"柔化"（当前取值落为默认）
    ('GetString("personal.material", MaterialEngine.Acrylic)', 'GetString("personal.material", MaterialEngine.None)'),
    ('GetString("personal.material", "acrylic")', 'GetString("personal.material", MaterialEngine.None)'),
    ('GetString("personal.dockCustom.material", MaterialEngine.Acrylic)', 'GetString("personal.dockCustom.material", MaterialEngine.Blur)'),
    ('GetString("personal.dockCustom.material", "acrylic")', 'GetString("personal.dockCustom.material", MaterialEngine.Blur)'),
    ('GetDouble("personal.boardOpacity", 100)', 'GetDouble("personal.boardOpacity", 91)'),
    ('GetDouble("display.dockSize", 56)', 'GetDouble("display.dockSize", 58.7)'),
    ('GetDouble("display.iconSize", 56)', 'GetDouble("display.iconSize", 67.1)'),
    ('GetDouble("display.rowSpacing", 12)', 'GetDouble("display.rowSpacing", 0)'),
    ('GetDouble("display.colSpacing", 12)', 'GetDouble("display.colSpacing", 10.9)'),
    ('GetString("display.labelLines", "one")', 'GetString("display.labelLines", "two")'),
    ('GetBool("advanced.dockFollowTheme", true)', 'GetBool("advanced.dockFollowTheme", false)'),
    ('GetBool("advanced.iconMask", false)', 'GetBool("advanced.iconMask", true)'),
    ('GetDouble("advanced.dockEdgeRange", 25)', 'GetDouble("advanced.dockEdgeRange", 40.243902439024396)'),
    ('GetBool("display.hoverLabels", false)', 'GetBool("display.hoverLabels", true)'),
]


def main():
    changed = 0
    for base, _, files in os.walk(SRC):
        if "obj" in base.split(os.sep) or "bin" in base.split(os.sep):
            continue
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(base, name)
            with open(path, encoding="utf-8") as f:
                text = f.read()
            orig = text
            for old, new in REPL:
                text = text.replace(old, new)
            if text != orig:
                with open(path, "w", encoding="utf-8", newline="") as f:
                    f.write(text)
                changed += 1
                print("updated", os.path.relpath(path, ROOT))
    print("files changed:", changed)


if __name__ == "__main__":
    main()
