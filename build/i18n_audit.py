#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
i18n 文案全量自检（步骤 07 §7 要求"扫描全部 Tr key，两个语言文件无缺漏"）。

检查三件事：
  1. zh-CN.json / en-US.json 键集合**完全对称**（缺一个都会在另一语言下露出 key 名）。
  2. 代码里 `I18n.Tr("字面量")` 引用的键**都必须存在**（否则界面显示 key 名本身）。
  3. 反向：两个语言文件里的键**是否在源码中出现过**——从不出现在任何源码里的键是
     "可能已无引用"的候选（动态拼接的键会误报，需人工确认后删）。

用法：python build/i18n_audit.py     （退出码 0=通过，1=有问题）
"""
import json, re, glob, os, sys

base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
I18N = os.path.join(base, 'src/Cship/dependencies/resources/i18n')


def load(lang):
    with open(os.path.join(I18N, lang + '.json'), encoding='utf-8') as f:
        return json.load(f)


def source_files():
    out = []
    for pat in ('src/Cship/**/*.cs', 'src/Cship/**/*.xaml'):
        for f in glob.glob(os.path.join(base, pat), recursive=True):
            if os.sep + 'obj' + os.sep in f or os.sep + 'bin' + os.sep in f:
                continue
            out.append(f)
    return out


def main():
    zh, en = load('zh-CN'), load('en-US')
    problems = []

    print('== 1. 键集合对称 ==')
    print('   zh-CN = %d 键   en-US = %d 键' % (len(zh), len(en)))
    only_zh = sorted(set(zh) - set(en))
    only_en = sorted(set(en) - set(zh))
    if only_zh:
        problems.append('en-US 缺失 %d 键: %s' % (len(only_zh), only_zh))
    if only_en:
        problems.append('zh-CN 缺失 %d 键: %s' % (len(only_en), only_en))
    print('   zh 独有 %d，en 独有 %d' % (len(only_zh), len(only_en)))

    files = source_files()
    texts = {f: open(f, encoding='utf-8').read() for f in files}
    blob = '\n'.join(texts.values())

    print('== 2. 代码引用 I18n.Tr("...") 的键必须存在 ==')
    used = set()
    for f, s in texts.items():
        for m in re.finditer(r'I18n\.Tr\(\s*"([^"]+)"', s):
            used.add(m.group(1))
    # 动态拼接（如 "cat." + id）会命中截断的 "cat."：过滤掉非完整键
    used = {k for k in used if k in zh or k in en or not k.endswith('.')}
    missing = sorted(k for k in used if k not in zh or k not in en)
    print('   字面量引用 %d 个键；缺失 %d 个' % (len(used), len(missing)))
    for k in missing:
        print('     MISSING: %r' % k)
    if missing:
        problems.append('代码引用了不存在的键: %s' % missing)

    print('== 3. 语言文件里"从未在源码出现"的键（可能已无引用）==')
    unused = sorted(k for k in zh if k not in blob)
    print('   候选 %d 个' % len(unused))
    for k in unused:
        print('     %-28s = %s' % (k, zh[k][:34]))

    print()
    if problems:
        print('== 结果：FAIL ==')
        for p in problems:
            print('  - ' + p)
        return 1
    print('== 结果：PASS（键集合对称，代码引用全部存在；候选键见上，需人工确认）==')
    return 0


if __name__ == '__main__':
    sys.exit(main())
