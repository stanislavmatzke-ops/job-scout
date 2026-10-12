<#
  Deploys Job Scout to Azure (infra/README.md).
    ./infra/deploy.ps1                   # the apps with the newest image built from main
    ./infra/deploy.ps1 -Tag sha-1a2b3c4  # an exact image
    ./infra/deploy.ps1 -Base             # base resources only (Key Vault, PostgreSQL, e-mail service)
  Personal values live in infra/local.env.ps1 (not in git - copy infra/local.env.example.ps1).
#>
param(
    [string]$Tag,
    [switch]$Base
)
# Not 'Stop': Windows PowerShell 5.1 turns any stderr line of az/gh (even a WARNING) into a terminating error.
# Native commands are checked by their exit code instead.
$ErrorActionPreference = 'Continue'

$local = Join-Path $PSScriptRoot 'local.env.ps1'
if (-not (Test-Path $local)) { throw "Missing $local - copy infra/local.env.example.ps1 and fill it in." }
. $local

if (-not $env:JOBSCOUT_PG_PASSWORD) {
    $secure = Read-Host 'PostgreSQL admin password' -AsSecureString
    $env:JOBSCOUT_PG_PASSWORD = [System.Net.NetworkCredential]::new('', $secure).Password
}
$env:JOBSCOUT_ADMIN_OBJECT_ID = az ad signed-in-user show --query id -o tsv --only-show-errors
if ($LASTEXITCODE -ne 0 -or -not $env:JOBSCOUT_ADMIN_OBJECT_ID) { throw 'az ad signed-in-user show failed - run az login first.' }
$env:JOBSCOUT_DOMAIN_CERT = az containerapp env certificate list -g rg-jobscout -n cae-jobscout --managed-certificates-only --query "[0].name" -o tsv --only-show-errors
$env:JOBSCOUT_DEPLOY_APPS = if ($Base) { 'false' } else { 'true' }

if (-not $Base) {
    if (-not $Tag) {
        if (Get-Command gh -ErrorAction SilentlyContinue) {
            # The newest image = the last successful "Docker image" run on main
            $sha = gh run list --workflow docker.yml --branch main --status success --limit 1 --json headSha --jq '.[0].headSha'
        }
        else {
            # No GitHub CLI: the commit you have checked out - it must be pushed and its Docker image built (Actions)
            $sha = git rev-parse HEAD
            Write-Host 'GitHub CLI not found - using the checked-out commit. Is it pushed and is its Docker image green in Actions?'
        }
        if (-not $sha) { throw 'No -Tag given and no commit found.' }
        $Tag = "sha-$($sha.Trim().Substring(0, 7))"
    }
    $env:JOBSCOUT_IMAGE = "ghcr.io/stanislavmatzke-ops/job-scout:$Tag"
    Write-Host "Deploying image $env:JOBSCOUT_IMAGE"
}

$deployment = if ($Base) { 'base' } else { 'apps' }
az deployment group create -g rg-jobscout -n $deployment -p (Join-Path $PSScriptRoot 'main.bicepparam') --query properties.outputs -o json --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "Deployment '$deployment' failed (see above)." }
Write-Host "Deployment '$deployment' done."

