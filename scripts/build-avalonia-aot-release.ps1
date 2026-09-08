param(
    [switch]$SkipVerification,
    [string]$ArchivePath = ''
)

$ErrorActionPreference = 'Stop'
$version = '1.3.0-preview.17'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'release'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'output\avalonia-aot-preview17'))
$stagingDirectory = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot "Alchemy Stars $version"))
$resolvedArchive = if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    [System.IO.Path]::GetFullPath((Join-Path $releaseRoot "AlchemyStars-$version-win-x64.zip"))
} elseif ([System.IO.Path]::IsPathRooted($ArchivePath)) {
    [System.IO.Path]::GetFullPath($ArchivePath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ArchivePath))
}

function Assert-ReleaseChild([string]$Path) {
    if (-not $Path.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a path outside the release directory: $Path"
    }
}

Assert-ReleaseChild $stagingDirectory
Assert-ReleaseChild $resolvedArchive

if (-not $SkipVerification) {
    & (Join-Path $PSScriptRoot 'verify-avalonia-aot.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Native AOT verification failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'AlchemyStars.Avalonia.exe'))) {
    throw "Verified Native AOT publish is missing: $publishDirectory"
}

foreach ($target in @($stagingDirectory, $resolvedArchive)) {
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
[System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stagingDirectory -Recurse -Force

$rootFiles = @(
    @{ Source = 'README.md'; Target = 'README.md' },
    @{ Source = 'README.zh-CN.md'; Target = 'README.zh-CN.md' },
    @{ Source = 'CHANGELOG.md'; Target = 'CHANGELOG.md' },
    @{ Source = 'LICENSE'; Target = 'LICENSE.txt' },
    @{ Source = 'THIRD_PARTY_NOTICES.md'; Target = 'THIRD_PARTY_NOTICES.md' },
    @{ Source = 'docs\dual-wield.zh-CN.md'; Target = 'Docs\DUAL-WIELD.zh-CN.md' },
    @{ Source = 'docs\releases\1.3.0-preview.17.zh-CN.md'; Target = 'Docs\RELEASE-NOTES.zh-CN.md' },
    @{ Source = 'docs\samples\appearance\theme.json'; Target = 'Samples\Appearance\theme.json' },
    @{ Source = 'docs\samples\appearance\icons-template.zip'; Target = 'Samples\Appearance\icons-template.zip' },
    @{ Source = 'docs\samples\appearance\README.zh-CN.md'; Target = 'Samples\Appearance\README.zh-CN.md' },
    @{ Source = 'fork\RedFox\LICENSE'; Target = 'Licenses\RedFox-LICENSE.txt' },
    @{ Source = 'third_party\cast\LICENSE'; Target = 'Licenses\Maya-CAST-LICENSE.txt' },
    @{ Source = 'docs\avalonia-aot-user-guide.md'; Target = 'Docs\USER-GUIDE.en-US.md' },
    @{ Source = 'docs\avalonia-aot-user-guide.zh-CN.md'; Target = 'Docs\USER-GUIDE.zh-CN.md' },
    @{ Source = 'docs\avalonia-aot-migration.md'; Target = 'Docs\AVALONIA-AOT-MIGRATION.md' },
    @{ Source = 'docs\dotnet11-preview.md'; Target = 'Docs\DOTNET11-PREVIEW.md' }
)
foreach ($entry in $rootFiles) {
    $source = Join-Path $repositoryRoot $entry.Source
    $target = Join-Path $stagingDirectory $entry.Target
    if (-not (Test-Path -LiteralPath $source)) { throw "Release input is missing: $source" }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}

$exampleRoot = Join-Path $repositoryRoot 'fork\AlchemyStars\Example'
$manifestPath = Join-Path $exampleRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$examplePaths = @('manifest.json') + @($manifest.StandardExamples.Path) + @($manifest.ImprovedExamples.Path) + @($manifest.Documentation)
foreach ($relativePath in $examplePaths) {
    $source = [System.IO.Path]::GetFullPath((Join-Path $exampleRoot $relativePath))
    if (-not $source.StartsWith($exampleRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $source)) {
        throw "Invalid example release input: $relativePath"
    }
    $target = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $stagingDirectory 'Example') $relativePath))
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}
foreach ($example in $manifest.StandardExamples) {
    $source = Join-Path $exampleRoot $example.Path
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $example.Sha256) {
        throw "Standard example changed: $($example.Path)"
    }
}

$sampleOutput = Join-Path $repositoryRoot 'output\avalonia-aot-appearance-samples'
[System.IO.Directory]::CreateDirectory($sampleOutput) | Out-Null
$previousSettingsPath = $env:ALCHEMY_STARS_SETTINGS_PATH
try {
    $env:ALCHEMY_STARS_SETTINGS_PATH = Join-Path $sampleOutput 'settings.json'
    $sampleArguments = '--appearance-samples-smoke --culture en-US --window-size 900x600 --render-smoke "' + (Join-Path $sampleOutput 'result.png') + '"'
    $sampleTest = Start-Process -FilePath (Join-Path $stagingDirectory 'AlchemyStars.Avalonia.exe') -ArgumentList $sampleArguments -WorkingDirectory $stagingDirectory -WindowStyle Hidden -Wait -PassThru
    if ($sampleTest.ExitCode -ne 0) { throw "Shipped appearance samples failed validation: $($sampleTest.ExitCode)" }
    Write-Output 'Shipped appearance samples: native import and light/dark render PASS'
} finally {
    $env:ALCHEMY_STARS_SETTINGS_PATH = $previousSettingsPath
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedArchive)) | Out-Null
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $resolvedArchive -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
try {
    $names = @($archive.Entries.FullName -replace '\\', '/')
    foreach ($required in @('AlchemyStars.Avalonia.exe', 'README.md', 'README.zh-CN.md', 'Example/manifest.json', 'MayaPlugin/castplugin.py', 'BlenderPlugin/io_scene_cast/__init__.py', 'BlenderPlugin/LICENSE', 'Converters/convert_cast.py', 'Docs/DUAL-WIELD.zh-CN.md', 'Docs/RELEASE-NOTES.zh-CN.md', 'Samples/Appearance/theme.json', 'Samples/Appearance/icons-template.zip', 'Samples/Appearance/README.zh-CN.md')) {
        if ($required -notin $names) { throw "Release archive is missing: $required" }
    }
    if (@($names | Where-Object { $_.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw 'Release archive unexpectedly contains debug symbols.'
    }
} finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "Release directory: $stagingDirectory"
Write-Output "Archive: $resolvedArchive"
Write-Output "SHA-256: $hash"
