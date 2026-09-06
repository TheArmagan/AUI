#!/usr/bin/env sh
# Builds the AUI language server and the VS Code extension, then installs the extension.
# Needs: dotnet SDK 8+, node 18+, and `code` on PATH.
set -e
cd "$(dirname "$0")"
for tool in dotnet node npm code; do
  command -v "$tool" >/dev/null 2>&1 || { echo "$tool is not on PATH" >&2; exit 1; }
done
npm install --no-audit --no-fund
npm run package
vsix=$(ls -t aui-*.vsix | head -n 1)
[ -n "$vsix" ] || { echo "no .vsix was produced" >&2; exit 1; }
code --install-extension "$vsix" --force
echo "installed $vsix. Reload VS Code windows that have .aui files open."
