param(
    [switch]$SkipVerification,
    [string]$ArchivePath = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'avalonia-build-configuration.ps1')
$build = Get-AvaloniaBuildConfiguration -RepositoryRoot $repositoryRoot
$version = $build.Version
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'release'))
$publishDirectory = $build.PublishDirectory
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
if (-not (Test-Path -LiteralPath $build.ReleaseNotesPath -PathType Leaf)) {
    throw "Release notes are missing for ${version}: $($build.ReleaseNotesPath)"
}

if (-not $SkipVerification) {
    & (Join-Path $PSScriptRoot 'verify-avalonia-aot.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Native AOT verification failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'AlchemyStars.Avalonia.exe'))) {
    throw "Native AOT publish is missing: $publishDirectory"
}
$publishedVersion = (Get-Item -LiteralPath (Join-Path $publishDirectory 'AlchemyStars.Avalonia.exe')).VersionInfo.ProductVersion
if ($publishedVersion -ne $version) {
    throw "Published executable version mismatch: expected $version, found $publishedVersion"
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
    @{ Source = 'docs\cod-weapon-db.zh-CN.md'; Target = 'Docs\COD-WEAPON-DB.zh-CN.md' },
    @{ Source = "docs\releases\$version.zh-CN.md"; Target = 'Docs\RELEASE-NOTES.zh-CN.md' },
    @{ Source = "docs\releases\$version.zh-CN.md"; Target = "Docs\releases\$version.zh-CN.md" },
    @{ Source = 'docs\engineering-workflow.zh-CN.md'; Target = 'Docs\ENGINEERING-WORKFLOW.zh-CN.md' },
    @{ Source = 'docs\model-merger.zh-CN.md'; Target = 'Docs\MODEL-MERGER.zh-CN.md' },
    @{ Source = 'third_party\modelmerger\README.md'; Target = 'Docs\MODEL-MERGER-UPSTREAM.md' },
    @{ Source = 'docs\samples\appearance\theme.json'; Target = 'Samples\Appearance\theme.json' },
    @{ Source = 'docs\samples\appearance\icons-template.zip'; Target = 'Samples\Appearance\icons-template.zip' },
    @{ Source = 'docs\samples\appearance\README.zh-CN.md'; Target = 'Samples\Appearance\README.zh-CN.md' },
    @{ Source = 'fork\RedFox\LICENSE'; Target = 'Licenses\RedFox-LICENSE.txt' },
    @{ Source = 'third_party\cast\LICENSE'; Target = 'Licenses\Maya-CAST-LICENSE.txt' },
    @{ Source = 'design-system\references\awesome-design-md\LICENSE'; Target = 'Licenses\awesome-design-md-LICENSE.txt' },
    @{ Source = 'docs\avalonia-aot-user-guide.md'; Target = 'Docs\USER-GUIDE.en-US.md' },
    @{ Source = 'docs\avalonia-aot-user-guide.zh-CN.md'; Target = 'Docs\USER-GUIDE.zh-CN.md' },
    @{ Source = 'docs\images\linear-workbench-dark.zh-CN.png'; Target = 'Docs\images\linear-workbench-dark.zh-CN.png' },
    @{ Source = 'docs\images\linear-workbench-light.en-US.png'; Target = 'Docs\images\linear-workbench-light.en-US.png' },
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

if (-not $SkipVerification) {
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
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedArchive)) | Out-Null
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $resolvedArchive -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
try {
    $names = @($archive.Entries.FullName -replace '\\', '/')
    foreach ($required in @('AlchemyStars.Avalonia.exe', 'README.md', 'README.zh-CN.md', 'Example/manifest.json', 'MayaPlugin/castplugin.py', 'BlenderPlugin/io_scene_cast/__init__.py', 'BlenderPlugin/LICENSE', 'Converters/convert_cast.py', 'Docs/DUAL-WIELD.zh-CN.md', 'Docs/COD-WEAPON-DB.zh-CN.md', 'Docs/RELEASE-NOTES.zh-CN.md', 'Samples/Appearance/theme.json', 'Samples/Appearance/icons-template.zip', 'Samples/Appearance/README.zh-CN.md', 'CodWeaponDb/weapons.jsonl', 'CodWeaponDb/blueprints.json', 'CodWeaponDb/qa.json', 'CodWeaponDb/refresh-manifest.json', 'CodWeaponDb/README.md')) {
        if ($required -notin $names) { throw "Release archive is missing: $required" }
    }
    if (@($names | Where-Object { $_.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw 'Release archive unexpectedly contains debug symbols.'
    }
    foreach ($required in @('Converters/alchemy-model-merger.exe', 'Licenses/ModelMergerGUI-LICENSE.txt', 'Docs/MODEL-MERGER.zh-CN.md', 'Docs/MODEL-MERGER-UPSTREAM.md')) {
        if ($required -notin $names) { throw "Release archive is missing ModelMerger integration: $required" }
    }
    foreach ($required in @('Docs/ENGINEERING-WORKFLOW.zh-CN.md', "Docs/releases/$version.zh-CN.md", 'Licenses/awesome-design-md-LICENSE.txt', 'Docs/images/linear-workbench-dark.zh-CN.png', 'Docs/images/linear-workbench-light.en-US.png')) {
        if ($required -notin $names) { throw "Release archive is missing engineering documentation: $required" }
    }
    foreach ($required in @('Samples/Ubuntu-Yaru/theme.json', 'Samples/Ubuntu-Yaru/icons.zip', 'Samples/Ubuntu-Yaru/README.zh-CN.md')) {
        if ($required -notin $names) { throw "Release archive is missing Ubuntu theme input: $required" }
    }
} finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "Release directory: $stagingDirectory"
Write-Output "Archive: $resolvedArchive"
Write-Output "SHA-256: $hash"
