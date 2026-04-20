# Data Model: Architecture and Contracts

**Branch**: `002-architecture-and-contracts` | **Date**: 2026-04-19

This spec produces no database tables. Its deliverables are versioned architecture documents that govern all downstream data models. The "model" here is the schema of each artifact.

---

## Artifact: Entity-Relationship Model

| Attribute | Value |
|---|---|
| Primary path | `docs/architecture/erd.md` |
| Supplement path | `docs/architecture/erd.dbml` |
| Format | Mermaid `erDiagram` (primary) + DBML (supplement) |
| Version | Semantic version in file header |
| Amendment gate | 1 human code-owner approval + downstream-impact note in PR |
| Consumers | Every Phase-1 domain spec (references its entity section) |

### ERD entity schema (per entity in the document)

| Field | Type | Notes |
|---|---|---|
| Entity name | String | PascalCase, domain-qualified if ambiguous |
| Attributes | List of (name, type, nullable, constraint) | Types are domain-agnostic (text, number, boolean, timestamp, uuid) |
| Primary key | Attribute reference | Always present |
| Foreign keys | List of (attribute, referenced entity) | Includes `vendor_id` nullable FK on all ownable entities |
| Ownership domain | String | Which Phase-1 domain owns this entity |
| Relationships | List of (cardinality, related entity, relationship label) | One-to-one, one-to-many, many-to-many |

### Multi-vendor readiness pattern

Every entity that can have a future vendor owner MUST include:
```
vendor_id UUID NULL FK → vendors(id)
```
Entities without this field are explicitly non-ownable (e.g., `audit_log_entry`, `market_config`).

---

## Artifact: State Machine Documents (× 7)

| Attribute | Value |
|---|---|
| Paths | `docs/architecture/state-machines/{domain}.md` |
| Domains | verification, cart, payment, order, shipment, return, quote |
| Format | Mermaid `stateDiagram-v2` block + Markdown table |
| Version | Semantic version per file header |
| CI validation | `mmdc` parse check on every PR touching these files |
| Amendment gate | 1 human code-owner approval |
| Consumers | Every domain spec that implements a state-machine domain |

### State machine table schema (per document)

| Column | Content |
|---|---|
| From state | Enumerated state name |
| To state | Enumerated state name |
| Trigger | Named event (e.g., `payment.authorized`, `user.cancelled`) |
| Authorized actor | Role(s) permitted to trigger (from permissions matrix) |
| Failure behavior | State the entity remains in or transitions to on error |
| Timeout behavior | State after TTL expiry (where applicable; N/A otherwise) |

### States required per domain (minimum)

| Domain | Required states (minimum) |
|---|---|
| verification | `draft`, `submitted`, `in_review`, `approved`, `rejected`, `info_requested`, `expired` |
| cart | `active`, `merged`, `abandoned`, `converted_to_order` |
| payment | `pending`, `authorizing`, `authorized`, `captured`, `failed`, `refunded`, `partially_refunded`, `voided` |
| order | `placed`, `confirmed`, `processing`, `shipped`, `delivered`, `cancelled`, `on_hold` |
| shipment | `pending`, `created`, `picked_up`, `in_transit`, `out_for_delivery`, `delivered`, `failed_delivery`, `returned` |
| return | `requested`, `approved`, `rejected`, `items_received`, `refund_initiated`, `completed`, `cancelled` |
| quote | `draft`, `submitted`, `under_review`, `revised`, `accepted`, `rejected`, `expired`, `converted_to_order` |

---

## Artifact: Permissions Matrix

| Attribute | Value |
|---|---|
| Path | `docs/architecture/permissions-matrix.md` |
| Format | Domain-grouped Markdown tables; rows = roles, columns = actions; cells = ✅ / ❌ / ⚠️ condition |
| Version | Semantic version in file header |
| Amendment gate | 1 human code-owner approval |
| Consumers | Every Phase-1 domain spec (references its domain section) |

### Roles in scope

| Role ID | Role name | Notes |
|---|---|---|
| G | Guest | Unauthenticated |
| C | Customer | Authenticated, not verified |
| P | Professional | Authenticated, verified (Principle 8) |
| BB | B2B Buyer | Authenticated, company member |
| BA | B2B Approver | Company member with approval authority |
| BrA | B2B Branch Admin | Manages one branch within a company |
| CO | B2B Company Owner | Full company account authority |
| AR | Admin Read-only | Internal staff, view only |
| AW | Admin Write | Internal staff, can edit |
| AS | Admin Super | Internal staff, full authority |

### Cell encoding

| Symbol | Meaning |
|---|---|
| ✅ | Allowed unconditionally |
| ❌ | Denied always |
| ⚠️ `[condition]` | Allowed only when condition is met (condition stated in footnote) |
| — | Not applicable to this role × resource combination |

---

## Artifact: Testing Strategy

| Attribute | Value |
|---|---|
| Path | `docs/architecture/testing-strategy.md` |
| Format | Per-spec-category sections; qualitative mandatory scenario types; no numeric thresholds |
| Version | Semantic version in file header |
| Amendment gate | 1 human code-owner approval |
| Consumers | Every Phase-1 spec at DoD review time |

### Spec categories and required layers

| Spec category | Required layers |
|---|---|
| Backend domain spec | Unit (handler/service), Integration (DB via Testcontainers), Contract (OpenAPI diff via oasdiff) |
| Flutter customer-app spec | Widget test, Integration test (flutter_test), RTL golden test (per screen) |
| Next.js admin spec | Jest unit (components + hooks), Playwright E2E (critical admin paths) |
| Integration adapter spec | Unit (mock adapter), Integration (provider sandbox or recorded cassette), Contract (schema diff) |
| Shared-contract spec | Contract diff on every PR; no separate test suite required |

### Universal mandatory scenario types (all categories)

1. Every state transition defined in the applicable state machine has at least one test.
2. Every error branch reachable by a user has at least one test.
3. Every permission boundary (role A allowed, role B denied for same action) has at least one test.
4. Every acceptance scenario in the spec maps to at least one test.

---

## Artifact: Architecture Artifact Index

| Attribute | Value |
|---|---|
| Path | `docs/architecture/index.md` |
| Format | Markdown link list with version + last-amended date per artifact |
| Amendment gate | 1 human code-owner approval (updated automatically on artifact amendment) |
| Consumers | `CLAUDE.md`, `docs/dod.md`, every domain spec header |

---

## Artifact: CI validation for Mermaid diagrams

| Attribute | Value |
|---|---|
| Job name | `validate-diagrams` |
| Added to | `.github/workflows/build-and-test.yml` |
| Trigger | Every PR touching a `.md` file |
| Tool | `mmdc` (Mermaid CLI, installed via npm) |
| Pass condition | All ` ```mermaid ` blocks in changed files parse without error |
| Fail behavior | PR merge blocked (same gate as lint-format) |
