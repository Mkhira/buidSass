// E1 Phase 1 (T021). Backend API container app.
// Min replicas 2, max 10; image placeholder replaced at deploy time by activate-revision.sh
// (Phase 2 T032). Managed identity from T014 attached. KEY_VAULT_URI env var enables
// AddLayeredConfiguration() in spec 003 to bootstrap.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('ACA managed environment resource id from aca-environment.bicep.')
param caEnvironmentId string

@description('Resource id of id-aca-<env> managed identity.')
param acaIdentityId string

@description('Key Vault URI (https://kv-dental-<env>.vault.azure.net/) for AddLayeredConfiguration.')
param keyVaultUri string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Container image — placeholder is overwritten by the deploy workflow.')
param image string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var backendAppName = 'ca-backend-api-${envShortName}'

resource backendApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: backendAppName
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
      activeRevisionsMode: 'Multiple'  // supports rollback by revision (T032 / AC-10)
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'backend-api'
          image: image
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
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
              name: 'ASPNETCORE_ENVIRONMENT'
              value: envShortName == 'prd' ? 'Production' : 'Staging'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 2
        maxReplicas: 10
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output backendAppId string = backendApp.id
output backendAppName string = backendApp.name
output backendEndpoint string = 'https://${backendApp.properties.configuration.ingress.fqdn}'
