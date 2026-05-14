// E1 Phase 1 (T023). One-shot ACA job for EF Core migrations.
// replicaTimeout=900, replicaRetryLimit=0 (fail fast), manual trigger. Image overwritten by
// the deploy workflow's run-migrations-job.sh (Phase 2 T031).

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral).')
param location string

@description('ACA managed environment resource id from aca-environment.bicep.')
param caEnvironmentId string

@description('Resource id of id-aca-<env> managed identity.')
param acaIdentityId string

@description('Key Vault URI for connection-string bootstrap.')
param keyVaultUri string

@description('Backend container image — placeholder is overwritten by the deploy workflow.')
param image string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var migrateJobName = 'caj-ef-migrate-${envShortName}'

resource migrateJob 'Microsoft.App/jobs@2024-03-01' = {
  name: migrateJobName
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${acaIdentityId}': {}
    }
  }
  properties: {
    environmentId: caEnvironmentId
    configuration: {
      replicaTimeout: 900
      replicaRetryLimit: 0
      triggerType: 'Manual'
      manualTriggerConfig: {
        replicaCompletionCount: 1
        parallelism: 1
      }
    }
    template: {
      containers: [
        {
          name: 'ef-migrate'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          // The migration entrypoint is the same backend image invoked with `dotnet
          // ef database update`. The deploy workflow's run-migrations-job.sh patches
          // the args field at runtime — leaving it unset here keeps the module idle.
          env: [
            {
              name: 'KEY_VAULT_URI'
              value: keyVaultUri
            }
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: envShortName == 'prd' ? 'Production' : 'Staging'
            }
          ]
        }
      ]
    }
  }
}

output migrateJobId string = migrateJob.id
output migrateJobName string = migrateJob.name
