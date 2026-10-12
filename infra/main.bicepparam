using 'main.bicep'

// Everything personal or secret comes from environment variables of your PowerShell session (infra/README.md) –
// this file stays generic and safe in a public repository.
// West Europe doesn't accept new subscriptions (Oct 2026: "not accepting new customers") – Frankfurt is close to Prague.
// Another EU region if this one refuses too: polandcentral, swedencentral, northeurope.
param location = readEnvironmentVariable('JOBSCOUT_LOCATION', 'germanywestcentral')
// PostgreSQL isn't offered to this subscription in Germany West Central (empty version list) – Warsaw is.
param postgresLocation = readEnvironmentVariable('JOBSCOUT_PG_LOCATION', 'polandcentral')
param name = 'jobscout'
param image = readEnvironmentVariable('JOBSCOUT_IMAGE', 'ghcr.io/stanislavmatzke-ops/job-scout:latest')
param deployApps = bool(readEnvironmentVariable('JOBSCOUT_DEPLOY_APPS', 'false'))
param adminObjectId = readEnvironmentVariable('JOBSCOUT_ADMIN_OBJECT_ID')
param postgresAdminPassword = readEnvironmentVariable('JOBSCOUT_PG_PASSWORD')
param googleClientId = readEnvironmentVariable('JOBSCOUT_GOOGLE_CLIENT_ID', '')
param microsoftClientId = readEnvironmentVariable('JOBSCOUT_MICROSOFT_CLIENT_ID', '')
param admins = readEnvironmentVariable('JOBSCOUT_ADMINS', '')
param customDomain = readEnvironmentVariable('JOBSCOUT_DOMAIN', 'jobscout.stanislavmatzke.com')
param customDomainCertificate = readEnvironmentVariable('JOBSCOUT_DOMAIN_CERT', '')
