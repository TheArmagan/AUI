# Builds the AUI language server and the VS Code extension, then installs the extension.
# Needs: dotnet SDK 8+, node 18+, and `code` on PATH.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
foreach ($tool in @("dotnet", "node", "npm", "code")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not on PATH" }
}
npm install --no-audit --no-fund
npm run package
$vsix = Get-ChildItem -Filter "aui-*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $vsix) { throw "no .vsix was produced" }
code --install-extension $vsix.FullName --force
Write-Host "installed $($vsix.Name). Reload VS Code windows that have .aui files open."
