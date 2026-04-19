#!/usr/bin/env bash
# Applies branch-protection rules on `main` per docs/branch-protection.md.
# Idempotent. Requires: gh CLI authenticated with admin access to the repo.
#
# Usage:
#   scripts/apply-branch-protection.sh                 # uses origin remote
#   REPO=owner/name scripts/apply-branch-protection.sh # explicit target

set -euo pipefail

if ! command -v gh >/dev/null 2>&1; then
  echo "error: gh CLI not found. Install with: brew install gh" >&2
  exit 1
fi

if ! gh auth status >/dev/null 2>&1; then
  echo "error: gh is not authenticated. Run: gh auth login" >&2
  exit 1
fi

if [ -z "${REPO:-}" ]; then
  REMOTE_URL="$(git remote get-url origin 2>/dev/null || true)"
  if [ -z "${REMOTE_URL}" ]; then
    echo "error: no origin remote and REPO env var not set" >&2
    exit 1
  fi
  REPO="$(echo "${REMOTE_URL}" | sed -E 's|^https://github\.com/||; s|^git@github\.com:||; s|\.git$||')"
fi

BRANCH="main"
echo "Applying branch protection to ${REPO}@${BRANCH}..."

PAYLOAD="$(cat <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "lint-format",
      "contract-diff",
      "verify-context-fingerprint",
      "build",
      "preview-deploy"
    ]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": {
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": true,
    "required_approving_review_count": 1,
    "require_last_push_approval": true
  },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false,
  "required_conversation_resolution": true,
  "lock_branch": false,
  "allow_fork_syncing": true,
  "required_signatures": true
}
JSON
)"

echo "${PAYLOAD}" | gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  "repos/${REPO}/branches/${BRANCH}/protection" \
  --input -

echo "Enforcing squash-merge-only at repo level..."
gh api --method PATCH -H "Accept: application/vnd.github+json" "repos/${REPO}" \
  -F allow_squash_merge=true \
  -F allow_merge_commit=false \
  -F allow_rebase_merge=false \
  -F delete_branch_on_merge=true >/dev/null

echo "Enabling required signatures explicitly (some plans need this endpoint)..."
gh api --method POST -H "Accept: application/vnd.github+json" \
  "repos/${REPO}/branches/${BRANCH}/protection/required_signatures" >/dev/null || true

echo "Done. Verify at: https://github.com/${REPO}/settings/branches"
