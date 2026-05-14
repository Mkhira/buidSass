// E1 Phase 3 (T048). Azure Policy that fails compliance if any permanent (non-PIM)
// `Key Vault Secrets Officer` or `Key Vault Administrator` role assignment exists on
// either E1 vault. The policy runs as a daily compliance scan (Azure Policy default
// evaluation cadence) and surfaces violations through the Policy Compliance dashboard
// and the alert-kv-anomaly-<env> alert (which already watches KV diagnostic logs).
//
// Lives in policies/ rather than modules/role-assignments.bicep because:
//   1. role-assignments.bicep is already at the upper end of its complexity budget.
//   2. Policy definitions + assignments are inherently subscription-scoped while the
//      role-assignment module is resourceGroup-scoped.
//
// Per AC-12: "no permanent privileged role assignments" — PIM-eligible assignments
// carry `conditionVersion` and are excluded by the policy rule.

targetScope = 'subscription'

@description('Environment short name: stg | prd.')
param envShortName string

@description('Subscription id where the policy is assigned.')
param subscriptionId string = subscription().subscriptionId

@description('Resource group containing the KV. Used to scope the policy assignment.')
param resourceGroupId string

// Built-in role definition ids for the two privileged KV roles.
var kvSecretsOfficerRoleDefId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var kvAdministratorRoleDefId  = '00482a5a-887f-4fb3-b363-3b7fe8e74483'

resource definition 'Microsoft.Authorization/policyDefinitions@2023-04-01' = {
  name: 'e1-no-permanent-kv-privileged-${envShortName}'
  properties: {
    displayName: 'E1 — no permanent KV Officer/Administrator role assignments'
    description: 'Fails compliance if any permanent (non-PIM) role assignment of Key Vault Secrets Officer or Key Vault Administrator exists. PIM-eligible assignments are exempt because they carry a conditionVersion.'
    policyType: 'Custom'
    mode: 'All'
    parameters: {}
    policyRule: {
      if: {
        allOf: [
          {
            field: 'type'
            equals: 'Microsoft.Authorization/roleAssignments'
          }
          {
            anyOf: [
              {
                field: 'Microsoft.Authorization/roleAssignments/roleDefinitionId'
                like: '*${kvSecretsOfficerRoleDefId}'
              }
              {
                field: 'Microsoft.Authorization/roleAssignments/roleDefinitionId'
                like: '*${kvAdministratorRoleDefId}'
              }
            ]
          }
          {
            // Permanent assignments do NOT carry a conditionVersion; PIM-eligible
            // assignments do. So we flag exactly the rows missing this field.
            field: 'Microsoft.Authorization/roleAssignments/conditionVersion'
            exists: false
          }
        ]
      }
      then: {
        effect: 'audit'
      }
    }
  }
}

resource assignment 'Microsoft.Authorization/policyAssignments@2023-04-01' = {
  name: 'e1-no-permanent-kv-privileged-${envShortName}-asg'
  properties: {
    displayName: 'E1 — no permanent KV privileged role assignments (${envShortName})'
    policyDefinitionId: definition.id
    enforcementMode: 'Default'
    scope: resourceGroupId
  }
}

output policyDefinitionId string = definition.id
output policyAssignmentId string = assignment.id
