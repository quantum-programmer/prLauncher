CREATE TABLE IF NOT EXISTS public."VersionInfo"
(
    "Version" bigint NOT NULL,
    "AppliedOn" timestamp without time zone,
    "Description" character varying(1024) COLLATE pg_catalog."default"
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'admin') THEN
        CREATE ROLE admin WITH
          LOGIN
          NOSUPERUSER
          INHERIT
          NOCREATEDB
          NOCREATEROLE
          NOREPLICATION
          NOBYPASSRLS
          ENCRYPTED PASSWORD 'SCRAM-SHA-256$4096:XPe2t0Z2+Xetr89Br7Db4w==$MxCBd6ZBaQEovqc+gWfkJwkM+zPE8HoizfQ2Rb1hTvU=:kgK0h76Tb1gKWnxMmIH0w0kxL5v4m9Mx4/kqbyMMywQ=';
    END IF;
END
$$;