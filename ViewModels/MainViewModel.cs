using System;
using System.Collections.ObjectModel;
using TranslationEplAddIn.Tool.Models;

namespace TranslationEplAddIn.Tool.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private string _projectName;
        public string ProjectName
        {
            get => _projectName;
            set => SetProperty(ref _projectName, value);
        }

        private string _selectedEngine;
        public string SelectedEngine
        {
            get => _selectedEngine;
            set => SetProperty(ref _selectedEngine, value);
        }

        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public ObservableCollection<TranslationItem> ExtractedItems { get; } = new ObservableCollection<TranslationItem>();

        public MainViewModel()
        {
            SelectedEngine = SettingsManager.Current.SelectedEngine;
        }
    }
}
