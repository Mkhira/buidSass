// E1 Phase 1 (T018). Azure Container Apps managed environment with workload profiles
// (Consumption + Dedicated D4), VNet integration into snet-cae, and Log Analytics binding.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('Resource id of the snet-cae subnet from network.bicep.')
param subnetCaeId string

@description('Log Analytics workspace resource id (for managed-env diagnostics).')
param logAnalyticsWorkspaceId string

@description('Log Analytics workspace customer id (for legacy log-analytics config).')
param logAnalyticsCustomerId string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var environmentName = 'cae-dental-${envShortName}-ksa'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}

resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: commonTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
      {
        name: 'D4'
        workloadProfileType: 'D4'
        minimumCount: 0
        maximumCount: 2
      }
    ]
    vnetConfiguration: {
      infrastructureSubnetId: subnetCaeId
      internal: false  // external ingress for /health and admin UI; KV ACL gates secret read
    }
    zoneRedundant: false
  }
}

output environmentId string = caEnv.id
output environmentName string = caEnv.name
output defaultDomain string = caEnv.properties.defaultDomain
