#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Генератор тестовых файлов для новых категорий NekoConverter:
звук (WAV), субтитры (SRT/ASS), данные (CSV/JSON/XML).
Нужен только для проверки — в поставку приложения не входит.
"""
import math
import os
import struct

OUT = os.path.dirname(os.path.abspath(__file__))


def make_wav(path, seconds=1.0, rate=44100, channels=2, freq=440.0):
    """WAV, 16 бит PCM: синус, разные фазы по каналам — чтобы каналы различались."""
    frames = int(seconds * rate)
    payload = bytearray()
    for i in range(frames):
        for ch in range(channels):
            value = math.sin(2 * math.pi * freq * (i / rate) + ch * 0.5)
            payload += struct.pack('<h', int(value * 20000))

    byte_rate = rate * channels * 2
    header = b'RIFF' + struct.pack('<I', 36 + len(payload)) + b'WAVE'
    header += b'fmt ' + struct.pack('<IHHIIHH', 16, 1, channels, rate, byte_rate, channels * 2, 16)
    header += b'data' + struct.pack('<I', len(payload))

    with open(path, 'wb') as f:
        f.write(header + bytes(payload))
    return len(header) + len(payload)


SRT = """1
00:00:01,000 --> 00:00:04,500
第一句字幕，用来验证中文。
Проверка кириллицы.

2
00:00:05,000 --> 00:00:08,000
第二句，带一个逗号, 和引号"测试"。

3
00:00:09,250 --> 00:00:12,750
第三句，换行测试
这是第二行。
"""

ASS = """[Script Info]
ScriptType: v4.00+
PlayResX: 1920
PlayResY: 1080

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:01.00,0:00:04.50,Default,,0,0,0,,第一句字幕，用来验证中文。
Dialogue: 0,0:00:05.00,0:00:08.00,Default,,0,0,0,,第二句，带一个逗号, 和样式标记\\N第二行
Dialogue: 0,0:00:09.25,0:00:12.75,Default,,0,0,0,,第三句。
"""

CSV = """name,age,note,city
张三,28,"含逗号的备注, 用来测试引号转义",北京
李四,35,普通备注,上海
Ольга,41,"Кавычки ""внутри"" поля",Москва
"""

JSON_DATA = """[
  { "name": "张三", "age": 28, "city": "北京" },
  { "name": "李四", "age": 35, "city": "上海" },
  { "name": "Ольга", "age": 41, "city": "Москва" }
]
"""

XML_DATA = """<?xml version="1.0" encoding="utf-8"?>
<table>
  <row><name>张三</name><age>28</age><city>北京</city></row>
  <row><name>李四</name><age>35</age><city>上海</city></row>
  <row><name>Ольга</name><age>41</age><city>Москва</city></row>
</table>
"""


def write(path, text):
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    return os.path.getsize(path)


def main():
    size = make_wav(os.path.join(OUT, 'sample.wav'))
    print(f'WAV  : sample.wav ({size} байт, 44100 Гц, 2 канала, 16 бит)')

    for name, text in [('sample.srt', SRT), ('sample.ass', ASS),
                       ('sample.csv', CSV), ('sample.json', JSON_DATA),
                       ('sample-xml.xml', XML_DATA)]:
        size = write(os.path.join(OUT, name), text)
        print(f'     : {name} ({size} байт)')


if __name__ == '__main__':
    main()
