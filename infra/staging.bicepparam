using 'main.bicep'

// Non-secret settings for staging. The deploy workflow sets the environment variables read below from the
// GitHub environment "staging" (Settings → Environments → Variables); secrets live in the Key Vault.
param environmentName = 'staging'
param appName = readEnvironmentVariable('AZURE_APP_NAME')
param appServiceSku = readEnvironmentVariable('APP_SERVICE_SKU', 'B1')
param sqlDatabaseSku = readEnvironmentVariable('SQL_DATABASE_SKU', 'S0')
param sqlAdminGroupName = readEnvironmentVariable('SQL_ADMIN_GROUP_NAME')
param sqlAdminGroupObjectId = readEnvironmentVariable('SQL_ADMIN_GROUP_OBJECT_ID')
param platformHost = readEnvironmentVariable('PLATFORM_HOST', '')
param bootstrapAdminEmail = readEnvironmentVariable('BOOTSTRAP_ADMIN_EMAIL', '')
param sellerLegalName = readEnvironmentVariable('SELLER_LEGAL_NAME', 'Fidoco')
param sellerOrganizationNumber = readEnvironmentVariable('SELLER_ORGANIZATION_NUMBER', '')
param sellerVatNumber = readEnvironmentVariable('SELLER_VAT_NUMBER', '')
param sellerAddress = readEnvironmentVariable('SELLER_ADDRESS', '')
param smtpHost = readEnvironmentVariable('SMTP_HOST', '')
param smtpUsername = readEnvironmentVariable('SMTP_USERNAME', '')
param emailFromAddress = readEnvironmentVariable('EMAIL_FROM_ADDRESS', 'noreply@fidoco.se')
