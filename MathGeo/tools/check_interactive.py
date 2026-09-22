"""校验交互式预览的自洽性。

没有浏览器可用，所以这里做的是"能被机器检查的部分"：
  · 脚本块的大括号/圆括号/方括号是否平衡（截断或漏转义会立刻暴露）；
  · 每一道题的帧之间是否真的不同（如果都相同，说明驱动没生效，动画是假的）；
  · 包围盒是否覆盖所有帧（否则擦洗到某一帧图形会跑出画布）；
  · 是否零外部引用。
"""

import json
import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 else "samples/rendered/interactive.html"
html = open(path, encoding="utf-8").read()

failures = []

# ——— 1. 括号平衡 ———
script = re.findall(r"<script>(.*?)</script>", html, re.S)[-1]

def balance(text, opener, closer):
    depth = 0
    in_string = None
    escaped = False
    i = 0
    while i < len(text):
        ch = text[i]
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == in_string:
                in_string = None
        else:
            if ch in "\"'`":
                in_string = ch
            elif text.startswith("//", i):
                i = text.find("\n", i)
                if i < 0:
                    break
                continue
            elif text.startswith("/*", i):
                end = text.find("*/", i)
                if end < 0:
                    break
                i = end + 1
            elif ch == opener:
                depth += 1
            elif ch == closer:
                depth -= 1
                if depth < 0:
                    return False
        i += 1
    return depth == 0

for opener, closer, name in [("{", "}", "花括号"), ("(", ")", "圆括号"), ("[", "]", "方括号")]:
    if not balance(script, opener, closer):
        failures.append(f"{name}不平衡")

# ——— 2. 题目数据 ———
match = re.search(r"const PROBLEMS = (\[.*?\]);\nconst CURSORS", html, re.S)
if not match:
    failures.append("找不到 PROBLEMS 数据")
    problems = []
else:
    try:
        problems = json.loads(match.group(1))
    except json.JSONDecodeError as error:
        failures.append(f"PROBLEMS 不是合法 JSON：{error}")
        problems = []

print(f"题目 {len(problems)} 道\n")

for problem in problems:
    frames = problem["frames"]
    kind = problem["kind"]
    name = problem["file"]

    if len(frames) < 2:
        print(f"  {name:34} {kind:9} 静态 1 帧")
        continue

    # 帧之间必须真的不同
    signatures = {json.dumps(frame, sort_keys=True) for frame in frames}
    if len(signatures) < len(frames) // 2:
        failures.append(f"{name}: 帧之间差异过小（{len(signatures)}/{len(frames)} 个不同）")

    # 包围盒必须覆盖每一帧
    min_x, min_y, width, height = problem["view"]
    max_x, max_y = min_x + width, min_y + height

    for index, frame in enumerate(frames):
        for shape in frame:
            points = []
            if shape["t"] == "l":
                points = [shape["a"], shape["b"]]
            elif shape["t"] == "p":
                points = shape["p"]
            elif shape["t"] in ("o", "x"):
                points = [shape["a"]]
            elif shape["t"] == "c":
                points = [shape["c"]]
            for x, y in points:
                if not (min_x <= x <= max_x and min_y <= y <= max_y):
                    failures.append(f"{name}: 第 {index} 帧有点落在包围盒外 ({x:.2f}, {y:.2f})")
                    break

    print(f"  {name:34} {kind:9} {len(frames):2} 帧  不同签名 {len(signatures):2}")

# ——— 3. 语言 ———
locales = re.search(r"const LOCALES = (\{.*?\});\nconst PALETTE", html, re.S)
if not locales:
    failures.append("找不到 LOCALES 数据")
else:
    try:
        table = json.loads(locales.group(1))
        rtl = [k for k, v in table.items() if v.get("_rtl")]
        unreviewed = [k for k, v in table.items() if v.get("_reviewed") is False]
        print(f"\n语言 {len(table)} 种")
        print(f"  RTL: {', '.join(sorted(rtl)) or '无'}")
        print(f"  待校对: {len(unreviewed)} 种")
        if len(table) != 16:
            failures.append(f"语言数应为 16，实际 {len(table)}")
    except json.JSONDecodeError as error:
        failures.append(f"LOCALES 不是合法 JSON：{error}")

# ——— 4. 外部引用 ———
external = {
    url for url in re.findall(r"https?://[^\"'\s)]+", html)
    if not url.startswith("http://www.w3.org/")
}
if external:
    failures.append(f"存在外部引用：{external}")

# ——— 5. 光标 ———
if "data:image/svg+xml" not in html:
    failures.append("没有内联光标")

print()
if failures:
    print("发现问题：")
    for failure in failures:
        print(f"  ✗ {failure}")
    sys.exit(1)

print(f"全部通过（{len(html) // 1024} KB，零外部引用）")
