Place PostgreSQL distribution files here for offline installation.

Windows:
  postgresql-16.14-*-windows-x64-binaries.zip

Linux:
  Astra Linux 1.8 / Debian 12 Bookworm PGDG packages are supported.
  Prefer this layout:
    Installers/packages/postgresql-16.14-astra18-amd64/*.deb

  Required PGDG packages:
    postgresql-client-common_*_all.deb
    postgresql-common_*_all.deb
    postgresql-client-16_*_amd64.deb
    postgresql-16_*_amd64.deb
    libpq5_*_amd64.deb

  For fully offline installation, include dependency packages in the same
  package folder. On the tested Astra Linux 1.8 VM, PostgreSQL also needed:
    libjson-perl_*_all.deb
    libllvm19_*_amd64.deb
    ssl-cert_*_all.deb

  The target Astra installation usually already contains base OS packages such
  as adduser, debconf, libc6, libgcc-s1, libicu72, libldap-2.5-0, liblz4-1,
  libpam0g, libselinux1, libssl3, libstdc++6, libsystemd0, libuuid1, libxml2,
  libxslt1.1, libzstd1, locales, perl, tzdata, ucf, zlib1g. Keep a copy of
  any missing dependency .deb packages in Installers/packages for offline sites.

Linux setup scripts:
  Optional OS preparation scripts can be placed here:
    Installers/sh/*.sh

  Scripts are executed before PostgreSQL package installation, in alphabetical
  order, with elevated privileges. They must be idempotent.

  VirtualBox/debug-only packages such as build-essential, dkms, linux-headers-*,
  virtualbox-guest-utils, and virtualbox-guest-x11 are not required for the
  customer PostgreSQL installation.
