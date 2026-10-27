// Fidoco Live on Azure: App Service (Linux), Azure SQL (DTU), Application Insights and a Key Vault for the
// app's secrets. One resource group per environment (staging, production); deployed by .github/workflows/deploy.yml.
// The app reaches SQL and Key Vault with its managed identity, so no password is stored anywhere.
targetScope = 'resourceGroup'

@description('staging or production.')
@allowed([
  'staging'
  'production'
])
param environmentName string

@description('Globally unique name of the web app; also the prefix of the other resources (3-18 characters).')
@minLength(3)
@maxLength(18)
param appName string

param location string = resourceGroup().location

@description('App Service plan SKU. B1 normally; scale up before large events (see RUNBOOK.md).')
param appServiceSku string = 'B1'

@description('Azure SQL DTU SKU: Basic, S0-S12 or P1-P15. Never serverless: the outbox polls every few seconds.')
param sqlDatabaseSku string = 'S0'

@description('Entra ID group that administers the SQL server (the deploy identity and you). SQL authentication is off.')
param sqlAdminGroupName string

@description('Object id of the SQL admin group.')
param sqlAdminGroupObjectId string

@description('Platform host (platform admin and webhooks), e.g. admin.fidoco.se. Empty: the default azurewebsites.net host.')
param platformHost string = ''

param tenantDomainSuffix string = 'fidoco.se'

@description('Creates the first platform admin and e-mails an invitation (only when none exists).')
param bootstrapAdminEmail string = ''

param sellerLegalName string = 'Fidoco'
param sellerOrganizationNumber string = ''
param sellerVatNumber string = ''
param sellerAddress string = ''

param emailFromAddress string = 'noreply@fidoco.se'
param smtpHost string = ''
param smtpPort int = 587
param smtpUsername string = ''

var tags = {
  app: 'fidoco-live'
  environment: environmentName
}
var databaseName = 'fidoco'
var sqlTier = startsWith(sqlDatabaseSku, 'S') ? 'Standard' : startsWith(sqlDatabaseSku, 'P') ? 'Premium' : 'Basic'
var hosts = empty(platformHost) ? '${appName}.azurewebsites.net' : platformHost
var keyVaultSecretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${appName}-sql'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// 0.0.0.0 means "Azure services": App Service reaches SQL over the public endpoint. The deploy job adds a
// temporary rule for its own IP to run migrations.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: sqlDatabaseSku
    tier: sqlTier
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${appName}-kv'
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${appName}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true
  }
}

// Secrets live in Key Vault (set once with `az keyvault secret set`, see README) and reach the app as references.
func secret(vaultName string, name string) string => '@Microsoft.KeyVault(VaultName=${vaultName};SecretName=${name})'

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true // the outbox processors and retention jobs must keep running
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/live'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'production' ? 'Production' : 'Staging' }
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'Database__Provider', value: 'SqlServer' }
        {
          name: 'Database__ConnectionString'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=30'
        }
        { name: 'Database__MigrateOnStartup', value: 'false' } // the deploy job runs the migration bundles
        { name: 'Platform__Hosts__0', value: hosts }
        { name: 'Platform__TenantDomainSuffix', value: tenantDomainSuffix }
        { name: 'Platform__BootstrapAdminEmail', value: bootstrapAdminEmail }
        { name: 'Platform__Seller__LegalName', value: sellerLegalName }
        { name: 'Platform__Seller__OrganizationNumber', value: sellerOrganizationNumber }
        { name: 'Platform__Seller__VatNumber', value: sellerVatNumber }
        { name: 'Platform__Seller__Address', value: sellerAddress }
        { name: 'Storage__LocalPath', value: '/home/data/files' }
        { name: 'Email__Provider', value: 'Smtp' }
        { name: 'Email__FromAddress', value: emailFromAddress }
        { name: 'Email__Smtp__Host', value: smtpHost }
        { name: 'Email__Smtp__Port', value: string(smtpPort) }
        { name: 'Email__Smtp__Username', value: smtpUsername }
        { name: 'Email__Smtp__Password', value: secret(vault.name, 'smtp-password') }
        { name: 'Stripe__SecretKey', value: secret(vault.name, 'stripe-secret-key') }
        { name: 'Stripe__WebhookSecret', value: secret(vault.name, 'stripe-webhook-secret') }
        { name: 'Mux__TokenId', value: secret(vault.name, 'mux-token-id') }
        { name: 'Mux__TokenSecret', value: secret(vault.name, 'mux-token-secret') }
        { name: 'Mux__WebhookSecret', value: secret(vault.name, 'mux-webhook-secret') }
        { name: 'Mux__SigningKeyId', value: secret(vault.name, 'mux-signing-key-id') }
        { name: 'Mux__SigningKeyPrivate', value: secret(vault.name, 'mux-signing-key-private') }
      ]
    }
  }
}

// Deployments use the Entra-authenticated (OIDC) publish endpoint; FTP and basic-auth publishing are off.
resource ftpPublishing 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: app
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmPublishing 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: app
  name: 'scm'
  properties: {
    allow: false
  }
}

resource appReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, app.id, keyVaultSecretsUserRole)
  properties: {
    roleDefinitionId: keyVaultSecretsUserRole
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output appName string = app.name
output appHostName string = app.properties.defaultHostName
output appPrincipalId string = app.identity.principalId
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output keyVaultName string = vault.name
