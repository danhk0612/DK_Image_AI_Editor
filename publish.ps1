$ErrorActionPreference = 'Stop'

$Project = Join-Path $PSScriptRoot 'src\DKImageAIEditor\DKImageAIEditor.csproj'

Write-Host 'DK Image AI Editor - Windows x64 publish'
dotnet publish $Project -c Release -p:PublishProfile=win-x64

$PublishDir = Join-Path $PSScriptRoot 'src\DKImageAIEditor\bin\Release\net8.0-windows\win-x64\publish'
Write-Host "완료: $PublishDir"
