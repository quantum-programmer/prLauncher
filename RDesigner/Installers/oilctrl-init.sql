CREATE TABLE IF NOT EXISTS public."VersionInfo"
(
    "Version" bigint NOT NULL,
    "AppliedOn" timestamp without time zone,
    "Description" character varying(1024) COLLATE pg_catalog."default"
);

