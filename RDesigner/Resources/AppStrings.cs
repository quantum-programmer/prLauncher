namespace RDesigner.Resources;

public static class AppStrings
{
    public static string ApplicationStarted = "Запуск приложения";
    public static string ApplicationFailed = "Приложение завершилось с ошибкой";
    public static string StartupDatabaseSettingsTitle = "Ошибка настройки базы данных";
    public static string InvalidDatabaseSettings = "Некорректные настройки подключения к базе данных.";
    public static string InvalidDatabaseSettingsLog = "Некорректные настройки подключения к базе данных: {Errors}";
    public static string DatabaseConnectionFailedFormat = "Подключение к базе данных не выполнено. {0}";
    public static string DatabaseUnavailable = "База данных недоступна";
    public static string DatabaseUnavailableWithPeriod = "База данных недоступна.";
    public static string DatabasePortMustBeNumeric = "Port должен быть числом.";
    public static string DatabaseOptionMustNotBeEmptyFormat = "{0} не должен быть пустым.";

    public static string ReportsNotificationSubscriptionStarted = "Подписка на уведомления PostgreSQL report_change активна.";
    public static string ReportsNotificationSubscriptionRetry = "Подписка на уведомления PostgreSQL report_change прервана. Повторная попытка подключения через 5 секунд.";
    public static string LogsDirectoryOpenFailed = "Не удалось открыть папку логов: {LogsDirectory}";
    public static string LanguageChanged = "Язык приложения изменен: {Language}";

    public static string AboutTitle = "О программе";
    public static string AboutVersion = "Версия:";
    public static string AboutBuild = "Сборка:";
    public static string AboutFullVersion = "Полная версия:";
    public static string AboutGitHash = "Git hash:";
    public static string AboutAssemblyVersion = "Версия сборки:";
    public static string AboutFileVersion = "Версия файла:";

    public static string CreateReportTitle = "Создать отчет";
    public static string CreateReportDialogTitle = "Создание отчета";
    public static string EditReportTitle = "Изменить отчет";
    public static string ReportNamePrompt = "Введите имя отчета:";
    public static string ReportDescriptionPrompt = "Введите описание отчета:";
    public static string Save = "Сохранить";
    public static string Cancel = "Отменить";
    public static string Untitled = "Без названия";

    public static string PrintableReports = "Печатные отчеты";
    public static string TableReports = "Табличные отчеты";
    public static string View = "Просмотр";
    public static string Settings = "Настройки";
    public static string Create = "Создать";
    public static string Edit = "Изменить";
    public static string Editor = "Редактор";
    public static string Delete = "Удалить";
    public static string ReportId = "Отчет ID:";
    public static string Revision = "Редакция:";
    public static string SoftwareAssociationCode = "Код ассоциации ПО";
    public static string Description = "Описание";
    public static string ReportsManagement = "Управление отчетами";
    public static string CreateDuplicate = "Создать дубликат";
    public static string Rename = "Переименовать";
    public static string ImportFromXml = "Импорт из XML";
    public static string ExportToXmlMenu = "Эспорт в XML";
    public static string ImportFromFrx = "Импорт из .frx";
    public static string ExportToFrxMenu = "Экспорт в .frx";
    public static string UploadReportsToDatabase = "Выгрузить отчеты в базу";

    public static string CommandLineArguments = "Аргументы командной строки: {Args}";
    public static string MainWindowNotFound = "Главное окно не найдено";
    public static string ReportIdNotFound = "Отчет с таким ID не найден";
    public static string ReportBodyIsEmpty = "Тело отчета пустое";
    public static string ReportLoadFailed = "Ошибка при загрузке отчета: {ErrorMessage}";
    public static string ReportsLoaded = "Отчеты успешно загружены из базы данных";
    public static string ReportsLoadFailed = "Ошибка при загрузке отчетов из базы данных";
    public static string ReportOpened = "Отчёт: {ReportName} успешно открыт для просмотра";
    public static string ReportClosed = "Отчёт: {ReportName} успешно закрыт";
    public static string ErrorTitle = "Ошибка";
    public static string ReportFileNotFoundLog = "Ошибка: файл отчета не найден.";
    public static string ReportFileNotFound = "Файл отчета не найден.";
    public static string ReportFileAccessDeniedLog = "Ошибка: нет доступа к файлу отчета.";
    public static string ReportFileAccessDenied = "Нет доступа к файлу отчета.";
    public static string ReportOpenIoError = "Ошибка ввода-вывода при открытии отчета.";
    public static string ReportOpenUnknownError = "Неизвестная ошибка при открытии отчета.";

    public static string ReportCopySavedForUpload = "Копия файла {ReportName}.frx сохранена в каталог not_uploaded для последующей записи в базу данных";
    public static string ReportSavedToDatabase = "Файл {ReportName}.frx сохранен в базу данных";
    public static string ReportUpdatedInDatabase = "Файл {ReportName}.frx изменен в базе данных";
    public static string ReportCopyRemovedFromPendingDirectory = "Копия файла {ReportName}.frx удалена из каталога not_uploaded";
    public static string ReportCopySavedToDirectory = "Копия файла {ReportName}.frx сохранена в каталог";
    public static string ReportCopyRemovedFromDirectory = "Копия файла {ReportName}.frx удалена из каталога";
    public static string ReportSaveFailedPendingCopySaved = "Отчет не был сохранен в базу данных. Копия файла сохранена в каталог not_uploaded.";
    public static string ReportSaveFailedPendingCopyLeft = "Отчет не был сохранен в базу данных. Копия файла осталась в каталоге not_uploaded.";
    public static string ReportSaveFailedApplicationCopySaved = "Отчет не был сохранен в базу данных. Копия файла сохранена в каталог приложения.";
    public static string ReportUpdateFailedPendingCopySaved = "Отчет не был изменен в базе данных. Копия файла сохранена в каталог not_uploaded.";
    public static string ReportUpdateFailedPendingCopyLeft = "Отчет не был изменен в базе данных. Копия файла осталась в каталоге not_uploaded.";
    public static string ReportUpdateFailedApplicationCopySaved = "Отчет не был изменен в базе данных. Копия файла сохранена в каталог приложения.";
    public static string ReportSavedLocallyTitle = "Отчет сохранен локально";
    public static string UnavailableDatabaseReportSavedLocallyFormat = "База данных недоступна. Отчет {0}.frx сохранен локально в папку not_uploaded.";
    public static string UnavailableDatabaseUpdatedReportSavedLocallyFormat = "База данных недоступна. Измененный отчет {0}.frx сохранен локально в папку not_uploaded.";
    public static string ReportCopyDeleteIoFailed = "Ошибка при удалении файла {ReportName}.frx. Возможно, файл занят другим процессом или отсутствуют права доступа.";
    public static string ReportCopyDeleteAccessDenied = "Ошибка при удалении файла {ReportName}.frx. Недостаточно прав доступа.";
    public static string DatabaseOperationFailed = "Ошибка при работе с базой данных (PostgreSQL)";
    public static string ReportChangeListenerStartFailed = "Ошибка при запуске прослушивания изменений отчетов.";
    public static string ReportSaveUnknownError = "Неизвестная ошибка при сохранении отчета в базу данных";
    public static string ReportUpdateUnknownError = "Неизвестная ошибка при изменении отчета в базе данных";
    public static string ReportFileCreateFailed = "Ошибка при создании файла отчета: {ErrorMessage}";
    public static string ReportSaveDialogTitle = "Сохранение отчета";
    public static string ReportSaveChangesPrompt = "Сохранить изменения в отчете?";
    public static string NewReportCloseHandlerAttached = "Подключен обработчик закрытия окна FastReport Designer для нового отчета {ReportName}";
    public static string ReportCloseHandlerAttached = "Подключен обработчик закрытия окна FastReport Designer для отчета {ReportName}";
    public static string NewReportSaveAsDisabled = "Команда Save As отключена в FastReport Designer для нового отчета {ReportName}";
    public static string ReportSaveAsDisabled = "Команда Save As отключена в FastReport Designer для отчета {ReportName}";
    public static string ReportDesignerClosedAfterSave = "Отчет {ReportName}.frx закрыт в FastReport Designer после сохранения в базу данных";
    public static string ReportDesignerClosedWithoutSave = "Отчет {ReportName}.frx закрыт в FastReport Designer без сохранения в базу данных";

    public static string ConfirmationTitle = "Подтверждение";
    public static string DeleteReportPrompt = "Вы уверены, что хотите удалить этот отчет?";
    public static string ReportDeleted = "Отчет {ReportName}.frx успешно удален из базы";
    public static string ReportDeleteFailed = "Отчет {ReportName}.frx не был удален из базы";

    public static string ImportCancelledFileNotSelected = "Импорт отменен: файл не выбран.";
    public static string ExportCancelledFileNotSelected = "Экспорт отменен: файл не выбран.";
    public static string ExportFrxCancelledFileNotSelected = "Экспорт FRX отменен: файл не выбран.";
    public static string FileNotFound = "Файл не найден: {FilePath}";
    public static string ImportedFileSaved = "Импортированный файл сохранен: {FilePath}";
    public static string XmlImportFailed = "Ошибка при импорте из XML";
    public static string XmlExportSucceeded = "Экспорт в XML выполнен успешно: {FilePath}";
    public static string XmlExportFailed = "Ошибка при экспорте в XML";
    public static string FrxExportSucceeded = "Экспорт в FRX выполнен успешно: {FilePath}";
    public static string FrxExportFailed = "Ошибка при экспорте в FRX";
    public static string FrxImportFailed = "Ошибка при импорте из FRX";

    public static string PendingReportsUploadUnavailable = "Попытка запустить выгрузку не выгруженных отчетов закончилась неудачей. Причина: база данных не доступна.";
    public static string PendingReportsUploadStarted = "Запущена выгрузка не выгруженных отчетов";
    public static string ImportedFromFile = "Импортированный из файла";
    public static string PendingReportUploaded = "Файл {FileName} успешно выгружен в базу данных.";
    public static string PendingReportProcessingFailed = "Ошибка при обработке файла {FileName}.";
    public static string PendingReportsProcessed = "Все файлы .frx из каталога {Directory} успешно обработаны.";
    public static string PendingReportsProcessingFailed = "Ошибка при поиске и обработке файлов .frx.";

    public static string SaveReportAsXmlTitle = "Сохранить отчет как XML";
    public static string ExportReportToFrxTitle = "Экспорт отчета в FRX";
    public static string SelectFileTitle = "Выберите файл";
    public static string XmlFilesFilter = "Файлы XML";
    public static string FrxFilesFilter = "Файлы FRX";

    public static string UnnamedReport = "Безымянный отчет";
    public static string GetReportByIdFailed = "Ошибка при получении отчета по ID.";
    public static string ReportsFetchFailed = "Ошибка при получении отчетов из базы данных.";
    public static string ReportInsertFailed = "Ошибка при добавлении отчета в базу данных.";
    public static string ReportUpdateFailed = "Ошибка при обновлении отчета в базе данных.";
    public static string ReportDeleteOperationFailed = "Ошибка при удалении отчета из базы данных.";
    public static string DatabasePortMustBeValidInteger = "Database:Port должен быть целым числом.";
}

