# infra/

Reserved for infrastructure-as-code.

ADR-010 locks deployment to **Azure Saudi Arabia Central** for all tenants (KSA + EG), single-region with per-market logical partitioning. The IaC to provision that environment — Azure Static Web Apps (admin_web), Container Apps (backend_api), Postgres Flexible Server, Meilisearch, Key Vault, Storage — lands in **Phase 1E** (spec 016+ per `docs/implementation-plan.md`).

Until then, this directory is intentionally empty.
