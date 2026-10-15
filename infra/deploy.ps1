<#
  Deploys Job Scout to Azure (infra/README.md).
    ./infra/deploy.ps1                   # the apps with the newest image built from main
    ./infra/deploy.ps1 -Tag sha-1a2b3c4  # an exact image
    ./infra/deploy.ps1 -Base             # base resources only (Key Vault, PostgreSQL, e-mail service)
  Personal values live in infra/local.env.ps1 (not in git - copy infra/local.env.example.ps1).
  The PostgreSQL password is asked for (and changed!) only with -Base; deploying the apps never touches it.
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

if ($Base) {
    if (-not $env:JOBSCOUT_PG_PASSWORD) {
        Write-Host 'The password you enter BECOMES the PostgreSQL admin password (server + db-connection in Key Vault).'
        $secure = Read-Host 'PostgreSQL admin password' -AsSecureString
        $env:JOBSCOUT_PG_PASSWORD = [System.Net.NetworkCredential]::new('', $secure).Password
    }
    if (-not $env:JOBSCOUT_PG_PASSWORD) { throw 'An empty PostgreSQL password is not allowed.' }
}
else {
    $env:JOBSCOUT_PG_PASSWORD = ''      # pass 2 never changes the database password (main.bicep)
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
            # No GitHub CLI: the newest checked-out commit that builds an image. Commits touching only docs/** or *.md
            # are skipped by docker.yml (paths-ignore), so they have no image - the same filter is applied here.
            $sha = git log -1 --format=%H -- . ':(exclude)docs/**' ':(exclude,glob)**/*.md'
            Write-Host 'GitHub CLI not found - using the newest checked-out commit with an image. Is it pushed and is its Docker image green in Actions?'
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

# -Base may have written a new db-connection (a new password). Running apps keep their copy of a Key Vault secret even
# across restarts - set the same reference again so they read it, and restart the API. The morning job reads it at its next run.
if ($Base) {
    $kv = az deployment group show -g rg-jobscout -n base --query properties.outputs.keyVaultName.value -o tsv --only-show-errors
    $identity = az identity show -g rg-jobscout -n id-jobscout --query id -o tsv --only-show-errors
    $ref = "db-connection=keyvaultref:https://$kv.vault.azure.net/secrets/db-connection,identityref:$identity"
    if (az containerapp show -g rg-jobscout -n ca-jobscout-api --query name -o tsv --only-show-errors 2>$null) {
        az containerapp secret set -g rg-jobscout -n ca-jobscout-api --secrets $ref --only-show-errors -o none
        $revision = az containerapp show -g rg-jobscout -n ca-jobscout-api --query properties.latestRevisionName -o tsv --only-show-errors
        az containerapp revision restart -g rg-jobscout -n ca-jobscout-api --revision $revision --only-show-errors -o none
        Write-Host "API: db-connection reloaded, revision $revision restarted."
    }
    if (az containerapp job show -g rg-jobscout -n caj-jobscout-daily --query name -o tsv --only-show-errors 2>$null) {
        az containerapp job secret set -g rg-jobscout -n caj-jobscout-daily --secrets $ref --only-show-errors -o none
        Write-Host 'Morning job: db-connection reloaded.'
    }
}
