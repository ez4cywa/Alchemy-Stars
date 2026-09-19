param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repositoryRoot 'fork\AlchemyStars\src\AlchemyStars.Avalonia\AlchemyStars.Avalonia.csproj'
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'output'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $outputRoot 'avalonia-aot-preview31'))
$bundledDotnet = Join-Path $repositoryRoot 'output\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $bundledDotnet) { $bundledDotnet } else { 'dotnet' }
function Assert-OutputChild([string]$Path) {
    if (-not $Path.StartsWith($outputRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the repository output directory: $Path"
    }
}
Assert-OutputChild $publishDirectory
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:ALCHEMY_STARS_SETTINGS_PATH = Join-Path $outputRoot 'avalonia-aot-verification-settings.json'

& (Join-Path $PSScriptRoot 'build-model-merger.ps1')
if ($LASTEXITCODE -ne 0) { throw 'ModelMerger verification failed.' }

& $dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    --no-restore `
    -o $publishDirectory `
    --nologo `
    -p:NuGetAudit=false `
    -p:UsedAvaloniaProducts=
if ($LASTEXITCODE -ne 0) {
    throw "Native AOT publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $publishDirectory 'AlchemyStars.Avalonia.exe'
& (Join-Path $PSScriptRoot 'verify-model-merger.ps1') -Executable (Join-Path $publishDirectory 'Converters\alchemy-model-merger.exe')
if ($LASTEXITCODE -ne 0) { throw 'Packaged ModelMerger companion verification failed.' }
$mergerChecks = Join-Path $outputRoot ('model-merger-native-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($mergerChecks) | Out-Null
$env:ALCHEMY_MODEL_MERGER_AMMO_FIXTURES = Join-Path $mergerChecks 'ammunition-fixture'
& cargo run --manifest-path (Join-Path $repositoryRoot 'third_party\modelmerger\rust\Cargo.toml') --locked -p alchemy-model-merger --example generate_ammunition_fixture -- $env:ALCHEMY_MODEL_MERGER_AMMO_FIXTURES
if ($LASTEXITCODE -ne 0) { throw 'Ammunition fixture generation failed.' }
$mergerFixture = Join-Path $repositoryRoot 'third_party\modelmerger\tests\fixtures\rust-migration\golden-small\part-00.cast'
$mergerPreviewCheck = Start-Process -FilePath $executable -ArgumentList ('--model-merger-preview-smoke --model-merger-preview-file "' + $mergerFixture + '"') -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
if ($mergerPreviewCheck.ExitCode -ne 0) { throw 'Native ModelMerger depth/index/preview checks failed.' }
$mergerUiCheck = Start-Process -FilePath $executable -ArgumentList ('--model-merger-ui-smoke --model-merger-preview-ui "' + $mergerFixture + '" --page model-merger --window-size 900x600 --render-smoke "' + (Join-Path $mergerChecks 'workspace.png') + '"') -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
if ($mergerUiCheck.ExitCode -ne 0) { throw 'Native ModelMerger workspace/preview UI checks failed.' }
Write-Output "ModelMerger Native AOT checks: PASS ($mergerChecks)"
$accessibilityInteraction = Start-Process -FilePath $executable -ArgumentList '--startup-smoke --accessibility-interaction-smoke' -WindowStyle Hidden -Wait -PassThru
if ($accessibilityInteraction.ExitCode -ne 0) { throw 'Native AOT accessibility interaction regression failed.' }
$selfTest = Start-Process `
    -FilePath $executable `
    -ArgumentList '--self-test' `
    -WorkingDirectory $publishDirectory `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($selfTest.ExitCode -ne 0) {
    throw "Native AOT contract self-test failed with exit code $($selfTest.ExitCode)."
}
foreach ($updateTest in @('--update-self-test', '--update-helper-self-test')) {
    $updateProcess = Start-Process -FilePath $executable -ArgumentList $updateTest -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
    if ($updateProcess.ExitCode -ne 0) { throw "Native AOT updater test $updateTest failed: $($updateProcess.ExitCode)" }
    Write-Output "Native AOT updater ${updateTest}: PASS"
}

& (Join-Path $PSScriptRoot 'test-avalonia-aot-startup.ps1') -PublishDirectory $publishDirectory
$accessibilityScript = Join-Path $PSScriptRoot 'test-avalonia-accessibility.ps1'
Start-Sleep -Seconds 5
try {
    & $accessibilityScript -PublishDirectory $publishDirectory
} catch {
    Write-Warning "First Native AOT UI Automation attachment failed; retrying once: $($_.Exception.Message)"
    Start-Sleep -Seconds 5
    & $accessibilityScript -PublishDirectory $publishDirectory
}

$codWeaponDbSmoke = Start-Process `
    -FilePath $executable `
    -ArgumentList '--cod-weapon-db-smoke' `
    -WorkingDirectory $publishDirectory `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($codWeaponDbSmoke.ExitCode -ne 0) { throw "Native AOT COD weapon database smoke failed with exit code $($codWeaponDbSmoke.ExitCode)." }
Write-Output 'Native AOT COD weapon database catalog/search/icon smoke: PASS'

foreach ($interactionTest in @('--shortcuts-smoke', '--textmenu-smoke', '--inspector-smoke', '--window-chrome-smoke', '--shared-base-batch-smoke', '--cod-weapon-db-ui-smoke')) {
    $interactionProcess = Start-Process -FilePath $executable -ArgumentList ('--startup-smoke ' + $interactionTest) -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
    if ($interactionProcess.ExitCode -ne 0) { throw "Native AOT interaction test $interactionTest failed: $($interactionProcess.ExitCode)" }
    Write-Output "Native AOT interaction ${interactionTest}: PASS"
}

$standardProject = Join-Path $repositoryRoot 'fork\AlchemyStars\Example\Hawk\HawkSprint.aprj'
$projectSmokeDirectory = [System.IO.Path]::GetFullPath((Join-Path $outputRoot 'avalonia-aot-project-smoke'))
Assert-OutputChild $projectSmokeDirectory
if (Test-Path -LiteralPath $projectSmokeDirectory) {
    Remove-Item -LiteralPath $projectSmokeDirectory -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($projectSmokeDirectory) | Out-Null

$projectData = Get-Content -LiteralPath $standardProject -Raw | ConvertFrom-Json
$projectInputs = @($projectData.Parts | ForEach-Object FilePath)
$projectInputs += @($projectData.Animations | ForEach-Object Name)
$projectInputs += @($projectData.Animations | ForEach-Object { $_.Layers | ForEach-Object Name })
if (@($projectInputs | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0) {
    $arguments = '--project-smoke "' + $standardProject + '" "' + $projectSmokeDirectory + '"'
    $projectSmoke = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
    if ($projectSmoke.ExitCode -ne 0) {
        throw "Native AOT standard-project export failed with exit code $($projectSmoke.ExitCode)."
    }
    $projectOutputs = @(Get-ChildItem -LiteralPath $projectSmokeDirectory -File)
    if ($projectOutputs.Count -ne $projectData.Animations.Count -or @($projectOutputs | Where-Object Length -le 0).Count -ne 0) {
        throw 'Native AOT standard-project export produced an invalid output set.'
    }
    Write-Output 'Native AOT standard Hawk project export: PASS'
    $castPreviewSource = $projectOutputs | Where-Object Extension -eq '.cast' | Select-Object -First 1
    if ($castPreviewSource) {
        $previewArguments = '--preview-test "' + $castPreviewSource.FullName + '"'
        $previewTest = Start-Process -FilePath $executable -ArgumentList $previewArguments -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
        if ($previewTest.ExitCode -ne 0) { throw 'Native AOT CAST preview sampling/rasterization regression failed.' }
        Write-Output 'Native AOT CAST preview: geometry, animation, reverse scrubbing and source integrity PASS'
    }
    $animationOnlySource = Join-Path $repositoryRoot 'fork\AlchemyStars\output\animation-only-cast\sat_vm_ar_hawk_sprint_alchemy_stars.cast'
    if (Test-Path -LiteralPath $animationOnlySource) {
        $skeletonArguments = '--preview-test "' + $animationOnlySource + '" --skeleton-project "' + $standardProject + '"'
        $skeletonTest = Start-Process -FilePath $executable -ArgumentList $skeletonArguments -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
        if ($skeletonTest.ExitCode -ne 0) { throw 'Native AOT animation-only CAST project-skeleton preview failed.' }
        Write-Output 'Native AOT animation-only CAST preview with matching project skeleton: PASS'
    }
} else {
    Write-Output 'Native AOT standard Hawk project export: SKIPPED (source assets unavailable)'
}

$renderDirectory = [System.IO.Path]::GetFullPath((Join-Path $outputRoot 'avalonia-aot-ui-smoke'))
Assert-OutputChild $renderDirectory
if (Test-Path -LiteralPath $renderDirectory) {
    Remove-Item -LiteralPath $renderDirectory -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($renderDirectory) | Out-Null
$renderCases = @(
    @{ Page = 'animations'; Culture = 'en-US'; Dialog = '' },
    @{ Page = 'parts'; Culture = 'zh-CN'; Dialog = '' },
    @{ Page = 'settings'; Culture = 'en-US'; Dialog = '' },
    @{ Page = 'about'; Culture = 'zh-CN'; Dialog = 'success' },
    @{ Page = 'cod-weapons'; Culture = 'zh-CN'; Dialog = '' }
)
foreach ($renderCase in $renderCases) {
    $renderPath = Join-Path $renderDirectory ($renderCase.Page + '.png')
    $arguments = '--culture "' + $renderCase.Culture + '" --window-size 900x600 --page "' + $renderCase.Page + '" --render-smoke "' + $renderPath + '" "' + $standardProject + '"'
    if ($renderCase.Dialog) {
        $arguments += ' --dialog "' + $renderCase.Dialog + '"'
    }
    if ($renderCase.Page -eq 'animations' -and $castPreviewSource) {
        $arguments += ' --preview-cast "' + $castPreviewSource.FullName + '"'
    }
    $renderProcess = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
    if ($renderProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $renderPath) -or (Get-Item -LiteralPath $renderPath).Length -eq 0) {
        throw "Native AOT render smoke failed for page '$($renderCase.Page)'."
    }
}
Write-Output 'Native AOT five-page and centered-dialog render smoke: PASS'

$verificationSettingsPath = $env:ALCHEMY_STARS_SETTINGS_PATH
try {
    foreach ($size in @('900x600', '1366x768')) {
        $appearanceDirectory = [System.IO.Path]::GetFullPath((Join-Path $outputRoot ('avalonia-aot-appearance-' + $size)))
        Assert-OutputChild $appearanceDirectory
        if (Test-Path -LiteralPath $appearanceDirectory) {
            Remove-Item -LiteralPath $appearanceDirectory -Recurse -Force
        }
        [System.IO.Directory]::CreateDirectory($appearanceDirectory) | Out-Null
        $env:ALCHEMY_STARS_SETTINGS_PATH = Join-Path $appearanceDirectory 'settings.json'
        $appearanceRenderPath = Join-Path $appearanceDirectory 'result.png'
        $appearanceArguments = '--desktop-smoke --utilities-smoke --appearance-smoke --culture en-US --window-size ' + $size + ' --render-smoke "' + $appearanceRenderPath + '" "' + $standardProject + '"'
        $appearanceProcess = Start-Process -FilePath $executable -ArgumentList $appearanceArguments -WorkingDirectory $publishDirectory -WindowStyle Hidden -Wait -PassThru
        if ($appearanceProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $appearanceRenderPath)) {
            throw "Native AOT appearance/import regression failed at $size (exit $($appearanceProcess.ExitCode))."
        }
        Write-Output "Native AOT appearance/import regression at ${size}: PASS"
    }
} finally {
    $env:ALCHEMY_STARS_SETTINGS_PATH = $verificationSettingsPath
}

if (Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File -Recurse) {
    throw 'Native AOT publish unexpectedly contains PDB files.'
}
Get-Item -LiteralPath $executable | Select-Object FullName, Length, LastWriteTime
