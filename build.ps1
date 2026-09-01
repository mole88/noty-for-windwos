<#
  Noty for Windows — build helper.

    .\build.ps1              debug build
    .\build.ps1 release      optimised build
    .\build.ps1 release run  build, then relaunch
    .\build.ps1 publish      single self-contained Noty.exe in .\publish
    .\build.ps1 installer    Inno Setup installer in .\dist
    .\build.ps1 installer -Version 1.0.2
#>
param(
    [string]$Mode = "debug",
    [string]$Then = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\Noty\Noty.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"
$installerScript = Join-Path $PSScriptRoot "installer\Noty.iss"
$distDir = Join-Path $PSScriptRoot "dist"

if ((-not [string]::IsNullOrWhiteSpace($Version)) -and
    $Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Version must contain three or four numeric parts, for example 1.0.2"
}

function Stop-Noty {
    Get-Process Noty -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Publish-Noty {
    Stop-Noty
    $publishArguments = @(
        "publish", $project,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $publishDir
    )
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $publishArguments += "-p:Version=$Version"
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Copy-Item (Join-Path $PSScriptRoot "LICENSE") $publishDir -Force
}

function Get-ProjectVersion {
    if (-not [string]::IsNullOrWhiteSpace($Version)) { return $Version }

    $projectVersion = (& dotnet msbuild $project --nologo -getProperty:Version | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($projectVersion)) {
        throw "Could not read the application version from Noty.csproj"
    }
    return $projectVersion
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
        $resolvedVersion = Get-ProjectVersion
        $iscc = Find-InnoCompiler
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
        & $iscc "/DMyAppVersion=$resolvedVersion" "/DPublishDir=$publishDir" "/O$distDir" $installerScript
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
        Write-Host (Join-Path $distDir "Noty-Setup-$resolvedVersion.exe")
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
