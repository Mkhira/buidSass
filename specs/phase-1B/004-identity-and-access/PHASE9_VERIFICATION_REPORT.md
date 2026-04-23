# Phase 9 Verification Report — Spec 004

## T113 OpenAPI generation

- Generated: `services/backend_api/openapi.identity.json`
- Source endpoint used: `http://127.0.0.1:5099/openapi/v1.json` (Development environment)

## T117 Fingerprint

- Command: `./scripts/compute-fingerprint.sh`
- Output: `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`

## T119 Test execution

### Full suite command

- Command: `dotnet test services/backend_api/ --filter "Category!=Skip"`
- Result: **FAILED**
- Failing tests observed:
  - `Identity.Tests.Integration.EnumerationResistanceTests.EnumerationTiming_RegistrationBranchesAreConstantTime`
  - `backend_api.Tests.Observability.HealthEndpointIntegrationTests.HealthEndpoint_WhenDatabaseRunning_Returns200_Within500ms`
  - `backend_api.Tests.AuditLog.AuditEventPublisherTests.PublishAsync_WhenDatabasePaused_ThrowsNpgsqlException`

### Contract coverage run

- Command: `dotnet test services/backend_api/Tests/Identity.Tests/Identity.Tests.csproj --filter "FullyQualifiedName~Identity.Tests.Contract"`
- Result: **PASSED**
- Totals: Passed 27, Failed 0, Skipped 0

## Added polish checks

- `scripts/dev/scan-plaintext-secrets.sh` (T111)
- `scripts/dev/identity-audit-spot-check.sh` (T112)
- Integration wiring: `OperationalScriptsTests`
- Advisory perf and property tests:
  - `Unit/Argon2idBenchmarkTests.cs` (T114)
  - `Unit/BreachListPropertyTests.cs` (T115)
