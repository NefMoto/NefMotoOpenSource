/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2017  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

Contact by Email: tony@nefariousmotorsports.com
*/

#if DEBUG
#define ENABLE_FAKE_USB_DEVICES
#endif


using ApplicationShared;
using Communication;
using FTD2XX_NET;
using Microsoft.Win32;
using Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Xml.Serialization;
using Path = System.IO.Path;

namespace ECUFlasher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application, INotifyPropertyChanged, IDataErrorInfo
    {
        protected string GetAppDataDirectory()
        {
            string appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string companyDirectory = appDataDirectory + "\\" + ECUFlasher.Properties.Resources.CompanyName;
			string applicationDirectory = companyDirectory + "\\" + ECUFlasher.Properties.Resources.ApplicationName;

            return applicationDirectory;
        }

        internal string GetApplicationName()
        {
            string appName = ECUFlasher.Properties.Resources.ApplicationName + " " + GetFullVersion();
            return appName;
        }

        internal string GetFullVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return !string.IsNullOrWhiteSpace(version)
                ? version.Trim()
                : "AssemblyInformationalVersion is not set";
        }

        internal string GetVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString();
            return !string.IsNullOrWhiteSpace(version)
                ? version.Trim()
                : "AssemblyVersion is not set";
        }

        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            //TODO: find a way to log an error about not being able to load an assembly

            return null;
        }

        public App()
        {
            try
            {
                mPropertyErrors = new Dictionary<string, string>();

                mLogFileDirectory = GetAppDataDirectory();
				mLogFileName =  Path.Combine ( mLogFileDirectory , ECUFlasher.Properties.Resources.ApplicationName + "Log.txt");
                CreateLogFile();

                DisplayStatusMessage("Opening " + GetApplicationName(), StatusMessageType.LOG);
                Shared.LogFallback.SetHandler(DisplayStatusMessage);

                // mFTDILibrary no longer needed - DeviceManager handles device enumeration

                Devices = new ObservableCollection<DeviceInfo>();

                //get the last used device (convert from legacy format if needed)
                var legacyDevice = ECUFlasher.Properties.Settings.Default.FTDIUSBDevice;
                if (legacyDevice != null)
                {
                    // Convert legacy ApplicationShared.FTDIDeviceInfo to Communication.FtdiDeviceInfo
                    var ftdiNode = new FTD2XX_NET.FTDI.FT_DEVICE_INFO_NODE
                    {
                        Description = legacyDevice.Description,
                        Flags = legacyDevice.Flags,
                        ID = legacyDevice.ID,
                        LocId = legacyDevice.LocId,
                        SerialNumber = legacyDevice.SerialNumber,
                        Type = legacyDevice.Type
                    };
                    SelectedDeviceInfo = new Communication.FtdiDeviceInfo(ftdiNode, legacyDevice.ChipID);
                }

                OnRefreshDevices();
            }
            catch(Exception e)
            {
                DisplayStatusMessage("Encountered exception while opening application: " + e.ToString(), StatusMessageType.LOG);
            }

            try
            {
				//trigger the creation of the comm interface
				DesiredProtocol = CommunicationInterface.Protocol.KWP2000;
            }
            catch(Exception e)
            {
                DisplayStatusMessage("Exception thrown while opening application: " + e.ToString(), StatusMessageType.LOG);
            }

            InitializeComponent();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DisplayStatusMessage("Opened " + GetApplicationName(), StatusMessageType.LOG);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                ECUFlasher.Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                try
                {
                    DisplayStatusMessage("Settings save failed on exit: " + ex.Message, StatusMessageType.LOG);
                }
                catch
                {
                    // Ignore if logging fails during shutdown
                }
            }

            DisplayStatusMessage("Closing " + GetApplicationName(), StatusMessageType.LOG);

            base.OnExit(e);
        }

        internal XmlSerializerNamespaces XMLNamespace
        {
            get
            {
                if (_XMLNamespace == null)
                {
                    _XMLNamespace = new XmlSerializerNamespaces();
                    _XMLNamespace.Add("", "");
                }

                return _XMLNamespace;
            }
        }
        private XmlSerializerNamespaces _XMLNamespace;

        public void SerializeToXML(Stream stream, object data)
        {
            new XmlSerializer(data.GetType()).Serialize(stream, data, XMLNamespace);
        }

        //get the error for idataerrorinfo
        public string Error
        {
            get
            {
                string error = null;

                foreach (string propertyName in mPropertyErrors.Keys)
                {
                    error = mPropertyErrors[propertyName];

                    if (error != null)
                    {
                        break;
                    }
                }

                return error;
            }
        }

        public string this[string columnName]
        {
            get
            {
                string error = null;

                if (mPropertyErrors.ContainsKey(columnName))
                {
                    error = mPropertyErrors[columnName];
                }

                return error;
            }
            private set
            {
                if (!mPropertyErrors.ContainsKey(columnName) || (value != mPropertyErrors[columnName]))
                {
                    mPropertyErrors[columnName] = value;
                    OnPropertyChanged(new PropertyChangedEventArgs(Binding.IndexerName));
                    OnPropertyChanged(new PropertyChangedEventArgs("Error"));
                }
            }
        }
        private Dictionary<string, string> mPropertyErrors;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
            }
        }

        private bool SelectedUSBDevicePredicate(DeviceInfo device)
        {
            if (device != null && SelectedDeviceInfo != null)
            {
                if ((SelectedDeviceInfo.Description != device.Description)
                    || (SelectedDeviceInfo.SerialNumber != device.SerialNumber)
                    || (SelectedDeviceInfo.Type != device.Type)
                    || (SelectedDeviceInfo.DeviceID != device.DeviceID))
                {
                    return false;
                }
            }

            return true;
        }

        private void DisplayApplicationStatusMessage(string message, StatusMessageType messageType)
        {
            if (message != null)
            {
                if (messageType == StatusMessageType.USER)
                {
                    mStatusTextBuilder.AppendLine(message);
                    OnPropertyChanged(new PropertyChangedEventArgs("StatusText"));
                }
            }
        }

        public string StatusText
        {
            get
            {
                return mStatusTextBuilder.ToString();
            }
        }
        private StringBuilder mStatusTextBuilder = new StringBuilder();


        public ReactiveCommand ClearStatusTextCommand
        {
            get
            {
                if (_ClearStatusTextCommand == null)
                {
                    _ClearStatusTextCommand = new ReactiveCommand(this.OnClearStatusText);
                    _ClearStatusTextCommand.Name = "Clear Status Text";
                    _ClearStatusTextCommand.Description = "Clear the status text. Will not clear the log file.";
                }

                return _ClearStatusTextCommand;
            }
        }
        private ReactiveCommand _ClearStatusTextCommand;

        private void OnClearStatusText()
        {
            mStatusTextBuilder.Length = 0;
            OnPropertyChanged(new PropertyChangedEventArgs("StatusText"));
        }

        internal UserPromptResult DisplayUserPrompt(string title, string message, UserPromptType promptType)
        {
            var button = MessageBoxButton.OKCancel;

            switch (promptType)
            {
				case UserPromptType.OK:
				{
					button = MessageBoxButton.OK;
					break;
				}
                case UserPromptType.OK_CANCEL:
                {
                    button = MessageBoxButton.OKCancel;
                    break;
                }
                case UserPromptType.YES_NO_CANCEL:
                {
                    button = MessageBoxButton.YesNoCancel;
                    break;
                }
                default:
                {
                    Debug.Fail("Unknown UserPromptType");
                    break;
                }
            }

            var owner = Application.Current?.MainWindow;
            var messageBoxResult = owner != null
                ? MessageBox.Show(owner, message, title, button)
                : MessageBox.Show(message, title, button);

            var result = UserPromptResult.NONE;

            switch (messageBoxResult)
            {
                case MessageBoxResult.None:
                {
                    result = UserPromptResult.NONE;
                    break;
                }
                case MessageBoxResult.OK:
                {
                    result = UserPromptResult.OK;
                    break;
                }
                case MessageBoxResult.Cancel:
                {
                    result = UserPromptResult.CANCEL;
                    break;
                }
                case MessageBoxResult.Yes:
                {
                    result = UserPromptResult.YES;
                    break;
                }
                case MessageBoxResult.No:
                {
                    result = UserPromptResult.NO;
                    break;
                }
                default:
                {
                    Debug.Fail("Unknown MessageBoxResult");
                    break;
                }
            }

            DisplayStatusMessage("User Prompt - Title: " + title + " Message: " + message + " Result: " + result, StatusMessageType.LOG);

            return result;
        }

        internal void DisplayStatusMessage(string message, StatusMessageType messageType)
        {
            if (message != null)
            {
#if DEBUG
                Debug.Print(message);
#else
                if ((messageType != StatusMessageType.DEV) && (messageType != StatusMessageType.DEV_USER))
#endif
				{
                    try
                    {
                        //display status message can be called from multiple threads
                        lock (mLogFileName)
                        {
                            string logFileEntry = DateTime.Now.ToString("dd/MMM/yyyy hh:mm:ss.fff") + ": " + messageType + ": " + message + Environment.NewLine;

                            //this will create the file if it doesn't exist
                            File.AppendAllText(mLogFileName, logFileEntry, Encoding.Default);
                            mLogFileSize += logFileEntry.Length;
                        }
                    }
                    catch
                    {
                    }

                    DisplayApplicationStatusMessage(message, messageType);
                }
            }
        }

        private void CreateLogFile()
        {
            try
            {
                //no touching the log file while we create it
                lock (mLogFileName)
                {
                    if (!Directory.Exists(mLogFileDirectory))
                    {
                        Directory.CreateDirectory(mLogFileDirectory);
                    }

                    mLogFileSize = 0;

                    if (File.Exists(mLogFileName))
                    {
                        FileInfo fileInfo = new FileInfo(mLogFileName);

                        mLogFileSize = fileInfo.Length;

                        TruncateLogFile();
                    }
                }

            }
            catch
            {
                DisplayStatusMessage("Failed to create log file.", StatusMessageType.USER);
            }
        }
        private long mLogFileSize = 0;
        private string mLogFileDirectory;
        private string mLogFileName;

        private void TruncateLogFile()
        {
            //no touching the log file while we are truncating it
            lock (mLogFileName)
            {
                const int MAX_FILE_SIZE_MEGS = 10;
                const int TRUNCATION_RATIO = 4;

				var timer = new Stopwatch();
				timer.Start();

                if (mLogFileSize > 1024 * 1024 * MAX_FILE_SIZE_MEGS)
                {
                    if (File.Exists(mLogFileName))
                    {
                        try
                        {
                            byte[] fileContents = File.ReadAllBytes(mLogFileName);
                            mLogFileSize = fileContents.LongLength;

                            int truncatedSize = fileContents.Length / TRUNCATION_RATIO;

                            if (truncatedSize > 0)
                            {
                                byte[] truncatedFileContents = new byte[fileContents.Length - truncatedSize];
                                Array.Copy(fileContents, truncatedSize, truncatedFileContents, 0, truncatedFileContents.Length);

                                File.WriteAllBytes(mLogFileName, truncatedFileContents);
                                mLogFileSize = truncatedFileContents.LongLength;

                                DisplayStatusMessage("Truncated log file because it was larger than " + MAX_FILE_SIZE_MEGS + " megabytes.", StatusMessageType.USER);
                            }
                            else
                            {
                                File.Delete(mLogFileName);
                                mLogFileSize = 0;
                                DisplayStatusMessage("Deleted log file because it was larger than " + MAX_FILE_SIZE_MEGS + " megabytes.", StatusMessageType.USER);
                            }

							DisplayStatusMessage("Log file truncation took: " + timer.Elapsed, StatusMessageType.LOG);
                        }
                        catch
                        {
                            DisplayStatusMessage("Failed to truncate log file.", StatusMessageType.USER);
                        }
                    }
                }

				timer.Stop();
            }
        }

        public bool OperationInProgress
        {
            get { return _OperationInProgress; }
            set
            {
                if (_OperationInProgress != value)
                {
                    _OperationInProgress = value;

                    if (_OperationInProgress)
                    {
                        TruncateLogFile();
                        PreventSleepMode();
                    }
                    else
                    {
                        RestoreSleepMode();
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("OperationInProgress"));
                }
            }
        }
        private bool _OperationInProgress = false;

        public Operation CurrentOperation
        {
            get { return _CurrentOperation; }
            set
            {
                if (_CurrentOperation != value)
                {
                    Debug.Assert((_CurrentOperation == null) || (value == null) || (!_CurrentOperation.IsRunning), "overwriting current operation that is running");

                    _CurrentOperation = value;

                    if (_CurrentOperation != null)
                    {
                        _CurrentOperation.PercentCompletedEvent += delegate(float percentComplete) { PercentOperationComplete = percentComplete; };
                    }
                    else
                    {
                        OperationInProgress = false;
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("CurrentOperation"));
                }
            }
        }
        private Operation _CurrentOperation = null;

        public float PercentOperationComplete
        {
            get { return mPercentComplete; }
            set
            {
                if (mPercentComplete != value)
                {
                    float oldPercentComplete = mPercentComplete;
                    mPercentComplete = value;

                    if (OperationInProgress && (Math.Floor(PercentOperationComplete) > Math.Floor(oldPercentComplete)))
                    {
                        DisplayStatusMessage(PercentOperationComplete.ToString("f0") + "% complete.", StatusMessageType.USER);
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("PercentOperationComplete"));
                }
            }
        }
        private float mPercentComplete;

        #region OpenLogFileCommand
        // NOTE: Using notepad.exe is simpler and sufficient for viewing log files. A custom log file pager
        // would require building a WPF window with TextBox/TextBlock, file reading logic, scroll handling,
        // and potentially virtualized rendering for large files. Notepad handles all of this already and
        // is familiar to users. Only consider a custom pager if you need features like auto-refresh,
        // filtering, search highlighting, or real-time tailing - otherwise notepad is the better choice.
        public ReactiveCommand OpenLogFileCommand
        {
            get
            {
                if (_OpenLogFileCommand == null)
                {
                    _OpenLogFileCommand = new ReactiveCommand(delegate()
                    {
                        try
                        {
                            if (File.Exists(mLogFileName))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "notepad.exe",
                                    Arguments = "\"" + mLogFileName + "\"",
                                    UseShellExecute = true
                                });
                            }
                            else
                            {
                                DisplayStatusMessage("Log file does not exist: " + mLogFileName, StatusMessageType.USER);
                            }
                        }
                        catch (Exception ex)
                        {
                            DisplayStatusMessage("Failed to open log file: " + ex.Message, StatusMessageType.USER);
                        }
                    });
                    _OpenLogFileCommand.Name = "Open Log File";
                    _OpenLogFileCommand.Description = "Open the log file";
                }

                return _OpenLogFileCommand;
            }
        }
        private ReactiveCommand _OpenLogFileCommand;
        #endregion

        #region OpenLogFileLocationCommand
        public ReactiveCommand OpenLogFileLocationCommand
        {
            get
            {
                if (_OpenLogFileLocationCommand == null)
                {
                    _OpenLogFileLocationCommand = new ReactiveCommand(delegate() { Process.Start(mLogFileDirectory); });
                    _OpenLogFileLocationCommand.Name = "Open Log File Location";
                    _OpenLogFileLocationCommand.Description = "Open the log file location";
                }

                return _OpenLogFileLocationCommand;
            }
        }
        private ReactiveCommand _OpenLogFileLocationCommand;
        #endregion

        #region ClearLogFileCommand
        public ReactiveCommand ClearLogFileCommand
        {
            get
            {
                if (_ClearLogFileCommand == null)
                {
                    _ClearLogFileCommand = new ReactiveCommand(this.OnClearLogFile);
                    _ClearLogFileCommand.Name = "Clear Log File";
                    _ClearLogFileCommand.Description = "Clear the contents of the log file.";
                }

                return _ClearLogFileCommand;
            }
        }
        private ReactiveCommand _ClearLogFileCommand;

        private void OnClearLogFile()
        {
            //no touching the log file while we are clearing it
            lock (mLogFileName)
            {
                File.Delete(mLogFileName);
                mLogFileSize = 0;
            }
        }
        #endregion

        #region OpenParameterCommand
        public ReactiveCommand OpenParameterCommand
        {
            get
            {
                if (_OpenParameterCommand == null)
                {
                    _OpenParameterCommand = new ReactiveCommand(delegate(object commandParam) {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = (string)commandParam,
                            UseShellExecute = true // Required for .NET Core/.NET 5+
                        });
                    });
                }

                return _OpenParameterCommand;
            }
        }
        private ReactiveCommand _OpenParameterCommand;
        #endregion

        #region ShowAboutDialogCommand
        public ReactiveCommand ShowAboutDialogCommand
        {
            get
            {
                if (_ShowAboutDialogCommand == null)
                {
                    _ShowAboutDialogCommand = new ReactiveCommand(delegate() {
                        var aboutDialog = new AboutDialog
                        {
                            Owner = Application.Current.MainWindow
                        };
                        aboutDialog.ShowDialog();
                    });
                    _ShowAboutDialogCommand.Name = "About";
                    _ShowAboutDialogCommand.Description = "Show information about NefMotoECUFlasher";
                }

                return _ShowAboutDialogCommand;
            }
        }
        private ReactiveCommand _ShowAboutDialogCommand;
        #endregion

        #region ExitCommand
        public ReactiveCommand ExitCommand
        {
            get
            {
                if (_ExitCommand == null)
                {
                    _ExitCommand = new ReactiveCommand(delegate() {
                        Application.Current.Shutdown();
                    });
                    _ExitCommand.Name = "Exit";
                    _ExitCommand.Description = "Exit the application";
                }

                return _ExitCommand;
            }
        }
        private ReactiveCommand _ExitCommand;
        #endregion

        #region CancelCurrentOperationCommand
        public ReactiveCommand CancelCurrentOperationCommand
        {
            get
            {
                if (_CancelCurrentOperationCommand == null)
                {
                    _CancelCurrentOperationCommand = new ReactiveCommand(this.OnCancelCurrentOperation);
                    _CancelCurrentOperationCommand.Name = "Cancel Current Operation";
                    _CancelCurrentOperationCommand.Description = "Cancels the currently running operation";
                    _CancelCurrentOperationCommand.AddWatchedProperty(this, "OperationInProgress");

                    _CancelCurrentOperationCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (!OperationInProgress)
                        {
                            reasonsDisabled.Add("No operation in progress");
                            result = false;
                        }

                        return result;
                    };
                }

                return _CancelCurrentOperationCommand;
            }
        }
        private ReactiveCommand _CancelCurrentOperationCommand;

        private void OnCancelCurrentOperation()
        {
            if ((CurrentOperation != null) && CurrentOperation.IsRunning)
            {
                var result = DisplayUserPrompt("Confirm Cancel Operation", "Press OK to cancel the current operation, or Cancel otherwise.", UserPromptType.OK_CANCEL);

                if (result == UserPromptResult.OK)
                {
                    DisplayStatusMessage("Cancelling current operation.", StatusMessageType.USER);

                    CurrentOperation.Abort();

                    //set these incase the operation completed handler doesn't clean up properly
                    OperationInProgress = false;
                }
            }
        }
        #endregion

        #region RefreshDevicesCommand
        public ReactiveCommand RefreshDevicesCommand
        {
            get
            {
                if (_RefreshDevicesCommand == null)
                {
                    _RefreshDevicesCommand = new ReactiveCommand(this.OnRefreshDevices);
                    _RefreshDevicesCommand.Name = "Refresh Devices";
                    _RefreshDevicesCommand.Description = "Refresh the connected USB devices";
                    _RefreshDevicesCommand.AddWatchedProperty(CommInterface, "ConnectionStatus");

                    _RefreshDevicesCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (CommInterface.ConnectionStatus != CommunicationInterface.ConnectionStatusType.CommunicationTerminated)
                        {
                            reasonsDisabled.Add("Connection is currently open");
                            result = false;
                        }

                        return result;
                    };
                }

                return _RefreshDevicesCommand;
            }
        }
        private ReactiveCommand _RefreshDevicesCommand;

        private void OnRefreshDevices()
        {
            RefreshDevices();

            DeviceInfo matchedInfoInList = Devices.FirstOrDefault(this.SelectedUSBDevicePredicate);
            if (matchedInfoInList == null)
            {
                //couldn't find the the selected device in the list, try to use the first one in the list
                SelectedDeviceInfo = Devices.FirstOrDefault();
            }
            else
            {
                //use the one in the list
                SelectedDeviceInfo = matchedInfoInList;
            }

            CollectionViewSource.GetDefaultView(Devices).MoveCurrentToFirst();
        }
        #endregion
        public DeviceInfo SelectedDeviceInfo
        {
            get
            {
                return _SelectedDeviceInfo;
            }
            set
            {
                if (_SelectedDeviceInfo != value)
                {
                    _SelectedDeviceInfo = value;

                    if (CommInterface != null)
                    {
                        CommInterface.SelectedDeviceInfo = _SelectedDeviceInfo;
                    }

                    string error = null;

                    if (_SelectedDeviceInfo == null)
                    {
                        error = "No device selected";
                    }
                    else if (!Devices.Contains(_SelectedDeviceInfo))
                    {
                        error = "Selected device is not connected";
                    }

                    if (error == null && _SelectedDeviceInfo is FtdiDeviceInfo ftdiInfo)
                    {
                        // Convert to legacy format for Settings serialization
                        var legacyDevice = new ApplicationShared.FTDIDeviceInfo(ftdiInfo.FtdiNode, 0, ftdiInfo.ChipID);
                        ECUFlasher.Properties.Settings.Default.FTDIUSBDevice = legacyDevice;
                    }
                    else if (error == null)
                    {
                        ECUFlasher.Properties.Settings.Default.FTDIUSBDevice = null;
                    }

                    this["SelectedDeviceInfo"] = error;

                    OnPropertyChanged(new PropertyChangedEventArgs("SelectedDeviceInfo"));
                }
            }
        }
        private DeviceInfo _SelectedDeviceInfo;

        public CommunicationInterface.Protocol DesiredProtocol
        {
            get
            {
                if (CommInterfaceViewModel == null)
                {
                    DesiredProtocol = CommunicationInterface.Protocol.KWP2000;
                }

                return CommInterfaceViewModel.CommInterface.CurrentProtocol;
            }
            set
            {
                if ((CommInterfaceViewModel == null) || (CommInterfaceViewModel.CommInterface == null) || (CommInterfaceViewModel.CommInterface.CurrentProtocol != value))
                {
                    CommunicationInterface_ViewModel newViewModel = null;

                    switch (value)
                    {
                        case CommunicationInterface.Protocol.KWP2000:
                            {
                                newViewModel = new KWP2000Interface_ViewModel();
                                break;
                            }
                        case CommunicationInterface.Protocol.BootMode:
                            {
                                newViewModel = new BootstrapInterface_ViewModel();
                                break;
                            }
                    }

                    //TODO: previously written code doesn't handle CommInterface being NULL

                    CommInterfaceViewModel = newViewModel;//done this way to only cause the change notification to happen once

                    if (CommInterface != null)
                    {
                        CommInterface.SelectedDeviceInfo = SelectedDeviceInfo;
                        CommInterfaceViewModel.FailedToStartConnectingEvent += OnFailedToStartConnecting;
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("DesiredProtocol"));
                }
            }
        }

        public CommunicationInterface_ViewModel CommInterfaceViewModel
        {
            get
            {
                return _CommInterfaceViewModel;
            }
            private set
            {
                if (_CommInterfaceViewModel != value)
                {
                    _CommInterfaceViewModel = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("CommInterfaceViewModel"));
                    OnPropertyChanged(new PropertyChangedEventArgs("CommInterface"));
                }
            }
        }
        private CommunicationInterface_ViewModel _CommInterfaceViewModel;

        public CommunicationInterface CommInterface
        {
            get
            {
                if (CommInterfaceViewModel != null)
                {
                    return CommInterfaceViewModel.CommInterface;
                }

                return null;
            }
        }

        public void OnFailedToStartConnecting()
        {
            //if we failed to connect, then maybe the device isn't valid
            // Refresh devices but preserve the current selection if it still exists
            OnRefreshDevices();
        }

        private void PreventSleepMode()
        {
            DisplayStatusMessage("Disabling Windows sleep mode.", StatusMessageType.LOG);

            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS | NativeMethods.ES_AWAYMODE_REQUIRED | NativeMethods.ES_SYSTEM_REQUIRED);
        }

        private void RestoreSleepMode()
        {
            DisplayStatusMessage("Restoring Windows sleep mode.", StatusMessageType.LOG);

            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        }

        private void RefreshDevices()
        {
            Devices.Clear();

            // Set up logging for DeviceManager enumeration
            Communication.DeviceManager.LogMessage = DisplayStatusMessage;

            try
            {
                // Use DeviceManager to enumerate all devices and add them to the collection
                foreach (var deviceInfo in Communication.DeviceManager.EnumerateAllDevices())
                {
                    Devices.Add(deviceInfo);
                }
            }
            catch (Exception ex)
            {
                DisplayStatusMessage($"Exception during device enumeration: {ex.GetType().Name}: {ex.Message}", StatusMessageType.USER);
                DisplayStatusMessage($"Stack trace: {ex.StackTrace}", StatusMessageType.LOG);
            }

            #region fakeDevices
#if ENABLE_FAKE_USB_DEVICES
            if ((Devices.Count == 0) && (DateTime.Now.Second % 3 == 0))
            {
                FTDI.FT_DEVICE_INFO_NODE tempInfo0 = new FTDI.FT_DEVICE_INFO_NODE();
                tempInfo0.Description = "Debug Placeholder FTDI Device 0";
                tempInfo0.ID = 1000;
                tempInfo0.SerialNumber = "SerialNum 1000";
                tempInfo0.Type = FTDI.FT_DEVICE.FT_DEVICE_232R;
                Devices.Add(new Communication.FtdiDeviceInfo(tempInfo0, 0x01010101));

                FTDI.FT_DEVICE_INFO_NODE tempInfo1 = new FTDI.FT_DEVICE_INFO_NODE();
                tempInfo1.Description = "Debug Placeholder FTDI Device 1";
                tempInfo1.ID = 1001;
                tempInfo1.SerialNumber = "SerialNum 1001";
                tempInfo1.Type = FTDI.FT_DEVICE.FT_DEVICE_UNKNOWN;
                Devices.Add(new Communication.FtdiDeviceInfo(tempInfo1, 0x00000000));

                FTDI.FT_DEVICE_INFO_NODE tempInfo2 = new FTDI.FT_DEVICE_INFO_NODE();
                tempInfo2.Description = "Debug Placeholder FTDI Device 2";
                tempInfo2.ID = 1002;
                tempInfo2.SerialNumber = "SerialNum 1002";
                tempInfo2.Type = FTDI.FT_DEVICE.FT_DEVICE_BM;
                Devices.Add(new Communication.FtdiDeviceInfo(tempInfo2, 0x01010101));
            }
#endif
            #endregion
        }

        public ObservableCollection<DeviceInfo> Devices { get; private set; }
    }
}
