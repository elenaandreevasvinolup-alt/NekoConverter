#!/usr/bin/env bash
#
# 打包 MathGeo 的 macOS 分发包。
#
# 与 NekoConverter 的 package-macos.sh 保持同一套做法：优先 Native AOT，
# 产出不依赖 .NET 运行时的单文件可执行程序。
#
# 为什么不是 .app 包：MathGeo 现在只有内核 + 命令行，没有窗口，
# 做成 .app 会得到一个双击没反应的图标。所以这里给的是一个可执行文件
# 加一份单文件 HTML 预览 —— 后者才是"看效果"的入口，双击就能看。
#
# 为什么有回退：Native AOT 要走 ILLink，而 ILLink 会启动 MSBuild 的进程外
# task host。在受限环境（容器、沙箱）里那个进程起不来，报 MSB4216。
# 这时自动回退到自包含单文件 —— 体积大约 39MB，比 AOT 大，但功能完全一样。
#
# 用法：
#   scripts/package-macos.sh                    # 自动（AOT 优先）
#   PUBLISH_MODE=selfcontained scripts/package-macos.sh   # 强制自包含
#   RID=osx-arm64 scripts/package-macos.sh      # Apple 芯片
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RID="${RID:-osx-x64}"
PUBLISH_MODE="${PUBLISH_MODE:-auto}"
PUBLISH_DIR="dist/$RID-publish"
OUT_DIR="dist/MathGeo-macos"
ZIP_PATH="dist/MathGeo-macos-$RID.zip"

publish_aot() {
  echo "==> 发布（Native AOT，$RID）"
  dotnet publish src/MathGeo.Cli/MathGeo.Cli.csproj \
    -c Release -r "$RID" \
    -p:PublishAot=true -p:StripSymbols=true \
    -o "$PUBLISH_DIR"
}

publish_selfcontained() {
  echo "==> 发布（自包含单文件，$RID）"
  dotnet publish src/MathGeo.Cli/MathGeo.Cli.csproj \
    -c Release -r "$RID" \
    -p:PublishSingleFile=true -p:SelfContained=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$PUBLISH_DIR"
}

rm -rf "$PUBLISH_DIR"

case "$PUBLISH_MODE" in
  aot)
    publish_aot
    ;;
  selfcontained)
    publish_selfcontained
    ;;
  *)
    if ! publish_aot; then
      echo
      echo "    AOT 失败（多半是受限环境里 ILLink 起不来），回退到自包含单文件。"
      echo "    在你自己的终端里重跑通常就能出 AOT 版，体积会小很多。"
      echo
      rm -rf "$PUBLISH_DIR"
      publish_selfcontained
    fi
    ;;
esac

echo
echo "==> 组装分发包"
rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR/samples" "$OUT_DIR/cursors"

cp "$PUBLISH_DIR/mathgeo" "$OUT_DIR/mathgeo"
cp samples/*.problem.json "$OUT_DIR/samples/"

# 光标源文件随包发出：外壳按需要的尺寸栅格化，各平台共用一份源。
cp assets/cursors/*.svg assets/cursors/manifest.json "$OUT_DIR/cursors/"

# 语言文件在发布输出里已经有一份（Core 的 csproj 会复制到 Locales/）。
# 它们必须落在可执行文件旁边 —— 译者改完 JSON 立刻生效，不需要重新编译。
if [ -d "$PUBLISH_DIR/Locales" ]; then
  cp -R "$PUBLISH_DIR/Locales" "$OUT_DIR/Locales"
  echo "    语言：$(ls "$OUT_DIR/Locales" | wc -l | tr -d ' ') 种（可直接改，改完不用重编译）"
fi

# 题目文件是数据不是代码：老师可以直接用文本编辑器改坐标、改条件，
# 改完重新生成预览就能看到变化，不需要重新编译任何东西。
echo "    题目：$(ls "$OUT_DIR/samples" | wc -l | tr -d ' ') 道"
echo "    光标：$(ls "$OUT_DIR/cursors"/*.svg | wc -l | tr -d ' ') 个"

echo
echo "==> 生成预览"
"$OUT_DIR/mathgeo" gallery "$OUT_DIR/samples" -o "$OUT_DIR/preview.html"
"$OUT_DIR/mathgeo" interactive "$OUT_DIR/samples" -o "$OUT_DIR/interactive.html"

echo
echo "==> 压缩（便于通过微信/网盘分发）"
rm -f "$ZIP_PATH"
(cd dist && zip -qr "$(basename "$ZIP_PATH")" "$(basename "$OUT_DIR")")

echo
echo "==> 完成"
du -sh "$OUT_DIR" "$ZIP_PATH" | sort -rh
echo
echo "    交互演示：  open \"$OUT_DIR/interactive.html\"    ← 用手指在图上左右拖"
echo "    静态样张：  open \"$OUT_DIR/preview.html\""
echo "    渲染：      \"$OUT_DIR/mathgeo\" render \"$OUT_DIR/samples/05-cube.problem.json\" -o /tmp/cube.svg"
echo "    检查：      \"$OUT_DIR/mathgeo\" inspect \"$OUT_DIR/samples/06-hexagon-section.problem.json\""
