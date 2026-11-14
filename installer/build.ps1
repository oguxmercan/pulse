# Pulse kurulum paketi (MSI) üretir.
# Gereksinim: dotnet tool install --global wix --version 5.0.2
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

Push-Location $root
try {
    dotnet publish -c Release -o dist
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }

    wix build installer/Package.wxs -d "PayloadDir=$root\dist" -o PulseSetup.msi -arch x64
    if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

    "PulseSetup.msi -> $root\PulseSetup.msi"
}
finally { Pop-Location }
