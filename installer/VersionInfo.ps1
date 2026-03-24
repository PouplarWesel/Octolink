param(
    [string]$ProjectPath = "..\Octolink\Octolink.csproj"
)

[xml]$xml = Get-Content $ProjectPath
$version = $xml.Project.PropertyGroup.Version
if (-not $version) {
    $version = $xml.Project.PropertyGroup.AssemblyVersion
}

if (-not $version) {
    throw "Could not find Version in $ProjectPath"
}

Write-Output $version
