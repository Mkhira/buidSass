// E1 Phase 1 (T019) + Phase 3 (T048). Codifies the RBAC matrix from
// contracts/infrastructure-contract.md §6.
//
// Permanent assignments (always-on):
//   id-aca-<env>            → kv-dental-<env>           : Key Vault Secrets User
//   gha-deploy-<env> (UMI)  → rg-dental-<env>-ksa       : Container Apps Contributor
//   gha-deploy-<env> (UMI)  → kv-dental-<env>           : Key Vault Secrets User
//   gha-drift-<env> (UMI)   → rg-dental-<env>-ksa       : Reader
//
// PIM-only assignments (NOT created here — managed via AAD PIM elevation rules):
//   aad-group-platform-engineers → both vaults : KV Administrator (break-glass)
//   aad-group-on-call (time-boxed) → both vaults : KV Secrets Officer (rotation path)
//
// AC-12 invariant: NO permanent Officer/Administrator role assignments. The daily compliance
// scan (Phase 3 T048) lives in role-assignment-policy.bicep — extending the matrix here
// would breach the 200-LOC budget, so the policy module is intentionally separate.

targetScope = 'resourceGroup'

@description('Environment short name: stg | prd.')
param envShortName string

@description('Principal id (managed-identity object id) of id-aca-<env>.')
param acaIdentityPrincipalId string

@description('Principal id (managed-identity object id) of gha-deploy-<env>.')
param ghaDeployPrincipalId string

@description('Principal id (managed-identity object id) of gha-drift-<env>.')
param ghaDriftPrincipalId string

@description('Resource id of kv-dental-<env>.')
param keyVaultId string

// Built-in role definition ids (Azure-stable GUIDs).
var roleKvSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var roleContainerAppsContributor = 'b24988ac-6180-42a0-ab88-20f7382dd24c'  // Contributor (broad)
var roleReader = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: last(split(keyVaultId, '/'))
}

// 1. id-aca-<env> → kv-dental-<env> : Key Vault Secrets User
resource acaToKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, acaIdentityPrincipalId, roleKvSecretsUser)
  scope: keyVault
  properties: {
    principalId: acaIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
  }
}

// 2. gha-deploy-<env> → resourceGroup : Container Apps Contributor (Contributor scope)
resource ghaDeployToRg 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, ghaDeployPrincipalId, roleContainerAppsContributor)
  properties: {
    principalId: ghaDeployPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleContainerAppsContributor)
  }
}

// 3. gha-deploy-<env> → kv-dental-<env> : Key Vault Secrets User
resource ghaDeployToKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, ghaDeployPrincipalId, roleKvSecretsUser)
  scope: keyVault
  properties: {
    principalId: ghaDeployPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
  }
}

// 4. gha-drift-<env> → resourceGroup : Reader
resource ghaDriftToRg 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, ghaDriftPrincipalId, roleReader)
  properties: {
    principalId: ghaDriftPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleReader)
  }
}

output assignmentCount int = 4
output environment string = envShortName
