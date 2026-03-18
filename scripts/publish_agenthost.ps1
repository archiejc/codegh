#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
$projectPath = Join-Path $repoRoot "src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repoRoot "dist/agenthost"
}

Write-Host "[publish_agenthost] Project: $projectPath"
Write-Host "[publish_agenthost] Configuration: $Configuration"
Write-Host "[publish_agenthost] Output: $Output"

dotnet publish $projectPath -c $Configuration -o $Output
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "[publish_agenthost] Done."
Write-Host "[publish_agenthost] Next:"
Write-Host "  python3 $repoRoot/scripts/smoke_mcp_stdio.py --agent-host $Output"
