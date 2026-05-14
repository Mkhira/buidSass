// E1 Phase 1 (T020). Self-hosted Meilisearch on Azure Container Apps with persistent storage
// via Azure File volume mount. Master key sourced from Key Vault at runtime.
//
// Design (per DECISIONS.md D-1): single replica (Meilisearch is single-leader; HA requires
// the v1.10+ replication features which we'll evaluate post-launch). Daily-snapshot volume
// gives us a point-in-time DR story. Per data-model.md §1 row 11/12.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('ACA managed environment resource id from aca-environment.bicep.')
param caEnvironmentId string

@description('Key Vault resource id (for KV-sourced master-key secret reference).')
param keyVaultId string

@description('Resource id of id-aca-<env> managed identity.')
param acaIdentityId string

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

// Meilisearch image — pinned. Bump via PR; CI tag-completeness + bicep-lint surfaces the diff.
@description('Meilisearch container image. Pin major.minor to control upgrade cadence.')
param meiliImage string = 'getmeili/meilisearch:v1.10'

@description('Storage account name for the Azure File share. Must be globally unique; 3-24 lowercase alnum.')
param storageAccountName string = 'stmeili${envShortName}ksa${uniqueString(resourceGroup().id)}'

@description('Azure File share quota in GB.')
param fileShareQuotaGB int = 100

var meiliAppName = 'ca-meili-${envShortName}'
var meiliShareName = 'vol-meili-${envShortName}'
var keyVaultName = last(split(keyVaultId, '/'))

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: commonTags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    shareDeleteRetentionPolicy: {
      enabled: true
      days: 14
    }
  }
}

resource share 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: meiliShareName
  properties: {
    shareQuota: fileShareQuotaGB
    enabledProtocols: 'SMB'
  }
}

// Bind the file share into the managed-env as a named storage (referenced by the container app).
resource caEnvStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  name: '${last(split(caEnvironmentId, '/'))}/${meiliShareName}'
  properties: {
    azureFile: {
      accountName: storage.name
      accountKey: storage.listKeys().keys[0].value
      shareName: meiliShareName
      accessMode: 'ReadWrite'
    }
  }
}

resource meiliApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: meiliAppName
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${acaIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: caEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 7700
        transport: 'http'
        allowInsecure: false
      }
      secrets: [
        {
          name: 'meili-master-key'
          keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/meili--multi--self-hosted--master-key'
          identity: acaIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'meilisearch'
          image: meiliImage
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            {
              name: 'MEILI_MASTER_KEY'
              secretRef: 'meili-master-key'
            }
            {
              name: 'MEILI_ENV'
              value: 'production'
            }
            {
              name: 'MEILI_DB_PATH'
              value: '/meili_data'
            }
          ]
          volumeMounts: [
            {
              volumeName: 'meili-data'
              mountPath: '/meili_data'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        {
          name: 'meili-data'
          storageType: 'AzureFile'
          storageName: meiliShareName
        }
      ]
    }
  }
  dependsOn: [
    caEnvStorage
  ]
}

output meiliAppId string = meiliApp.id
output meiliAppName string = meiliApp.name
output meiliEndpoint string = meiliApp.properties.configuration.ingress.fqdn
output storageAccountId string = storage.id
output fileShareName string = meiliShareName
