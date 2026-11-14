CREATE TABLE IF NOT EXISTS schema_migrations (version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE accounts (
  uid text PRIMARY KEY CHECK (length(uid) BETWEEN 1 AND 128),
  clock bigint NOT NULL DEFAULT 0 CHECK (clock >= 0),
  disabled boolean NOT NULL DEFAULT false,
  request_window timestamptz NOT NULL DEFAULT now(), request_count integer NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE works (
  owner_uid text NOT NULL REFERENCES accounts(uid),
  id text NOT NULL CHECK (id ~ '^[0-9a-f]{32}$'),
  revision bigint NOT NULL CHECK (revision > 0),
  document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY(owner_uid, id)
);
CREATE INDEX works_changes ON works(owner_uid, revision);
CREATE TABLE sync_requests (
  owner_uid text NOT NULL REFERENCES accounts(uid), request_id text NOT NULL,
  fingerprint text NOT NULL, revision bigint NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY(owner_uid, request_id)
);
CREATE INDEX sync_requests_expiry ON sync_requests(created_at);
INSERT INTO schema_migrations(version) VALUES(1);
