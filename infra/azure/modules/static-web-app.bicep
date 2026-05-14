// E1 Phase 1 (T015). Azure Static Web App hosting the Flutter customer web bundle.
// SKU Standard, location westeurope (SWA is GA in a small set of regions — closest GA
// region to KSA / Egypt; latency to KSA / EG is acceptable because content is served
// globally via the Azure CDN edge). The KSA residency commitment under ADR-010 applies
// to personal data processing; SWA hosts only the compiled Flutter bundle (no PII at
// rest). See infra/azure/DECISIONS.md §3 for the residency-clearance attestation.

@description('Environment short name: stg | prd.')
param envShortName string

@description('Static Web App region (westeurope per data-model.md §1 row 17 note).')
param swaLocation string = 'westeurope'

@description('Four-tag map computed in main.bicep and threaded into every module.')
param commonTags object

var swaName = 'swa-customer-flutter-${envShortName}'

resource swa 'Microsoft.Web/staticSites@2023-12-01' = {
  name: swaName
  location: swaLocation
  tags: commonTags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    // Build / repo configuration lives in the deploy workflow; the resource is provisioned
    // empty here and content is uploaded by the GitHub Actions deploy step.
    allowConfigFileUpdates: true
    enterpriseGradeCdnStatus: 'Disabled'  // upgrade to Enabled post-launch if traffic warrants
  }
}

output swaId string = swa.id
output swaName string = swa.name
output swaDefaultHostname string = swa.properties.defaultHostname
