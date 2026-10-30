using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using BackgroundTuner.Models;
using BackgroundTuner.Services;

namespace BackgroundTuner.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ArchiveHandler _archiveHandler;
        private readonly BSMLHandler _bsmlHandler;
        private readonly BRMLExtractor _brmlExtractor;
        private readonly BGRecalculator _bgRecalculator;

        private Dataset? _dataset;
        private string? _loadedBsmlPath;
        private bool _isBsmlLoaded;
        private bool _isMasterBlankLoaded;
        private bool _isCurrentBlankLoaded;

        private string _bsmlStatusText = "No file selected";
        private string _masterBlankStatusText = "No file selected";
        private string _currentBlankStatusText = "No file selected";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand LoadBsmlCommand { get; }
        public ICommand LoadBrmlCommand { get; }
        public ICommand RecalculateCommand { get; }

        public string BsmlStatusText
        {
            get => _bsmlStatusText;
            private set
            {
                if (_bsmlStatusText != value)
                {
                    _bsmlStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MasterBlankStatusText
        {
            get => _masterBlankStatusText;
            private set
            {
                if (_masterBlankStatusText != value)
                {
                    _masterBlankStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentBlankStatusText
        {
            get => _currentBlankStatusText;
            private set
            {
                if (_currentBlankStatusText != value)
                {
                    _currentBlankStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public MainViewModel()
        {
            _archiveHandler = new ArchiveHandler();
            _bsmlHandler = new BSMLHandler(_archiveHandler);
            _brmlExtractor = new BRMLExtractor();
            _bgRecalculator = new BGRecalculator();

            LoadBsmlCommand = new RelayCommand(_ => LoadBsml());
            LoadBrmlCommand = new RelayCommand(param => LoadBrml(param as string), _ => CanLoadBrml());
            RecalculateCommand = new RelayCommand(_ => RecalculateBackground(), _ => CanRecalculate());
        }

        public void LoadBsml()
        {
            var filePath = GetFilePath("BSML files (*.bsml)|*.bsml", ".bsml");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!filePath.EndsWith(".bsml", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Please select a .bsml file.", "Invalid file type", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _dataset = _bsmlHandler.LoadBSML(filePath);
                _loadedBsmlPath = filePath;
                _isBsmlLoaded = true;
                _isMasterBlankLoaded = false;
                _isCurrentBlankLoaded = false;

                BsmlStatusText = Path.GetFileName(filePath);
                MasterBlankStatusText = "No file selected";
                CurrentBlankStatusText = "No file selected";

                RefreshCommandStates();
            }
            catch (Exception ex)
            {
                _isBsmlLoaded = false;
                _loadedBsmlPath = null;
                MessageBox.Show($"Failed to load BSML file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshCommandStates();
            }
        }

        public void LoadBrml(string? blankType)
        {
            if (!_isBsmlLoaded || _dataset == null)
            {
                return;
            }

            if (blankType != "MasterBlank" && blankType != "CurrentBlank")
            {
                MessageBox.Show("Unknown blank type.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var filePath = GetFilePath("BRML files (*.brml)|*.brml", ".brml");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!filePath.EndsWith(".brml", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Please select a .brml file.", "Invalid file type", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Dictionary<string, bool> lineDictionary = _dataset.Lines
                    .Where(line => !string.IsNullOrWhiteSpace(line._name))
                    .ToDictionary(line => line._name!, line => line._hasSensorBoost);

                var intensities = _brmlExtractor.LoadBRML(filePath, lineDictionary);

                foreach (var line in _dataset.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line._name))
                    {
                        continue;
                    }

                    if (intensities.TryGetValue(line._name, out double intensity))
                    {
                        if (blankType == "MasterBlank")
                        {
                            line._masterBlankIntensity = intensity;
                        }
                        else
                        {
                            line._currentBlankIntensity = intensity;
                        }
                    }
                }

                if (blankType == "MasterBlank")
                {
                    _isMasterBlankLoaded = true;
                    MasterBlankStatusText = Path.GetFileName(filePath);
                }
                else
                {
                    _isCurrentBlankLoaded = true;
                    CurrentBlankStatusText = Path.GetFileName(filePath);
                }

                RefreshCommandStates();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load BRML file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void RecalculateBackground()
        {
            if (_dataset == null || string.IsNullOrWhiteSpace(_loadedBsmlPath))
            {
                return;
            }

            bool isComplete = true;
            foreach (var line in _dataset.Lines)
            {
                isComplete &= _bgRecalculator.Calculate(line);
            }

            if (!isComplete)
            {
                MessageBox.Show("Recalculation could not be completed for all lines.", "Recalculation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? outputFilePath = GetSaveFilePath(_loadedBsmlPath);
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                return;
            }

            try
            {
                _bsmlHandler.EditBSML(_dataset, outputFilePath);
                MessageBox.Show("Recalculation finished and BSML was saved.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save recalculated BSML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanLoadBrml()
        {
            return _isBsmlLoaded && _dataset != null && _dataset.Lines.Count > 0;
        }

        private bool CanRecalculate()
        {
            if (!_isBsmlLoaded || !_isMasterBlankLoaded || !_isCurrentBlankLoaded || _dataset == null || _dataset.Lines.Count == 0)
            {
                return false;
            }

            return _dataset.Lines.All(line =>
                line._masterDriftIntensity.HasValue &&
                line._currentDriftIntensity.HasValue &&
                line._masterBlankIntensity.HasValue &&
                line._currentBlankIntensity.HasValue &&
                line._theoBGFactorOld.HasValue);
        }

        private static string? GetFilePath(string filter, string defaultExtension)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExtension,
                CheckFileExists = true,
                Multiselect = false
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static string? GetSaveFilePath(string sourceBsmlPath)
        {
            var sourceDirectory = Path.GetDirectoryName(sourceBsmlPath) ?? string.Empty;
            var sourceFileName = Path.GetFileNameWithoutExtension(sourceBsmlPath);

            var dialog = new SaveFileDialog
            {
                Filter = "BSML files (*.bsml)|*.bsml",
                DefaultExt = ".bsml",
                AddExtension = true,
                InitialDirectory = sourceDirectory,
                FileName = $"{sourceFileName}_recalculated.bsml",
                OverwritePrompt = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void RefreshCommandStates()
        {
            ((RelayCommand)LoadBrmlCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RecalculateCommand).RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
