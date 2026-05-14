// E1 Phase 1 (T016). Workspace-based Application Insights, linked to the Log Analytics
// workspace from T012. Depends on T012's `workspaceId` output.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('Resource id of the Log Analytics workspace from log-analytics.bicep.')
param logAnalyticsWorkspaceId string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var componentName = 'appi-dental-${envShortName}-ksa'

resource component 'Microsoft.Insights/components@2020-02-02' = {
  name: componentName
  location: location
  tags: commonTags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    Request_Source: 'rest'
    WorkspaceResourceId: logAnalyticsWorkspaceId
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output componentId string = component.id
output componentName string = component.name
output connectionString string = component.properties.ConnectionString
output instrumentationKey string = component.properties.InstrumentationKey
