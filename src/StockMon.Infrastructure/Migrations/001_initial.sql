-- Bootstrap/migration identity only. Runtime identities are never table owners.
CREATE ROLE stockmon_reader NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE stockmon_collector NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA stockmon;
REVOKE ALL ON SCHEMA stockmon FROM PUBLIC;

CREATE TABLE stockmon.owners (
    id text PRIMARY KEY CHECK (id ~ '^[A-Za-z0-9_-]{1,80}$')
);
CREATE TABLE stockmon.principals (
    id text PRIMARY KEY,
    db_login name UNIQUE NOT NULL,
    role text NOT NULL CHECK (role IN ('owner','account_viewer','risk_viewer','operator','collector','analyst','notifier')),
    owner_id text REFERENCES stockmon.owners,
    active boolean NOT NULL DEFAULT false
);
CREATE TABLE stockmon.accounts (
    id text PRIMARY KEY CHECK (id ~ '^[A-Za-z0-9_-]{1,80}$'),
    owner_id text NOT NULL REFERENCES stockmon.owners,
    broker text NOT NULL CHECK (broker IN ('Kis','Shinhan')),
    product text NOT NULL CHECK (product IN ('Isa','Domestic','Overseas','Pension')),
    environment text NOT NULL CHECK (environment IN ('Live','Demo')),
    status text NOT NULL DEFAULT 'verification_required' CHECK (status IN ('verification_required','disabled','synthetic')),
    UNIQUE (id, owner_id)
);
CREATE TABLE stockmon.access_grants (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    principal_id text NOT NULL REFERENCES stockmon.principals,
    account_id text NOT NULL,
    owner_id text NOT NULL,
    permission text NOT NULL CHECK (permission IN ('detail','aggregate','collect','notify','analyze')),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    evidence_ref text NOT NULL CHECK (length(trim(evidence_ref)) > 0),
    FOREIGN KEY (account_id, owner_id) REFERENCES stockmon.accounts(id, owner_id)
);
CREATE TABLE stockmon.consents (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id text NOT NULL REFERENCES stockmon.owners,
    principal_id text NOT NULL REFERENCES stockmon.principals,
    permission text NOT NULL CHECK (permission IN ('detail','aggregate','collect','notify','analyze')),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    evidence_ref text NOT NULL CHECK (length(trim(evidence_ref)) > 0)
);
CREATE TABLE stockmon.instruments (
    id text PRIMARY KEY,
    code text NOT NULL,
    exchange_mic text NOT NULL CHECK (exchange_mic ~ '^[A-Z]{4}$'),
    currency text NOT NULL CHECK (currency IN ('KRW','USD')),
    valid_from date NOT NULL,
    valid_to date CHECK (valid_to >= valid_from),
    UNIQUE(exchange_mic, code, valid_from)
);
-- MVP-03 scoped storage probe; complete position/cash snapshots start in MVP-05.
CREATE TABLE stockmon.account_observations (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id text NOT NULL,
    owner_id text NOT NULL,
    as_of timestamptz NOT NULL,
    quality text NOT NULL CHECK (quality IN ('Synthetic','Complete','Partial','Stale','Unknown')),
    FOREIGN KEY (account_id, owner_id) REFERENCES stockmon.accounts(id, owner_id)
);
CREATE TABLE stockmon.audit_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    actor name NOT NULL,
    entity text NOT NULL,
    operation text NOT NULL CHECK (operation IN ('INSERT','UPDATE','DELETE'))
);

-- Identity is bound to the authenticated PostgreSQL login, not a caller-set owner variable.
-- Provision distinct credentials/pools per principal. A shared dashboard login is unsupported.
CREATE FUNCTION stockmon.can_access(target_account text, target_owner text, requested text)
RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, stockmon AS $$
 SELECT EXISTS (
   SELECT 1 FROM stockmon.principals p
   WHERE p.db_login = session_user AND p.active
   AND CASE p.role
     WHEN 'owner' THEN requested IN ('detail','aggregate')
     WHEN 'account_viewer' THEN requested IN ('detail','aggregate')
     WHEN 'risk_viewer' THEN requested = 'aggregate'
     WHEN 'collector' THEN requested = 'collect' AND p.owner_id = target_owner
     WHEN 'analyst' THEN requested = 'analyze'
     WHEN 'notifier' THEN requested = 'notify'
     ELSE false END
   AND (
     (p.role = 'owner' AND p.owner_id = target_owner)
     OR (
       EXISTS (SELECT 1 FROM stockmon.access_grants g
         WHERE g.principal_id = p.id AND g.account_id = target_account AND g.owner_id = target_owner
         AND g.permission = requested AND g.expires_at > statement_timestamp() AND g.revoked_at IS NULL)
       AND (p.owner_id = target_owner OR EXISTS (
         SELECT 1 FROM stockmon.consents c WHERE c.owner_id = target_owner AND c.principal_id = p.id
         AND c.permission = requested AND c.expires_at > statement_timestamp() AND c.revoked_at IS NULL))
     )
   )
 );
$$;
REVOKE ALL ON FUNCTION stockmon.can_access(text,text,text) FROM PUBLIC;

CREATE FUNCTION stockmon.record_audit() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, stockmon AS $$
BEGIN
    INSERT INTO stockmon.audit_events(actor, entity, operation) VALUES (session_user, TG_TABLE_NAME, TG_OP);
    RETURN NULL;
END;
$$;
REVOKE ALL ON FUNCTION stockmon.record_audit() FROM PUBLIC;
CREATE TRIGGER accounts_audit AFTER INSERT OR UPDATE OR DELETE ON stockmon.accounts
    FOR EACH ROW EXECUTE FUNCTION stockmon.record_audit();
CREATE TRIGGER grants_audit AFTER INSERT OR UPDATE OR DELETE ON stockmon.access_grants
    FOR EACH ROW EXECUTE FUNCTION stockmon.record_audit();
CREATE TRIGGER consents_audit AFTER INSERT OR UPDATE OR DELETE ON stockmon.consents
    FOR EACH ROW EXECUTE FUNCTION stockmon.record_audit();
CREATE TRIGGER principals_audit AFTER INSERT OR UPDATE OR DELETE ON stockmon.principals
    FOR EACH ROW EXECUTE FUNCTION stockmon.record_audit();
CREATE TRIGGER observations_audit AFTER INSERT OR UPDATE OR DELETE ON stockmon.account_observations
    FOR EACH ROW EXECUTE FUNCTION stockmon.record_audit();

ALTER TABLE stockmon.accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE stockmon.accounts FORCE ROW LEVEL SECURITY;
CREATE POLICY accounts_read ON stockmon.accounts FOR SELECT TO stockmon_reader
    USING (stockmon.can_access(id, owner_id, 'detail'));
CREATE POLICY accounts_collect ON stockmon.accounts FOR SELECT TO stockmon_collector
    USING (stockmon.can_access(id, owner_id, 'collect'));
ALTER TABLE stockmon.account_observations ENABLE ROW LEVEL SECURITY;
ALTER TABLE stockmon.account_observations FORCE ROW LEVEL SECURITY;
CREATE POLICY observations_read ON stockmon.account_observations FOR SELECT TO stockmon_reader
    USING (stockmon.can_access(account_id, owner_id, 'detail'));
CREATE POLICY observations_collect ON stockmon.account_observations FOR SELECT TO stockmon_collector
    USING (stockmon.can_access(account_id, owner_id, 'collect'));
CREATE POLICY observations_insert ON stockmon.account_observations FOR INSERT TO stockmon_collector
    WITH CHECK (stockmon.can_access(account_id, owner_id, 'collect'));

GRANT USAGE ON SCHEMA stockmon TO stockmon_reader, stockmon_collector;
GRANT EXECUTE ON FUNCTION stockmon.can_access(text,text,text) TO stockmon_reader, stockmon_collector;
GRANT SELECT ON stockmon.accounts, stockmon.account_observations, stockmon.instruments TO stockmon_reader, stockmon_collector;
GRANT INSERT ON stockmon.account_observations TO stockmon_collector;
GRANT USAGE ON SEQUENCE stockmon.account_observations_id_seq TO stockmon_collector;
-- No runtime permission on principals, grants, consent, audit, migrations or credentials.
