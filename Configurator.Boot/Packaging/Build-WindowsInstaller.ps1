[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$CertificateThumbprint,

    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Find-Iscc {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $localApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidates = @(
        (Join-Path $localApplicationData 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $localApplicationData 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 7\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'ISCC.exe was not found. Install the latest stable Inno Setup and restart the terminal.'
}

function Find-SignTool {
    $command = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $kitsBin = Join-Path $programFilesX86 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsBin) {
        $candidate = Get-ChildItem -LiteralPath $kitsBin -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
    }

    throw 'signtool.exe was not found. Install Windows SDK or omit -CertificateThumbprint.'
}

function Invoke-AuthenticodeSign {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$SignTool
    )

    Invoke-Checked $SignTool 'sign' '/sha1' $CertificateThumbprint '/fd' 'SHA256' '/tr' $TimestampUrl '/td' 'SHA256' $Path
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solutionPath = Join-Path $repoRoot 'DesktopTemplate.slnx'
$bootProject = Join-Path $repoRoot 'Configurator.Boot\Configurator.Boot.csproj'
$publishDirectory = Join-Path $repoRoot "artifacts\publish\$Version\win-x64"
$releaseDirectory = Join-Path $repoRoot "artifacts\release\$Version"
$installerScript = Join-Path $PSScriptRoot 'Windows\PromFlow.Dispatcher.iss'

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory, $releaseDirectory -Force | Out-Null

Push-Location $repoRoot
try {
    Invoke-Checked 'dotnet' 'restore' $solutionPath
    Invoke-Checked 'dotnet' 'build' $solutionPath '-c' 'Release' '--no-restore'
    Invoke-Checked 'dotnet' 'test' $solutionPath '-c' 'Release' '--no-build' '--no-restore'
    Invoke-Checked 'dotnet' 'restore' $bootProject '-r' 'win-x64'
    Invoke-Checked 'dotnet' 'publish' $bootProject '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '--no-restore' '-p:PublishProfile=win-x64' "-p:Version=$Version" '-o' $publishDirectory
}
finally {
    Pop-Location
}

$applicationExe = Join-Path $publishDirectory 'PromFlow.Dispatcher.exe'
if (-not (Test-Path -LiteralPath $applicationExe)) {
    throw "Published executable was not found: $applicationExe"
}

$signTool = $null
if ($CertificateThumbprint) {
    $signTool = Find-SignTool
    Invoke-AuthenticodeSign -Path $applicationExe -SignTool $signTool
}
else {
    Write-Warning 'Certificate thumbprint was not supplied. The executable and installer will be unsigned.'
}

$iscc = Find-Iscc
$versionDefine = "/DAppVersion=$Version"
$sourceDefine = "/DSourceDir=$publishDirectory"
$outputDefine = "/DOutputDir=$releaseDirectory"
Invoke-Checked $iscc $versionDefine $sourceDefine $outputDefine $installerScript

$installerPath = Join-Path $releaseDirectory "PromFlow.Dispatcher-$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not created: $installerPath"
}

if ($CertificateThumbprint) {
    Invoke-AuthenticodeSign -Path $installerPath -SignTool $signTool
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$checksumPath = "$installerPath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($installerPath))" -Encoding ascii

Write-Host "Installer: $installerPath"
Write-Host "SHA-256:  $checksumPath"
