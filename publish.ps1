$ErrorActionPreference = 'Stop'

$AppProject = Join-Path $PSScriptRoot 'src\DKImageAIEditor\DKImageAIEditor.csproj'
$LauncherProject = Join-Path $PSScriptRoot 'src\DKImageAIEditor.Launcher\DKImageAIEditor.Launcher.csproj'

[xml]$ProjectXml = Get-Content $AppProject
$Version = [string]($ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw '프로젝트 Version 값을 찾을 수 없습니다.'
}

$AppPublishDir = Join-Path $PSScriptRoot 'src\DKImageAIEditor\bin\Release\net10.0-windows\win-x64\publish-app'
$LauncherPublishDir = Join-Path $PSScriptRoot 'src\DKImageAIEditor.Launcher\bin\Release\net10.0-windows\win-x64\publish'
$DistDir = Join-Path $PSScriptRoot 'dist'
$PackageName = "DK-Image-AI-Editor-v$Version-win-x64"
$StageDir = Join-Path $DistDir $PackageName
$ZipPath = Join-Path $DistDir "$PackageName.zip"

function Assert-LastExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step 실패 (exit code: $LASTEXITCODE)"
    }
}

Write-Host "DK Image AI Editor v$Version - Windows x64 publish"

dotnet restore $AppProject -r win-x64
Assert-LastExitCode 'app dotnet restore'

dotnet build $AppProject -c Release --no-restore
Assert-LastExitCode 'app dotnet build'

dotnet publish $AppProject -c Release -r win-x64 -p:PublishProfile=win-x64-single --no-restore
Assert-LastExitCode 'app dotnet publish'

dotnet restore $LauncherProject -r win-x64
Assert-LastExitCode 'launcher dotnet restore'

dotnet publish $LauncherProject -c Release -r win-x64 --no-restore
Assert-LastExitCode 'launcher dotnet publish'

$AppExe = Join-Path $AppPublishDir 'DKImageAIEditor.App.exe'
$LauncherExe = Join-Path $LauncherPublishDir 'DKImageAIEditor.exe'

if (-not (Test-Path $AppExe)) {
    throw "앱 EXE를 찾을 수 없습니다: $AppExe"
}
if (-not (Test-Path $LauncherExe)) {
    throw "런처 EXE를 찾을 수 없습니다: $LauncherExe"
}

if (Test-Path $DistDir) {
    Remove-Item $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null

Copy-Item $LauncherExe $StageDir -Force
Copy-Item $AppExe $StageDir -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') $StageDir -Force
Copy-Item (Join-Path $PSScriptRoot 'LICENSE') $StageDir -Force

Compress-Archive -Path (Join-Path $StageDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Host "Launcher: $LauncherExe"
Write-Host "App: $AppExe"
Write-Host "Package: $ZipPath"
