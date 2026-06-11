$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'LSMA.csproj'
$publishDir = Join-Path $repoRoot 'bin\win-x64\publish'
$onlineDir = Join-Path $repoRoot 'dist\online'
$coreDir = Join-Path $onlineDir 'core'
$packageSourceDir = Join-Path $onlineDir 'package-src'
$depsDir = Join-Path $repoRoot 'web\download\deps'
$runtimeIncludePath = Join-Path $onlineDir 'RuntimePackages.iss'
$installerScript = Join-Path $onlineDir 'LSMA-Online.generated.iss'
$version = ([xml](Get-Content -LiteralPath $projectPath)).Project.PropertyGroup.Version |
    Select-Object -First 1

dotnet publish $projectPath -p:PublishProfile=win-x64 -p:Platform=x64

Remove-Item -LiteralPath $onlineDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $depsDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $coreDir, $packageSourceDir, $depsDir | Out-Null

$corePatterns = @(
    'LSMA.exe',
    'LSMA.dll',
    'LSMA.deps.json',
    'LSMA.runtimeconfig.json',
    'resources.pri',
    'CommunityToolkit.Mvvm.dll',
    'SharpCompress.dll',
    'Assets\*',
    'Pages\*',
    'Themes\*'
)

function Test-CoreFile {
    param([string]$RelativePath)
    foreach ($pattern in $corePatterns) {
        if ($RelativePath -like $pattern) {
            return $true
        }
    }

    return $false
}

function Copy-RelativeFile {
    param(
        [string]$Source,
        [string]$RelativePath,
        [string]$DestinationRoot
    )

    $destination = Join-Path $DestinationRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $destination -Force
}

$publishRoot = (Resolve-Path $publishDir).Path
$files = Get-ChildItem -Path $publishRoot -Recurse -File | ForEach-Object {
    $relativePath = $_.FullName.Substring($publishRoot.Length + 1)
    [pscustomobject]@{
        FullName = $_.FullName
        RelativePath = $relativePath
        Length = $_.Length
        IsCore = Test-CoreFile $relativePath
    }
}

foreach ($file in $files | Where-Object IsCore) {
    Copy-RelativeFile -Source $file.FullName -RelativePath $file.RelativePath -DestinationRoot $coreDir
}

$targetRawPackageBytes = 40MB
$packages = New-Object System.Collections.Generic.List[object]
foreach ($file in ($files | Where-Object { -not $_.IsCore } | Sort-Object Length -Descending)) {
    $placed = $false
    foreach ($package in $packages) {
        if (($package.RawBytes + $file.Length) -le $targetRawPackageBytes) {
            $package.Files.Add($file) | Out-Null
            $package.RawBytes += $file.Length
            $placed = $true
            break
        }
    }

    if (-not $placed) {
        $packageFiles = New-Object System.Collections.Generic.List[object]
        $packageFiles.Add($file) | Out-Null
        $packages.Add([pscustomobject]@{
            Files = $packageFiles
            RawBytes = [int64]$file.Length
        }) | Out-Null
    }
}

$runtimeEntries = New-Object System.Collections.Generic.List[string]
$packageResults = @()
$index = 1
foreach ($package in $packages) {
    $packageId = 'lsma-runtime-{0:00}' -f $index
    $zipName = "$packageId.zip"
    $markerName = "$packageId-v$version.ok"
    $sourceDir = Join-Path $packageSourceDir $packageId
    New-Item -ItemType Directory -Path $sourceDir | Out-Null

    foreach ($file in $package.Files) {
        Copy-RelativeFile -Source $file.FullName -RelativePath $file.RelativePath -DestinationRoot $sourceDir
    }

    $markerPath = Join-Path $sourceDir ".lsma-runtime\$markerName"
    New-Item -ItemType Directory -Path (Split-Path -Parent $markerPath) -Force | Out-Null
    Set-Content -LiteralPath $markerPath -Value $version -Encoding ASCII

    $zipPath = Join-Path $depsDir $zipName
    Compress-Archive -Path (Join-Path $sourceDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

    $zipItem = Get-Item -LiteralPath $zipPath
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
    if ($zipItem.Length -ge 25MB) {
        throw "$zipName is $($zipItem.Length) bytes, exceeding the 25 MiB Cloudflare Pages file limit."
    }

    $runtimeEntries.Add(
        "Source: ""{#RuntimeBaseUrl}/$zipName""; DestName: ""$zipName""; DestDir: ""{app}""; " +
        "ExternalSize: $($zipItem.Length); Hash: ""$hash""; " +
        "Flags: external download extractarchive recursesubdirs ignoreversion; " +
        "Check: NeedsRuntimePackage('$markerName')"
    ) | Out-Null

    $packageResults += [pscustomobject]@{
        Package = $zipName
        RawBytes = $package.RawBytes
        ZipBytes = $zipItem.Length
        Sha256 = $hash
        Marker = $markerName
    }

    $index++
}

Set-Content -LiteralPath $runtimeIncludePath -Value ($runtimeEntries -join [Environment]::NewLine) -Encoding UTF8

$installerContent = @"
#define MyAppName "LSMA"
#define MyAppVersion "$version"
#define MyAppPublisher "L"
#define MyAppExeName "LSMA.exe"
#define RuntimeBaseUrl "https://lsma.lixingyu.top/download/deps"

[Setup]
AppId={{FDE4A75B-8F72-469F-B4EF-2E9813EE8373}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\web\download
OutputBaseFilename=LSMA-Setup-x64
SetupIconFile=..\..\Assets\LSMA.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
ArchiveExtraction=full
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no

[Messages]
PreparingDesc=Setup is downloading and installing LSMA runtime files.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "core\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#include "RuntimePackages.iss"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCR; Subkey: "nxm"; ValueType: string; ValueName: ""; ValueData: "URL:LSMA Nexus Download"; Flags: uninsdeletekey
Root: HKCR; Subkey: "nxm"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "nxm\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKCR; Subkey: "nxm\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsRuntimePackage(MarkerName: String): Boolean;
begin
  Result := not FileExists(ExpandConstant('{app}\.lsma-runtime\' + MarkerName));
end;
"@

Set-Content -LiteralPath $installerScript -Value $installerContent -Encoding UTF8

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidatePaths = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path -LiteralPath $candidatePath) {
            $iscc = Get-Item -LiteralPath $candidatePath
            break
        }
    }
}

if (-not $iscc) {
    throw 'ISCC.exe not found. Install Inno Setup 6, then rerun Installer\Build-Online-Installer.ps1.'
}

$isccPath = if ($iscc.Source) { $iscc.Source } else { $iscc.FullName }
& $isccPath $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $repoRoot 'web\download\LSMA-Setup-x64.exe'
$installerItem = Get-Item -LiteralPath $installerPath
if ($installerItem.Length -ge 25MB) {
    throw "Online installer is $($installerItem.Length) bytes, exceeding the 25 MiB Cloudflare Pages file limit."
}

$summary = [pscustomobject]@{
    InstallerBytes = $installerItem.Length
    InstallerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash
    CoreBytes = (Get-ChildItem -Path $coreDir -Recurse -File | Measure-Object Length -Sum).Sum
    RuntimePackageCount = $packageResults.Count
    LargestRuntimePackageBytes = ($packageResults | Measure-Object ZipBytes -Maximum).Maximum
}

$summary | Format-List
$packageResults | Format-Table Package, RawBytes, ZipBytes, Marker -AutoSize
