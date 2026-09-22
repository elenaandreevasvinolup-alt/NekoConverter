#!/usr/bin/env bash
#
# 打包 MathGeo 的 macOS 应用（MathGeo.app）。
#
# 和 CLI 的 package-macos.sh 的区别：那个打的是"内核 + 命令行"，给开发者和 agent 用；
# 这个打的是**有窗口的应用**，双击就能用，给老师用。
#
# 为什么现在才能做 .app：之前 MathGeo 只有控制台程序，包成 .app 会得到一个
# 双击弹终端、打印用法、退出的东西。有了 Avalonia 外壳之后，.app 才有意义。
#
# 体积上做了两件事：
#   1. Native AOT 优先 —— 不需要带 .NET 运行时，体积能砍掉一半以上。
#      受限环境里 ILLink 的进程外 task host 起不来时会自动回退到自包含。
#   2. lipo 瘦身 —— SkiaSharp / HarfBuzzSharp 在 NuGet 里是 x86_64 + arm64
#      的 fat 二进制，一半的体积是另一套架构的代码。按 RID 切掉。
#
# 用法：
#   scripts/package-app-macos.sh                        # osx-x64（Intel），AOT 优先
#   PUBLISH_MODE=selfcontained scripts/package-app-macos.sh
#   RID=osx-arm64 scripts/package-app-macos.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RID="${RID:-osx-x64}"
VERSION="${VERSION:-0.1.0}"
PUBLISH_MODE="${PUBLISH_MODE:-auto}"

PUBLISH_DIR="dist/app-$RID"
APP_DIR="dist/MathGeo.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"
RESOURCES_DIR="$APP_DIR/Contents/Resources"

# lipo 用的架构名和 RID 不一样。
case "$RID" in
  osx-x64)   ARCH="x86_64" ;;
  osx-arm64) ARCH="arm64" ;;
  *)         ARCH="" ;;
esac

publish_aot() {
  echo "==> 发布（Native AOT，$RID）"
  dotnet publish src/MathGeo.App/MathGeo.App.csproj \
    -c Release -r "$RID" \
    -p:PublishAot=true -p:StripSymbols=true \
    -o "$PUBLISH_DIR"
}

publish_selfcontained() {
  echo "==> 发布（自包含，$RID）"
  dotnet publish src/MathGeo.App/MathGeo.App.csproj \
    -c Release -r "$RID" --self-contained true \
    -o "$PUBLISH_DIR"
}

rm -rf "$PUBLISH_DIR"

case "$PUBLISH_MODE" in
  aot) publish_aot ;;
  selfcontained) publish_selfcontained ;;
  *)
    if ! publish_aot; then
      echo
      echo "    AOT 失败（受限环境里 ILLink 起不来），回退到自包含。"
      echo "    在你自己终端里重跑通常就能出 AOT 版，体积会小很多。"
      echo
      rm -rf "$PUBLISH_DIR"
      publish_selfcontained
    fi
    ;;
esac

echo
echo "==> 组装 .app 包"
rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

cp -R "$PUBLISH_DIR"/* "$MACOS_DIR/"

# 动态库瘦身：fat 二进制里有一半是另一套架构的代码。
for lib_path in "$MACOS_DIR"/*.dylib; do
  [ -f "$lib_path" ] || continue
  [ -n "$ARCH" ] || continue

  if lipo -info "$lib_path" 2>/dev/null | grep -q "arm64" \
     && lipo -info "$lib_path" 2>/dev/null | grep -q "x86_64"; then
    lib="$(basename "$lib_path")"
    echo "    瘦身 $lib -> $ARCH"
    lipo -thin "$ARCH" "$lib_path" -output "$lib_path.thin"
    mv "$lib_path.thin" "$lib_path"
  fi
done

# 示例题目拷到可执行文件旁边：外壳启动时会在自己旁边找 samples/，
# 找到就直接装进题目列表。老师第一次打开不该面对一块空白画布。
mkdir -p "$MACOS_DIR/samples"
cp samples/*.problem.json "$MACOS_DIR/samples/"
echo "    题目：$(ls "$MACOS_DIR/samples" | wc -l | tr -d ' ') 道"

if [ -d "$MACOS_DIR/Locales" ]; then
  echo "    语言：$(ls "$MACOS_DIR/Locales" | wc -l | tr -d ' ') 种"
fi

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>MathGeo</string>
  <key>CFBundleDisplayName</key><string>MathGeo</string>
  <key>CFBundleIdentifier</key><string>dev.mathgeo.app</string>
  <key>CFBundleExecutable</key><string>MathGeo</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>MIT</string>
</dict>
</plist>
PLIST

echo
echo "==> Ad-hoc 签名"
# 本地测试 ad-hoc 就够。分发给别人时对方第一次打开要在
# "系统设置 → 隐私与安全性"放行，或右键 → 打开。
# 正式分发需要 Apple 开发者账号。
codesign --force --deep --sign - "$APP_DIR" 2>/dev/null \
  || echo "    警告：签名失败，本地测试不影响"

echo
echo "==> 压缩（便于通过微信/网盘分发）"
ZIP_PATH="dist/MathGeo-app-$RID.zip"
rm -f "$ZIP_PATH"
(cd dist && zip -qr "$(basename "$ZIP_PATH")" "MathGeo.app")

echo
echo "==> 完成"
du -sh "$APP_DIR" "$ZIP_PATH" | sort -rh
echo
echo "    打开：  open \"$APP_DIR\""
echo "    无头渲染：\"$MACOS_DIR/MathGeo\" --render \"$MACOS_DIR/samples/05-cube.problem.json\" /tmp/cube.png"
