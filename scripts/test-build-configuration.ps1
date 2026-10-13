param([string]$PublishedExecutable = '')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'avalonia-build-configuration.ps1')

function Assert-BuildContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Set-TestVersion([string]$Version, [switch]$Conditional, [switch]$Duplicate) {
    [xml]$metadata = '<Project><PropertyGroup><Version /></PropertyGroup></Project>'
    $metadata.Project.PropertyGroup.Version = $Version
    if ($Conditional) { $metadata.Project.PropertyGroup.SetAttribute('Condition', "'`$(Configuration)' == 'Debug'") }
    if ($Duplicate) {
        $node = $metadata.CreateElement('Version')
        $node.InnerText = $Version
        $metadata.Project.PropertyGroup.AppendChild($node) | Out-Null
    }
    $metadata.Save((Join-Path $testRoot 'fork\AlchemyStars\BuildVersion.props'))
}

# Fake native commands exercise orchestration failures without compiling or launching the GUI.
function dotnet {
    $arguments = @($args)
    Assert-BuildContract ($PWD.Path -eq $testRoot) 'The pinned SDK would not resolve from the repository root.'
    $dotnetStages.Add($Arguments[0])
    $global:LASTEXITCODE = if ($Arguments[0] -eq $failureStage) { 17 } else { 0 }
    if ($Arguments[0] -eq 'publish' -and $LASTEXITCODE -eq 0) {
        $destination = $Arguments[[Array]::IndexOf($Arguments, '-o') + 1]
        New-Item -ItemType File -Path (Join-Path $destination 'AlchemyStars.Avalonia.exe') | Out-Null
    }
}

function cargo {
    $global:LASTEXITCODE = if ($failureStage -eq 'rust') { 19 } else { 0 }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AlchemyStars-build-contract-' + [guid]::NewGuid().ToString('N'))
$originalLocation = $PWD.Path
$environmentNames = @('DOTNET_CLI_TELEMETRY_OPTOUT', 'AVALONIA_TELEMETRY_OPTOUT',
    'ALCHEMY_STARS_SETTINGS_PATH', 'ALCHEMY_MODEL_MERGER_AMMO_FIXTURES')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    foreach ($directory in @('scripts', 'fork\AlchemyStars\src\AlchemyStars.Avalonia',
        'third_party\modelmerger\rust\target\release')) {
        New-Item -ItemType Directory -Path (Join-Path $testRoot $directory) -Force | Out-Null
    }
    foreach ($name in @('avalonia-build-configuration.ps1', 'verify-avalonia-aot.ps1', 'build-model-merger.ps1', 'build-avalonia-aot-release.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $testRoot 'scripts')
    }
    New-Item -ItemType File -Path (Join-Path $testRoot 'third_party\modelmerger\rust\target\release\alchemy-model-merger.exe') | Out-Null

    foreach ($version in @('1.3.0-preview.34', '1.3.0-preview.35', '1.3.0', '1.4.0-rc.1')) {
        Set-TestVersion $version
        $build = Get-AvaloniaBuildConfiguration -RepositoryRoot $testRoot
        Assert-BuildContract ($build.Version -eq $version) 'Version was not read from the shared metadata.'
        Assert-BuildContract ($build.PublishDirectory -eq (Join-Path $testRoot "output\avalonia-aot-$version")) 'Publish directories must include the complete version.'
        Assert-BuildContract ($build.ReleaseNotesPath -eq (Join-Path $testRoot "docs\releases\$version.zh-CN.md")) 'Release notes resolved to another version.'
    }
    foreach ($version in @('', '../outside', '1.3', '$(Version)', '1.3.0/escape', '1.3.0-preview.35+../../escape')) {
        Set-TestVersion $version
        $rejected = $false
        try { Get-AvaloniaBuildConfiguration -RepositoryRoot $testRoot | Out-Null } catch { $rejected = $true }
        Assert-BuildContract $rejected "Invalid metadata accepted: $version"
    }
    foreach ($case in @('conditional', 'duplicate')) {
        Set-TestVersion '1.3.0-preview.35' -Conditional:($case -eq 'conditional') -Duplicate:($case -eq 'duplicate')
        $rejected = $false
        try { Get-AvaloniaBuildConfiguration -RepositoryRoot $testRoot | Out-Null } catch { $rejected = $true }
        Assert-BuildContract $rejected "Ambiguous metadata accepted: $case"
    }

    Set-TestVersion '1.3.0-preview.35'
    $build = Get-AvaloniaBuildConfiguration -RepositoryRoot $testRoot
    foreach ($withEnvironment in @($false, $true)) {
        foreach ($stage in @('rust', 'restore', 'publish', 'success')) {
            foreach ($name in $environmentNames) {
                if ($withEnvironment) { [Environment]::SetEnvironmentVariable($name, 'caller-value', 'Process') }
                else { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
            }
            New-Item -ItemType Directory -Path $build.PublishDirectory -Force | Out-Null
            $previousPublish = Join-Path $build.PublishDirectory 'previous-build.txt'
            New-Item -ItemType File -Path $previousPublish -Force | Out-Null
            $script:failureStage = $stage
            $script:dotnetStages = [Collections.Generic.List[string]]::new()
            $caught = $null
            try { & (Join-Path $testRoot 'scripts\verify-avalonia-aot.ps1') -BuildOnly | Out-Null }
            catch { $caught = $_ }
            Assert-BuildContract (($null -eq $caught) -eq ($stage -eq 'success')) "Wrong outcome for $stage : $caught"
            if ($stage -in @('rust', 'restore')) {
                Assert-BuildContract (Test-Path -LiteralPath $previousPublish) "Prerequisite failure deleted the last publish: $stage"
            }
            if ($stage -eq 'success') {
                Assert-BuildContract (($dotnetStages -join ',') -eq 'restore,publish') 'Publish was not preceded by restore.'
                Assert-BuildContract (Test-Path -LiteralPath (Join-Path $build.PublishDirectory 'AlchemyStars.Avalonia.exe')) 'Successful build produced no executable.'
            }
            Assert-BuildContract ($PWD.Path -eq $originalLocation) 'Verification changed the caller working directory.'
            foreach ($name in $environmentNames) {
                $expected = if ($withEnvironment) { 'caller-value' } else { $null }
                Assert-BuildContract ([Environment]::GetEnvironmentVariable($name, 'Process') -eq $expected) "Verification leaked environment variable $name after $stage"
            }
        }
    }
    Write-Output 'Build contracts: 4 version mappings, 8 invalid/ambiguous metadata cases, 8 pipeline/environment cases PASS'

    if (-not [string]::IsNullOrWhiteSpace($PublishedExecutable)) {
        $publishedVersion = (Get-Item -LiteralPath $PublishedExecutable).VersionInfo.ProductVersion
        Assert-BuildContract (-not [string]::IsNullOrWhiteSpace($publishedVersion)) 'Executable has no product version.'
        $wrongVersion = if ($publishedVersion -eq '0.0.0') { '0.0.1' } else { '0.0.0' }
        Set-TestVersion $wrongVersion
        $build = Get-AvaloniaBuildConfiguration -RepositoryRoot $testRoot
        New-Item -ItemType Directory -Path $build.PublishDirectory, (Split-Path -Parent $build.ReleaseNotesPath) -Force | Out-Null
        Copy-Item -LiteralPath $PublishedExecutable -Destination (Join-Path $build.PublishDirectory 'AlchemyStars.Avalonia.exe')
        New-Item -ItemType File -Path $build.ReleaseNotesPath | Out-Null
        $releaseDirectory = Join-Path $testRoot "release\Alchemy Stars $wrongVersion"
        New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
        $previousPackage = Join-Path $releaseDirectory 'previous-package.txt'
        New-Item -ItemType File -Path $previousPackage | Out-Null
        $rejected = $false
        try { & (Join-Path $testRoot 'scripts\build-avalonia-aot-release.ps1') -SkipVerification | Out-Null }
        catch { $rejected = $_.Exception.Message.StartsWith('Published executable version mismatch:') }
        Assert-BuildContract $rejected 'Packaging accepted a stale executable under a different version.'
        Assert-BuildContract (Test-Path -LiteralPath $previousPackage) 'Packaging removed the previous package before checking the executable version.'
        Write-Output 'Real executable packaging: version mismatch rejected before staging replacement PASS'
    }
} finally {
    foreach ($name in $environmentNames) {
        if ($null -eq $savedEnvironment[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    }
    if ($PWD.Path -ne $originalLocation) { Set-Location -LiteralPath $originalLocation }
    # Only the unique directory created by this test is disposable.
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
