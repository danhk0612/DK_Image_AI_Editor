$ErrorActionPreference = 'Stop'

$Project = Join-Path $PSScriptRoot 'src\DKImageAIEditor\DKImageAIEditor.csproj'
[xml]$ProjectXml = Get-Content $Project
$Version = [string]($ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw '프로젝트 Version 값을 찾을 수 없습니다.'
}

$PublishDir = Join-Path $PSScriptRoot 'src\DKImageAIEditor\bin\Release\net8.0-windows\win-x64\publish'
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

dotnet restore $Project -r win-x64
Assert-LastExitCode 'dotnet restore'

dotnet build $Project -c Release --no-restore
Assert-LastExitCode 'dotnet build'

dotnet publish $Project -c Release -r win-x64 -p:PublishProfile=win-x64 --no-restore
Assert-LastExitCode 'dotnet publish'

if (-not (Test-Path $PublishDir)) {
    throw "Publish 폴더를 찾을 수 없습니다: $PublishDir"
}

if (Test-Path $DistDir) {
    Remove-Item $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null

Copy-Item (Join-Path $PublishDir '*') $StageDir -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') $StageDir -Force
Copy-Item (Join-Path $PSScriptRoot 'LICENSE') $StageDir -Force

Compress-Archive -Path (Join-Path $StageDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Host "Publish: $PublishDir"
Write-Host "Package: $ZipPath"
