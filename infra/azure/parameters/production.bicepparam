// E1 Phase 1 (T027). Production parameters for main.bicep.
// AC-22 invariant: parity-by-construction with staging.bicepparam — every difference here
// is INTENTIONAL and listed below.
//
// Intentional Production deltas:
//   - env: prd (vs stg)
//   - owner: aad-group-platform-engineers (PIM-only — no plaintext email)
//   - ghaDeployPrincipalId / ghaDriftPrincipalId: separate federated principals
//
// Everything else (region, market codes, cost center, Postgres SKU, replica counts,
// etc.) is identical. `az deployment sub what-if --no-pretty-print` diff between
// staging.bicepparam and production.bicepparam at any time should show ONLY name +
// principal-id changes; zero structural differences (verified by AC-22 / T029).

using '../main.bicep'

param env = 'prd'
param locationCode = 'saudiarabiacentral'
param marketCodes = 'sa,eg'
param costCenter = 'dental-platform'

// Owner: AAD group OID for `aad-group-platform-engineers`. PIM-elevated humans only.
param ownerAadGroupOid = readEnvironmentVariable('AZURE_OWNER_OID', 'aad-group-platform-engineers')

// Postgres admin password — sourced from KV-mirrored GHA secret in the deploy workflow.
param postgresAdminPassword = readEnvironmentVariable('PG_ADMIN_PASSWORD', '')

// Federated GHA principal ids for Production.
param ghaDeployPrincipalId = readEnvironmentVariable('AZURE_GHA_DEPLOY_PRD_PRINCIPAL_ID', '')
param ghaDriftPrincipalId = readEnvironmentVariable('AZURE_GHA_DRIFT_PRD_PRINCIPAL_ID', '')
