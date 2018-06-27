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

using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Xml.Serialization;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Communication;
using Shared;
using ApplicationShared;
using FTD2XX_NET;

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
			string appName = ECUFlasher.Properties.Resources.ApplicationName + " " + Assembly.GetExecutingAssembly().GetName().Version.ToString();			
#if DEBUG
			appName = "DEBUG " + appName + " DEBUG";
#endif
			return appName;
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
				mLogFileName = mLogFileDirectory + "\\" + ECUFlasher.Properties.Resources.ApplicationName + "Log.txt";
                CreateLogFile();

                DisplayStatusMessage("Opening " + GetApplicationName(), StatusMessageType.LOG);

                mFTDILibrary = new FTDI(delegate(string message) { this.DisplayStatusMessage("FTDI Error: " + message, StatusMessageType.USER); },
                                        delegate(string message) { this.DisplayStatusMessage("FTDI Warning: " + message, StatusMessageType.LOG); });

                FTDIDevices = new ObservableCollection<FTDIDeviceInfo>();                

                //get the last used device
                SelectedDeviceInfo = ECUFlasher.Properties.Settings.Default.FTDIUSBDevice;

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
            ECUFlasher.Properties.Settings.Default.Save();

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

        private bool SelectedUSBDevicePredicate(FTDIDeviceInfo device)
        {
            if (device != null)
            {
                if (SelectedDeviceInfo != null)
                {
                    if ((SelectedDeviceInfo.Description != device.Description)
                        || (SelectedDeviceInfo.SerialNumber != device.SerialNumber)
                        || (SelectedDeviceInfo.Type != device.Type)
                        || (SelectedDeviceInfo.ID != device.ID))
                    {
                        return false;
                    }
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

            var messageBoxResult = MessageBox.Show(message, title, button);

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
        public ReactiveCommand OpenLogFileCommand 
        {
            get
            {
                if (_OpenLogFileCommand == null)
                {
                    _OpenLogFileCommand = new ReactiveCommand(delegate() { Process.Start(mLogFileName); });
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
                    _OpenParameterCommand = new ReactiveCommand(delegate(object commandParam) { Process.Start((string)commandParam); });
                }

                return _OpenParameterCommand;
            }
        }
        private ReactiveCommand _OpenParameterCommand;
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
                    _RefreshDevicesCommand.Description = "Refresh the connected FTDI USB devices";
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

            FTDIDeviceInfo matchedInfoInList = FTDIDevices.FirstOrDefault(this.SelectedUSBDevicePredicate);
            if (matchedInfoInList == null)
            {
                //couldn't find the the selected device in the list, try to use the first one in the list
                SelectedDeviceInfo = FTDIDevices.FirstOrDefault();
            }
            else
            {
                //use the one in the list
                SelectedDeviceInfo = matchedInfoInList;
            }

            CollectionViewSource.GetDefaultView(FTDIDevices).MoveCurrentToFirst();
        }
        #endregion

        #region SaveDeviceInfoCommand
        public ReactiveCommand SaveDeviceInfoCommand
        {
            get
            {
                if (_SaveDeviceInfoCommand == null)
                {
                    _SaveDeviceInfoCommand = new ReactiveCommand(this.OnSaveDeviceInfo);
                    _SaveDeviceInfoCommand.Name = "Save Device Info to File";
                    _SaveDeviceInfoCommand.Description = "Save the FTDI device info of currently connected devices to a file";
                    _SaveDeviceInfoCommand.AddWatchedCollection(this, "FTDIDevices", FTDIDevices);

                    _SaveDeviceInfoCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (!FTDIDevices.Any())
                        {
                            reasonsDisabled.Add("There must be at least one FTDI device connected");
                            result = false;
                        }

                        return result;
                    };
                }

                return _SaveDeviceInfoCommand;
            }
        }
        private ReactiveCommand _SaveDeviceInfoCommand;

        private void OnSaveDeviceInfo()
        {          
            string deviceSaveLocation = ECUFlasher.Properties.Settings.Default.SaveDeviceInfoLocation;

            if (string.IsNullOrEmpty(deviceSaveLocation))
            {
                deviceSaveLocation = Directory.GetCurrentDirectory();
            }

            var dialog = new SaveFileDialog();
			dialog.DefaultExt = FTDIDevicesFile.EXT;//gets long extensions to work properly when they are added to a filename when saved
			dialog.Filter = FTDIDevicesFile.FILTER;
            dialog.InitialDirectory = deviceSaveLocation;
            dialog.OverwritePrompt = true;
            dialog.Title = "Select Where to Save Device Info File";

            if (dialog.ShowDialog() == true)
            {
                bool successfullySavedInfo = false;

				//replace .xml with .FTDIDevices.xml
				var actualFileName = ExtensionFixer.SwitchToLongExtension(dialog.FileName, FTDIDevicesFile.SHORT_EXT, FTDIDevicesFile.EXT);

                try
                {
                    using (var fStream = new FileStream(actualFileName, FileMode.Create, FileAccess.Write, FileShare.Write))
                    {
						var devicesFile = new FTDIDevicesFile();
						devicesFile.Devices.AddRange(FTDIDevices);

						SerializeToXML(fStream, devicesFile);

                        successfullySavedInfo = true;
                    }
                }
                catch(Exception e)
                {
					DisplayStatusMessage("Encountered exception while saving device info to file: " + e.Message, StatusMessageType.USER);
                    successfullySavedInfo = false;
                }                
                
                if (!String.IsNullOrEmpty(dialog.FileName))
                {
                    DirectoryInfo dirInfo = Directory.GetParent(dialog.FileName);

                    if (dirInfo != null)
                    {
                        ECUFlasher.Properties.Settings.Default.SaveDeviceInfoLocation = dirInfo.FullName;
                    }
                }

                if (successfullySavedInfo)
                {
                    DisplayStatusMessage("Successfully saved device info to file.", StatusMessageType.USER);
                }
                else
                {
                    DisplayStatusMessage("Failed to save device info to file.", StatusMessageType.USER);
                }
            }
        }

        #endregion

        #region DoDevicesSupportLicensing
        public ReactiveCommand DoDevicesSupportLicensing
        {
            get
            {
                if (_DoDevicesSupportLicensing == null)
                {
                    _DoDevicesSupportLicensing = new ReactiveCommand(this.OnDoDevicesSupportLicensing);
                    _DoDevicesSupportLicensing.Name = "Check if FTDI Devices Support Licensing";
                    _DoDevicesSupportLicensing.Description = "Checks if the connected FTDI devices support premium feature licensing";
                    _DoDevicesSupportLicensing.AddWatchedProperty(CommInterface, "ConnectionStatus");
                    _DoDevicesSupportLicensing.AddWatchedCollection(this, "FTDIDevices", FTDIDevices);

                    _DoDevicesSupportLicensing.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool canExecute = true;

                        //getting the ChipID fails when we have a connection open, so easiest thing is to disable this
                        if (CommInterface.IsConnectionOpen())
                        {
                            canExecute = false;
                            reasonsDisabled.Add("Cannot check if FTDI devices support licensing while connected to ECU");
                        }

                        if(FTDIDevices.Count <= 0)
                        {
                            canExecute = false;
                            reasonsDisabled.Add("There must be at least one FTDI device connected");
                        }

                        return canExecute;
                    };
                }

                return _DoDevicesSupportLicensing;
            }
        }
        private ReactiveCommand _DoDevicesSupportLicensing;
        #endregion

        private void OnDoDevicesSupportLicensing()
        {
            DisplayStatusMessage("Checking if connected FTDI devices support premium feature licensing:", StatusMessageType.USER);

            if(FTDIDevices.Count > 0)
            {
                foreach (FTDIDeviceInfo device in FTDIDevices)
                {
                    if (device.ChipID != 0)
                    {
                        DisplayStatusMessage("Device " + device.Index + " supports premium feature licensing.", StatusMessageType.USER);
                    }
                    else
                    {
                        DisplayStatusMessage("Device " + device.Index + " does NOT support premium feature licensing.", StatusMessageType.USER);
                    }
                }
            }
            else
            {
                DisplayStatusMessage("There are no FTDI devices connected to check.", StatusMessageType.USER);
            }
        }

        public FTDIDeviceInfo SelectedDeviceInfo
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
                        error = "No FTDI device selected";
                    }
                    else if (!FTDIDevices.Contains(_SelectedDeviceInfo))
                    {
                        error = "Selected FTDI device is not connected";
                    }

                    if (error == null)
                    {
                        ECUFlasher.Properties.Settings.Default.FTDIUSBDevice = _SelectedDeviceInfo;
                    }

                    this["SelectedDeviceInfo"] = error;

                    OnPropertyChanged(new PropertyChangedEventArgs("SelectedDeviceInfo"));
                }
            }
        }
        private FTDIDeviceInfo _SelectedDeviceInfo;

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
            //if we failed to connect, then maybe the FTDI device isn't valid
            RefreshDevices();
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
            FTDIDevices.Clear();

            var newDevices = mFTDILibrary.EnumerateFTDIDevices();

            uint index = 0;
            foreach (var node in newDevices)
            {
                if (node.Type != FTDI.FT_DEVICE.FT_DEVICE_UNKNOWN)
                {
                    uint chipID = 0;
                    if (FTDI.IsFTDChipIDDLLLoaded())
                    {
                        mFTDILibrary.GetChipIDFromDeviceIndex(index, out chipID);
                    }

                    FTDIDevices.Add(new FTDIDeviceInfo(node, index, chipID));
                    index++;
                }
            }

            #region fakeDevices
#if ENABLE_FAKE_USB_DEVICES
            if ((FTDIDevices.Count == 0) && (DateTime.Now.Second % 3 == 0))
            {
                FTDI.FT_DEVICE_INFO_NODE tempInfo0 = new FTDI.FT_DEVICE_INFO_NODE();
                tempInfo0.Description = "Debug Placeholder FTDI Device 0";
                tempInfo0.ID = 1000;
                tempInfo0.SerialNumber = "SerialNum 1000";
                tempInfo0.Type = FTDI.FT_DEVICE.FT_DEVICE_232R;
                FTDIDevices.Add(new FTDIDeviceInfo(tempInfo0, index++, 0x01010101));

                FTDI.FT_DEVICE_INFO_NODE tempInfo1 = new FTDI.FT_DEVICE_INFO_NODE();
                tempInfo1.Description = "Debug Placeholder FTDI Device 1";
                tempInfo1.ID = 1001;
                tempInfo1.SerialNumber = "SerialNum 1001";
                tempInfo1.Type = FTDI.FT_DEVICE.FT_DEVICE_UNKNOWN;
                FTDIDevices.Add(new FTDIDeviceInfo(tempInfo1, index++, 0x00000000));

                FTDI.FT_DEVICE_INFO_NODE tempInfo2 = new FTDI.FT_DEVICE_INFO_NODE();
                tempInfo2.Description = "Debug Placeholder FTDI Device 2";
                tempInfo2.ID = 1002;
                tempInfo2.SerialNumber = "SerialNum 1002";
                tempInfo2.Type = FTDI.FT_DEVICE.FT_DEVICE_BM;
                FTDIDevices.Add(new FTDIDeviceInfo(tempInfo2, index++, 0x01010101));
            }
#endif
            #endregion
        }

        private FTDI mFTDILibrary;
        public ObservableCollection<FTDIDeviceInfo> FTDIDevices { get; private set; }        
    }
}
