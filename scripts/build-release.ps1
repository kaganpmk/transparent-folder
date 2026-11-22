param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\TransparentFolder\TransparentFolder.csproj"
$DistDir = Join-Path $RepoRoot "dist"
$ReleaseDir = Join-Path $RepoRoot "release"

$ZipName = if ([string]::IsNullOrWhiteSpace($Version)) {
    "TransparentFolder-win-x64.zip"
} else {
    $NormalizedVersion = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
    "TransparentFolder-$NormalizedVersion-win-x64.zip"
}

$ZipPath = Join-Path $ReleaseDir $ZipName

Push-Location $RepoRoot
try {
    if (Test-Path $DistDir) {
        Remove-Item -Path $DistDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

    Write-Host "Publishing self-contained win-x64 build..." -ForegroundColor Cyan
    dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true -o $DistDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $ExePath = Join-Path $DistDir "TransparentFolder.exe"
    $WebIndexPath = Join-Path $DistDir "web\index.html"
    if (-not (Test-Path $ExePath)) {
        throw "Expected executable not found: $ExePath"
    }
    if (-not (Test-Path $WebIndexPath)) {
        throw "Expected web asset not found: $WebIndexPath"
    }

    if (Test-Path $ZipPath) {
        Remove-Item -Path $ZipPath -Force
    }

    Write-Host "Creating release archive: $ZipPath" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $DistDir "*") -DestinationPath $ZipPath -Force

    $ZipInfo = Get-Item $ZipPath
    $SizeMB = [Math]::Round($ZipInfo.Length / 1MB, 2)
    Write-Host "Done! Archive created ($SizeMB MB): $ZipPath" -ForegroundColor Green
}
finally {
    Pop-Location
}
