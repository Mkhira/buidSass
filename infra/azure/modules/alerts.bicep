// E1 Phase 1 (T024). Action group + four metric alerts per data-model.md §1 rows 18-22.
// Email + Microsoft Teams webhook (deferred-default DD-1 per DECISIONS.md).

@description('Environment short name: stg | prd.')
param envShortName string

@description('Azure region (e.g. saudiarabiacentral). Alerts are global resources but the
              action group respects a location for diagnostic-log retention.')
param location string = 'global'

@description('Application Insights component id (source for high-5xx + health alerts).')
param appInsightsId string

@description('Backend ACA app id (source for health-probe alert).')
param backendAppId string

@description('Key Vault id (source for kv-anomaly alert via diagnostic logs).')
param keyVaultId string

@description('Email distribution list for the platform-eng on-call rotation.')
param oncallEmail string = 'platform-oncall@example.com'

@description('Microsoft Teams Incoming Webhook URL. Populated post-provisioning via KV — leave
              blank here to provision the action group with no Teams receiver and patch later.')
param teamsWebhookUrl string = ''

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var actionGroupName = 'ag-oncall-${envShortName}'

resource actionGroup 'Microsoft.Insights/actionGroups@2023-09-01-preview' = {
  name: actionGroupName
  location: location
  tags: commonTags
  properties: {
    groupShortName: 'oncall-${envShortName}'
    enabled: true
    emailReceivers: [
      {
        name: 'platform-oncall-email'
        emailAddress: oncallEmail
        useCommonAlertSchema: true
      }
    ]
    webhookReceivers: empty(teamsWebhookUrl) ? [] : [
      {
        name: 'platform-oncall-teams'
        serviceUri: teamsWebhookUrl
        useCommonAlertSchema: true
      }
    ]
  }
}

// Alert 1 — deploy failure. Sourced from workflow-run-resulted-in-failure GitHub events
// surfaced via Log Analytics custom log (the deploy workflow audit-emits a failure row;
// a saved query against `audit_log_entries` raises this alert). The alert below is a
// metric placeholder; the actual log-search alert is wired post-provisioning when the
// workspace has data.
resource alertDeployFailure 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-deploy-failure-${envShortName}'
  location: 'global'
  tags: commonTags
  properties: {
    severity: 1
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'deploy-failure-customEvents'
          metricNamespace: 'microsoft.insights/components'
          metricName: 'customEvents/count'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Count'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// Alert 2 — health probe failures (3 consecutive non-200 in 90s).
resource alertHealthProbe 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-health-probe-${envShortName}'
  location: 'global'
  tags: commonTags
  properties: {
    severity: 1
    enabled: true
    scopes: [ backendAppId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'health-non-200'
          metricNamespace: 'Microsoft.App/containerApps'
          metricName: 'Requests'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// Alert 3 — high 5xx rate (>1% over 5 min).
resource alertHigh5xx 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-high-5xx-${envShortName}'
  location: 'global'
  tags: commonTags
  properties: {
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'request-failure-rate'
          metricNamespace: 'microsoft.insights/components'
          metricName: 'requests/failed'
          operator: 'GreaterThan'
          threshold: 1
          timeAggregation: 'Count'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// Alert 4 — Key Vault anomaly (any read by principal ≠ id-aca-<env>).
resource alertKvAnomaly 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-kv-anomaly-${envShortName}'
  location: 'global'
  tags: commonTags
  properties: {
    severity: 1
    enabled: true
    scopes: [ keyVaultId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'kv-secret-read'
          metricNamespace: 'Microsoft.KeyVault/vaults'
          metricName: 'ServiceApiHit'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Count'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

output actionGroupId string = actionGroup.id
output actionGroupName string = actionGroup.name
output deployFailureAlertId string = alertDeployFailure.id
output healthProbeAlertId string = alertHealthProbe.id
output high5xxAlertId string = alertHigh5xx.id
output kvAnomalyAlertId string = alertKvAnomaly.id
