// E1 Phase 1 (T026). Staging parameters for main.bicep.
// `using` declaration locks the parameter signature to the orchestrator template.
//
// The four mandatory tags collapse to:
//   environment: staging (derived in main.bicep from env=stg)
//   market_codes: sa,eg
//   cost_center: dental-platform
//   owner: <ownerAadGroupOid>

using '../main.bicep'

param env = 'stg'
param locationCode = 'saudiarabiacentral'
param marketCodes = 'sa,eg'
param costCenter = 'dental-platform'

// Owner AAD group / email. Threaded via vars.AZURE_OWNER_OID in the deploy workflow.
// Local applies should override at the command line.
param ownerAadGroupOid = readEnvironmentVariable('AZURE_OWNER_OID', 'aad-group-platform-engineers')

// Tenant id is auto-resolved by main.bicep from `subscription().tenantId`. Override only
// for cross-tenant scenarios (not v1).

// Postgres admin password — provisioned-time secret, rotated into KV immediately by the
// post-apply step. Sourced from a GitHub Actions repo secret OR (for local dev) generated
// with `openssl rand -base64 24`.
param postgresAdminPassword = readEnvironmentVariable('PG_ADMIN_PASSWORD', '')

// Federated GHA principal ids — emitted by scripts/azure/setup-federated-credentials.sh.
// Threaded via vars.AZURE_GHA_DEPLOY_STG_PRINCIPAL_ID / AZURE_GHA_DRIFT_STG_PRINCIPAL_ID.
param ghaDeployPrincipalId = readEnvironmentVariable('AZURE_GHA_DEPLOY_STG_PRINCIPAL_ID', '')
param ghaDriftPrincipalId = readEnvironmentVariable('AZURE_GHA_DRIFT_STG_PRINCIPAL_ID', '')
