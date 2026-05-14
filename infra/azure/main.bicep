// E1 Phase 1 (T025). Subscription-scoped orchestrator for the Phase 1E infrastructure.
// Creates the resource group, then threads `commonTags` into every module so all 17+
// resources carry the four mandatory tags identically (verified by AC-2 and the
// tag-completeness-check CI gate).
//
// Module ordering follows the dependency DAG in plan.md §"Bicep module structure":
//   1. network.bicep
//   2. log-analytics.bicep   [parallel with 1]
//   3. keyvault.bicep         [parallel with 1, 2]
//   4. managed-identity.bicep [parallel with 1, 2, 3]
//   5. static-web-app.bicep   [parallel]
//   6. app-insights.bicep     [depends on 2]
//   7. postgres.bicep         [depends on 1]
//   8. aca-environment.bicep  [depends on 1, 2]
//   9. role-assignments.bicep [depends on 3, 4]
//  10. meili.bicep            [depends on 3, 8]
//  11. aca-app-backend.bicep  [depends on 3, 4, 8]
//  12. aca-app-admin.bicep    [depends on 3, 4, 8]
//  13. aca-job-migrate.bicep  [depends on 7, 8, 3]
//  14. alerts.bicep           [depends on 6]
//
// keyvault-bootstrap.bicep is a SEPARATE module call (resource-group scope) executed
// AFTER this template completes — it cannot run subscription-scoped, and the vault
// must exist before its secret children can be addressed.

targetScope = 'subscription'

@description('Environment short name. stg | prd.')
@allowed([ 'stg', 'prd' ])
param env string

@description('Azure region for the resource group and all in-region resources.')
@allowed([ 'saudiarabiacentral', 'westeurope', 'uaenorth' ])
param locationCode string = 'saudiarabiacentral'

@description('Comma-separated market codes for the market_codes tag.')
param marketCodes string = 'sa,eg'

@description('Cost center for the cost_center tag.')
param costCenter string = 'dental-platform'

@description('AAD group object id (or email) that owns this stack — used as the owner tag.')
param ownerAadGroupOid string

@description('Tenant id (subscription tenant) for the Key Vault.')
param tenantId string = subscription().tenantId

@description('Postgres admin password — passed through to postgres.bicep then KV.')
@secure()
param postgresAdminPassword string

@description('Principal id of gha-deploy-<env> federated identity (created by setup-federated-credentials.sh).')
param ghaDeployPrincipalId string

@description('Principal id of gha-drift-<env> federated identity.')
param ghaDriftPrincipalId string

@description('Resource group name (defaults to rg-dental-<env>-ksa).')
param resourceGroupName string = 'rg-dental-${env}-ksa'

// commonTags is the canonical four-tag map threaded into every module.
// CI guard scripts/ci/check-tag-completeness.py treats the literal name `commonTags` as
// an accepted tag-reference target.
var commonTags = {
  environment: env == 'prd' ? 'production' : 'staging'
  market_codes: marketCodes
  cost_center: costCenter
  owner: ownerAadGroupOid
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: locationCode
  tags: commonTags
}

module network 'modules/network.bicep' = {
  name: 'network'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    commonTags: commonTags
  }
}

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'log-analytics'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    commonTags: commonTags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    tenantId: tenantId
    commonTags: commonTags
  }
}

module managedIdentity 'modules/managed-identity.bicep' = {
  name: 'managed-identity'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    commonTags: commonTags
  }
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'static-web-app'
  scope: resourceGroup
  params: {
    envShortName: env
    commonTags: commonTags
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'app-insights'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    commonTags: commonTags
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    subnetPgPeId: network.outputs.subnetPgPeId
    administratorLoginPassword: postgresAdminPassword
    commonTags: commonTags
  }
}

module caEnv 'modules/aca-environment.bicep' = {
  name: 'aca-environment'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    subnetCaeId: network.outputs.subnetCaeId
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    logAnalyticsCustomerId: logAnalytics.outputs.workspaceCustomerId
    commonTags: commonTags
  }
}

module roleAssignments 'modules/role-assignments.bicep' = {
  name: 'role-assignments'
  scope: resourceGroup
  params: {
    envShortName: env
    acaIdentityPrincipalId: managedIdentity.outputs.identityPrincipalId
    ghaDeployPrincipalId: ghaDeployPrincipalId
    ghaDriftPrincipalId: ghaDriftPrincipalId
    keyVaultId: keyVault.outputs.vaultId
  }
}

module meili 'modules/meili.bicep' = {
  name: 'meili'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    caEnvironmentId: caEnv.outputs.environmentId
    keyVaultId: keyVault.outputs.vaultId
    acaIdentityId: managedIdentity.outputs.identityId
    commonTags: commonTags
  }
}

module backendApp 'modules/aca-app-backend.bicep' = {
  name: 'aca-app-backend'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    caEnvironmentId: caEnv.outputs.environmentId
    acaIdentityId: managedIdentity.outputs.identityId
    keyVaultUri: keyVault.outputs.vaultUri
    appInsightsConnectionString: appInsights.outputs.connectionString
    commonTags: commonTags
  }
}

module adminApp 'modules/aca-app-admin.bicep' = {
  name: 'aca-app-admin'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    caEnvironmentId: caEnv.outputs.environmentId
    acaIdentityId: managedIdentity.outputs.identityId
    keyVaultUri: keyVault.outputs.vaultUri
    appInsightsConnectionString: appInsights.outputs.connectionString
    commonTags: commonTags
  }
}

module migrateJob 'modules/aca-job-migrate.bicep' = {
  name: 'aca-job-migrate'
  scope: resourceGroup
  params: {
    envShortName: env
    location: locationCode
    caEnvironmentId: caEnv.outputs.environmentId
    acaIdentityId: managedIdentity.outputs.identityId
    keyVaultUri: keyVault.outputs.vaultUri
    commonTags: commonTags
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  scope: resourceGroup
  params: {
    envShortName: env
    appInsightsId: appInsights.outputs.componentId
    backendAppId: backendApp.outputs.backendAppId
    keyVaultId: keyVault.outputs.vaultId
    commonTags: commonTags
  }
}

output resourceGroupName string = resourceGroup.name
output keyVaultName string = keyVault.outputs.vaultName
output keyVaultUri string = keyVault.outputs.vaultUri
output backendEndpoint string = backendApp.outputs.backendEndpoint
output adminEndpoint string = adminApp.outputs.adminEndpoint
output postgresFqdn string = postgres.outputs.serverFqdn
output meiliEndpoint string = meili.outputs.meiliEndpoint
output swaName string = staticWebApp.outputs.swaName
