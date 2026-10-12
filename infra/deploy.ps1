<#
  Deploys Job Scout to Azure (infra/README.md).
    ./infra/deploy.ps1                   # the apps with the newest image built from main
    ./infra/deploy.ps1 -Tag sha-1a2b3c4  # an exact image
    ./infra/deploy.ps1 -Base             # base resources only (Key Vault, PostgreSQL, e-mail service)
  Personal values live in infra/local.env.ps1 (not in git – copy infra/local.env.example.ps1).
#>
param(
    [string]$Tag,
    [switch]$Base
)
$ErrorActionPreference = 'Stop'

$local = Join-Path $PSScriptRoot 'local.env.ps1'
if (-not (Test-Path $local)) { throw "Missing $local – copy infra/local.env.example.ps1 and fill it in." }
. $local

if (-not $env:JOBSCOUT_PG_PASSWORD) {
    $secure = Read-Host 'PostgreSQL admin password' -AsSecureString
    $env:JOBSCOUT_PG_PASSWORD = [System.Net.NetworkCredential]::new('', $secure).Password
}
$env:JOBSCOUT_ADMIN_OBJECT_ID = az ad signed-in-user show --query id -o tsv
$env:JOBSCOUT_DOMAIN_CERT = az containerapp env certificate list -g rg-jobscout -n cae-jobscout --managed-certificates-only --query "[0].name" -o tsv 2>$null
$env:JOBSCOUT_DEPLOY_APPS = if ($Base) { 'false' } else { 'true' }

if (-not $Base) {
    if (-not $Tag) {
        # The newest image = the last successful "Docker image" run on main
        $sha = gh run list --workflow docker.yml --branch main --status success --limit 1 --json headSha --jq '.[0].headSha' 2>$null
        if (-not $sha) { throw 'No -Tag given and no successful Docker image run found (is the GitHub CLI installed and signed in?).' }
        $Tag = "sha-$($sha.Substring(0, 7))"
    }
    $env:JOBSCOUT_IMAGE = "ghcr.io/stanislavmatzke-ops/job-scout:$Tag"
    Write-Host "Deploying image $env:JOBSCOUT_IMAGE"
}

$deployment = if ($Base) { 'base' } else { 'apps' }
az deployment group create -g rg-jobscout -n $deployment -p (Join-Path $PSScriptRoot 'main.bicepparam') --query properties.outputs -o json
