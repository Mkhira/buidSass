// E1 Phase 1 (T028). Provisions the 12 ADR-007/008/009 placeholder secrets + 4 E1-owned
// secrets in the target Key Vault. Sentinel value `__placeholder_set_by_E1__` for the
// placeholders; real values for the 4 E1-owned secrets.
//
// Per data-model.md §2: Azure Key Vault secret names accept only `[a-zA-Z0-9-]+`, so the
// canonical logical path (e.g. `meili/multi/self-hosted/master-key`) is flattened to
// `meili--multi--self-hosted--master-key` on disk. Conversion is mechanical (`s|/|--|g`).
//
// The emission of `secret.placeholder_replaced` audit events when 025/026/027 overwrite a
// sentinel is OWNED by those downstream specs, not by E1. This module only writes the
// placeholders. AC-5 verifies 16 secrets with `set_by_spec=E1` exist.

targetScope = 'resourceGroup'

@description('Environment short name: stg | prd.')
param envShortName string

@description('Key Vault resource name (kv-dental-<env>).')
param keyVaultName string

@description('Sentinel value written into every placeholder slot. Real values land via 025/026/027.')
@secure()
param placeholderSentinel string = '__placeholder_set_by_E1__'

@description('Real Postgres connection string for E1-owned secret. Sourced from main.bicep.')
@secure()
param postgresConnectionString string

@description('Real Meilisearch master key (generated once, persisted in KV).')
@secure()
param meiliMasterKey string

@description('Real JWT signing key (generated once, persisted in KV).')
@secure()
param jwtSigningKey string

@description('Real ASP.NET Data Protection key (generated once, persisted in KV).')
@secure()
param dataProtectionKey string

// The 12 ADR-007/008/009 placeholders + 4 E1-owned slots.
// Logical paths are documented in data-model.md §2; on-disk names flatten / to --.
var placeholderSlots = [
  // payments — populated by spec 027
  { logicalPath: 'payments/sa/tbd-by-027/api-key',               expectedSpec: '027', cadenceDays: 90 }
  { logicalPath: 'payments/sa/tbd-by-027/api-secret',            expectedSpec: '027', cadenceDays: 90 }
  { logicalPath: 'payments/sa/tbd-by-027/webhook-signing-key',   expectedSpec: '027', cadenceDays: 90 }
  { logicalPath: 'payments/eg/tbd-by-027/api-key',               expectedSpec: '027', cadenceDays: 90 }
  { logicalPath: 'payments/eg/tbd-by-027/api-secret',            expectedSpec: '027', cadenceDays: 90 }
  { logicalPath: 'payments/eg/tbd-by-027/webhook-signing-key',   expectedSpec: '027', cadenceDays: 90 }
  // shipping — populated by spec 026
  { logicalPath: 'shipping/sa/tbd-by-026/api-key',               expectedSpec: '026', cadenceDays: 90 }
  { logicalPath: 'shipping/eg/tbd-by-026/api-key',               expectedSpec: '026', cadenceDays: 90 }
  // notifications — populated by spec 025
  { logicalPath: 'notifications-email/multi/tbd-by-025/api-key', expectedSpec: '025', cadenceDays: 90 }
  { logicalPath: 'notifications-sms/sa/tbd-by-025/api-key',      expectedSpec: '025', cadenceDays: 90 }
  { logicalPath: 'notifications-sms/eg/tbd-by-025/api-key',      expectedSpec: '025', cadenceDays: 90 }
  { logicalPath: 'notifications-push/multi/tbd-by-025/service-account-json', expectedSpec: '025', cadenceDays: 90 }
]

var e1OwnedSlots = [
  { logicalPath: 'db/multi/postgres-flex/connection-string', value: postgresConnectionString, cadenceDays: 30 }
  { logicalPath: 'meili/multi/self-hosted/master-key',       value: meiliMasterKey,           cadenceDays: 90 }
  { logicalPath: 'app/multi/backend-api/jwt-signing-key',    value: jwtSigningKey,            cadenceDays: 365 }
  { logicalPath: 'app/multi/backend-api/data-protection-key', value: dataProtectionKey,       cadenceDays: 365 }
]

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource placeholderSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [for (slot, idx) in placeholderSlots: {
  parent: keyVault
  name: replace(slot.logicalPath, '/', '--')
  tags: {
    set_by_spec: 'E1'
    expected_real_value_in: slot.expectedSpec
    rotation_cadence_days: '${slot.cadenceDays}'
    logical_path: slot.logicalPath
    environment: envShortName
  }
  properties: {
    value: placeholderSentinel
    contentType: 'placeholder/sentinel'
  }
}]

resource ownedSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [for (slot, idx) in e1OwnedSlots: {
  parent: keyVault
  name: replace(slot.logicalPath, '/', '--')
  tags: {
    set_by_spec: 'E1'
    rotation_cadence_days: '${slot.cadenceDays}'
    logical_path: slot.logicalPath
    environment: envShortName
    rotated_at: utcNow('yyyy-MM-ddTHH:mm:ssZ')
  }
  properties: {
    value: slot.value
  }
}]

output placeholderCount int = length(placeholderSlots)
output ownedCount int = length(e1OwnedSlots)
output totalSecretsCount int = length(placeholderSlots) + length(e1OwnedSlots)
