<#
  Noty for Windows — build helper.

    .\build.ps1              debug build
    .\build.ps1 release      optimised build
    .\build.ps1 release run  build, then relaunch
    .\build.ps1 publish      single self-contained Noty.exe in .\publish
#>
param(
    [string]$Mode = "debug",
    [string]$Then = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\Noty\Noty.csproj"

function Stop-Noty {
    Get-Process Noty -ErrorAction SilentlyContinue | Stop-Process -Force
}

switch ($Mode.ToLower()) {
    "publish" {
        Stop-Noty
        dotnet publish $project -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -o (Join-Path $PSScriptRoot "publish")
        Write-Host "publish\Noty.exe"
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
