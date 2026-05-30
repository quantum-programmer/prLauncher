using RDesigner.Services;
using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RDesigner.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastReport;
using System.IO;
using FastReport.Export.Dbf;
using Npgsql;
using RDesigner.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Serilog;
using Newtonsoft.Json;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using Avalonia.Threading;
using FastReport.Data;
using FastReport.Design;
using FastReport.Design.StandardDesigner;
using FastReport.Utils;
using RDesigner.Resources;

namespace RDesigner.ViewModels;

    public partial class MainViewModel:ViewModelBase
    {
        private readonly IDBService _dbService;
    
        [ObservableProperty]
        private ObservableCollection<ARMReport> reports = new();
        
        private ObservableCollection<ARMReport> FRXReports = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ViewReportCommand))]
        [NotifyCanExecuteChangedFor(nameof(UpdateReportCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteReportCommand))]
        [NotifyCanExecuteChangedFor(nameof(DuplicateReportCommand))]
        [NotifyCanExecuteChangedFor(nameof(RenameReportCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportToXmlCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportFRXCommand))]
        private ARMReport? _selectedReport;

        public MainViewModel(IDBService myService)
        {
            this._dbService = myService;
            if (!ProcessCommandLineArgs())
            {
                LoadReportsAsync();
                _ = StartListeningForReportsChangeAsync();
            }
        }

        private bool HasSelectedReport()
        {
            return SelectedReport != null;
        }


        private bool ProcessCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();
            Log.Information(AppStrings.CommandLineArguments, string.Join(" ", args));

            // Упрощенный парсинг без Dictionary
            string cmd = null;
            string id = null;

            for (int i = 1; i < args.Length; i++) // Пропускаем первый аргумент (путь к exe)
            {
                if (args[i].StartsWith("cmd="))
                    cmd = args[i].Substring(4);
                else if (args[i].StartsWith("id="))
                    id = args[i].Substring(3);
            }

            if (cmd == "show" && id != null && int.TryParse(id, out int reportId))
            {
                /* if (_lifetime.MainWindow != null)
                     _lifetime.MainWindow.Hide();*/

                ViewReportDirectly(reportId);
                return true;
            }
            else
                return false;
        }

        // Метод для проверки окон FastReport
        private bool HasFastReportWindows()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Проверяем все окна в текущем ApplicationLifetime
                return desktop.Windows.Any(w =>
                    w.Title.Contains("Preview", StringComparison.OrdinalIgnoreCase) ||
                    w.GetType().Name.Contains("Preview"));
            }
            return false;
        }

        private async void ViewReportDirectly(int id)
        {

            IClassicDesktopStyleApplicationLifetime desktop = null;
            Window mainWindow = null;

            // Ждём появления главного окна (с таймаутом)
            for (int i = 0; i < 10 && mainWindow == null; i++)
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                {
                    desktop = d;
                    mainWindow = d.MainWindow;
                }

                if (mainWindow == null)
                    await Task.Delay(100);
            }

            if (mainWindow == null)
            {
                Log.Information(AppStrings.MainWindowNotFound);
                return;
            }

            // Устанавливаем прозрачность
            var originalOpacity = mainWindow.Opacity;
            mainWindow.Opacity = 0.01;

            
            var report = await _dbService.GetReportById(id);

            if (report is null)
            {
                Log.Information(AppStrings.ReportIdNotFound);
                mainWindow.Close();
            }
                

            if (report.reportData == null || report.reportData.Length == 0)
            {
                Log.Information(AppStrings.ReportBodyIsEmpty);
            }
            try
            {
                using var stream = new MemoryStream(report.reportData);
                var loadedReport = new FastReport.Report();
                loadedReport.Load(stream);
//                loadedReport.Show();

            // Создаем и запускаем таймер проверки
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };

                timer.Tick += (s, e) =>
                {
                    // Проверяем наличие дочерних окон FastReport
                    if (!HasFastReportWindows())
                    {
                        timer.Stop();
                        mainWindow.Close();
                    }
                };

                loadedReport.Show();
                timer.Start();            
            }

            catch (Exception ex)
            {
                Log.Information(AppStrings.ReportLoadFailed, ex.Message);
            }
        // Или если нужно экспортировать в PDF/другой формат
        // loadedReport.Export(...);
        }

        private async Task LoadReportsAsync()
        {
            try
            {
                var reportsFromDb = await _dbService.GetAllReportsAsync();
                Reports = new ObservableCollection<ARMReport>(reportsFromDb);
                Log.Information(AppStrings.ReportsLoaded);
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.ReportsLoadFailed); // Использование Log
            }
        }

        // Вспомогательный метод для отображения сообщения об ошибке
        private async Task ShowErrorMessageAsync(string title, string message)
        {
            var messageBox = MessageBoxManager.GetMessageBoxStandard(
                title,
                message,
                ButtonEnum.Ok,
                Icon.Error
            );

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                await messageBox.ShowWindowDialogAsync(desktop.MainWindow);
            }
        }

        // Команда для кнопки "Просмотр"
        [RelayCommand(CanExecute = nameof(HasSelectedReport))]
        private async void ViewReport()
        {
            if (SelectedReport != null)
            {
                try 
                {
                    // Загрузка отчета из бинарных данных
                    using (MemoryStream stream = new MemoryStream(SelectedReport.reportData))
                    {
                        Report report = new Report();

                        PostgresDataConnection conn = new PostgresDataConnection();
                        conn.ConnectionString = _dbService.GetConnectionString();
                        conn.Name = "ConnectionPG";
                        conn.CreateAllTables();
                        report.Dictionary.Connections.Add(conn);

                        report.Load(stream);
                        // Отображение отчета
                        Log.Information(AppStrings.ReportOpened, SelectedReport.Name);
                        report.Show();
                        Log.Information(AppStrings.ReportClosed, SelectedReport.Name);
                    }
                }
                catch (FileNotFoundException ex)
                {
                    Log.Error(ex, AppStrings.ReportFileNotFoundLog);
                    await ShowErrorMessageAsync(AppStrings.ErrorTitle, AppStrings.ReportFileNotFound);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Error(ex, AppStrings.ReportFileAccessDeniedLog);
                    await ShowErrorMessageAsync(AppStrings.ErrorTitle, AppStrings.ReportFileAccessDenied);
                }
                catch (IOException ex)
                {
                    Log.Error(ex, AppStrings.ReportOpenIoError);
                    await ShowErrorMessageAsync(AppStrings.ErrorTitle, AppStrings.ReportOpenIoError);
                }
                catch (Exception ex) // Общий случай для всех остальных исключений
                {
                    Log.Error(ex, AppStrings.ReportOpenUnknownError);
                    await ShowErrorMessageAsync(AppStrings.ErrorTitle, AppStrings.ReportOpenUnknownError);
                }
            }
        }

        // Команда для кнопки "Создать"
        [RelayCommand]    
        private async void CreateReport()
        {
            var Owner = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var createReportViewModel = new CreateReportViewModel(_dbService);
            var createReportWindow = new CreateReportView { DataContext = createReportViewModel };
            //createReportWindow.ShowDialog(Owner);
            LocalizationManager.BindWindowTitle(createReportWindow, nameof(AppStrings.CreateReportTitle));
            // Показываем окно и ждем результата
            var result = await createReportWindow.ShowDialog<bool>(Owner);
            // Если нажата кнопка "Сохранить"
            if (result)
            {
                // Получаем введенное имя отчета
                string reportName = string.IsNullOrEmpty(createReportViewModel.ReportName)
                    ? AppStrings.Untitled
                    : createReportViewModel.ReportName;
                // Получаем введенное описание отчета
                string reportDescription = string.IsNullOrEmpty(createReportViewModel.ReportDescription)
                    ? AppStrings.Untitled
                    : createReportViewModel.ReportDescription;
                string copyFilePath = Path.Combine(GetNotUploadedReportsDirectory(), $"{reportName}.frx");
                bool allowDesignerClose = false;
                bool closePromptIsOpen = false;
                Designer? fastReportDesigner = null;
                System.Windows.Forms.Form? designerForm = null;
                System.Windows.Forms.FormClosingEventHandler? designerFormClosingHandler = null;
                EventHandler? designerWindowAvailableHandler = null;
                System.Windows.Forms.Timer? designerFormAttachTimer = null;
                EventHandler? saveCommandStateHandler = null;
                System.Windows.Forms.Timer? saveCommandStateTimer = null;

                ARMReport newReport = new()
                {
                    ParentID = 0,
                    UniqueID = 1,
                    Name = reportName,
                    isFolder = false,
                    isDelete = false,
                    Description = reportDescription
                };

                try
                {                    
                    Report report = new Report();

                    PostgresDataConnection conn = new PostgresDataConnection();
                    conn.ConnectionString = _dbService.GetConnectionString();
                    conn.Name = "ConnectionPG";
                    conn.CreateAllTables();
                    report.Dictionary.Connections.Add(conn);

                    void SaveReportCopy(Report reportToSave)
                    {
                        using MemoryStream stream = new();
                        reportToSave.Save(stream);
                        newReport.reportData = stream.ToArray();
                        Directory.CreateDirectory(GetNotUploadedReportsDirectory());
                        File.WriteAllBytes(copyFilePath, newReport.reportData);

                        var operation = newReport.ARMReportID > 0
                            ? PendingReportOperation.Update
                            : PendingReportOperation.Insert;
                        int? reportId = newReport.ARMReportID > 0 ? newReport.ARMReportID : null;
                        SavePendingReportMetadata(copyFilePath, operation, reportId, reportName, reportDescription);
                    }

                    async Task<bool> SaveDesignedReportToDatabaseAsync(Report reportToSave)
                    {
                        SaveReportCopy(reportToSave);
                        Log.Information(AppStrings.ReportCopySavedForUpload, reportName);

                        try
                        {
                            bool saved = newReport.ARMReportID > 0
                                ? await _dbService.UpdateReportAsync(newReport)
                                : await _dbService.InsertReportAsync(newReport);

                            if (saved)
                            {
                                Log.Information(AppStrings.ReportSavedToDatabase, reportName);
                                DeletePendingReportFiles(copyFilePath);
                                Log.Information(AppStrings.ReportCopyRemovedFromPendingDirectory, reportName);
                                return true;
                            }

                            Log.Error(AppStrings.ReportSaveFailedPendingCopySaved);
                            await ShowErrorMessageAsync(
                                AppStrings.ReportSavedLocallyTitle,
                                string.Format(AppStrings.UnavailableDatabaseReportSavedLocallyFormat, reportName));
                            return false;
                        }
                        catch (IOException ioEx)
                        {
                            Log.Error(ioEx, AppStrings.ReportCopyDeleteIoFailed, reportName);
                            return false;
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                            Log.Error(uaEx, AppStrings.ReportCopyDeleteAccessDenied, reportName);
                            return false;
                        }
                        catch (NpgsqlException ex)
                        {
                            Log.Error(ex, AppStrings.DatabaseOperationFailed);
                            await ShowErrorMessageAsync(
                                AppStrings.ReportSavedLocallyTitle,
                                string.Format(AppStrings.UnavailableDatabaseReportSavedLocallyFormat, reportName));
                            return false;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, AppStrings.ReportSaveUnknownError);
                            return false;
                        }
                    }

                    async Task<ButtonResult> ShowCloseReportDialogAsync()
                    {
                        var messageBox = MessageBoxManager.GetMessageBoxStandard(
                            AppStrings.ReportSaveDialogTitle,
                            AppStrings.ReportSaveChangesPrompt,
                            ButtonEnum.YesNoCancel,
                            Icon.Question);

                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                            return await messageBox.ShowWindowDialogAsync(desktop.MainWindow);

                        return ButtonResult.Cancel;
                    }

                    async void DesignerFormClosingHandler(object? sender, System.Windows.Forms.FormClosingEventArgs e)
                    {
                        if (fastReportDesigner == null
                            || allowDesignerClose
                            || !fastReportDesigner.Modified)
                        {
                            return;
                        }

                        e.Cancel = true;
                        if (closePromptIsOpen)
                            return;

                        closePromptIsOpen = true;
                        try
                        {
                            var closeResult = await ShowCloseReportDialogAsync();
                            if (closeResult == ButtonResult.Cancel)
                                return;

                            if (closeResult == ButtonResult.Yes)
                                await SaveDesignedReportToDatabaseAsync(report);

                            fastReportDesigner.Modified = false;
                            allowDesignerClose = true;
                            designerForm?.Close();
                        }
                        finally
                        {
                            closePromptIsOpen = false;
                        }
                    }

                    void AttachDesignerFormClosingHandler()
                    {
                        if (fastReportDesigner == null || designerFormClosingHandler == null)
                            return;

                        var form = fastReportDesigner.FindForm();
                        if (form == null || ReferenceEquals(designerForm, form))
                            return;

                        if (designerForm != null)
                            designerForm.FormClosing -= designerFormClosingHandler;

                        designerForm = form;
                        designerForm.FormClosing += designerFormClosingHandler;
                        designerFormAttachTimer?.Stop();
                        Log.Information(AppStrings.NewReportCloseHandlerAttached, reportName);
                    }

                    async void SaveCommandHandler(object? sender, EventArgs e)
                    {
                        if (fastReportDesigner == null)
                            return;

                        if (await SaveDesignedReportToDatabaseAsync(report))
                            MarkDesignerSaved(fastReportDesigner);
                    }

                    void SaveAsCommandHandler(object? sender, EventArgs e)
                    {
                        Log.Information(AppStrings.NewReportSaveAsDisabled, reportName);
                    }

                    static void MarkDesignerSaved(Designer designer)
                    {
                        designer.Modified = false;
                        designer.Restrictions.DontSaveReport = true;
                        designer.UpdatePlugins(null);
                        RemoveDesignerModifiedMarker(designer);
                    }

                    static void RemoveDesignerModifiedMarker(Designer designer)
                    {
                        var form = designer.FindForm();
                        if (form != null && form.Text.Contains('*'))
                            form.Text = form.Text.Replace("*", string.Empty);
                    }

                    static void HideSaveAsCommand(Designer designer)
                    {
                        if (designer.Plugins.FindType("DesignerMenu") is DesignerMenu menu)
                        {
                            menu.miFileSaveAs.Visible = false;
                            menu.miFileSaveAs.ShortcutKeys = System.Windows.Forms.Keys.None;
                        }
                    }

                    static void UpdateSaveCommandState(Designer designer)
                    {
                        bool disableSave = !designer.Modified;
                        if (designer.Restrictions.DontSaveReport == disableSave)
                            return;

                        designer.Restrictions.DontSaveReport = disableSave;
                        designer.UpdatePlugins(null);
                        HideSaveAsCommand(designer);
                    }

                    void DesignerLoadedHandler(object? sender, EventArgs e)
                    {
                        if (sender is not Designer designer)
                            return;

                        fastReportDesigner = designer;
                        designer.AskSave = false;
                        HideSaveAsCommand(designer);
                        UpdateSaveCommandState(designer);
                        designer.cmdSave.CustomAction += SaveCommandHandler;
                        designer.cmdSaveAs.CustomAction += SaveAsCommandHandler;
                        saveCommandStateHandler = (_, _) => UpdateSaveCommandState(designer);
                        saveCommandStateTimer = new System.Windows.Forms.Timer { Interval = 100 };
                        saveCommandStateTimer.Tick += saveCommandStateHandler;
                        saveCommandStateTimer.Start();
                        designerFormClosingHandler = DesignerFormClosingHandler;
                        designerWindowAvailableHandler = (_, _) => AttachDesignerFormClosingHandler();
                        designer.VisibleChanged += designerWindowAvailableHandler;
                        designerFormAttachTimer = new System.Windows.Forms.Timer { Interval = 100 };
                        designerFormAttachTimer.Tick += designerWindowAvailableHandler;
                        designerFormAttachTimer.Start();
                        AttachDesignerFormClosingHandler();
                    }

                    Config.DesignerSettings.DesignerLoaded += DesignerLoadedHandler;

                    try
                    {
                        report.Design();
                    }
                    finally
                    {
                        Config.DesignerSettings.DesignerLoaded -= DesignerLoadedHandler;
                        if (designerForm != null && designerFormClosingHandler != null)
                            designerForm.FormClosing -= designerFormClosingHandler;
                        if (fastReportDesigner != null)
                        {
                            if (designerWindowAvailableHandler != null)
                            {
                                fastReportDesigner.VisibleChanged -= designerWindowAvailableHandler;
                                if (designerFormAttachTimer != null)
                                    designerFormAttachTimer.Tick -= designerWindowAvailableHandler;
                            }

                            designerFormAttachTimer?.Stop();
                            designerFormAttachTimer?.Dispose();
                            if (saveCommandStateTimer != null && saveCommandStateHandler != null)
                                saveCommandStateTimer.Tick -= saveCommandStateHandler;
                            saveCommandStateTimer?.Stop();
                            saveCommandStateTimer?.Dispose();
                            fastReportDesigner.cmdSave.CustomAction -= SaveCommandHandler;
                            fastReportDesigner.cmdSaveAs.CustomAction -= SaveAsCommandHandler;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, AppStrings.ReportFileCreateFailed, ex.Message);
                }
            }                
        }


        private async Task<bool> CreateReportAsync(ARMReport report, string? sourceFilePath = null)
        {
            string copyFilePath = sourceFilePath ?? Path.Combine(GetNotUploadedReportsDirectory(), $"{report.Name}.frx");
            try
            {
                if (await _dbService.InsertReportAsync(report))
                {
                    Log.Information(AppStrings.ReportSavedToDatabase, report.Name);
                    if (File.Exists(copyFilePath))
                    {
                        DeletePendingReportFiles(copyFilePath);
                        Log.Information(AppStrings.ReportCopyRemovedFromPendingDirectory, report.Name);
                    }

                    return true;
                }
                else
                {
                    Log.Error(AppStrings.ReportSaveFailedPendingCopyLeft);
                    return false;
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, AppStrings.ReportCopyDeleteIoFailed, report.Name);
                return false;
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, AppStrings.ReportCopyDeleteAccessDenied, report.Name);
                return false;
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, AppStrings.DatabaseOperationFailed);
                return false;
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, AppStrings.ReportSaveUnknownError);
                return false;
            }
            
        }

        // Команда для кнопки "Изменить"
        [RelayCommand(CanExecute = nameof(HasSelectedReport))]
        private void UpdateReport()
        {
            if (SelectedReport == null)
                return;

             // Получаем введенное имя отчета
            string reportName = SelectedReport.Name;
            // Получаем введенное описание отчета
            string reportDescription = SelectedReport.Description;
            int reportId = SelectedReport.ARMReportID;
            byte[] reportBytes = SelectedReport.reportData;
            string copyFilePath = Path.Combine(GetNotUploadedReportsDirectory(), $"{reportName}.frx");
            bool savedToDatabaseOnClose = false;
            bool allowDesignerClose = false;
            bool closePromptIsOpen = false;
            Designer? fastReportDesigner = null;
            System.Windows.Forms.Form? designerForm = null;
            System.Windows.Forms.FormClosingEventHandler? designerFormClosingHandler = null;
            EventHandler? designerWindowAvailableHandler = null;
            System.Windows.Forms.Timer? designerFormAttachTimer = null;
            EventHandler? saveCommandStateHandler = null;
            System.Windows.Forms.Timer? saveCommandStateTimer = null;

            try
            {
                using (MemoryStream stream = new MemoryStream(reportBytes))
                {
                    Report report = new Report();
                    report.Load(stream);
                        
                    PostgresDataConnection conn = new PostgresDataConnection();
                    conn.ConnectionString = _dbService.GetConnectionString();
                    conn.Name = "ConnectionPG";
                    conn.CreateAllTables();
                    report.Dictionary.Connections.Add(conn);

                    DataConnectionBase oldConn = report.Dictionary.Connections[0];
                    DataConnectionBase newConn = report.Dictionary.Connections[1];

                    newConn.Tables.Clear();

                    foreach (TableDataSource tds in oldConn.Tables.OfType<TableDataSource>().ToList())
                    {
                        TableDataSource newTds = new TableDataSource();
                        newTds.SelectCommand = tds.SelectCommand;
                        newTds.Enabled = tds.Enabled;
                        newTds.TableName = tds.TableName;
                        newTds.Alias = tds.Alias;
                        newTds.Name = GetUniqueTableName(newConn, tds.Name ?? tds.TableName ?? "Table");
                        newTds.Connection = newConn;
                        foreach (var paramObj in tds.Parameters)
                        {
                            if (paramObj is CommandParameter param && param != null)
                            {
                                CommandParameter newParam = new CommandParameter
                                {
                                    Name = param.Name,
                                    DataType = param.DataType,
                                    Value = param.Value,
                                    Expression = param.Expression,
                                    DefaultValue = param.DefaultValue ?? "",
                                };
                                newTds.Parameters.Add(newParam);
                            }
                        }

                        newConn.Tables.Add(newTds);
                    }

                    //newConn.CreateAllTables();
                    if (oldConn != null)
                    {
                        // Очищаем таблицы старого подключения
                        var tablesToRemove = oldConn.Tables.OfType<TableDataSource>().ToList();
                        foreach (var tds in tablesToRemove)
                        {
                            oldConn.Tables.Remove(tds);
                            tds.Connection = null;
                        }

                        report.Dictionary.Connections.Remove(oldConn);
                    }
                    newConn.CreateAllTables();
                    //report.RegisterData();

                    void SaveReportCopy(Report reportToSave)
                    {
                        using MemoryStream newStream = new();
                        reportToSave.Save(newStream);
                        reportBytes = newStream.ToArray();
                        Directory.CreateDirectory(GetNotUploadedReportsDirectory());
                        File.WriteAllBytes(copyFilePath, reportBytes);
                        SavePendingReportMetadata(copyFilePath, PendingReportOperation.Update, reportId, reportName, reportDescription);
                    }

                    ARMReport BuildUpdatedReport()
                    {
                        return new ARMReport
                        {
                            ARMReportID = reportId,
                            ParentID = 0,
                            UniqueID = 1,
                            Name = reportName,
                            isFolder = false,
                            isDelete = false,
                            Description = reportDescription,
                            reportData = reportBytes
                        };
                    }

                    async Task<bool> SaveDesignedReportToDatabaseAsync(Report reportToSave)
                    {
                        SaveReportCopy(reportToSave);
                        Log.Information(AppStrings.ReportCopySavedForUpload, reportName);

                        try
                        {
                            if (await _dbService.UpdateReportAsync(BuildUpdatedReport()))
                            {
                                Log.Information(AppStrings.ReportUpdatedInDatabase, reportName);
                                DeletePendingReportFiles(copyFilePath);
                                Log.Information(AppStrings.ReportCopyRemovedFromPendingDirectory, reportName);
                                return true;
                            }
                            else
                            {
                                Log.Error(AppStrings.ReportUpdateFailedPendingCopySaved);
                                await ShowErrorMessageAsync(
                                    AppStrings.ReportSavedLocallyTitle,
                                    string.Format(AppStrings.UnavailableDatabaseUpdatedReportSavedLocallyFormat, reportName));
                                return false;
                            }
                        }
                        catch (IOException ioEx)
                        {
                            Log.Error(ioEx, AppStrings.ReportCopyDeleteIoFailed, reportName);
                            return false;
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                            Log.Error(uaEx, AppStrings.ReportCopyDeleteAccessDenied, reportName);
                            return false;
                        }
                        catch (NpgsqlException ex)
                        {
                            Log.Error(ex, AppStrings.DatabaseOperationFailed);
                            await ShowErrorMessageAsync(
                                AppStrings.ReportSavedLocallyTitle,
                                string.Format(AppStrings.UnavailableDatabaseUpdatedReportSavedLocallyFormat, reportName));
                            return false;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, AppStrings.ReportSaveUnknownError);
                            return false;
                        }
                    }

                    async Task<ButtonResult> ShowCloseReportDialogAsync()
                    {
                        var messageBox = MessageBoxManager.GetMessageBoxStandard(
                            AppStrings.ReportSaveDialogTitle,
                            AppStrings.ReportSaveChangesPrompt,
                            ButtonEnum.YesNoCancel,
                            Icon.Question);

                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                            return await messageBox.ShowWindowDialogAsync(desktop.MainWindow);

                        return ButtonResult.Cancel;
                    }

                    async void DesignerFormClosingHandler(object? sender, System.Windows.Forms.FormClosingEventArgs e)
                    {
                        if (fastReportDesigner == null
                            || allowDesignerClose
                            || !fastReportDesigner.Modified)
                        {
                            return;
                        }

                        e.Cancel = true;
                        if (closePromptIsOpen)
                            return;

                        closePromptIsOpen = true;
                        try
                        {
                            var result = await ShowCloseReportDialogAsync();
                            if (result == ButtonResult.Cancel)
                                return;

                            if (result == ButtonResult.Yes)
                            {
                                await SaveDesignedReportToDatabaseAsync(report);
                                savedToDatabaseOnClose = true;
                            }

                            fastReportDesigner.Modified = false;
                            allowDesignerClose = true;
                            designerForm?.Close();
                        }
                        finally
                        {
                            closePromptIsOpen = false;
                        }
                    }

                    void AttachDesignerFormClosingHandler()
                    {
                        if (fastReportDesigner == null || designerFormClosingHandler == null)
                            return;

                        var form = fastReportDesigner.FindForm();
                        if (form == null || ReferenceEquals(designerForm, form))
                            return;

                        if (designerForm != null)
                            designerForm.FormClosing -= designerFormClosingHandler;

                        designerForm = form;
                        designerForm.FormClosing += designerFormClosingHandler;
                        designerFormAttachTimer?.Stop();
                        Log.Information(AppStrings.ReportCloseHandlerAttached, reportName);
                    }

                    async void SaveCommandHandler(object? sender, EventArgs e)
                    {
                        if (fastReportDesigner == null)
                            return;

                        if (await SaveDesignedReportToDatabaseAsync(report))
                            MarkDesignerSaved(fastReportDesigner);
                    }

                    void SaveAsCommandHandler(object? sender, EventArgs e)
                    {
                        Log.Information(AppStrings.ReportSaveAsDisabled, reportName);
                    }

                    static void MarkDesignerSaved(Designer designer)
                    {
                        designer.Modified = false;
                        designer.Restrictions.DontSaveReport = true;
                        designer.UpdatePlugins(null);
                        RemoveDesignerModifiedMarker(designer);
                    }

                    static void RemoveDesignerModifiedMarker(Designer designer)
                    {
                        var form = designer.FindForm();
                        if (form != null && form.Text.Contains('*'))
                            form.Text = form.Text.Replace("*", string.Empty);
                    }

                    static void HideSaveAsCommand(Designer designer)
                    {
                        if (designer.Plugins.FindType("DesignerMenu") is DesignerMenu menu)
                        {
                            menu.miFileSaveAs.Visible = false;
                            menu.miFileSaveAs.ShortcutKeys = System.Windows.Forms.Keys.None;
                        }
                    }

                    static void UpdateSaveCommandState(Designer designer)
                    {
                        bool disableSave = !designer.Modified;
                        if (designer.Restrictions.DontSaveReport == disableSave)
                            return;

                        designer.Restrictions.DontSaveReport = disableSave;
                        designer.UpdatePlugins(null);
                        HideSaveAsCommand(designer);
                    }

                    void DesignerLoadedHandler(object? sender, EventArgs e)
                    {
                        if (sender is not Designer designer)
                            return;

                        fastReportDesigner = designer;
                        designer.AskSave = false;
                        HideSaveAsCommand(designer);
                        UpdateSaveCommandState(designer);
                        designer.cmdSave.CustomAction += SaveCommandHandler;
                        designer.cmdSaveAs.CustomAction += SaveAsCommandHandler;
                        saveCommandStateHandler = (_, _) => UpdateSaveCommandState(designer);
                        saveCommandStateTimer = new System.Windows.Forms.Timer { Interval = 100 };
                        saveCommandStateTimer.Tick += saveCommandStateHandler;
                        saveCommandStateTimer.Start();
                        designerFormClosingHandler = DesignerFormClosingHandler;
                        designerWindowAvailableHandler = (_, _) => AttachDesignerFormClosingHandler();
                        designer.VisibleChanged += designerWindowAvailableHandler;
                        designerFormAttachTimer = new System.Windows.Forms.Timer { Interval = 100 };
                        designerFormAttachTimer.Tick += designerWindowAvailableHandler;
                        designerFormAttachTimer.Start();
                        AttachDesignerFormClosingHandler();
                    }

                    // Отображение отчета
                    Config.DesignerSettings.DesignerLoaded += DesignerLoadedHandler;

                    try
                    {
                        report.Design();
                    }
                    finally
                    {
                        Config.DesignerSettings.DesignerLoaded -= DesignerLoadedHandler;
                        if (designerForm != null && designerFormClosingHandler != null)
                            designerForm.FormClosing -= designerFormClosingHandler;
                        if (fastReportDesigner != null)
                        {
                            if (designerWindowAvailableHandler != null)
                            {
                                fastReportDesigner.VisibleChanged -= designerWindowAvailableHandler;
                                if (designerFormAttachTimer != null)
                                    designerFormAttachTimer.Tick -= designerWindowAvailableHandler;
                            }

                            designerFormAttachTimer?.Stop();
                            designerFormAttachTimer?.Dispose();
                            if (saveCommandStateTimer != null && saveCommandStateHandler != null)
                                saveCommandStateTimer.Tick -= saveCommandStateHandler;
                            saveCommandStateTimer?.Stop();
                            saveCommandStateTimer?.Dispose();
                            fastReportDesigner.cmdSave.CustomAction -= SaveCommandHandler;
                            fastReportDesigner.cmdSaveAs.CustomAction -= SaveAsCommandHandler;
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                // Обработка ошибок (например, если файл не найден или нет прав доступа)
                Log.Error(AppStrings.ReportFileCreateFailed, ex.Message);
                return;
            }

            if (savedToDatabaseOnClose)
            {
                Log.Information(AppStrings.ReportDesignerClosedAfterSave, reportName);
                return;
            }

            Log.Information(AppStrings.ReportDesignerClosedWithoutSave, reportName);
           
        }


        private static string GetUniqueTableName(DataConnectionBase connection, string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "Table";
            baseName = baseName.Replace(":", "_").Replace(".", "_").Replace(" ", "_");

            string name = baseName;
            int counter = 1;
            while (ContainsTableName(connection, name))
            {
                name = $"{baseName}_{counter}";
                counter++;
            }

            return name;
        }


        private static bool ContainsTableName(DataConnectionBase connection, string name)
        {
            foreach (TableDataSource table in connection.Tables.OfType<TableDataSource>())
            {
                if (string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false; 
        }

        // Команда для кнопки "Удалить"
        [RelayCommand(CanExecute = nameof(HasSelectedReport))]
        private async void DeleteReport()
        {
            if (SelectedReport == null)
                return;
            // Показываем MessageBox с подтверждением
            var messageBox = MessageBoxManager.GetMessageBoxStandard(
                AppStrings.ConfirmationTitle, // Заголовок окна
                AppStrings.DeleteReportPrompt, // Текст сообщения
                ButtonEnum.YesNo, // Кнопки (Да/Нет)
                Icon.Question // Иконка (вопросительный знак)
            );

            // Отображаем MessageBox и ждем результата
            var result = ButtonResult.No;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                result = await messageBox.ShowWindowDialogAsync(desktop.MainWindow);

            // Если пользователь нажал "Да"
            if (result == ButtonResult.Yes)
            {                
                if (SelectedReport != null)
                {
                    // Удаление отчета из базы данных
                    var f_del = await _dbService.DeleteReportAsync(SelectedReport.ARMReportID);
                    if (f_del)
                    {                    
                        Log.Information(AppStrings.ReportDeleted, SelectedReport.Name);
                        // Обновление списка отчетов
                        await LoadReportsAsync();
                    }
                    else
                        Log.Information(AppStrings.ReportDeleteFailed, SelectedReport.Name);
                }
            }
        }

        private async Task<bool> UpdatePendingReportAsync(ARMReport report, string sourceFilePath)
        {
            try
            {
                if (await _dbService.UpdateReportAsync(report))
                {
                    Log.Information(AppStrings.ReportUpdatedInDatabase, report.Name);
                    DeletePendingReportFiles(sourceFilePath);
                    Log.Information(AppStrings.ReportCopyRemovedFromPendingDirectory, report.Name);
                    return true;
                }

                Log.Error(AppStrings.ReportUpdateFailedPendingCopyLeft);
                return false;
            }
            catch (NpgsqlException ex)
            {
                Log.Error(ex, AppStrings.DatabaseOperationFailed);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.ReportUpdateUnknownError);
                return false;
            }
        }

        private static string GetPendingReportMetadataPath(string reportFilePath)
        {
            return $"{reportFilePath}.metadata.json";
        }

        private static void SavePendingReportMetadata(
            string reportFilePath,
            PendingReportOperation operation,
            int? reportId,
            string? reportName,
            string? reportDescription)
        {
            var metadata = new PendingReportMetadata
            {
                Operation = operation,
                ReportId = reportId,
                Name = reportName,
                Description = reportDescription
            };

            File.WriteAllText(GetPendingReportMetadataPath(reportFilePath), JsonConvert.SerializeObject(metadata));
        }

        private static PendingReportMetadata LoadPendingReportMetadata(string reportFilePath)
        {
            var metadataPath = GetPendingReportMetadataPath(reportFilePath);
            if (!File.Exists(metadataPath))
            {
                return new PendingReportMetadata { Operation = PendingReportOperation.Insert };
            }

            return JsonConvert.DeserializeObject<PendingReportMetadata>(File.ReadAllText(metadataPath))
                ?? new PendingReportMetadata { Operation = PendingReportOperation.Insert };
        }

        private static void DeletePendingReportFiles(string reportFilePath)
        {
            if (File.Exists(reportFilePath))
            {
                File.Delete(reportFilePath);
            }

            var metadataPath = GetPendingReportMetadataPath(reportFilePath);
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }

        private enum PendingReportOperation
        {
            Insert,
            Update
        }

        private sealed class PendingReportMetadata
        {
            public PendingReportOperation Operation { get; set; }

            public int? ReportId { get; set; }

            public string? Name { get; set; }

            public string? Description { get; set; }
        }

        private static string GetNotUploadedReportsDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "not_uploaded");
        }

        // Команда для кнопки "Создать дубликат"
        [RelayCommand(CanExecute = nameof(HasSelectedReport))]
        private async void DuplicateReport()
        {
            ARMReport newReport = new();
            newReport.ParentID = 0;
            newReport.UniqueID = 1;
            newReport.Name = SelectedReport.Name;
            newReport.isFolder = false;
            newReport.isDelete = false;
            newReport.Description = SelectedReport.Description;
            newReport.reportData = SelectedReport.reportData;

        // var reportName = DuplicateName(newReport.Name);
            var reportName = newReport.Name;
            string copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

            try
            {
                Report report = new Report();
                report.Save(copyFilePath);
                Log.Information(AppStrings.ReportCopySavedToDirectory, reportName);

                if (await _dbService.InsertReportAsync(newReport))
                {
                    Log.Information(AppStrings.ReportSavedToDatabase, reportName);
                    File.Delete(copyFilePath);
                    Log.Information(AppStrings.ReportCopyRemovedFromDirectory, reportName);
                }
                else
                {
                    Log.Error(AppStrings.ReportSaveFailedApplicationCopySaved);
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, AppStrings.ReportCopyDeleteIoFailed, reportName);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, AppStrings.ReportCopyDeleteAccessDenied, reportName);
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, AppStrings.DatabaseOperationFailed);
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, AppStrings.ReportSaveUnknownError);
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectedReport))]
        private async void RenameReport()
        {
            if (SelectedReport == null)
                return;

            var Owner = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var createReportViewModel = new CreateReportViewModel(_dbService);
            var createReportWindow = new CreateReportView { DataContext = createReportViewModel };
            createReportViewModel.ReportName = SelectedReport.Name;
            createReportViewModel.ReportDescription = SelectedReport.Description;
            LocalizationManager.BindWindowTitle(createReportWindow, nameof(AppStrings.EditReportTitle));
            var result = await createReportWindow.ShowDialog<bool>(Owner);
            // Если нажата кнопка "Сохранить"
            if (result)
            {
                // Получаем введенное имя отчета
                string reportName = string.IsNullOrEmpty(createReportViewModel.ReportName)
                    ? AppStrings.Untitled
                    : createReportViewModel.ReportName;
                // Получаем введенное описание отчета
                string reportDescription = string.IsNullOrEmpty(createReportViewModel.ReportDescription)
                    ? AppStrings.Untitled
                    : createReportViewModel.ReportDescription;
                byte[] reportBytes = SelectedReport.reportData;
                string copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

                try
                {
                    using (MemoryStream stream = new MemoryStream(reportBytes))
                    {
                        Report report = new Report();
                        report.Load(stream);
                        report.Save(copyFilePath);
                        Log.Information(AppStrings.ReportCopySavedToDirectory, reportName);

                        // Сохраняем отчет в новый поток
                        using (MemoryStream newStream = new MemoryStream())
                        {
                            report.Save(newStream);
                            reportBytes = newStream.ToArray();
                        }
                    }
                }

                catch (Exception ex)
                {
                    // Обработка ошибок (например, если файл не найден или нет прав доступа)
                    Log.Error(AppStrings.ReportFileCreateFailed, ex.Message);
                    return;
                }

                ARMReport newReport = new();
                newReport.ARMReportID = SelectedReport.ARMReportID;
                newReport.ParentID = 0;
                newReport.UniqueID = 1;
                newReport.Name = reportName;
                newReport.isFolder = false;
                newReport.isDelete = false;
                newReport.Description = reportDescription;
                newReport.reportData = reportBytes;

                try
                {
                    if (await _dbService.UpdateReportAsync(newReport))
                    {
                        Log.Information(AppStrings.ReportUpdatedInDatabase, reportName);
                        File.Delete(copyFilePath);
                        Log.Information(AppStrings.ReportCopyRemovedFromDirectory, reportName);
                    }
                    else
                    {
                        Log.Error(AppStrings.ReportUpdateFailedApplicationCopySaved);
                    }
                }
                catch (IOException ioEx)
                {
                    // Обработка ошибок, связанных с файловой системой
                    Log.Error(ioEx, AppStrings.ReportCopyDeleteIoFailed, reportName);
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    // Обработка ошибок, связанных с отсутствием прав доступа
                    Log.Error(uaEx, AppStrings.ReportCopyDeleteAccessDenied, reportName);
                }
                catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
                {
                    Log.Error(ex, AppStrings.DatabaseOperationFailed);
                }

                catch (Exception ex) // Обработка всех остальных исключений
                {
                    Log.Error(ex, AppStrings.ReportSaveUnknownError);
                }

            }
        }

        [RelayCommand]
        private async void ImportFromXml()
        {
            byte[] reportBytes;
            string reportName = "";
            string copyFilePath = "";
            try
            {
                // Вызываем диалог выбора файла
                string filePath = await OpenFileXMLDialogAsync();

                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information(AppStrings.ImportCancelledFileNotSelected);
                    return;
                }

                // Проверяем, что файл .xml существует
                if (!File.Exists(filePath))
                {
                    Log.Error(AppStrings.FileNotFound, filePath);
                    return;
                }

                reportName = Path.GetFileNameWithoutExtension(filePath);
                copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

                using (MemoryStream stream = new MemoryStream())
                {
                    Report report = new Report();
                    report.Load(filePath);
                    report.Save(stream);
                    report.Save(copyFilePath);
                    reportBytes = stream.ToArray();
                    Log.Information(AppStrings.ImportedFileSaved, copyFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.XmlImportFailed);
                return;
            }

            string reportDescription = reportName;
            ARMReport newReport = new();
            newReport.ParentID = 0;
            newReport.UniqueID = 1;
            newReport.Name = reportName;
            newReport.isFolder = false;
            newReport.isDelete = false;
            newReport.Description = reportDescription;
            newReport.reportData = reportBytes;

            try
            {
                if (await _dbService.InsertReportAsync(newReport))
                {
                    Log.Information(AppStrings.ReportSavedToDatabase, reportName);
                    File.Delete(copyFilePath);
                    Log.Information(AppStrings.ReportCopyRemovedFromDirectory, reportName);
                }
                else
                {
                    Log.Error(AppStrings.ReportSaveFailedApplicationCopySaved);
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, AppStrings.ReportCopyDeleteIoFailed, reportName);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, AppStrings.ReportCopyDeleteAccessDenied, reportName);
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, AppStrings.DatabaseOperationFailed);
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, AppStrings.ReportSaveUnknownError);
            }

        }
    
        [RelayCommand(CanExecute = nameof(HasSelectedReport))]
        private async void ExportToXml()
        {
            if (SelectedReport == null)
                return;

            try
            {
                // Вызываем диалог выбора файла
                string filePath = await SaveFileToXMLDialogAsync();

                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information(AppStrings.ExportCancelledFileNotSelected);
                    return;
                }
                using (MemoryStream stream = new MemoryStream(SelectedReport.reportData))
                {
                    Report report = new Report();
                    report.Load(stream);
                    report.Save(filePath);
                    Log.Information(AppStrings.XmlExportSucceeded, filePath);
                }                                
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.XmlExportFailed);
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectedReport))]
        private async void ExportFRX()
        {
            if (SelectedReport == null)
                return;

            try
            {
                string filePath = await SaveFileToFRXDialogAsync(SelectedReport.Name);

                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information(AppStrings.ExportFrxCancelledFileNotSelected);
                    return;
                }

                using (MemoryStream stream = new MemoryStream(SelectedReport.reportData))
                {
                    Report report = new Report();
                    report.Load(stream);
                    report.Save(filePath);
                }

                Log.Information(AppStrings.FrxExportSucceeded, filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.FrxExportFailed);
            }
        }

        [RelayCommand]
        private async void ImportFromFRX()        
        {
            byte[] reportBytes;
            string reportName = "";
            string copyFilePath = "";
            try
            {
                // Вызываем диалог выбора файла
                string filePath = await OpenFileFRXDialogAsync();

                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information(AppStrings.ImportCancelledFileNotSelected);
                    return;
                }

                // Проверяем, что файл .fxr существует
                if (!File.Exists(filePath))
                {
                    Log.Error(AppStrings.FileNotFound, filePath);
                    return;
                }

                reportName = Path.GetFileNameWithoutExtension(filePath);
                copyFilePath = Path.Combine(AppContext.BaseDirectory, $"{reportName}.frx");

                using (MemoryStream stream = new MemoryStream())
                {
                    Report report = new Report();
                    report.Load(filePath);
                    using (MemoryStream streamForSave = new MemoryStream())
                    {
                        report.Save(streamForSave);
                        reportBytes = streamForSave.ToArray();
                    }
                    report.Save(copyFilePath);                    
                    Log.Information(AppStrings.ImportedFileSaved, copyFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.FrxImportFailed);
                return;
            }

            string reportDescription = reportName;
            ARMReport newReport = new();
            newReport.ParentID = 0;
            newReport.UniqueID = 1;
            newReport.Name = reportName;
            newReport.isFolder = false;
            newReport.isDelete = false;
            newReport.Description = reportDescription;
            newReport.reportData = reportBytes;

            try
            {
                if (await _dbService.InsertReportAsync(newReport))
                {
                    Log.Information(AppStrings.ReportSavedToDatabase, reportName);
                    File.Delete(copyFilePath);
                    Log.Information(AppStrings.ReportCopyRemovedFromDirectory, reportName);
                }
                else
                {
                    Log.Error(AppStrings.ReportSaveFailedApplicationCopySaved);
                }
            }
            catch (IOException ioEx)
            {
                // Обработка ошибок, связанных с файловой системой
                Log.Error(ioEx, AppStrings.ReportCopyDeleteIoFailed, reportName);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Обработка ошибок, связанных с отсутствием прав доступа
                Log.Error(uaEx, AppStrings.ReportCopyDeleteAccessDenied, reportName);
            }
            catch (NpgsqlException ex) // Обработка исключений, связанных с PostgreSQL
            {
                Log.Error(ex, AppStrings.DatabaseOperationFailed);
            }

            catch (Exception ex) // Обработка всех остальных исключений
            {
                Log.Error(ex, AppStrings.ReportSaveUnknownError);
            }

        }

        [RelayCommand]
        private async void UploadAllFRXToDB()
        {
            if (!_dbService.CheckConnection())
            {
                Log.Error(AppStrings.PendingReportsUploadUnavailable);
                await ShowErrorMessageAsync(AppStrings.DatabaseUnavailable, AppStrings.PendingReportsUploadUnavailable);
                return;
            }

            Log.Information(AppStrings.PendingReportsUploadStarted);

            try
            {
                // Получаем каталог с отчетами, которые еще не попали в базу
                string currentDirectory = GetNotUploadedReportsDirectory();
                Directory.CreateDirectory(currentDirectory);

                // Ищем все файлы с расширением .frx в каталоге not_uploaded
                string[] frxFiles = Directory.GetFiles(currentDirectory, "*.frx");

                // Очищаем коллекцию FRXReports перед заполнением
                FRXReports.Clear();

                // Проходим по всем найденным файлам
                foreach (string filePath in frxFiles)
                {
                    try
                    {
                        // Читаем содержимое файла в массив байтов
                        byte[] reportData = await File.ReadAllBytesAsync(filePath);
                        var metadata = LoadPendingReportMetadata(filePath);

                        // Создаем объект ARMReport
                        ARMReport report = new ARMReport
                        {
                            ARMReportID = metadata.ReportId ?? 0,
                            Name = string.IsNullOrWhiteSpace(metadata.Name)
                                ? Path.GetFileNameWithoutExtension(filePath)
                                : metadata.Name,
                            Description = string.IsNullOrWhiteSpace(metadata.Description)
                                ? AppStrings.ImportedFromFile
                                : metadata.Description,
                            ParentID = 0,
                            UniqueID = 1,
                            isFolder = false,
                            isDelete = false,
                            reportData = reportData
                        };

                        var uploaded = metadata.Operation == PendingReportOperation.Update && metadata.ReportId.HasValue
                            ? await UpdatePendingReportAsync(report, filePath)
                            : await CreateReportAsync(report, filePath);

                        if (uploaded)
                        {
                            Log.Information(AppStrings.PendingReportUploaded, Path.GetFileName(filePath));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, AppStrings.PendingReportProcessingFailed, Path.GetFileName(filePath));
                    }
                }

                Log.Information(AppStrings.PendingReportsProcessed, currentDirectory);
                await LoadReportsAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.PendingReportsProcessingFailed);
            }
        }

        private async void HandleReportChange(string payload)
        {
            // Десериализация JSON-уведомления            
            if (payload != "")
            {
                Log.Information($"{payload}");
                // Загружаем полные данные отчета из базы данных
                await LoadReportsAsync();
            }
        }

        public async Task StartListeningForReportsChangeAsync()
        {
            try
            {
                await _dbService.ListenForReportsChangeAsync(HandleReportChange);
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.ReportChangeListenerStartFailed);
            }
        }

        public static async Task<string> OpenFileXMLDialogAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = AppStrings.SelectFileTitle,
                AllowMultiple = false, // Позволяет выбирать только один файл            
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = AppStrings.XmlFilesFilter, // Название фильтра
                        Extensions = new List<string> { "xml" } // Расширения файлов
                    }
                }
            };

            var window = Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            var result = await dialog.ShowAsync(window);
            return result?.FirstOrDefault(); // Возвращает путь к первому выбранному файлу или null, если
        }

        public static async Task<string>SaveFileToXMLDialogAsync()
        {

                // Создаем диалог сохранения файла
                var saveFileDialog = new SaveFileDialog
                {
                    Title = AppStrings.SaveReportAsXmlTitle,
                    Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = AppStrings.XmlFilesFilter, // Название фильтра
                        Extensions = new List<string> { "xml" } // Расширения файлов
                    }
                },
                    InitialFileName = "report.xml" // Предлагаемое имя файла по умолчанию
                };

                // Получаем главное окно приложения
                var window = Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                // Открываем диалог сохранения файла
                string outputFilePath = await saveFileDialog.ShowAsync(window);
                return outputFilePath;
        }

        public static async Task<string> SaveFileToFRXDialogAsync(string? reportName)
        {
            string initialFileName = string.IsNullOrWhiteSpace(reportName)
                ? "report.frx"
                : $"{reportName}.frx";

            var saveFileDialog = new SaveFileDialog
            {
                Title = AppStrings.ExportReportToFrxTitle,
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = AppStrings.FrxFilesFilter,
                        Extensions = new List<string> { "frx" }
                    }
                },
                InitialFileName = initialFileName
            };

            var window = Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            string outputFilePath = await saveFileDialog.ShowAsync(window);
            return outputFilePath;
        }

        public static async Task<string> OpenFileFRXDialogAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = AppStrings.SelectFileTitle,
                AllowMultiple = false, // Позволяет выбирать только один файл            
                Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter
                        {
                            Name = AppStrings.FrxFilesFilter, // Название фильтра
                            Extensions = new List<string> { "frx" } // Расширения файлов
                        }
                    }
            };

            var window = Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            var result = await dialog.ShowAsync(window);
            return result?.FirstOrDefault(); // Возвращает путь к первому выбранному файлу или null, если
        }

}
