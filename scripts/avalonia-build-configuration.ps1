function Get-AvaloniaBuildConfiguration {
    param(
        [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
    $projectPath = Join-Path $root 'fork\AlchemyStars\src\AlchemyStars.Avalonia\AlchemyStars.Avalonia.csproj'
    $versionPath = Join-Path $root 'fork\AlchemyStars\BuildVersion.props'
    [xml]$metadata = Get-Content -LiteralPath $versionPath -Raw -ErrorAction Stop
    $versionNodes = $metadata.SelectNodes('/Project/PropertyGroup/Version')
    if ($versionNodes.Count -ne 1 -or $versionNodes[0].ParentNode.HasAttribute('Condition') -or
        $versionNodes[0].HasAttribute('Condition')) {
        throw "Expected one unconditional Version in $versionPath"
    }

    $version = $versionNodes[0].InnerText.Trim()
    # Release paths require a literal version; MSBuild expressions must not become filesystem paths.
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?$') {
        throw "Expected a literal release version in ${versionPath}: $version"
    }

    [pscustomobject]@{
        Version = $version
        ProjectPath = $projectPath
        PublishDirectory = Join-Path $root "output\avalonia-aot-$version"
        ReleaseNotesPath = Join-Path $root "docs\releases\$version.zh-CN.md"
    }
}
