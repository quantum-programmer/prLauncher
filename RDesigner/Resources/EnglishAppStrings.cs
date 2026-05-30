namespace RDesigner.Resources;

public static class EnglishAppStrings
{
    public const string ApplicationStarted = "Application started";
    public const string ApplicationFailed = "The application exited with an error";
    public const string StartupDatabaseSettingsTitle = "Database settings error";
    public const string InvalidDatabaseSettings = "Invalid database connection settings.";
    public const string InvalidDatabaseSettingsLog = "Invalid database connection settings: {Errors}";
    public const string DatabaseConnectionFailedFormat = "Database connection failed. {0}";
    public const string DatabaseUnavailable = "Database unavailable";
    public const string DatabaseUnavailableWithPeriod = "Database unavailable.";
    public const string DatabasePortMustBeNumeric = "Port must be numeric.";
    public const string DatabaseOptionMustNotBeEmptyFormat = "{0} must not be empty.";

    public const string ReportsNotificationSubscriptionStarted = "PostgreSQL report_change notification subscription is active.";
    public const string ReportsNotificationSubscriptionRetry = "PostgreSQL report_change notification subscription was interrupted. Reconnecting in 5 seconds.";
    public const string LogsDirectoryOpenFailed = "Could not open the logs directory: {LogsDirectory}";
    public const string LanguageChanged = "Application language changed: {Language}";

    public const string AboutTitle = "About";
    public const string AboutVersion = "Version:";
    public const string AboutBuild = "Build:";
    public const string AboutFullVersion = "Full version:";
    public const string AboutGitHash = "Git hash:";
    public const string AboutAssemblyVersion = "Assembly version:";
    public const string AboutFileVersion = "File version:";

    public const string CreateReportTitle = "Create report";
    public const string CreateReportDialogTitle = "Report creation";
    public const string EditReportTitle = "Edit report";
    public const string ReportNamePrompt = "Enter report name:";
    public const string ReportDescriptionPrompt = "Enter report description:";
    public const string Save = "Save";
    public const string Cancel = "Cancel";
    public const string Untitled = "Untitled";

    public const string PrintableReports = "Printable reports";
    public const string TableReports = "Table reports";
    public const string View = "View";
    public const string Settings = "Settings";
    public const string Create = "Create";
    public const string Edit = "Edit";
    public const string Editor = "Editor";
    public const string Delete = "Delete";
    public const string ReportId = "Report ID:";
    public const string Revision = "Revision:";
    public const string SoftwareAssociationCode = "Software association code";
    public const string Description = "Description";
    public const string ReportsManagement = "Report management";
    public const string CreateDuplicate = "Create duplicate";
    public const string Rename = "Rename";
    public const string ImportFromXml = "Import from XML";
    public const string ExportToXmlMenu = "Export to XML";
    public const string ImportFromFrx = "Import from .frx";
    public const string ExportToFrxMenu = "Export to .frx";
    public const string UploadReportsToDatabase = "Upload reports to database";

    public const string CommandLineArguments = "Command-line arguments: {Args}";
    public const string MainWindowNotFound = "Main window was not found";
    public const string ReportIdNotFound = "Report with this ID was not found";
    public const string ReportBodyIsEmpty = "Report body is empty";
    public const string ReportLoadFailed = "Error loading report: {ErrorMessage}";
    public const string ReportsLoaded = "Reports loaded from the database";
    public const string ReportsLoadFailed = "Error loading reports from the database";
    public const string ReportOpened = "Report: {ReportName} opened for viewing";
    public const string ReportClosed = "Report: {ReportName} closed";
    public const string ErrorTitle = "Error";
    public const string ReportFileNotFoundLog = "Error: report file was not found.";
    public const string ReportFileNotFound = "Report file was not found.";
    public const string ReportFileAccessDeniedLog = "Error: report file access was denied.";
    public const string ReportFileAccessDenied = "Report file access was denied.";
    public const string ReportOpenIoError = "I/O error while opening the report.";
    public const string ReportOpenUnknownError = "Unknown error while opening the report.";

    public const string ReportCopySavedForUpload = "Copy of file {ReportName}.frx was saved to not_uploaded for later database upload";
    public const string ReportSavedToDatabase = "File {ReportName}.frx was saved to the database";
    public const string ReportUpdatedInDatabase = "File {ReportName}.frx was updated in the database";
    public const string ReportCopyRemovedFromPendingDirectory = "Copy of file {ReportName}.frx was removed from not_uploaded";
    public const string ReportCopySavedToDirectory = "Copy of file {ReportName}.frx was saved to the directory";
    public const string ReportCopyRemovedFromDirectory = "Copy of file {ReportName}.frx was removed from the directory";
    public const string ReportSaveFailedPendingCopySaved = "The report was not saved to the database. A copy was saved to not_uploaded.";
    public const string ReportSaveFailedPendingCopyLeft = "The report was not saved to the database. A copy remains in not_uploaded.";
    public const string ReportSaveFailedApplicationCopySaved = "The report was not saved to the database. A copy was saved to the application directory.";
    public const string ReportUpdateFailedPendingCopySaved = "The report was not updated in the database. A copy was saved to not_uploaded.";
    public const string ReportUpdateFailedPendingCopyLeft = "The report was not updated in the database. A copy remains in not_uploaded.";
    public const string ReportUpdateFailedApplicationCopySaved = "The report was not updated in the database. A copy was saved to the application directory.";
    public const string ReportSavedLocallyTitle = "Report saved locally";
    public const string UnavailableDatabaseReportSavedLocallyFormat = "Database unavailable. Report {0}.frx was saved locally to not_uploaded.";
    public const string UnavailableDatabaseUpdatedReportSavedLocallyFormat = "Database unavailable. Updated report {0}.frx was saved locally to not_uploaded.";
    public const string ReportCopyDeleteIoFailed = "Error deleting file {ReportName}.frx. The file may be used by another process or access may be unavailable.";
    public const string ReportCopyDeleteAccessDenied = "Error deleting file {ReportName}.frx. Access rights are insufficient.";
    public const string DatabaseOperationFailed = "Database operation failed (PostgreSQL)";
    public const string ReportChangeListenerStartFailed = "Error while starting to listen for report changes.";
    public const string ReportSaveUnknownError = "Unknown error while saving the report to the database";
    public const string ReportUpdateUnknownError = "Unknown error while updating the report in the database";
    public const string ReportFileCreateFailed = "Error creating report file: {ErrorMessage}";
    public const string ReportSaveDialogTitle = "Report saving";
    public const string ReportSaveChangesPrompt = "Save report changes?";
    public const string NewReportCloseHandlerAttached = "FastReport Designer close handler attached for new report {ReportName}";
    public const string ReportCloseHandlerAttached = "FastReport Designer close handler attached for report {ReportName}";
    public const string NewReportSaveAsDisabled = "Save As command is disabled in FastReport Designer for new report {ReportName}";
    public const string ReportSaveAsDisabled = "Save As command is disabled in FastReport Designer for report {ReportName}";
    public const string ReportDesignerClosedAfterSave = "Report {ReportName}.frx closed in FastReport Designer after database save";
    public const string ReportDesignerClosedWithoutSave = "Report {ReportName}.frx closed in FastReport Designer without database save";

    public const string ConfirmationTitle = "Confirmation";
    public const string DeleteReportPrompt = "Are you sure you want to delete this report?";
    public const string ReportDeleted = "Report {ReportName}.frx was deleted from the database";
    public const string ReportDeleteFailed = "Report {ReportName}.frx was not deleted from the database";

    public const string ImportCancelledFileNotSelected = "Import cancelled: no file selected.";
    public const string ExportCancelledFileNotSelected = "Export cancelled: no file selected.";
    public const string ExportFrxCancelledFileNotSelected = "FRX export cancelled: no file selected.";
    public const string FileNotFound = "File not found: {FilePath}";
    public const string ImportedFileSaved = "Imported file saved: {FilePath}";
    public const string XmlImportFailed = "Error importing from XML";
    public const string XmlExportSucceeded = "XML export succeeded: {FilePath}";
    public const string XmlExportFailed = "Error exporting to XML";
    public const string FrxExportSucceeded = "FRX export succeeded: {FilePath}";
    public const string FrxExportFailed = "Error exporting to FRX";
    public const string FrxImportFailed = "Error importing from FRX";

    public const string PendingReportsUploadUnavailable = "The attempt to upload pending reports failed because the database is unavailable.";
    public const string PendingReportsUploadStarted = "Pending reports upload started";
    public const string ImportedFromFile = "Imported from file";
    public const string PendingReportUploaded = "File {FileName} was uploaded to the database.";
    public const string PendingReportProcessingFailed = "Error processing file {FileName}.";
    public const string PendingReportsProcessed = "All .frx files from directory {Directory} were processed.";
    public const string PendingReportsProcessingFailed = "Error finding and processing .frx files.";

    public const string SaveReportAsXmlTitle = "Save report as XML";
    public const string ExportReportToFrxTitle = "Export report to FRX";
    public const string SelectFileTitle = "Select file";
    public const string XmlFilesFilter = "XML files";
    public const string FrxFilesFilter = "FRX files";

    public const string UnnamedReport = "Unnamed report";
    public const string GetReportByIdFailed = "Error getting report by ID.";
    public const string ReportsFetchFailed = "Error while fetching reports from the database.";
    public const string ReportInsertFailed = "Error while inserting the report into the database.";
    public const string ReportUpdateFailed = "Error while updating the report in the database.";
    public const string ReportDeleteOperationFailed = "Error while deleting the report from the database.";
    public const string DatabasePortMustBeValidInteger = "Database:Port must be a valid integer.";
}
