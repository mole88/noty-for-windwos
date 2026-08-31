<#
  Noty for Windows — build helper.

    .\build.ps1              debug build
    .\build.ps1 release      optimised build
    .\build.ps1 release run  build, then relaunch
    .\build.ps1 publish      single self-contained Noty.exe in .\publish
    .\build.ps1 installer    Inno Setup installer in .\dist
#>
param(
    [string]$Mode = "debug",
    [string]$Then = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\Noty\Noty.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"
$installerScript = Join-Path $PSScriptRoot "installer\Noty.iss"
$distDir = Join-Path $PSScriptRoot "dist"

function Stop-Noty {
    Get-Process Noty -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Publish-Noty {
    Stop-Noty
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Copy-Item (Join-Path $PSScriptRoot "LICENSE") $publishDir -Force
}

function Get-ProjectVersion {
    $version = (& dotnet msbuild $project --nologo -getProperty:Version | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read the application version from Noty.csproj"
    }
    return $version
}

function Find-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw "Inno Setup 6 or 7 is required. Install it from https://jrsoftware.org/isdl.php"
}

switch ($Mode.ToLower()) {
    "publish" {
        Publish-Noty
        Write-Host "publish\Noty.exe"
    }
    "installer" {
        Publish-Noty
        $version = Get-ProjectVersion
        $iscc = Find-InnoCompiler
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
        & $iscc "/DMyAppVersion=$version" "/DPublishDir=$publishDir" "/O$distDir" $installerScript
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
        Write-Host (Join-Path $distDir "Noty-Setup-$version.exe")
    }
    "release" {
        Stop-Noty
        dotnet build $project -c Release --nologo
        if ($Then -eq "run") {
            Start-Process (Join-Path $PSScriptRoot "src\Noty\bin\Release\net8.0-windows\Noty.exe")
        }
    }
    default {
        Stop-Noty
        dotnet build $project -c Debug --nologo
        if ($Then -eq "run") {
            Start-Process (Join-Path $PSScriptRoot "src\Noty\bin\Debug\net8.0-windows\Noty.exe")
        }
    }
}
