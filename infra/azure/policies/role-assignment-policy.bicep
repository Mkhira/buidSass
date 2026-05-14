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

// Scoping note: the assignment is deployed at subscription scope (matches
// targetScope above). Cross-scope assignment to a specific RG would require a
// nested deployment, which adds complexity without security benefit — a
// subscription-wide rule is strictly stronger than an RG-scoped one for AC-12.

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
  }
}

output policyDefinitionId string = definition.id
output policyAssignmentId string = assignment.id
