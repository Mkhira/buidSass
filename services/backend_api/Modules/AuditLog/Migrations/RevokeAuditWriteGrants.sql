-- Append-only hardening for audit_log_entries.
REVOKE UPDATE, DELETE ON TABLE audit_log_entries FROM dental_api_app;
GRANT INSERT, SELECT ON TABLE audit_log_entries TO dental_api_app;
