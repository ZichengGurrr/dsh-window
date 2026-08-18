param(
    [string]$Version = "1.2.2",
    [string]$IconPath = "",
    [string]$WebView2Version = "1.0.4129.50",
    [string]$ExeName = "DeepSeek Harness Window.exe",
    [switch]$Portable,
    [string]$NodeVersion = "24.19.0",
    [string]$MinGitVersion = "2.55.0.4",
    [string]$DshVersion = "0.1.0-rc.6",
    [string]$NpmRegistry = "https://registry.npmjs.org/"
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
# Default icon: assets\icon.ico when present and no explicit icon was given.
if (-not $IconPath) {
    $defaultIcon = Join-Path $Root "assets\icon.ico"
    if (Test-Path $defaultIcon) { $IconPath = $defaultIcon }
}
$BuildDir = Join-Path $Root ".build"
$DistDir = Join-Path $Root "dist"
$PortableDir = Join-Path $Root "dist-portable"
$SrcFile = Join-Path $Root "src\DeepSeekHarnessWindow.cs"
$ZipName = "DeepSeek-Harness-Window-v${Version}-win-x64.zip"
$PortableZipName = "DeepSeek-Harness-Window-portable-v${Version}-win-x64.zip"

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
    ("/r:" + (Join-Path $fw "System.Web.Extensions.dll")),
    ("/r:" + (Join-Path $DistDir "Microsoft.Web.WebView2.Core.dll")),
    ("/r:" + (Join-Path $DistDir "Microsoft.Web.WebView2.WinForms.dll"))
)
$cscArgs = @("/nologo", "/target:winexe", "/platform:x64", "/optimize+")
if ($IconPath) { $cscArgs += ("/win32icon:" + $IconPath) }
# Embed the application manifest (PerMonitorV2 DPI awareness + comctl32 v6).
$manifestPath = Join-Path $Root "app.manifest"
if (Test-Path $manifestPath) { $cscArgs += ("/win32manifest:" + $manifestPath) }
$cscArgs += ("/out:" + (Join-Path $DistDir $ExeName))
foreach ($ref in $refs) { $cscArgs += $ref }
$cscArgs += $SrcFile

Write-Host "compiling ..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}

# Package the slim build.
$zipPath = Join-Path $Root $ZipName
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$DistDir\*" -DestinationPath $zipPath

if ($Portable) {
    Write-Host "assembling portable bundle ..."
    Remove-Item "$PortableDir\*" -Recurse -Force -ErrorAction SilentlyContinue

    # Portable Node.js.
    $nodeZip = Join-Path $BuildDir "node-v$NodeVersion-win-x64.zip"
    if (-not (Test-Path $nodeZip)) {
        Write-Host "downloading Node.js v$NodeVersion ..."
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest ("https://nodejs.org/dist/v{0}/node-v{0}-win-x64.zip" -f $NodeVersion) -OutFile $nodeZip
    }
    # The node zip wraps everything in one top-level folder; flatten it into runtime\node.
    $nodeExtract = Join-Path $PortableDir "runtime\node-extract"
    New-Item -ItemType Directory -Force -Path $nodeExtract | Out-Null
    Expand-Archive -Path $nodeZip -DestinationPath $nodeExtract -Force
    $inner = Get-ChildItem $nodeExtract -Directory | Select-Object -First 1
    if (-not $inner) { throw "node zip layout unexpected: $nodeZip" }
    Move-Item $inner.FullName (Join-Path $PortableDir "runtime\node") -Force
    Remove-Item $nodeExtract -Recurse -Force
    $nodeDir = Join-Path $PortableDir "runtime\node"
    if (-not (Test-Path (Join-Path $nodeDir "npm.cmd"))) {
        throw "npm.cmd not found after node extraction"
    }

    # Portable Git (MinGit).
    $gitZip = Join-Path $BuildDir "MinGit-$MinGitVersion-64-bit.zip"
    if (-not (Test-Path $gitZip)) {
        Write-Host "downloading MinGit $MinGitVersion ..."
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest ("https://github.com/git-for-windows/git/releases/download/v{0}.windows.{1}/MinGit-{1}-64-bit.zip" -f ($MinGitVersion -replace '\.\d+$',''), $MinGitVersion) -OutFile $gitZip
    }
    $gitDir = Join-Path $PortableDir "runtime\git"
    New-Item -ItemType Directory -Force -Path $gitDir | Out-Null
    Expand-Archive -Path $gitZip -DestinationPath $gitDir -Force
    if (-not (Test-Path (Join-Path $gitDir "cmd\git.exe"))) {
        throw "git.exe not found after MinGit extraction"
    }

    # Install @deepseek-ai/dsh into the portable runtime.
    Write-Host "installing @deepseek-ai/dsh@$DshVersion into portable runtime (this downloads packages) ..."
    $npm = Join-Path $nodeDir "npm.cmd"
    # npm writes warnings to stderr; with ErrorActionPreference=Stop that
    # would abort the build even when npm exits 0. Relax it around native
    # calls and rely on $LASTEXITCODE instead.
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $npm install --prefix (Join-Path $PortableDir "runtime") "@deepseek-ai/dsh@$DshVersion" `
        --no-audit --no-fund --registry $NpmRegistry
    $npmExit = $LASTEXITCODE
    $ErrorActionPreference = $previousEap
    if ($npmExit -ne 0) {
        throw "npm install failed with exit code $npmExit"
    }
    if (-not (Test-Path (Join-Path $PortableDir "runtime\node_modules\@deepseek-ai\dsh\lib\bin.js"))) {
        throw "dsh entry not found after npm install"
    }

    # Strip debug source maps (safe: devtools-only, saves tens of MB).
    $mapFiles = Get-ChildItem (Join-Path $PortableDir "runtime\node_modules") -Recurse -Filter *.map -File -ErrorAction SilentlyContinue
    if ($mapFiles) { $mapFiles | Remove-Item -Force }

    # Copy the app files into the portable bundle root.
    Copy-Item "$DistDir\*" $PortableDir -Force

    $portableZipPath = Join-Path $Root $PortableZipName
    Remove-Item $portableZipPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path "$PortableDir\*" -DestinationPath $portableZipPath
}

Write-Host ""
Write-Host "build OK" -ForegroundColor Green
Get-ChildItem $DistDir | ForEach-Object { Write-Host ("  dist/" + $_.Name + " (" + $_.Length + " bytes)") }
Write-Host ("  " + $ZipName + " (" + (Get-Item $zipPath).Length + " bytes)")
if ($Portable) {
    Write-Host ("  " + $PortableZipName + " (" + (Get-Item $portableZipPath).Length + " bytes)")
}
