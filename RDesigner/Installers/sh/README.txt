Place optional Astra Linux setup scripts here.

The installer runs *.sh files before PostgreSQL package installation:
  1. files are sorted by name;
  2. each script is executed with elevated privileges;
  3. scripts must be idempotent and safe to run more than once.

Example names:
  00-astra-preflight.sh
  10-postgresql-os-tuning.sh
