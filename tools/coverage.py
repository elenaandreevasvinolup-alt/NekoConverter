#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Сводка покрытия форматов: сколько доступно сразу, сколько с зависимостями."""
import collections
import json
import os
import re

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')

with open(os.path.join(ROOT, 'src', 'NekoConverter.Core', 'formats.json'), encoding='utf-8') as f:
    raw = f.read()

raw = re.sub(r'^\s*//[^\n]*', '', raw, flags=re.M)
raw = re.sub(r',(\s*[\]}])', r'\1', raw)
data = json.loads(raw)

# Движки, встроенные в приложение (без внешних загрузок).
BUILTIN = {'skia', 'sips', 'pcm', 'afconvert', 'subtitle', 'data', 'builtin', 'assimp'}
# Плюс те, что появляются после установки пакетов-зависимостей.
WITH_PACKAGES = BUILTIN | {'ffmpeg', 'pandoc'}

by_kind = collections.defaultdict(lambda: [0, 0, 0])

for fmt in data['formats']:
    read_engines = fmt.get('readEngines') or [fmt['engine']]
    write_engines = fmt.get('writeEngines') or [fmt['engine']]

    slot = by_kind[fmt['kind']]
    slot[0] += 1

    if (fmt.get('read') and any(e in BUILTIN for e in read_engines)) or \
       (fmt.get('write') and any(e in BUILTIN for e in write_engines)):
        slot[1] += 1

    if (fmt.get('read') and any(e in WITH_PACKAGES for e in read_engines)) or \
       (fmt.get('write') and any(e in WITH_PACKAGES for e in write_engines)):
        slot[2] += 1

print('{:<10}{:>6}{:>9}{:>9}'.format('категория', 'всего', 'в ядре', '+пакеты'))
print('-' * 34)

total = core = full = 0
for kind in sorted(by_kind):
    t, c, f = by_kind[kind]
    total += t
    core += c
    full += f
    print('{:<10}{:>6}{:>9}{:>9}'.format(kind, t, c, f))

print('-' * 34)
print('{:<10}{:>6}{:>9}{:>9}'.format('итого', total, core, full))
