# Infrastructure (Azure, Bicep)

Everything Job Scout needs in Azure, as code (ADR-0018, ADR-0019): `main.bicep` + `main.bicepparam`.
Personal values and secrets never live in the repository – the parameter file reads them from environment
variables, the apps read secrets from Key Vault through a managed identity.

| Resource | What for |
|---|---|
| `id-jobscout` (managed identity) | the apps read Key Vault as this identity |
| `kv-jobscout-…` (Key Vault, RBAC) | secrets: `db-connection`, `anthropic-api-key`, `google-client-secret`, `microsoft-client-secret` |
| `psql-jobscout-…` (PostgreSQL Flexible, B1ms) | the database `jobscout` |
| `log-jobscout` (Log Analytics, 0.2 GB/day cap) | logs of both apps |
| `cae-jobscout` (Container Apps environment) | where the apps run |
| `ca-jobscout-api` (Container App, 1 replica) | web + API |
| `caj-jobscout-daily` (Container Apps Job, cron UTC) | the morning run |

## Deploy (PowerShell, from the repository root)

```powershell
# Once per session
$env:JOBSCOUT_ADMIN_OBJECT_ID = az ad signed-in-user show --query id -o tsv
$env:JOBSCOUT_PG_PASSWORD     = "<strong password – keep it in your password manager>"

# Pass 1 – base resources (PostgreSQL takes ~10 minutes)
az deployment group create -g rg-jobscout -n base -p infra/main.bicepparam

# Secrets into Key Vault (db-connection is written by the deployment itself)
$kv = az deployment group show -g rg-jobscout -n base --query properties.outputs.keyVaultName.value -o tsv
az keyvault secret set --vault-name $kv --name anthropic-api-key       --value $env:ANTHROPIC_API_KEY
az keyvault secret set --vault-name $kv --name google-client-secret    --value "<Google client secret>"
az keyvault secret set --vault-name $kv --name microsoft-client-secret --value "<Microsoft client secret>"

# Pass 2 – the apps
$env:JOBSCOUT_GOOGLE_CLIENT_ID    = "<Google client ID>"
$env:JOBSCOUT_MICROSOFT_CLIENT_ID = "<Microsoft client ID>"
$env:JOBSCOUT_IMAGE               = "ghcr.io/stanislavmatzke-ops/job-scout:sha-<commit>"
$env:JOBSCOUT_DEPLOY_APPS         = "true"
az deployment group create -g rg-jobscout -n apps -p infra/main.bicepparam
az deployment group show -g rg-jobscout -n apps --query properties.outputs.apiUrl.value -o tsv
```

A new version = the same pass 2 with a new `JOBSCOUT_IMAGE`. Admins: `$env:JOBSCOUT_ADMINS = "microsoft:<subject>"`
(comma-separated), then pass 2 again.

**The database password belongs to pass 1 only.** `./infra/deploy.ps1` (the apps) neither asks for it nor changes it;
`./infra/deploy.ps1 -Base` sets the password you enter on the server *and* in `db-connection`, then makes the running API
and the morning job read the secret again – Container Apps keep their copy of a Key Vault secret even across a restart,
until the reference is set again (`az containerapp secret set … keyvaultref:…`).

## Custom domain (once)

```powershell
# 1. DNS at Cloudflare (DNS only – grey cloud): CNAME jobscout → <fqdn>, TXT asuid.jobscout → <verification id>
az containerapp show -g rg-jobscout -n ca-jobscout-api --query "{fqdn:properties.configuration.ingress.fqdn, verificationId:properties.customDomainVerificationId}" -o table
# 2. Bind the hostname with a free managed certificate (takes a few minutes)
az containerapp hostname add  -g rg-jobscout -n ca-jobscout-api --hostname jobscout.stanislavmatzke.com
az containerapp hostname bind -g rg-jobscout -n ca-jobscout-api --hostname jobscout.stanislavmatzke.com --environment cae-jobscout --validation-method CNAME
# 3. Remember the certificate name for every later deployment (otherwise pass 2 would drop the domain)
$env:JOBSCOUT_DOMAIN_CERT = az containerapp env certificate list -g rg-jobscout -n cae-jobscout --managed-certificates-only --query "[0].name" -o tsv
```

## Deploy a new version (after the first setup)

```powershell
./infra/deploy.ps1                    # newest image from main (needs the GitHub CLI: winget install GitHub.cli; gh auth login)
./infra/deploy.ps1 -Tag sha-1a2b3c4   # or an exact one
```

## E-mails (7f, once)

```powershell
az provider register --namespace Microsoft.Communication
./infra/deploy.ps1 -Base                                   # creates the e-mail service + the domain (unverified)
az deployment group show -g rg-jobscout -n base --query properties.outputs.emailDnsRecords.value -o json
# Add the records to Cloudflare (DNS only): Domain (TXT @), SPF (TXT @), DKIM + DKIM2 (CNAME selector1-…/selector2-…)
az communication email domain initiate-verification -g rg-jobscout --email-service-name ecs-jobscout --domain-name stanislavmatzke.com --verification-type Domain
az communication email domain initiate-verification -g rg-jobscout --email-service-name ecs-jobscout --domain-name stanislavmatzke.com --verification-type SPF
az communication email domain initiate-verification -g rg-jobscout --email-service-name ecs-jobscout --domain-name stanislavmatzke.com --verification-type DKIM
az communication email domain initiate-verification -g rg-jobscout --email-service-name ecs-jobscout --domain-name stanislavmatzke.com --verification-type DKIM2
az communication email domain show -g rg-jobscout --email-service-name ecs-jobscout -n stanislavmatzke.com --query verificationStates -o json
# All four "Verified" → JOBSCOUT_EMAIL_VERIFIED = 'true' in local.env.ps1, then:
./infra/deploy.ps1 -Base
./infra/deploy.ps1
```

## Operate

```powershell
az containerapp logs show -g rg-jobscout -n ca-jobscout-api --follow           # API log
az containerapp job start -g rg-jobscout -n caj-jobscout-daily                 # run the morning job now
az containerapp job execution list -g rg-jobscout -n caj-jobscout-daily -o table
```
