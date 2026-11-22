param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\TransparentFolder\TransparentFolder.csproj"
$UninstallBatPath = Join-Path $RepoRoot "Uninstall.bat"
$ReleaseDir = Join-Path $RepoRoot "release"

$VersionTag = if ([string]::IsNullOrWhiteSpace($Version)) {
    ""
} else {
    $NormalizedVersion = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
    "$NormalizedVersion-"
}

function Publish-ReleaseVariant {
    param(
        [string]$VariantName,
        [bool]$SelfContained,
        [bool]$CompressSingleFile
    )

    $StageDir = Join-Path $ReleaseDir "stage-$VariantName"
    $ZipName = "TransparentFolder-${VersionTag}win-x64-$VariantName.zip"
    $ZipPath = Join-Path $ReleaseDir $ZipName

    if (Test-Path $StageDir) {
        Remove-Item -Path $StageDir -Recurse -Force
    }
    if (Test-Path $ZipPath) {
        Remove-Item -Path $ZipPath -Force
    }

    try {
        Write-Host "Publishing $VariantName win-x64 single-file build..." -ForegroundColor Cyan

        $PublishArgs = @(
            "publish", $ProjectPath,
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-p:AllowedReferenceRelatedFileExtensions=none",
            "-o", $StageDir
        )
        if ($CompressSingleFile) {
            $PublishArgs += "-p:EnableCompressionInSingleFile=true"
        }

        & dotnet @PublishArgs | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish ($VariantName) failed with exit code $LASTEXITCODE."
        }

        $ExePath = Join-Path $StageDir "TransparentFolder.exe"
        if (-not (Test-Path $ExePath)) {
            throw "Expected executable not found: $ExePath"
        }
        if (-not (Test-Path $UninstallBatPath)) {
            throw "Expected Uninstall.bat not found: $UninstallBatPath"
        }

        # Keep only TransparentFolder.exe + Uninstall.bat in stage (remove any stray pdb/xml/subfolders)
        Get-ChildItem -Path $StageDir -Force | Where-Object { $_.Name -ne "TransparentFolder.exe" } | Remove-Item -Recurse -Force
        Copy-Item -Path $UninstallBatPath -Destination (Join-Path $StageDir "Uninstall.bat") -Force

        $StageFiles = @(Get-ChildItem -Path $StageDir -Recurse -File | Sort-Object Name)
        $StageNames = @($StageFiles | ForEach-Object { $_.Name })
        if ($StageFiles.Count -ne 2 -or $StageNames[0] -ne "TransparentFolder.exe" -or $StageNames[1] -ne "Uninstall.bat") {
            throw "Stage directory must contain ONLY TransparentFolder.exe and Uninstall.bat. Found: $($StageNames -join ', ')"
        }

        Write-Host "Creating release archive: $ZipPath" -ForegroundColor Cyan
        Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $ZipPath -Force

        $Archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
        try {
            $EntryNames = @($Archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
            if ($EntryNames.Count -ne 2 -or $EntryNames[0] -ne "TransparentFolder.exe" -or $EntryNames[1] -ne "Uninstall.bat") {
                throw "Archive $ZipPath must contain ONLY 2 files (TransparentFolder.exe, Uninstall.bat). Found: $($EntryNames -join ', ')"
            }
        }
        finally {
            $Archive.Dispose()
        }

        $ZipInfo = Get-Item $ZipPath
        $SizeMB = [Math]::Round($ZipInfo.Length / 1MB, 2)
        Write-Host "Created $VariantName archive: $ZipPath ($SizeMB MB / $($ZipInfo.Length) bytes)" -ForegroundColor Green
        return $ZipInfo
    }
    finally {
        if (Test-Path $StageDir) {
            Remove-Item -Path $StageDir -Recurse -Force
        }
    }
}

Push-Location $RepoRoot
try {
    New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

    $StandaloneZip = Publish-ReleaseVariant -VariantName "standalone" -SelfContained $true -CompressSingleFile $true
    $LiteZip = Publish-ReleaseVariant -VariantName "lite" -SelfContained $false -CompressSingleFile $false

    Write-Host ""
    Write-Host "Release summary:" -ForegroundColor Yellow
    Write-Host ("  standalone : {0} ({1:N2} MB, {2} bytes)" -f $StandaloneZip.FullName, ($StandaloneZip.Length / 1MB), $StandaloneZip.Length)
    Write-Host ("  lite       : {0} ({1:N2} MB, {2} bytes)" -f $LiteZip.FullName, ($LiteZip.Length / 1MB), $LiteZip.Length)
}
finally {
    Pop-Location
}
