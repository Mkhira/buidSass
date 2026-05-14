// E1 Phase 1 (T011). Network module — VNet + two subnets.
// Address space 10.0.0.0/16; snet-cae (10.0.1.0/24) delegated to Microsoft.App/environments,
// snet-pg-pe (10.0.2.0/24) hosts the Postgres private endpoint (no delegation).
// All four mandatory tags applied via the threaded `commonTags` object from main.bicep.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var vnetName = 'vnet-dental-${envShortName}-ksa'
var subnetCaeName = 'snet-cae'
var subnetPgPeName = 'snet-pg-pe'

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnetName
  location: location
  tags: commonTags
  properties: {
    addressSpace: {
      addressPrefixes: [ '10.0.0.0/16' ]
    }
    subnets: [
      {
        name: subnetCaeName
        properties: {
          addressPrefix: '10.0.1.0/24'
          delegations: [
            {
              name: 'aca-delegation'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: subnetPgPeName
        properties: {
          addressPrefix: '10.0.2.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output subnetCaeId string = '${vnet.id}/subnets/${subnetCaeName}'
output subnetPgPeId string = '${vnet.id}/subnets/${subnetPgPeName}'
