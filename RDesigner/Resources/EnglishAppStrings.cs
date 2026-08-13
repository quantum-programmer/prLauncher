namespace Pyramid.Resources;

public static class EnglishAppStrings
{
    public const string ApplicationStarted = "Application started";
    public const string ApplicationFailed = "The application exited with an error";
    public const string LogsDirectoryOpenFailed = "Could not open the logs directory: {LogsDirectory}";
    public const string LanguageChanged = "Application language changed: {Language}";

    public const string AboutTitle = "About";
    public const string AboutVersion = "Version:";
    public const string AboutBuild = "Build:";
    public const string AboutFullVersion = "Full version:";
    public const string AboutGitHash = "Git hash:";
    public const string AboutAssemblyVersion = "Assembly version:";
    public const string AboutFileVersion = "File version:";

    public const string InstallerTitle = "Pyramid";
    public const string InstallerWelcomeText = "Welcome to the Prompribor workstation setup";
    public const string InstallerServerInstallTitle = "Server workstation installation";
    public const string InstallerServerInstallDescription = "Installs client and server components on one computer. Work uses a single shared database.";
    public const string InstallerClientInstallTitle = "Client workstation installation";
    public const string InstallerClientInstallDescription = "Installs only client components. Users work with the shared database from multiple workstations.";
    public const string InstallerAdvancedTitle = "Advanced";
    public const string InstallerAdvancedDescription = "Additional installation and maintenance scenarios will be available later.";
    public const string InstallerBackButton = "Back";
    public const string InstallerNextButton = "Next";
    public const string InstallerLicenseTitle = "License Agreement";
    public const string InstallerLicenseAcceptText = "I accept the terms of the agreement";
    public const string InstallerLicenseText = """
        SOFTWARE LICENSE AGREEMENT FOR THE PROMPRIBOR WORKSTATION

        This License Agreement governs the installation and use of the Prompribor workstation software (the Software).

        1. RIGHT OF USE
        The user is granted a non-exclusive right to install and use the Software within the limits established by the supply agreement and operating documentation.

        2. RESTRICTIONS
        The Software may not be transferred to third parties, redistributed, used to alter licensing mechanisms, or used in ways not permitted by this Agreement and the accompanying documentation.

        3. RESPONSIBILITY
        The user is responsible for proper equipment configuration, data backups, and compliance with information security requirements. The Software must be used by qualified personnel for its intended purpose.

        4. CONFIDENTIALITY
        Components, documentation, and technical information supplied with the Software must not be disclosed to third parties without the copyright holder's permission, except where required by law.

        5. TERM
        This Agreement remains in force throughout the period in which the Software is used. A breach of its terms may result in termination of the right to use the Software.

        By accepting this Agreement and continuing the installation, the user confirms that they have read and understood these terms and have the authority required to install the Software.
        """;
    public const string InstallerLinuxPostgresSystemInstallNote = "On Linux, PostgreSQL is installed system-wide from offline packages. The PostgreSQL installation folder is selected by the package manager.";
    public const string InstallerSubtitle = "Installation";
    public const string InstallerInstallFolder = "PostgreSQL install folder";
    public const string InstallerInstallFolderWatermark = "PostgreSQL installation folder";
    public const string InstallerBrowse = "Browse";
    public const string InstallerSilentInstall = "Install workstation";
    public const string InstallerRunWizard = "Run wizard";
    public const string InstallerCheckConnection = "Check connection";
    public const string InstallerCopy = "Copy";
    public const string InstallerReadyStatus = "Ready to install workstation";
    public const string InstallerInstallOperation = "PostgreSQL installation";
    public const string InstallerArmInstallOperation = "Workstation installation";
    public const string InstallerArmAlreadyInstalledLog = "Installed workstation version detected: {Directory}";
    public const string InstallerArmInstallCancelledLog = "Workstation application installation was cancelled by the user.";
    public const string InstallerArmInstallCancelledShort = "Cancelled by user";
    public const string InstallerArmReinstallQuestion = "An installed workstation version was found in:{NewLine}{Directory}{NewLine}{NewLine}Reinstall workstation?";
    public const string InstallerArmReinstallButton = "Reinstall";
    public const string InstallerCancelButton = "Cancel";
    public const string InstallerYesButton = "Yes";
    public const string InstallerNoButton = "No";
    public const string InstallerLaunchOilCtrlCfgQuestion = "Workstation installation completed. Start OilCtrlCfg to configure the database?";
    public const string InstallerOilCtrlCfgExitedLog = "OilCtrlCfg started, but exited immediately with code {ExitCode}: {Path}";
    public const string InstallerOilCtrlCfgStartedLog = "OilCtrlCfg started: {Path}";
    public const string InstallerOilCtrlCfgLaunchFailedLog = "OilCtrlCfg launch failed: {Message}";
    public const string InstallerProductSourceBundleRootLog = "Application bundle source folder: {Directory}";
    public const string InstallerProductInstallRootLog = "Application install folder: {Directory}";
    public const string InstallerProductRemovingPreviousInstallLog = "Removing previous workstation applications from: {Directory}. PostgreSQL and other data are preserved.";
    public const string InstallerProductRemovingApplicationLog = "Removing application {Application}: {Directory}";
    public const string InstallerProductApplicationFolderNotFound = "Application folder was not found: {Directory}";
    public const string InstallerProductInstallingApplicationLog = "Installing {Application}: {Source} -> {Target}";
    public const string InstallerProductOilCtrlCfgNotFound = "OilCtrlCfg was not found: {Path}";
    public const string InstallerProductPlatformNotSupported = "Application installation is supported only on Windows and Linux.";
    public const string InstallerProductBundleRootNotFound = "The product bundle folder with ASNCtrl_Linux, R_Designer_L, and OilCtrlCfg was not found. Start Pyramid from the shared single build.";
    public const string InstallerProductWindowsCopyRequiresAdminLog = "Copying applications to C:\\Prompribor. Administrator privileges are required.";
    public const string InstallerProductLinuxCopyRequiresAdminLog = "Copying applications to /opt/prompribor. Administrator privileges are required.";
    public const string InstallerProductElevatedHelperStartFailed = "Could not start the elevated helper for application installation.";
    public const string InstallerProductInstallFailedWithExitCode = "Application installation finished with exit code {ExitCode}.";
    public const string InstallerProductCurrentExecutableNotFound = "Current application executable path was not found.";
    public const string InstallerProductSharedSettingsMissingLog = "Shared appsettings.json was not found in the product bundle. Pyramid will create/update it after PostgreSQL configuration.";
    public const string InstallerProductSharedSettingsCopiedLog = "Shared appsettings.json copied: {Path}";
    public const string InstallerProductLocalSettingsDeletedLog = "Local appsettings.json was removed from the application folder: {Path}";
    public const string ConfigurationTrailingNullBytesRepaired = "A corrupt configuration file tail was repaired: {ConfigurationPath}";
    public const string InstallerProductExecutablePermissionSetLog = "Execution permission was set for executable file: {Path}";
    public const string InstallerProductInstallTreePermissionsSetLog = "Read permissions were set for install folder: {Directory}";
    public const string InstallerProductCopiedFilesProgressLog = "Copied files for {Application}: {Count}...";
    public const string InstallerProductInstallCompletedLog = "{Application} installation completed. Copied files: {Count}.";
    public const string InstallerWizardOperation = "PostgreSQL GUI installer";
    public const string InstallerCheckOperation = "PostgreSQL check";
    public const string InstallerCompletedStatus = "Operation completed";
    public const string InstallerErrorStatus = "Error";
    public const string InstallerSystemCancelledStatus = "Cancelled by system";
    public const string InstallerPutInstallerLog = "Put the PostgreSQL 16.14 Windows archive or Astra Linux 1.8 PGDG .deb packages into the Installers folder next to the application.";
    public const string InstallerMainWindowNotFoundLog = "Main window was not found. Folder dialog cannot be opened.";
    public const string InstallerSelectPostgresFolderTitle = "Select PostgreSQL installation folder";
    public const string InstallerDirectorySelectedLog = "Install directory selected: {Directory}";
    public const string InstallerFolderSelectionFailedLog = "Folder selection failed: {Message}";
    public const string InstallerClipboardUnavailableLog = "Clipboard is not available.";
    public const string InstallerLogCopiedLog = "Log copied to clipboard.";
    public const string InstallerCopyLogFailedLog = "Copy log failed: {Message}";
    public const string InstallerDoneLog = "Done.";
    public const string InstallerOsLog = "Operating system: {Description}";
    public const string InstallerTargetPostgresVersionLog = "Target PostgreSQL version: {Version}";
    public const string InstallerPlatformNotSupported = "Only Windows and Linux are supported.";
    public const string InstallerInteractiveWindowsOnly = "Interactive PostgreSQL installer mode is available only on Windows.";
    public const string InstallerExeNotFound = "PostgreSQL 16.14 installer was not found. Put postgresql-16.14-*-windows-x64.exe into the Installers folder next to the application.";
    public const string InstallerBinariesArchiveNotFound = "PostgreSQL 16.14 binaries archive was not found. Put postgresql-16.14-*-windows-x64-binaries.zip into the Installers folder next to the application.";
    public const string InstallerInstallerPathLog = "Installer: {Path}";
    public const string InstallerBinariesArchiveLog = "Binaries archive: {Path}";
    public const string InstallerStartingGuiLog = "Starting PostgreSQL GUI installer. Administrator privileges are required.";
    public const string InstallerWizardValuesLog = "Use these values in the PostgreSQL wizard:";
    public const string InstallerWizardInstallDirLog = "- Installation directory: {Directory}";
    public const string InstallerWizardDataDirLog = "- Data directory: {Directory}";
    public const string InstallerWizardPortLog = "- Port: {Port}";
    public const string InstallerWizardSuperPasswordLog = "- Superuser password: {Password}";
    public const string InstallerWizardComponentsLog = "- Components: PostgreSQL Server and Command Line Tools are required.";
    public const string InstallerWizardStackBuilderLog = "- Stack Builder: clear this checkbox; it is not required for OilCtrl.";
    public const string InstallerWizardPgAdminLog = "- pgAdmin 4: optional.";
    public const string InstallerPsqlNotFoundLog = "psql was not found. PostgreSQL is not installed or its bin directory is not in PATH.";
    public const string InstallerPsqlPathLog = "psql: {Path}";
    public const string InstallerPortLog = "Port: {Port}";
    public const string InstallerStartingSilentBinariesLog = "Starting silent PostgreSQL binaries installation.";
    public const string InstallerWindowsBinariesInstallRequiresAdminLog = "Installing PostgreSQL binaries into the selected folder. Administrator privileges are required.";
    public const string InstallerWindowsBinariesInstallFailed = "PostgreSQL binaries installation finished with exit code {ExitCode}.";
    public const string InstallerPreparingWindowsDataDirectoryAclLog = "Preparing PostgreSQL data directory permissions: {Directory}";
    public const string InstallerWindowsDataDirectoryAclFailed = "PostgreSQL data directory permission setup finished with exit code {ExitCode}: {Directory}";
    public const string InstallerCurrentWindowsUserSidNotFound = "Failed to determine current Windows user SID for PostgreSQL data directory permission setup.";
    public const string InstallerInstallDirLog = "Install directory: {Directory}";
    public const string InstallerDataDirLog = "Data directory: {Directory}";
    public const string InstallerSelectedPostgresPortLog = "Selected PostgreSQL port: {Port}";
    public const string InstallerWindowsServiceLog = "Windows service: {ServiceName}";
    public const string InstallerComponentsSkippedLog = "Components: PostgreSQL Server and Command Line Tools only. Stack Builder and pgAdmin are skipped.";
    public const string InstallerDirectoryNotEmpty = "Installation directory is not empty: {Directory}. Remove this directory or choose another OilCtrl PostgreSQL install directory before retrying.";
    public const string InstallerPostgresAlreadyExistsInDirectory = "PostgreSQL was already detected in the selected folder: {Directory}. Select another PostgreSQL installation folder.";
    public const string InstallerWindowsPostgresReinstallDetectedLog = "A previous PostgreSQL installation was detected: {Directory}";
    public const string InstallerWindowsPostgresReinstallRequiresAdminLog = "Backing up the database and removing the previous PostgreSQL installation. Administrator privileges are required.";
    public const string InstallerWindowsPostgresUnsafeReinstallDirectory = "The selected PostgreSQL directory is unsafe. The drive root and the entire C:\\Prompribor directory cannot be removed.";
    public const string InstallerWindowsPostgresBackupPreparingLog = "Backing up the OilCtrl database before reinstalling PostgreSQL.";
    public const string InstallerWindowsPostgresBackupToolNotFound = "pg_dump was not found for database backup: {Path}";
    public const string InstallerWindowsPostgresServiceStartingForBackupLog = "PostgreSQL service {ServiceName} is stopped. Starting it for backup.";
    public const string InstallerWindowsPostgresManualStartForBackupLog = "The PostgreSQL service is unavailable. Starting the server directly for backup.";
    public const string InstallerWindowsPostgresCouldNotStartForBackup = "The previous PostgreSQL instance could not be started for backup.";
    public const string InstallerWindowsPostgresBackupExitCode = "pg_dump finished with exit code {ExitCode}";
    public const string InstallerWindowsPostgresBackupCompletedLog = "The OilCtrl database backup was created: {Path}";
    public const string InstallerWindowsPostgresBackupFailedContinuingLog = "The OilCtrl database could not be backed up: {Reason}. Reinstallation will continue.";
    public const string InstallerWindowsPostgresStoppingServerLog = "Stopping the previous PostgreSQL instance.";
    public const string InstallerWindowsPostgresStopServerFailed = "The previous PostgreSQL instance could not be stopped. Exit code: {ExitCode}.";
    public const string InstallerWindowsPostgresStoppingServiceLog = "Stopping PostgreSQL service: {ServiceName}";
    public const string InstallerWindowsPostgresDeletingServiceLog = "Deleting PostgreSQL service: {ServiceName}";
    public const string InstallerWindowsPostgresDeleteServiceFailed = "PostgreSQL service {ServiceName} could not be deleted. Exit code: {ExitCode}.";
    public const string InstallerWindowsPostgresRemovingDirectoryLog = "Removing the previous PostgreSQL directory: {Directory}";
    public const string InstallerWindowsPostgresPreviousInstallRemovedLog = "The previous PostgreSQL installation was removed.";
    public const string InstallerWindowsPostgresRemoveDirectoryFailed = "The previous PostgreSQL directory could not be removed: {Reason}";
    public const string InstallerWindowsPostgresReinstallFailed = "Preparing PostgreSQL for reinstallation finished with exit code {ExitCode}.";
    public const string InstallerArmInstallSystemCancelledShort = "Cancelled by system";
    public const string InstallerRequiredExecutablesNotFound = "PostgreSQL binaries were extracted, but required executables were not found in the bin directory.";
    public const string InstallerClientAlreadyExistsLog = "PostgreSQL client already exists: {Path}";
    public const string InstallerMatchingWindowsServiceNotFoundLog = "Matching PostgreSQL Windows service was not found for this installation directory.";
    public const string InstallerLinuxPsqlNotFoundLog = "psql was not found.";
    public const string InstallerLinuxPostgresReinstallScanLog = "Searching for the previous PostgreSQL instance containing the OilCtrl database before reinstalling the workstation.";
    public const string InstallerLinuxPostgresConfiguredPortLog = "The installed workstation configuration specifies PostgreSQL port {Port}.";
    public const string InstallerLinuxPostgresClusterToolsNotFoundLog = "pg_lsclusters was not found. The previous PostgreSQL cluster check was skipped.";
    public const string InstallerLinuxPostgresNoClustersLog = "No PostgreSQL clusters were found on the system.";
    public const string InstallerLinuxPostgresCheckingClusterLog = "Checking PostgreSQL cluster {Cluster} on port {Port}.";
    public const string InstallerLinuxPostgresStartingClusterForBackupLog = "PostgreSQL cluster {Cluster} is stopped. Starting it for backup.";
    public const string InstallerLinuxPostgresClusterCheckFailedLog = "The databases in PostgreSQL cluster {Cluster} could not be checked: {Reason}";
    public const string InstallerLinuxPostgresOilCtrlFoundLog = "The OilCtrl database was found in PostgreSQL cluster {Cluster} on port {Port}.";
    public const string InstallerLinuxPostgresPgDumpNotFoundLog = "pg_dump was not found for the OilCtrl database backup.";
    public const string InstallerLinuxPostgresBackupCompletedLog = "The OilCtrl database backup was created: {Path}";
    public const string InstallerLinuxPostgresBackupFailedContinuingLog = "The OilCtrl database could not be backed up: {Reason}. Reinstallation will continue.";
    public const string InstallerLinuxPostgresRemovingClusterLog = "Removing the previous PostgreSQL cluster: {Cluster}";
    public const string InstallerLinuxPostgresClusterRemovedLog = "The previous PostgreSQL cluster {Cluster} was removed.";
    public const string InstallerLinuxPostgresClusterRemoveFailedLog = "The previous PostgreSQL cluster {Cluster} could not be removed.";
    public const string InstallerLinuxPostgresOilCtrlNotFoundLog = "A previous PostgreSQL cluster containing the OilCtrl database was not found. Installation will continue.";
    public const string InstallerLinuxDistributionLog = "Linux distribution: {Id}, version {Version}";
    public const string InstallerLinuxDistributionNotSupported = "Linux installer supports Astra Linux/Debian-compatible systems only.";
    public const string InstallerLinuxDebPackagesNotFound = "PostgreSQL 16.14 .deb packages were not found. Put the PGDG Debian 12 packages, including libpq5, into an Installers subfolder.";
    public const string InstallerLinuxDebPackagesLog = "Offline PostgreSQL .deb packages: {Directory}";
    public const string InstallerLinuxSetupScriptLog = "Running Linux setup script: {Path}";
    public const string InstallerLinuxClusterLog = "Linux PostgreSQL cluster: {Cluster}";
    public const string InstallerLinuxClusterNameExistsOnPortLog = "TCP port {Port} is free, but Linux PostgreSQL cluster/service already exists: {Cluster}, {ServiceName}. Trying next port.";
    public const string InstallerStartLinuxServiceLog = "Starting Linux PostgreSQL service: {ServiceName}";
    public const string InstallerLinuxSettingPasswordLog = "Setting PostgreSQL superuser password for Linux cluster.";
    public const string InstallerLinuxPrivilegeToolNotFound = "pkexec or sudo was not found. Linux installation requires privilege elevation.";
    public const string InstallerLinuxPrivilegeRunnerFailedLog = "Privilege elevation helper {Tool} failed with exit code {ExitCode}. Trying next helper.";
    public const string InstallerClientFoundLog = "PostgreSQL client found: {Path}";
    public const string InstallerUsingPostgresPortLog = "Using PostgreSQL port: {Port}";
    public const string InstallerCheckingDatabaseLog = "Checking database {Database}.";
    public const string InstallerCreatingDatabaseLog = "Creating database {Database}.";
    public const string InstallerDatabaseExistsLog = "Database {Database} already exists.";
    public const string InstallerApplyingInitSqlLog = "Applying oilctrl-init.sql.";
    public const string InstallerWineCheckingLog = "Checking for Wine.";
    public const string InstallerWineAlreadyInstalledLog = "Wine is already installed: {Version}";
    public const string InstallerWineReinstallQuestion = "Installed Wine was detected: {Version}.{NewLine}Reinstall?";
    public const string InstallerWineReinstallDeclinedLog = "Wine reinstallation was declined. The installed version will be used.";
    public const string InstallerWineRemovingLog = "Removing the installed Wine before reinstallation.";
    public const string InstallerWineRemovalCompletedLog = "The installed Wine was removed.";
    public const string InstallerWineRemovalFailed = "Removing the installed Wine failed with exit code {ExitCode}.";
    public const string InstallerWinePackagesDirectoryLog = "Offline Wine packages: {Directory}";
    public const string InstallerWineStagingPackagesLog = "Copying Wine packages to a temporary local folder: {Directory}";
    public const string InstallerWineStagedPackageLog = "Prepared Wine package {Current} of {Total}: {Package}";
    public const string InstallerWinePackagesDirectoryNotFound = "The offline Wine package folder was not found: {Directory}";
    public const string InstallerWinePackageNotFound = "A required Wine package was not found: {Pattern}";
    public const string InstallerWineMultiplePackagesFound = "Multiple Wine packages matching {Pattern} were found. Keep only one package for each dependency.";
    public const string InstallerWineInstallRequiresAdminLog = "Installing Wine from offline packages. Administrator privileges are required.";
    public const string InstallerWineInstallingStageLog = "Installing Wine components, stage {Stage} of {TotalStages}.";
    public const string InstallerWineDpkgNotFound = "The system command /usr/bin/dpkg was not found.";
    public const string InstallerWineDpkgFailed = "Wine component installation failed at stage {Stage} with exit code {ExitCode}.";
    public const string InstallerWineInstallCompletedLog = "Wine installation completed: {Version}";
    public const string InstallerWinePrivilegeHelperFailedLog = "Privilege elevation helper {Tool} failed with exit code {ExitCode}. Trying the next helper.";
    public const string InstallerWinePrivilegeHelperErrorLog = "Could not start privilege elevation helper {Tool}: {Message}";
    public const string InstallerWinePrivilegeCommandLog = "[{Tool}] > {Command}";
    public const string InstallerWinePrivilegeToolNotFound = "Neither fly-su nor pkexec was found. Wine installation requires administrator privileges.";
    public const string InstallerWineInstallFailedWithExitCode = "Wine installation finished with exit code {ExitCode}.";
    public const string InstallerWineCurrentExecutableNotFound = "The current Pyramid executable path could not be determined.";
    public const string InstallerExtractingBinariesLog = "Extracting PostgreSQL binaries...";
    public const string InstallerUnsafeArchiveEntry = "Unsafe archive entry path: {Entry}";
    public const string InstallerExtractedFilesLog = "Extracted {Count} files...";
    public const string InstallerExtractionCompletedLog = "Extraction completed. Extracted {Count} files.";
    public const string InstallerDataDirectoryExistsLog = "Data directory already exists and is not empty: {Directory}";
    public const string InstallerInitDbLog = "Initializing PostgreSQL data directory with initdb...";
    public const string InstallerConfigNotFound = "postgresql.conf was not found: {Path}";
    public const string InstallerConfiguringPostgresLog = "Configuring PostgreSQL port and listen address...";
    public const string InstallerRegisterServiceLog = "Registering and starting PostgreSQL Windows service. Administrator privileges are required.";
    public const string InstallerServiceInstallationLog = "Service installation log: {Path}";
    public const string InstallerServiceRegistrationFailed = "PostgreSQL service registration failed with exit code {ExitCode}.";
    public const string InstallerExistingServicesNotFoundLog = "Existing PostgreSQL Windows services were not found.";
    public const string InstallerExistingServicesLog = "Existing PostgreSQL Windows services:";
    public const string InstallerUnknownPort = "unknown port";
    public const string InstallerPortValue = "port {Port}";
    public const string InstallerNoOccupiedPortsLog = "No occupied TCP ports in range 5432-5500.";
    public const string InstallerOccupiedPortsLog = "Occupied TCP ports in range 5432-5500: {Ports}";
    public const string InstallerServiceAlreadyRunningLog = "Windows service is already running: {ServiceName}";
    public const string InstallerServiceNotRunningLog = "Windows service {ServiceName} is not running. Current state: {State}.";
    public const string InstallerStartServiceLog = "Starting PostgreSQL Windows service. Administrator privileges are required.";
    public const string InstallerServiceStartLog = "Service start log: {Path}";
    public const string InstallerServiceStartFailed = "PostgreSQL service start failed with exit code {ExitCode}.";
    public const string InstallerPortFallbackLog = "PostgreSQL port was not found in installation metadata. Falling back to preferred port {Port}.";
    public const string InstallerServiceNameExistsOnPortLog = "TCP port {Port} is free, but Windows service already exists: {ServiceName}. Trying next port.";
    public const string InstallerSelectedFreeTcpPortLog = "Selected free TCP port: {Port}";
    public const string InstallerNoFreeTcpWithServiceLog = "Could not find a free TCP port with a free OilCtrl Windows service name in preferred ranges 5433-5500 and 15432-15531.";
    public const string InstallerNoFreeTcpLog = "Could not find a free TCP port in preferred ranges 5433-5500 and 15432-15531.";
    public const string InstallerNoFreeTcp = "No free TCP port was found for PostgreSQL.";
    public const string InstallerCommandFailed = "Command failed with exit code {ExitCode}: {FileName}";
    public const string InstallerTraceTailLog = "Last lines from {Path}:";
}
