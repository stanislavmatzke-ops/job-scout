// Job Scout – Azure infrastructure (ADR-0019, ADR-0018). Deployed into an existing resource group:
//   az deployment group create -g rg-jobscout -p infra/main.bicepparam      (values come from environment variables)
// Two passes (see infra/README.md): 1) deployApps=false – base resources; you then put the secrets into Key Vault;
// 2) deployApps=true – the API and the morning job, which read those secrets at start.
// The PostgreSQL server and its password (+ the db-connection secret) belong to pass 1 only: a new version of the apps
// must never change the database password (15 Oct 2026 – a mistyped password in an apps deployment changed the
// server's password while the running apps kept the old connection string).

targetScope = 'resourceGroup'

@description('Azure region – EU, the data stays in the EU. Resources may live in another region than the resource group.')
param location string = resourceGroup().location

@description('Region of the PostgreSQL server – may differ from the rest: a new subscription may be refused PostgreSQL in a region that is fine for everything else.')
param postgresLocation string = location

@description('Short name used in every resource name.')
param name string = 'jobscout'

@description('Pass 2 only: create the Container App (API) and the Container Apps Job (morning run).')
param deployApps bool = false

@description('Image to run – prefer an exact tag (sha-xxxxxxx) over latest, so you always know which build runs.')
param image string = 'ghcr.io/stanislavmatzke-ops/job-scout:latest'

@description('Your own Entra object ID (az ad signed-in-user show --query id -o tsv) – may write secrets into Key Vault.')
param adminObjectId string

@description('PostgreSQL administrator login.')
param postgresAdminLogin string = 'jobscoutadmin'

@secure()
@description('PostgreSQL administrator password – pass 1 only (it sets the server and the db-connection secret); ignored in pass 2. Never stored in the repo.')
param postgresAdminPassword string = ''

@description('Google OAuth client ID (not secret). Empty = no Google button.')
param googleClientId string = ''

@description('Microsoft (Entra) app client ID (not secret). Empty = no Microsoft button.')
param microsoftClientId string = ''

@description('Admins, comma-separated "provider:subject" (GET /api/me → identity). Empty until you have signed in once.')
param admins string = ''

@description('Custom domain of the API, e.g. jobscout.stanislavmatzke.com. Empty = only the *.azurecontainerapps.io address.')
param customDomain string = ''

@description('Name of the managed certificate for customDomain in the environment (created once by "az containerapp hostname bind" – infra/README.md).')
param customDomainCertificate string = ''

@description('E-mail domain (Azure Communication Services, 7f) – the sender is jobscout@<emailDomain>.')
param emailDomain string = 'stanislavmatzke.com'

@description('The e-mail domain is verified (its DNS records are in Cloudflare – infra/README.md): link it and turn e-mails on.')
param emailDomainVerified bool = false

@description('Who gets the "new access request" e-mails.')
param adminEmail string = ''

@description('The job wakes up every night hour (cron in UTC = 0–7 o\'clock Prague time in summer and winter); NightSchedule picks tonight\'s hour (1–5, never the same two nights in a row) and the retry an hour later.')
param morningCron string = '13 0-5,23 * * *'

var suffix = uniqueString(resourceGroup().id)
var keyVaultName = 'kv-${name}-${take(suffix, 6)}'        // globally unique, max 24 chars
var postgresName = 'psql-${name}-${take(suffix, 6)}'       // globally unique (DNS name)
var postgresHost = '${postgresName}.postgres.database.azure.com'

// ---------- identity: the apps read Key Vault as this identity (no secrets in the app definition) ----------
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}'
  location: location
}

// ---------- logs: small and cheap ----------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.2') }     // a runaway log can't run up the bill
  }
}

// ---------- Key Vault (RBAC) ----------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

var secretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var secretsOfficerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')

resource appsReadSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, 'secrets-user')
  scope: keyVault
  properties: { roleDefinitionId: secretsUserRole, principalId: identity.properties.principalId, principalType: 'ServicePrincipal' }
}

resource adminWritesSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, adminObjectId, 'secrets-officer')
  scope: keyVault
  properties: { roleDefinitionId: secretsOfficerRole, principalId: adminObjectId, principalType: 'User' }
}

// ---------- PostgreSQL Flexible Server – Burstable B1ms (free 12 months on the free account) ----------
// Pass 1 only (see the top of the file): pass 2 leaves the server, its password and db-connection untouched.
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = if (!deployApps) {
  name: postgresName
  location: postgresLocation
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32, autoGrow: 'Disabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = if (!deployApps) {
  parent: postgres
  name: name
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// 0.0.0.0 = "allow Azure services" – the Container Apps reach the server; the rest of the internet doesn't
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = if (!deployApps) {
  parent: postgres
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

// The connection string goes straight into Key Vault – the password never leaves this deployment.
// Written after the server took the password. Apps that already run keep their copy of the secret until they are told
// to read it again – deploy.ps1 -Base does that (infra/README.md).
resource dbSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!deployApps) {
  parent: keyVault
  name: 'db-connection'
  properties: {
    value: 'Host=${postgresHost};Database=${name};Username=${postgresAdminLogin};Password=${postgresAdminPassword};Ssl Mode=Require'
  }
  dependsOn: [postgres, database]
}

// ---------- E-mail: Azure Communication Services (7f) ----------
// The domain needs DNS records (verification, SPF, DKIM) before it can send – see the output emailDnsRecords.
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'ecs-${name}'
  location: 'global'
  properties: { dataLocation: 'Europe' }
}

resource emailDomainResource 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: emailDomain
  location: 'global'
  properties: { domainManagement: 'CustomerManaged', userEngagementTracking: 'Disabled' }
}

resource emailSender 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-04-01' = {
  parent: emailDomainResource
  name: 'jobscout'
  properties: { username: 'jobscout', displayName: 'Job Scout' }
}

resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-${name}-${take(suffix, 6)}'
  location: 'global'
  properties: {
    dataLocation: 'Europe'
    linkedDomains: emailDomainVerified ? [emailDomainResource.id] : []     // only a verified domain can be linked
  }
}

resource emailSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'email-connection'
  properties: { value: communication.listKeys().primaryConnectionString }
}

// ---------- Container Apps ----------
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${name}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Secrets the apps read from Key Vault (put them there between pass 1 and pass 2 – infra/README.md)
var kvUri = keyVault.properties.vaultUri
var commonSecrets = [
  { name: 'db-connection', keyVaultUrl: '${kvUri}secrets/db-connection', identity: identity.id }
  { name: 'anthropic-api-key', keyVaultUrl: '${kvUri}secrets/anthropic-api-key', identity: identity.id }
]
var authSecrets = concat(
  empty(googleClientId) ? [] : [{ name: 'google-client-secret', keyVaultUrl: '${kvUri}secrets/google-client-secret', identity: identity.id }],
  empty(microsoftClientId) ? [] : [{ name: 'microsoft-client-secret', keyVaultUrl: '${kvUri}secrets/microsoft-client-secret', identity: identity.id }])

var emailSecrets = emailDomainVerified
  ? [{ name: 'email-connection', keyVaultUrl: '${kvUri}secrets/email-connection', identity: identity.id }]
  : []
var emailEnv = concat(
  emailDomainVerified ? [
    { name: 'Email__ConnectionString', secretRef: 'email-connection' }
    { name: 'Email__From', value: 'jobscout@${emailDomain}' }
  ] : [],
  empty(customDomain) ? [] : [{ name: 'Email__AppUrl', value: 'https://${customDomain}' }],
  empty(adminEmail) ? [] : [{ name: 'Email__AdminAddresses__0', value: adminEmail }])

var commonEnv = [
  { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'Database__Provider', value: 'Postgres' }
  { name: 'ConnectionStrings__JobScout', secretRef: 'db-connection' }
  { name: 'ANTHROPIC_API_KEY', secretRef: 'anthropic-api-key' }
]
var adminList = filter(split(admins, ','), a => !empty(trim(a)))
var adminEnv = [for (admin, i) in adminList: { name: 'Auth__Admins__${i}', value: trim(admin) }]
var authEnv = concat(
  empty(googleClientId) ? [] : [
    { name: 'Auth__Google__ClientId', value: googleClientId }
    { name: 'Auth__Google__ClientSecret', secretRef: 'google-client-secret' }
  ],
  empty(microsoftClientId) ? [] : [
    { name: 'Auth__Microsoft__ClientId', value: microsoftClientId }
    { name: 'Auth__Microsoft__ClientSecret', secretRef: 'microsoft-client-secret' }
  ],
  adminEnv)

resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'ca-${name}-api'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // Without this a redeploy would drop the domain that was bound by the CLI
        customDomains: empty(customDomain) || empty(customDomainCertificate) ? [] : [
          {
            name: customDomain
            bindingType: 'SniEnabled'
            certificateId: '${environment.id}/managedCertificates/${customDomainCertificate}'
          }
        ]
      }
      secrets: concat(commonSecrets, authSecrets, emailSecrets)
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, authEnv, emailEnv, [
            { name: 'Demo__Enabled', value: 'true' }        // "Try it without signing up" (ADR-0020)
          ])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 20, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 15 }
          ]
        }
      ]
      // One replica: the first-search queue lives in memory (ADR-0019); no cold start for the first visitor
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  dependsOn: [appsReadSecrets]
}

resource morningJob 'Microsoft.App/jobs@2024-03-01' = if (deployApps) {
  name: 'caj-${name}-daily'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: { cronExpression: morningCron, parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 2700          // 45 min is plenty (a run takes minutes) and ends before the next hourly wake-up
      replicaRetryLimit: 0          // the retry is our own (NightSchedule, an hour later); the search window covers 3 days
      secrets: concat(commonSecrets, emailSecrets)
    }
    template: {
      containers: [
        {
          name: 'daily'
          image: image
          command: ['dotnet']
          args: ['/app/worker/JobScout.Worker.dll', '--contentRoot', '/app/worker', '--Scout:Mode=daily']
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: concat(commonEnv, emailEnv, [
            { name: 'Scout__ReportDir', value: '/tmp/reports' }     // the image is read-only for the app user
          ])
        }
      ]
    }
  }
  dependsOn: [appsReadSecrets]
}

output keyVaultName string = keyVault.name
output postgresHost string = postgresHost
output apiUrl string = deployApps ? 'https://${api!.properties.configuration.ingress.fqdn}' : ''
output environmentDomain string = environment.properties.defaultDomain
output environmentStaticIp string = environment.properties.staticIp
output emailDnsRecords object = emailDomainResource.properties.verificationRecords
