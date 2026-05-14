// E1 Phase 1 (T022). Admin web container app (Next.js admin per ADR-006).
// Min replicas 1, max 5; image placeholder replaced at deploy time. Managed identity attached
// so the admin app can read its own KV secrets via the same Key Vault Secrets User assignment.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('ACA managed environment resource id from aca-environment.bicep.')
param caEnvironmentId string

@description('Resource id of id-aca-<env> managed identity.')
param acaIdentityId string

@description('Key Vault URI (for runtime config bootstrap, when the admin app needs KV secrets).')
param keyVaultUri string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Container image — placeholder is overwritten by the deploy workflow.')
param image string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var adminAppName = 'ca-admin-web-${envShortName}'

resource adminApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: adminAppName
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
      activeRevisionsMode: 'Multiple'
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'admin-web'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'KEY_VAULT_URI'
              value: keyVaultUri
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
            {
              name: 'NODE_ENV'
              value: 'production'
            }
            {
              name: 'PORT'
              value: '3000'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
      }
    }
  }
}

output adminAppId string = adminApp.id
output adminAppName string = adminApp.name
output adminEndpoint string = 'https://${adminApp.properties.configuration.ingress.fqdn}'
