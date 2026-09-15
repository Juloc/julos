-- 0003_audit_append_only
-- Give SQLite the append-only audit guarantee PostgreSQL enforces with a trigger
-- (docs/TECHNICAL_SPECIFICATION.md, decision D043).
--
-- PostgreSQL uses one BEFORE UPDATE OR DELETE trigger calling a RAISE function.
-- SQLite has no OR in a trigger event and no procedural language, so the same rule needs
-- one trigger per event, each aborting with RAISE.
--
-- Hand-written: triggers are not part of the Entity Framework model on either provider.

CREATE TRIGGER "audit_event_is_append_only_update"
BEFORE UPDATE ON "audit_events"
BEGIN
    SELECT RAISE(ABORT, 'Audit events are append-only.');
END;

CREATE TRIGGER "audit_event_is_append_only_delete"
BEFORE DELETE ON "audit_events"
BEGIN
    SELECT RAISE(ABORT, 'Audit events are append-only.');
END;
