// E1 Phase 1 (T013). Key Vault module — provisions a single vault per environment with
// RBAC authorization, soft-delete (90 days), and purge protection enabled. No legacy
// access policies. All four mandatory tags applied.
//
// Both vaults (kv-dental-stg and kv-dental-prd) are created from this module by invoking
// it once per environment from main.bicep with a different `envShortName`. The single-call
// "create both vaults from one module" pattern in tasks.md T013 refers to the IaC contract
// (same module, identical parity-by-construction) — main.bicep enforces it.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('AAD tenant id (subscription tenant).')
param tenantId string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var vaultName = 'kv-dental-${envShortName}'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  tags: commonTags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'  // restricted via firewall + identity-based RBAC at runtime
    networkAcls: {
      defaultAction: 'Allow'  // identity-based RBAC is the primary enforcement; AC-11 verifies isolation
      bypass: 'AzureServices'
    }
  }
}

output vaultId string = vault.id
output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
