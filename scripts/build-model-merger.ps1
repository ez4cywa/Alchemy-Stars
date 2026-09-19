param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifest = Join-Path $repositoryRoot 'third_party\modelmerger\rust\Cargo.toml'
if (-not $SkipTests) {
    & cargo test --locked --manifest-path $manifest --workspace
    if ($LASTEXITCODE -ne 0) { throw 'ModelMerger Rust tests failed.' }
}
& cargo build --locked --release --manifest-path $manifest
if ($LASTEXITCODE -ne 0) { throw 'ModelMerger native companion build failed.' }
$executable = Join-Path $repositoryRoot 'third_party\modelmerger\rust\target\release\alchemy-model-merger.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw "Missing native companion: $executable" }
Write-Output "ModelMerger native companion: $executable"
