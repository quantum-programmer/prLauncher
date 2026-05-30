using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDesigner.Services;
using Avalonia.Controls;

namespace RDesigner.ViewModels
{
    public partial class CreateReportViewModel : ViewModelBase
    {
        private readonly IDBService _dbService;

        public CreateReportViewModel(IDBService dbService)
        {
            _dbService = dbService;
        }

        [ObservableProperty]
        private string? _reportName;
        
        [ObservableProperty]
        private string? _reportDescription;

        [RelayCommand]
        private void Save(Window window)
        {
            // Закрываем окно с результатом true (сохранение)
            window.Close(true);
        }

        [RelayCommand]
        private void Cancel(Window window)
        {
            // Закрываем окно с результатом false (отмена)
            window.Close(false);
        }
    }
}