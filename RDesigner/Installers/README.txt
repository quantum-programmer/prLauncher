Place PostgreSQL distribution files here for offline installation.

Windows:
  postgresql-16.14-*-windows-x64-binaries.zip

Linux:
  Astra Linux 1.8 / Debian 12 Bookworm PGDG packages are supported:
    postgresql-client-common_*_all.deb
    postgresql-common_*_all.deb
    postgresql-client-16_*_amd64.deb
    postgresql-16_*_amd64.deb
    libpq5_*_amd64.deb
  Put them into any subfolder under Installers, for example:
    Installers/postgresql-16.14.1 linux

  Astra repositories can provide dependencies such as libjson-perl and libllvm19.
  For fully offline installation, place their .deb packages into the same folder too.
