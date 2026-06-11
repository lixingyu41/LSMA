$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'LSMA.csproj'
$installerScript = Join-Path $PSScriptRoot 'LSMA.iss'

dotnet publish $projectPath -p:PublishProfile=win-x64 -p:Platform=x64

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
    throw 'ISCC.exe not found. Install Inno Setup 6, then rerun Installer\Build-Installer.ps1.'
}

$isccPath = if ($iscc.Source) { $iscc.Source } else { $iscc.FullName }
& $isccPath $installerScript
