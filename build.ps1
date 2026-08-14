param(
    [string]$Version = "1.0.0",
    [string]$IconPath = "",
    [string]$WebView2Version = "1.0.4129.50",
    [string]$ExeName = "DeepSeek Harness Window.exe"
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir = Join-Path $Root ".build"
$DistDir = Join-Path $Root "dist"
$SrcFile = Join-Path $Root "src\DeepSeekHarnessWindow.cs"
$ZipName = "DeepSeek-Harness-Window-v${Version}-win-x64.zip"

if (-not (Test-Path $SrcFile)) {
    throw "source file not found: $SrcFile"
}
if ($IconPath -and -not (Test-Path $IconPath)) {
    throw "icon file not found: $IconPath"
}

New-Item -ItemType Directory -Force -Path $BuildDir, $DistDir | Out-Null

# Locate the .NET Framework C# compiler (ships with Windows).
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "csc.exe not found. Install .NET Framework 4.x developer tools or Visual Studio."
}

# Download the WebView2 SDK (managed wrappers + native loader) from NuGet.
$pkg = Join-Path $BuildDir "microsoft.web.webview2.$WebView2Version.nupkg"
if (-not (Test-Path $pkg)) {
    Write-Host "downloading Microsoft.Web.WebView2 $WebView2Version ..."
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest ("https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/{0}/microsoft.web.webview2.{0}.nupkg" -f $WebView2Version) -OutFile $pkg
}

$pkgDir = Join-Path $BuildDir "pkg-$WebView2Version"
if (-not (Test-Path $pkgDir)) {
    Write-Host "extracting SDK ..."
    $tmpZip = Join-Path $env:TEMP ("wv2-" + [guid]::NewGuid().ToString("N") + ".zip")
    Copy-Item $pkg $tmpZip
    Expand-Archive -Path $tmpZip -DestinationPath $pkgDir -Force
    Remove-Item $tmpZip -Force
}

$lib = Join-Path $pkgDir "lib\net462"
$native = Join-Path $pkgDir "runtimes\win-x64\native"
if (-not (Test-Path (Join-Path $lib "Microsoft.Web.WebView2.Core.dll"))) {
    throw "SDK layout unexpected (no net462 assemblies): $pkgDir"
}

# Assemble the distributable folder (exe + 3 runtime files).
Remove-Item "$DistDir\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $lib "Microsoft.Web.WebView2.Core.dll") $DistDir
Copy-Item (Join-Path $lib "Microsoft.Web.WebView2.WinForms.dll") $DistDir
Copy-Item (Join-Path $native "WebView2Loader.dll") $DistDir

# Compile. (The launcher URL / port / patch overlay live in the .cs source;
# edit src\DeepSeekHarnessWindow.cs to change them.)
$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$refs = @(
    ("/r:" + (Join-Path $fw "System.dll")),
    ("/r:" + (Join-Path $fw "System.Core.dll")),
    ("/r:" + (Join-Path $fw "System.Windows.Forms.dll")),
    ("/r:" + (Join-Path $fw "System.Drawing.dll")),
    ("/r:" + (Join-Path $DistDir "Microsoft.Web.WebView2.Core.dll")),
    ("/r:" + (Join-Path $DistDir "Microsoft.Web.WebView2.WinForms.dll"))
)
$cscArgs = @("/nologo", "/target:winexe", "/platform:x64", "/optimize+")
if ($IconPath) { $cscArgs += ("/win32icon:" + $IconPath) }
$cscArgs += ("/out:" + (Join-Path $DistDir $ExeName))
foreach ($ref in $refs) { $cscArgs += $ref }
$cscArgs += $SrcFile

Write-Host "compiling ..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}

# Package.
$zipPath = Join-Path $Root $ZipName
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$DistDir\*" -DestinationPath $zipPath

Write-Host ""
Write-Host "build OK" -ForegroundColor Green
Get-ChildItem $DistDir | ForEach-Object { Write-Host ("  dist/" + $_.Name + " (" + $_.Length + " bytes)") }
Write-Host ("  " + $ZipName + " (" + (Get-Item $zipPath).Length + " bytes)")
