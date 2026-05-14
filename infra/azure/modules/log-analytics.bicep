// E1 Phase 1 (T012). Log Analytics workspace.
// Retention 90 days, PerGB2018 SKU per data-model.md §1 row 14.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var workspaceName = 'log-dental-${envShortName}-ksa'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: commonTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 90
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

output workspaceId string = workspace.id
output workspaceName string = workspace.name
output workspaceCustomerId string = workspace.properties.customerId
