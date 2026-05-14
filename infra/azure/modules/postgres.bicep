// E1 Phase 1 (T017). Postgres Flexible Server with private endpoint into snet-pg-pe.
// Version 16, SKU Standard_D2s_v3 (clarify-locked per DECISIONS.md D-3), 256 GB storage,
// geo-redundant backup 30-day retention, public access DISABLED. Database `dental` UTF-8.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('Resource id of the snet-pg-pe subnet from network.bicep.')
param subnetPgPeId string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

@description('Admin login name. Real credentials are rotated into Key Vault post-provisioning.')
param administratorLogin string = 'pgadmin'

@secure()
@description('Initial admin password — rotated immediately post-provisioning into KV.')
param administratorLoginPassword string

@description('Postgres SKU. Clarify-locked default per DECISIONS.md D-3.')
param skuName string = 'Standard_D2s_v3'

@description('Storage in GB.')
param storageSizeGB int = 256

@description('Backup retention days.')
param backupRetentionDays int = 30

var serverName = 'pg-dental-${envShortName}-ksa'
var databaseName = 'dental'
var privateDnsZoneName = 'privatelink.postgres.database.azure.com'

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: privateDnsZoneName
  location: 'global'
  tags: commonTags
}

resource pgServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  tags: commonTags
  sku: {
    name: skuName
    tier: 'GeneralPurpose'
  }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: 'Enabled'
    }
    highAvailability: {
      mode: 'Disabled'  // enable in Phase 1.5 once HA tier rollout is approved
    }
    network: {
      publicNetworkAccess: 'Disabled'
      delegatedSubnetResourceId: subnetPgPeId
      privateDnsZoneArmResourceId: privateDnsZone.id
    }
  }
}

resource db 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: pgServer
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

output serverId string = pgServer.id
output serverName string = pgServer.name
output serverFqdn string = pgServer.properties.fullyQualifiedDomainName
output databaseName string = db.name
