# 打包 MathGeo 的 Windows 版本。
#
# 和 macOS 的 package-app-macos.sh 对称，但有两处必须不同的地方：
#
#   1. 输出不是 .app 而是一个文件夹 + zip。Windows 没有 bundle 这种约定，
#      绿色软件就是"解压就能跑"。
#
#   2. Native AOT **不能交叉编译**。在 macOS 上跑这个脚本只能出自我包含版
#      （约 100MB）；要出 AOT 版（约 40MB）必须在 Windows 上跑，
#      或者在 CI 里用 windows-latest runner。
#
# 用法（在 Windows 上，PowerShell）：
#   ./scripts/package-app-windows.ps1
#   ./scripts/package-app-windows.ps1 -Rid win-arm64
#
# 用法（在 macOS/Linux 上交叉编译自包含版）：
#   pwsh ./scripts/package-app-windows.ps1 -SelfContained

param(
    [string]$Rid = "win-x64",
    [string]$Version = "0.1.0",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$publishDir = "dist/app-$Rid"
$outDir = "dist/MathGeo-windows-$Rid"
$zipPath = "dist/MathGeo-windows-$Rid.zip"

# 只有在 Windows 上才尝试 AOT —— 别的平台上 AOT 交叉编译是不支持的。
$canAot = $IsWindows -and -not $SelfContained

if ($canAot) {
    Write-Host "==> 发布（Native AOT，$Rid）"
    dotnet publish src/MathGeo.App/MathGeo.App.csproj `
        -c Release -r $Rid `
        -p:PublishAot=true -p:StripSymbols=true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "    AOT 失败，回退到自包含。"
        Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
        $canAot = $false
    }
}

if (-not $canAot) {
    Write-Host "==> 发布（自包含，$Rid）"
    if ($IsWindows) {
        Write-Host "    提示：在 Windows 上不加 -SelfContained 会出 AOT 版，体积小一半以上。"
    } else {
        Write-Host "    提示：当前不是 Windows，AOT 无法交叉编译，只能出自包含版。"
    }

    dotnet publish src/MathGeo.App/MathGeo.App.csproj `
        -c Release -r $Rid --self-contained true `
        -o $publishDir
}

Write-Host ""
Write-Host "==> 组装分发包"
Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Copy-Item -Recurse -Force "$publishDir/*" $outDir

# 示例题目放在可执行文件旁边：外壳启动时会找 samples/，找到就直接装进题目列表。
# 老师第一次打开不该面对一块空白画布。
New-Item -ItemType Directory -Force -Path "$outDir/samples" | Out-Null
Copy-Item -Force samples/*.problem.json "$outDir/samples/"
$count = (Get-ChildItem "$outDir/samples" -Filter *.problem.json).Count
Write-Host "    题目：$count 道"

if (Test-Path "$outDir/Locales") {
    $langs = (Get-ChildItem "$outDir/Locales" -Filter strings.*.json).Count
    Write-Host "    语言：$langs 种"
}

# 放一个说明文件。Windows 上用户拿到一个文件夹会不知道点哪个。
@"
MathGeo $Version

双击 MathGeo.exe 启动。

第一次打开如果 Windows 提示"已保护你的电脑"：
  点"更多信息" → "仍要运行"。
  这是因为本地构建没有代码签名证书，不是文件有问题。

示例题目在 samples\ 文件夹里，软件启动时会自动装进左侧列表。
要换成自己的题目：把 .problem.json 放进任意文件夹，然后在软件里点"打开文件夹"。

语言文件在 Locales\ 里，是普通 JSON。改完直接重启软件就生效，不需要重新编译。
"@ | Set-Content -Path "$outDir/使用说明.txt" -Encoding UTF8

Write-Host ""
Write-Host "==> 压缩"
Remove-Item -Force $zipPath -ErrorAction SilentlyContinue
Compress-Archive -Path $outDir -DestinationPath $zipPath

Write-Host ""
Write-Host "==> 完成"
Write-Host "    $outDir"
Write-Host "    $zipPath"
Write-Host ""
Write-Host "    在 Windows 上运行：$outDir\MathGeo.exe"
Write-Host "    无头渲染：        $outDir\MathGeo.exe --render $outDir\samples\05-cube.problem.json out.png"
