// E1 Phase 1 (T014). User-assigned managed identity for the ACA backend + admin apps.
// One identity per environment (id-aca-stg / id-aca-prd), attached to both containerapps.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var identityName = 'id-aca-${envShortName}'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: commonTags
}

output identityId string = identity.id
output identityName string = identity.name
output identityPrincipalId string = identity.properties.principalId
output identityClientId string = identity.properties.clientId
