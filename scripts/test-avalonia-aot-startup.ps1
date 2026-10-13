param(
    [string]$PublishDirectory = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    . (Join-Path $PSScriptRoot 'avalonia-build-configuration.ps1')
    $PublishDirectory = (Get-AvaloniaBuildConfiguration).PublishDirectory
}
$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$executable = Join-Path $resolvedPublishDirectory 'AlchemyStars.Avalonia.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Native AOT executable was not found: $executable"
}

$process = Start-Process `
    -FilePath $executable `
    -ArgumentList '--startup-smoke' `
    -WorkingDirectory $resolvedPublishDirectory `
    -WindowStyle Hidden `
    -Wait `
    -PassThru

if ($process.ExitCode -ne 0) {
    $unsignedExitCode = [uint32]($process.ExitCode -band 0xffffffffL)
    throw ('Native AOT window startup failed with exit code 0x{0:X8}.' -f $unsignedExitCode)
}

Write-Output 'Native AOT window startup smoke: PASS'
