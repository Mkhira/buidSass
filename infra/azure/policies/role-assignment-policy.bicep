// E1 Phase 3 (T048). Azure Policy that audits every `Key Vault Secrets Officer` and
// `Key Vault Administrator` role assignment subscription-wide. Runs daily as part of
// Azure Policy's default evaluation cadence and surfaces violations through the
// Policy Compliance dashboard plus alert-kv-anomaly-<env>.
//
// Lives in policies/ rather than modules/role-assignments.bicep because:
//   1. role-assignments.bicep is already at the upper end of its complexity budget.
//   2. Policy definitions + assignments are inherently subscription-scoped while the
//      role-assignment module is resourceGroup-scoped.
//
// Per AC-12 ("no permanent privileged role assignments"): Azure Policy cannot
// reliably distinguish permanent from PIM-activated role assignments at evaluation
// time — `Microsoft.Authorization/roleAssignments/conditionVersion` is the ABAC
// condition syntax version (per Microsoft docs), NOT a PIM eligibility marker. PIM
// eligibility lives on separate resources (`Microsoft.Authorization/roleEligibilitySchedules`
// + `roleAssignmentSchedules`) that this single-resource policy cannot cross-reference.
//
// Therefore this policy intentionally flags EVERY Officer/Administrator assignment.
// PIM-activated assignments are transient (auto-expire) and the operator workflow is:
//   1. Compliance dashboard (T072 panel) joins flagged rows against
//      `roleAssignmentSchedules` to drop transient PIM activations.
//   2. Anything remaining after the join IS a permanent grant — actionable AC-12 hit.
// The runbook §"AC-12 violation triage" documents this two-step procedure.

targetScope = 'subscription'

@description('Environment short name: stg | prd.')
@allowed([
  'stg'
  'prd'
])
param envShortName string

// Scoping note: the assignment is deployed at subscription scope (matches
// targetScope above). Cross-scope assignment to a specific RG would require a
// nested deployment, which adds complexity without security benefit — a
// subscription-wide rule is strictly stronger than an RG-scoped one for AC-12.

// Built-in role definition ids for the two privileged KV roles.
var kvSecretsOfficerRoleDefId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var kvAdministratorRoleDefId  = '00482a5a-887f-4fb3-b363-3b7fe8e74483'

resource definition 'Microsoft.Authorization/policyDefinitions@2023-04-01' = {
  name: 'e1-kv-privileged-audit-${envShortName}'
  properties: {
    displayName: 'E1 — audit KV Officer/Administrator role assignments'
    description: 'Audits every role assignment of Key Vault Secrets Officer or Key Vault Administrator subscription-wide. PIM-activated assignments will also be flagged (transient by design); the compliance dashboard joins flagged rows against roleAssignmentSchedules to filter them — anything remaining is a permanent grant that violates AC-12.'
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
        ]
      }
      then: {
        effect: 'audit'
      }
    }
  }
}

resource assignment 'Microsoft.Authorization/policyAssignments@2023-04-01' = {
  name: 'e1-kv-privileged-audit-${envShortName}-asg'
  properties: {
    displayName: 'E1 — audit KV privileged role assignments (${envShortName})'
    policyDefinitionId: definition.id
    enforcementMode: 'Default'
  }
}

output policyDefinitionId string = definition.id
output policyAssignmentId string = assignment.id
