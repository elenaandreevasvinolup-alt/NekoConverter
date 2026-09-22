"""生成数学相关的光标资源。

为什么是 SVG 而不是 .cur/.ani：SVG 是源文件，可读可改可缩放；
真正的光标格式各平台不同（Windows 要 .cur/.ani 或 PNG，macOS 要位图，
Avalonia 收 Bitmap）。所以这里给源，由外壳按需要的尺寸栅格化 ——
一份源出所有平台，不会出现"改了 Windows 的光标忘了 macOS"。

统一做法：白色描边打底 + 黑色主体。
教学软件经常要压在浅色课本和深色黑板上，单色光标总有一半场景看不见。

热点写在 manifest.json 里：SVG 本身没有地方声明"哪个像素是指针尖"。
"""

import json
import pathlib

OUT = pathlib.Path("assets/cursors")

# (id, 热点, 中文名, SVG 内容)
CURSORS = [
    ("select", (4, 3), "选择", """
  <path d="M4 3 L4 19.5 L8.4 15.2 L11.3 21 L13.7 19.9 L10.8 14.2 L16.8 14.1 Z"
        fill="#fff" stroke="#000" stroke-width="1.4" stroke-linejoin="round"/>
"""),

    ("point", (12, 12), "取点", """
  <g stroke="#fff" stroke-width="4" stroke-linecap="round">
    <path d="M12 2v6M12 16v6M2 12h6M16 12h6"/>
  </g>
  <g stroke="#000" stroke-width="1.6" stroke-linecap="round">
    <path d="M12 2v6M12 16v6M2 12h6M16 12h6"/>
  </g>
  <circle cx="12" cy="12" r="3.6" fill="#fff" stroke="#000" stroke-width="1.4"/>
  <circle cx="12" cy="12" r="1.5" fill="#000"/>
"""),

    ("segment", (4, 4), "画线段", """
  <path d="M4 4 L20 20" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
  <path d="M4 4 L20 20" stroke="#000" stroke-width="1.9" stroke-linecap="round"/>
  <circle cx="4" cy="4" r="2.8" fill="#fff" stroke="#000" stroke-width="1.4"/>
  <circle cx="20" cy="20" r="2.8" fill="#fff" stroke="#000" stroke-width="1.4"/>
"""),

    ("circle", (12, 3), "画圆", """
  <path d="M12 3 L5.5 20.5" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
  <path d="M12 3 L18.5 20.5" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
  <path d="M12 3 L5.5 20.5" stroke="#000" stroke-width="1.8" stroke-linecap="round"/>
  <path d="M12 3 L18.5 20.5" stroke="#000" stroke-width="1.8" stroke-linecap="round"/>
  <circle cx="12" cy="3" r="3" fill="#fff" stroke="#000" stroke-width="1.4"/>
  <circle cx="12" cy="3" r="1.1" fill="#000"/>
"""),

    ("angle", (4, 20), "标角", """
  <path d="M4 20 L4 5" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
  <path d="M4 20 L20 20" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
  <path d="M4 20 L4 5" stroke="#000" stroke-width="1.8" stroke-linecap="round"/>
  <path d="M4 20 L20 20" stroke="#000" stroke-width="1.8" stroke-linecap="round"/>
  <path d="M4 12 A8 8 0 0 0 12 20" fill="none" stroke="#000" stroke-width="1.4"/>
  <circle cx="4" cy="20" r="1.8" fill="#000"/>
"""),

    ("label", (12, 12), "标注", """
  <text x="12" y="17.5" font-family="Times New Roman, serif" font-size="17"
        font-style="italic" text-anchor="middle"
        fill="none" stroke="#fff" stroke-width="3.6">A</text>
  <text x="12" y="17.5" font-family="Times New Roman, serif" font-size="17"
        font-style="italic" text-anchor="middle" fill="#000">A</text>
"""),

    ("orbit", (12, 12), "转视角", """
  <path d="M19.5 12 A7.5 7.5 0 1 1 14.2 5.1" fill="none"
        stroke="#fff" stroke-width="5" stroke-linecap="round"/>
  <path d="M19.5 12 A7.5 7.5 0 1 1 14.2 5.1" fill="none"
        stroke="#000" stroke-width="1.9" stroke-linecap="round"/>
  <path d="M14.2 1.6 L19 4.4 L15 8 Z" fill="#fff" stroke="#000"
        stroke-width="1.4" stroke-linejoin="round"/>
  <circle cx="12" cy="12" r="1.8" fill="#fff" stroke="#000" stroke-width="1.3"/>
"""),

    ("pan", (12, 12), "平移", """
  <path d="M9 11 V5.5 a1.6 1.6 0 0 1 3.2 0 V10 M12.2 10 V4.4 a1.6 1.6 0 0 1 3.2 0 V10.6
           M15.4 10.6 V6.6 a1.6 1.6 0 0 1 3.2 0 V15 a6 6 0 0 1-6 6 h-1.6
           a5 5 0 0 1-3.9-1.9 L4.6 15 a1.6 1.6 0 0 1 2.5-2 l1.9 2.2"
        fill="#fff" stroke="#000" stroke-width="1.5" stroke-linejoin="round"/>
"""),
]

TEMPLATE = """<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">
{body}</svg>
"""

OUT.mkdir(parents=True, exist_ok=True)

manifest = []
for identifier, hotspot, name, body in CURSORS:
    (OUT / f"{identifier}.svg").write_text(TEMPLATE.format(body=body), encoding="utf-8")
    manifest.append({
        "id": identifier,
        "file": f"{identifier}.svg",
        "hotspot": list(hotspot),
        "name": name,
    })
    print(f"  {identifier:9} 热点 {str(hotspot):10} {name}")

(OUT / "manifest.json").write_text(
    json.dumps({"size": 24, "default": "select", "cursors": manifest},
               ensure_ascii=False, indent=2) + "\n",
    encoding="utf-8",
)

print(f"\n共 {len(CURSORS)} 个光标 → {OUT}")
print("注意：光标只服务鼠标/触控板。希沃一体机是触摸屏，没有光标，")
print("      所以工具的选择与切换必须另有可见的入口（工具栏），不能只靠光标形状。")
