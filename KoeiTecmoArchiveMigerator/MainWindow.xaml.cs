using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace KoeiTecmoArchiveMigrator
{
  public partial class MainWindow : Window
  {
    private const string SAVE_DATA_PATTERN = @"^SAVEDATA\d+$";
    private const string AUTO_SAVE_DIR = "SAVEDATAAUTO";
    private const string SAVE_FILE_NAME = "SAVEDATA.BIN";
    private const string BACKUP_SUFFIX = ".KoeiTecmoArchiveMigrator";
    private const int DEVICE_FLAG_OFFSET = 0x10;
    private const int DEVICE_FLAG_LENGTH = 8;

    private ObservableCollection<ArchiveItem> archiveItems = new ObservableCollection<ArchiveItem>();
    private byte[]? autoDeviceFlag;

    public MainWindow()
    {
      InitializeComponent();
      RootDirTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KoeiTecmo");
      ArchiveDataGrid.ItemsSource = archiveItems;
      LoadGames();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
      var dialog = new Microsoft.Win32.OpenFileDialog
      {
        Title = (string)Application.Current.Resources["SelectFolderText"],
        ValidateNames = false,
        CheckFileExists = false,
        CheckPathExists = true,
      };

      if (dialog.ShowDialog() == true)
      {
        RootDirTextBox.Text = Path.GetDirectoryName(dialog.FileName);
        LoadGames();
      }
    }

    private void LoadGames()
    {
      try
      {
        GameComboBox.Items.Clear();
        var rootDir = RootDirTextBox.Text;
        if (Directory.Exists(rootDir))
        {
          var dirs = Directory.GetDirectories(rootDir).Select(Path.GetFileName);
          foreach (var dir in dirs) GameComboBox.Items.Add(dir);
          if (dirs.Count() > 0) GameComboBox.SelectedIndex = 0;
        }
      }
      catch (Exception ex)
      {
        ShowError(string.Format(GetResources("ErrorDirReadFailed"), ex.Message));
      }
    }

    private void GameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (GameComboBox.SelectedItem != null)
      {
        LoadSteamIds();
      }
    }

    private void LoadSteamIds()
    {
      try
      {
        SteamIdComboBox.Items.Clear();
        var gameDir = Path.Combine(RootDirTextBox.Text, GameComboBox.SelectedItem.ToString(), "Savedata");
        if (Directory.Exists(gameDir))
        {
          var dirs = Directory.GetDirectories(gameDir).Select(Path.GetFileName);
          foreach (var dir in dirs) SteamIdComboBox.Items.Add(dir);
          if (dirs.Count() > 0) SteamIdComboBox.SelectedIndex = 0;
        }
      }
      catch (Exception ex)
      {
        ShowError(string.Format(GetResources("ErrorSteamIdReadFailed"), ex.Message));
      }
    }

    private void SteamIdComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (SteamIdComboBox.SelectedItem != null)
      {
        LoadArchives();
      }
    }

    private void LoadArchives()
    {
      archiveItems.Clear();
      autoDeviceFlag = null;
      MigrateButton.Visibility = Visibility.Hidden;

      try
      {
        var baseDir = Path.Combine(RootDirTextBox.Text, GameComboBox.SelectedItem.ToString(), "Savedata", SteamIdComboBox.SelectedItem.ToString());
        var autoFile = Path.Combine(baseDir, AUTO_SAVE_DIR, SAVE_FILE_NAME);

        if (File.Exists(autoFile))
        {
          autoDeviceFlag = ReadDeviceFlag(autoFile);
        }

        var dirs = Directory.GetDirectories(baseDir).Where(d => Regex.IsMatch(Path.GetFileName(d), SAVE_DATA_PATTERN));
        foreach (var dir in dirs)
        {
          var file = Path.Combine(dir, SAVE_FILE_NAME);
          var item = new ArchiveItem { Name = Path.GetFileName(dir) };
          if (!File.Exists(file))
          {
            item.Status = GetResources("StatusFileMissing");
            item.StatusValue = "FileMissing";
          }
          else if (autoDeviceFlag == null)
          {
            item.Status = GetResources("StatusUnknown");
            item.StatusValue = "Unknown";
          }
          else
          {
            var deviceFlag = ReadDeviceFlag(file);
            if (deviceFlag.SequenceEqual(autoDeviceFlag))
            {
              item.Status = GetResources("StatusSuccess");
              item.StatusValue = "Success";
            }
            else
            {
              item.Status = GetResources("StatusNeedsMigration");
              item.StatusValue = "NeedsMigration";
            }
          }
          archiveItems.Add(item);
        }

        UpdateMigrateButton();
      }
      catch (Exception ex)
      {
        ShowError(string.Format(GetResources("ErrorArchiveLoadFailed"), ex.Message));
      }
    }

    private byte[] ReadDeviceFlag(string filePath)
    {
      using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
      {
        fs.Seek(DEVICE_FLAG_OFFSET, SeekOrigin.Begin);
        byte[] buffer = new byte[DEVICE_FLAG_LENGTH];
        fs.Read(buffer, 0, DEVICE_FLAG_LENGTH);
        return buffer;
      }
    }

    private void MigrateButton_Click(object sender, RoutedEventArgs e)
    {
      foreach (var item in archiveItems.Where(i => i.IsSelected && i.StatusValue == "NeedsMigration"))
      {
        var file = Path.Combine(RootDirTextBox.Text, GameComboBox.SelectedItem.ToString(), "Savedata", SteamIdComboBox.SelectedItem.ToString(), item.Name, SAVE_FILE_NAME);
        var backup = file + BACKUP_SUFFIX;
        File.Copy(file, backup, true);
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Write))
        {
          fs.Seek(DEVICE_FLAG_OFFSET, SeekOrigin.Begin);
          fs.Write(autoDeviceFlag, 0, DEVICE_FLAG_LENGTH);
        }
      }
      LoadArchives();
    }

    private void UpdateMigrateButton()
    {
      var hasModifiable = archiveItems.Any(i => i.StatusValue == "NeedsMigration");
      MigrateButton.Visibility = hasModifiable ? Visibility.Visible : Visibility.Hidden;
      MigrateButton.IsEnabled = archiveItems.Any(i => i.IsSelected && i.StatusValue == "NeedsMigration");
    }

    private void ArchiveDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      UpdateMigrateButton();
    }

    private void ShowError(string message)
    {
      MessageBox.Show(message, (string)Application.Current.Resources["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
      ArchiveDataGrid.IsEnabled = false;
      MigrateButton.Visibility = Visibility.Hidden;
    }

    private void LanguageChinese_Click(object sender, RoutedEventArgs e)
    {
      ChineseMenuItem.IsChecked = true;
      EnglishMenuItem.IsChecked = false;

      SetLanguage("zh-CN");
    }

    private void LanguageEnglish_Click(object sender, RoutedEventArgs e)
    {
      EnglishMenuItem.IsChecked = true;
      ChineseMenuItem.IsChecked = false;

      SetLanguage("en-US");
    }

    private void SetLanguage(string culture)
    {
      try
      {
        // 1. Load new language resource
        var dict = new ResourceDictionary
        {
          Source = new Uri($"Resources/Lang.{culture}.xaml", UriKind.Relative)
        };

        // 2. Replace current resources
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(dict);

        // 3. Refresh UI
        // LoadArchives();
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to load language resources: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private string GetResources(string key)
    {
      return (string)Application.Current.Resources[key];
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
      MessageBox.Show(GetResources("AboutText"), GetResources("MenuAbout"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
  }

  public class ArchiveItem : INotifyPropertyChanged
  {
    private bool _isSelected;
    public bool IsSelected
    {
      get => _isSelected;
      set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? StatusValue { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }
}