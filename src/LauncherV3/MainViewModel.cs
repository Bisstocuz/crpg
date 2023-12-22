﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Text;
using LauncherV3.LauncherHelper;
using static LauncherV3.LauncherHelper.GameInstallationFolderResolver;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Xml;
using System.DirectoryServices.ActiveDirectory;
using ICSharpCode.SharpZipLib.Tar;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Reflection;
using System.Collections;
using System.Resources;
using System.Windows.Resources;
using System.Windows;

namespace LauncherV3;

public partial class MainViewModel : ObservableObject
{
    public enum Platform
    {
        Steam,
        Epic,
        Xbox
    }

    [ObservableProperty]
    private bool isUpdating;
    [ObservableProperty]
    private bool isVerifying;
    private ConcurrentQueue<string> _messageQueue = new();
    public static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Crpg Launcher");
    private static readonly string HashFileName = "CrpgHash.xml";
    private static readonly string ConfigFileName = "config.json";
    [ObservableProperty]
    public bool isGameUpToDate;

    // PlatformViewModel Properties and Methods
    [ObservableProperty]
    private Platform _selectedPlatform;

    public List<Platform> PlatformOptions { get; }

    [ObservableProperty]
    private GameInstallationInfo? gameLocation;
    [ObservableProperty]
    private string version = "1.0.0";
    public event EventHandler? RequestClose;

    private void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    public MainViewModel()
    {
        // Initialize PlatformViewModel Properties
        PlatformOptions = Enum.GetValues(typeof(Platform)).Cast<Platform>().ToList();
    }

    private bool CanOpenFolder()
    {
        bool CantOpenFolder = (SelectedPlatform != Platform.Steam) || IsUpdating || IsVerifying;
        return !CantOpenFolder;
    }

    private bool CanVerify()
    {
        bool CanVerify = !IsUpdating && !IsVerifying && GameLocation != null;
        return CanVerify;
    }

    private bool CanStartCrpg()
    {
        bool canStartCrpg = !IsUpdating && !IsVerifying && GameLocation != null && IsGameUpToDate;
        return canStartCrpg;
    }

    private bool CanDetect()
    {
        bool canDetect = !IsUpdating && !IsVerifying && GameLocation == null;
        return canDetect;
    }

    private void NotifyUI()
    {
        VerifyGameFilesActionCommand.NotifyCanExecuteChanged();
        UpdateGameFilesCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        StartCrpgCommand.NotifyCanExecuteChanged();
        DetectCommand.NotifyCanExecuteChanged();
    }

    partial void OnGameLocationChanged(GameInstallationInfo? value)
    {
        NotifyUI();
    }

    partial void OnIsUpdatingChanged(bool value)
    {
        NotifyUI();
    }

    partial void OnIsVerifyingChanged(bool value)
    {
        NotifyUI();
    }


    private bool CanUpdate()
    {
        bool CanUpdate = !IsUpdating && !IsVerifying && GameLocation != null;
        return CanUpdate;
    }

    [RelayCommand(CanExecute = nameof(CanStartCrpg))]
    private void StartCrpg()
    {
        if (gameLocation == null)
        {
            WriteToConsole("Game Location is not set!");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            WorkingDirectory = gameLocation.ProgramWorkingDirectory ?? string.Empty,
            FileName = gameLocation.Program,
            Arguments = gameLocation.ProgramArguments ?? string.Empty,
            UseShellExecute = true,
        });
        Close();
    }

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        var folderDialog = new CommonOpenFileDialog
        {
            IsFolderPicker = true,
        };

        if (folderDialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            GameLocation = GameInstallationFolderResolver.CreateGameInstallationInfo(folderDialog.FileName, SelectedPlatform);
        }

        if (GameLocation != null)
        {
            SelectedPlatform = GameLocation.platform;
            Config.GameLocations[SelectedPlatform] = GameLocation;
            Config.LastPlatform = SelectedPlatform;
            WriteConfig();
            IsCrpgInstalled = true;

        }
        else
        {
            WriteToConsole("Bannerlord was not found at current Location");
            IsCrpgInstalled = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDetect))]
    public void Detect()
    {
        UpdateGameLocation(SelectedPlatform);
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    public async Task VerifyGameFilesActionAsync()
    {
        IsVerifying = true;
        if (gameLocation == null)
        {
            WriteToConsole("Game Location is not properly set");
            IsVerifying = false;
            return;
        }

        await VerifyGameFilesAsync();

        
    }

    private async Task VerifyGameFilesAsync(bool download = true)
    {
        WriteToConsole("Verifying Game Files, Launcher May become unresponsive for the next 60 secs");
        if (gameLocation != null)
        {
            await CrpgHashMethods.VerifyGameFiles(gameLocation.InstallationPath, ProgramDataPath, HashFileName);
            await Task.Delay(500);
            if (download)
            {
                await UpdateGameFilesAsync();
            }
        }
        else
        {
            WriteToConsole("Bannerlord was not found at current location");
        }
        IsVerifying = false;
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateGameFilesAsync()
    {
        IsUpdating = true;
        if (!HashExist())
        {
            await VerifyGameFilesAsync();
        }

        XmlDocument doc = new XmlDocument();
        try
        {
            string url = "https://c-rpg.eu/hash.xml"; // Replace with your XML URL
            using (WebClient client = new WebClient())
            {
                string xmlContent = client.DownloadString(url);
                doc.LoadXml(xmlContent);
            }
        }
        catch (Exception ex)
        {
            WriteToConsole($"Website may be updating, Use modmail to report it if issue persists in 15 minutes");
            WriteToConsole($"Error: {ex.Message}");
            IsUpdating = false;
            return;
        }


        if (doc?.DocumentElement == null)
        {
            IsUpdating = false;
            return;
        }

        Dictionary<string, string> distantAssets = new Dictionary<string, string>();
        Dictionary<string, string> distantMaps = new Dictionary<string, string>();
        string distantRestHash = CrpgHashMethods.ReadHash(doc, distantAssets, distantMaps);
        XmlDocument doc2 = new XmlDocument();
        try
        {
            doc2.Load(Path.Combine(ProgramDataPath, HashFileName));
        }
        catch (Exception ex)
        {
            WriteToConsole(ex.Message);
            WriteToConsole("Please Verify your game files first");
            IsUpdating = false;
            return;
        }

        Dictionary<string, string> localAssets = new Dictionary<string, string>();
        Dictionary<string, string> localMaps = new Dictionary<string, string>();

        string localRestHash = CrpgHashMethods.ReadHash(doc2, localAssets, localMaps);
        bool downloadRest = localRestHash != distantRestHash;

        var assetsToDownload = distantAssets.Where(a => !localAssets.Contains(a)).ToList();
        var assetsToDelete = localAssets.Where(a => !distantAssets.Contains(a)).ToList();
        if (Config.DevMode)
        {
            assetsToDelete = localAssets.Where(a => distantAssets.ContainsKey(a.Key) && !distantAssets.ContainsValue(a.Value)).ToList();
        }

        var mapsToDelete = localMaps.Where(a => !distantMaps.Contains(a)).ToList();
        if (Config.DevMode)
        {
            mapsToDelete = localMaps.Where(a => distantMaps.ContainsKey(a.Key) && !distantMaps.ContainsValue(a.Value)).ToList();
        }

        var mapsToDownload = distantMaps.Where(a => !localMaps.Contains(a)).ToList();

        if (assetsToDelete.Count == 0 && assetsToDownload.Count == 0 && mapsToDownload.Count == 0 && mapsToDelete.Count == 0 && !downloadRest)
        {
            WriteToConsole("Your game is Up To Date");
            IsUpdating = false;
            return;
        }

        if (GameLocation == null)
        {
            WriteToConsole("Cannot Download update as Bannerlord Location is not known");
            IsUpdating = false;
            return;
        }

        foreach (var assetToDelete in assetsToDelete)
        {
            string pathToDelete = Path.Combine(GameLocation.InstallationPath, "Modules/cRPG/AssetPackages/", assetToDelete.Key);
            WriteToConsole(pathToDelete);
            try
            {
                File.Delete(pathToDelete);
            }
            catch
            {
            }
        }

        foreach (var mapToDelete in mapsToDelete)
        {
            string pathToDelete = Path.Combine(GameLocation.InstallationPath, "Modules/cRPG/SceneObj/", mapToDelete.Key);
            WriteToConsole($"deleting {pathToDelete}");
            try
            {
                Directory.Delete(pathToDelete, recursive: true);
            }
            catch
            {
            }
        }

        string cRPGFolder = Path.Combine(GameLocation.InstallationPath, "Modules/cRPG/");
        if (Config.DevMode)
        {
            WriteToConsole("You're in Dev Mode. Only Assets and Maps will update. Other files remain untouched");
            WriteToConsole("If you want to update the other files, Uncheck Dev Mode , then put back your files");
        }
        else
        {
            if (downloadRest && Directory.Exists(cRPGFolder))
            {
                foreach (string dir in Directory.GetDirectories(cRPGFolder))
                {
                    if (Path.GetFileName(dir) == "SceneObj" || Path.GetFileName(dir) == "AssetPackages")
                    { continue; }
                    else
                    {
                        WriteToConsole($"deleting {dir}");
                        Directory.Delete(dir, recursive: true);
                    }
                }

                foreach (string file in Directory.GetFiles(cRPGFolder))
                {
                    WriteToConsole($"deleting {file}");
                    File.Delete(file);
                }

                try
                {
                    string subModulePath = Path.Combine(GameLocation.InstallationPath, "Modules/cRPG/SubModule.xml");
                    if (File.Exists(subModulePath))
                    {
                        WriteToConsole($"deleting {subModulePath}");
                        File.Delete(subModulePath);
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }

        List<Task> allTasks = new List<Task>();
        bool updateSuccessful = true;
        using (var client = new HttpClient())
        {
            foreach (var assetToDownload in assetsToDownload)
            {
                try
                {
                    // Download the file
                    WriteToConsole($"Downloading and extracting {assetToDownload.Key + ".tar.gz"} ");
                    var response = await client.GetAsync("https://c-rpg.eu/AssetPackages/" + assetToDownload.Key + ".tar.gz");
                    response.EnsureSuccessStatusCode();
                    string? contentType = response.Content!.Headers!.ContentType!.MediaType;
                    if (contentType == "text/html")
                    {
                        throw new Exception("Expected file, but received HTML content. The file may not exist at the specified URL.");
                    }

                    var extractionTask1 = Task.Run(() => Extract(response, Path.Combine(GameLocation.InstallationPath, "Modules/cRPG/AssetPackages/")));
                    allTasks.Add(extractionTask1);
                }
                catch (Exception ex)
                {
                    updateSuccessful = false;
                    WriteToConsole(ex.Message);
                }
            }

            foreach (var mapToDownload in mapsToDownload)
            {
                try
                {
                    // Download the file
                    WriteToConsole($"Downloading and extracting {mapToDownload.Key + ".tar.gz"} ");
                    var response = await client.GetAsync("https://c-rpg.eu/SceneObj/" + mapToDownload.Key + ".tar.gz");
                    response.EnsureSuccessStatusCode();
                    string? contentType = response.Content!.Headers!.ContentType!.MediaType;
                    if (contentType == "text/html")
                    {
                        throw new Exception("Expected file, but received HTML content. The file may not exist at the specified URL.");
                    }

                    var extractionTask2 = Task.Run(() => Extract(response, Path.Combine(GameLocation.InstallationPath, "Modules/cRPG/SceneObj/")));
                    allTasks.Add(extractionTask2);
                }
                catch (Exception ex)
                {
                    updateSuccessful = false;
                    WriteToConsole(ex.Message);
                }
            }

            if (downloadRest)
            {
                try
                {
                    // Download the file
                    WriteToConsole($"Downloading and extracting the xmls files : rest.tar.gz");
                    var response = await client.GetAsync("https://c-rpg.eu/rest.tar.gz");
                    response.EnsureSuccessStatusCode();
                    string? contentType = response.Content!.Headers!.ContentType!.MediaType;
                    if (contentType == "text/html")
                    {
                        throw new Exception("Expected file, but received HTML content. The file may not exist at the specified URL.");
                    }

                    var extractionTask3 = Task.Run(() => Extract(response, Path.Combine(GameLocation.InstallationPath, "Modules/cRPG/")));
                    allTasks.Add(extractionTask3);
                }

                catch (Exception ex)
                {
                    updateSuccessful = false;
                    WriteToConsole(ex.Message);
                }
            }
        }

        await Task.WhenAll(allTasks);
        if (updateSuccessful)
        {
            doc.Save(Path.Combine(ProgramDataPath, HashFileName));
            WriteToConsole("Update Finished");
            IsGameUpToDate = true;
            Config.IsGameUpToDate = true;
            WriteConfig();
        }
        else
        {
            WriteToConsole("There were issues during the update");
            WriteToConsole("Verifying Game Files to validate Download");
            IsUpdating = false;
            IsVerifying = true;
            await VerifyGameFilesAsync(false);
            WriteToConsole("It is possible that we are currently updating cRPG");
            WriteToConsole("If problem persist in an hour, please contact and moderator on discord");
            Config.IsGameUpToDate = false;
            IsGameUpToDate = false;
        }

        IsUpdating = false;
    }

    public void WriteToConsole(string text)
    {
        _messageQueue.Enqueue(text);
    }

    public string FlushText()
    {
        StringBuilder sb = new StringBuilder();
        while (_messageQueue.TryDequeue(out string? text))
        {
            sb.AppendLine(text);
        }

        return sb.ToString();
    }

    private bool HashExist()
    {
        return File.Exists(Path.Combine(ProgramDataPath, ConfigFileName));
    }

    private async void Extract(HttpResponseMessage response, string path)
    {
        using (var stream = await response.Content.ReadAsStreamAsync())
        {
            using (var gzipStream = new GZipStream(stream, CompressionMode.Decompress))
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                using (var tarArchive = TarArchive.CreateInputTarArchive(gzipStream))
                {
                    try
                    {
                        tarArchive.ExtractContents(path);
                    }
                    catch (Exception e)
                    {
                        WriteToConsole("Error while trying to extract download file");
                        WriteToConsole(e.Message);
                    }

                }
            }
        }
    }
    partial void OnSelectedPlatformChanged(Platform value)
    {
        UpdateGameLocation(value);
    }

    public void UpdateGameLocation(Platform platform)
    {
        if (Config.GameLocations.ContainsKey(platform))
        {
            GameLocation = Config.GameLocations[platform];
            Config.LastPlatform = platform;
        }

        if (GameLocation == null)
        {
            WriteToConsole("Trying to Auto Resolve Bannerlord Location");
            if (platform == Platform.Epic)
            {
                GameLocation = ResolveBannerlordEpicGamesInstallation();
                HandleGameLocationChange(platform);
            }

            if (platform == Platform.Steam)
            {
                GameLocation = ResolveBannerlordSteamInstallation();
                HandleGameLocationChange(platform);
            }

            if (platform == Platform.Xbox)
            {
                GameLocation = ResolveBannerlordEpicGamesInstallation();
                HandleGameLocationChange(platform);
            }
        }
        else
        {
            Config.LastPlatform = platform;
            WriteConfig();
        }

    }

    private async void HandleGameLocationChange(Platform platform)
    {
        if (GameLocation == null)
        {
            WriteToConsole("Bannerlord was not found in the location");
            Config.GameLocations[platform] = null;
            Config.LastPlatform = platform;
            WriteConfig();
        }
        else
        {
            WriteToConsole("Bannerlord was detected in the location");
            Config.GameLocations[platform] = GameLocation;
            Config.LastPlatform = platform;
            WriteConfig();
            if (!HashExist())
            {
                if (!Directory.Exists(Path.Combine(GameLocation.InstallationPath, "Modules/cRPG")))
                {
                    WriteToConsole("cRPG is not Installed, Click on Install Mod to Install");
                    IsCrpgInstalled = false;
                    IsGameUpToDate = false;
                    Config.IsGameUpToDate = false;
                }
                else
                {
                    WriteToConsole("Discovering Potential cRPG Installation");
                    await VerifyGameFilesAsync(true);
                }
            }
        }
    }

    [ObservableProperty]
    private bool isCrpgInstalled;

    public string UpdateButtoneText(bool value) => DetermineUpdateButtonText(value);

    private string DetermineUpdateButtonText(bool value)
    {
        if(value)
        {
            return "Update cRPG";
        }
        else
        {
            return "Install cRPG";
        }
    }

    partial void OnIsCrpgInstalledChanged(bool value) // Make sure this matches the property name's casing
    {
        OnPropertyChanged(nameof(UpdateButtoneText));
    }

    private bool WriteConfig()
    {
        return Config.WriteConfig(ProgramDataPath, ConfigFileName);
    }

    public bool HasWritePermissionOnConfigDir()
    {
        try
        {
            if (!Directory.Exists(ProgramDataPath))
            {
                Directory.CreateDirectory(ProgramDataPath);
            }

            List<string> lines = new()
            {
                "test",
            };
            File.WriteAllLines(Path.Combine(ProgramDataPath, "test.ini"), lines);
            File.Delete(Path.Combine(ProgramDataPath, "test.ini"));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // If an unauthorized access exception occurred, we don't have write permissions
            return false;
        }
        catch (IOException)
        {
            // Handle other IO exceptions if necessary
            return false;
        }
    }
    public void StartRoutine()
    {
        IsVerifying = false;
        IsUpdating = false;
        ApplySettings();
        Version = ReadTextFromResource("pack://application:,,,/launcherversion.txt");
        CheckNewVersion();

        VerifyGameFilesActionCommand.NotifyCanExecuteChanged();
        UpdateGameFilesCommand.NotifyCanExecuteChanged();

    }
    public void ApplySettings()
    {
        if (ReadConfig())
        {
            SelectedPlatform = Config.LastPlatform;
            GameLocation = Config.GameLocations.TryGetValue(SelectedPlatform, out var gameLocation) ? gameLocation : null;
            IsGameUpToDate = Config.IsGameUpToDate;
        }
    }

    public bool ReadConfig()
    {
        return Config.ReadConfig(ProgramDataPath, ConfigFileName);
    }
    private string ReadTextFromResource(string packUri)
    {
        Uri uri = new Uri(packUri, UriKind.RelativeOrAbsolute);
        StreamResourceInfo? resourceInfo = Application.GetResourceStream(uri);

        if (resourceInfo != null)
        {
            using (StreamReader reader = new StreamReader(resourceInfo.Stream))
            {
                return reader.ReadToEnd();
            }
        }
        return "Resource not found.";
    }

    private async void CheckNewVersion()
    {
        var onlineVersion = await OnlineLauncherVersion("https://c-rpg.eu/LauncherVersion.txt");
        if (onlineVersion == "failed")
        {
            return;
        }

        if (Version.Replace("\r", "").Replace("\n", "") != onlineVersion)
        {
            WriteToConsole($"This version is outdated please download version {onlineVersion}");
            WriteToConsole($"at https://c-rpg.eu/LauncherV3.exe");
        }
        else
        {
            WriteToConsole("Your Launcher is up to date");
        }
    }
    private async Task<string> OnlineLauncherVersion(string url)
    {
        using (HttpClient client = new HttpClient())
        {
            try
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var firstLine = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                return firstLine;
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., network errors)
                return "failed";
            }
        }
    }
}
