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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Globalization;
using Microsoft.Win32;

using Shared;
using Communication;
using ApplicationShared;

namespace ECUFlasher
{
	public partial class ECUInfoControl : BaseUserControl
	{
		public ECUInfoControl()
		{
			InitializeComponent();
		}

		public ICommand SaveDTCsCommand
		{
			get
			{
				if (_SaveDTCsCommand == null)
				{
					_SaveDTCsCommand = new ReactiveCommand(this.OnSaveDTCs);
					_SaveDTCsCommand.Name = "Save DTCs";
					_SaveDTCsCommand.Description = "Save DTCs to a file";

					_SaveDTCsCommand.AddWatchedCollection(this, "ECUDTCs", ECUDTCs);

					_SaveDTCsCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						bool result = true;

						if (ECUDTCs.Count <= 0)
						{
							reasonsDisabled.Add("There are no DTCs to save");
							result = false;
						}

						return result;
					};
				}

				return _SaveDTCsCommand;
			}
		}
		private ReactiveCommand _SaveDTCsCommand;

		public ICommand LoadDTCsCommand
		{
			get
			{
				if (_LoadDTCsCommand == null)
				{
					_LoadDTCsCommand = new ReactiveCommand(this.OnLoadDTCs);
					_LoadDTCsCommand.Name = "Load DTCs";
					_LoadDTCsCommand.Description = "Load DTCs from a file";
				}

				return _LoadDTCsCommand;
			}
		}
		private ReactiveCommand _LoadDTCsCommand;

		private void OnSaveDTCs()
		{
			App.DisplayStatusMessage("Starting saving of DTCs.", StatusMessageType.USER);

            var dialog = new SaveFileDialog();
			dialog.DefaultExt = DTCsFile.EXT;//gets long extensions to work properly when they are added to a filename when saved
			dialog.Filter = DTCsFile.FILTER;
			dialog.OverwritePrompt = true;
            dialog.Title = "Select Location to Save DTCs File";
			dialog.InitialDirectory = Directory.GetCurrentDirectory();//TODO: remember last used directory

			if (dialog.ShowDialog() == true)
			{
				//replace .xml with .DTCs.xml
				var actualFileName = ExtensionFixer.SwitchToLongExtension(dialog.FileName, DTCsFile.SHORT_EXT, DTCsFile.EXT);

				try
				{
					using (var fileStream = new FileStream(actualFileName, FileMode.Create, FileAccess.Write))
					{						
						var dtcsFile = new DTCsFile();

						foreach(var dtc in ECUDTCs)
						{
							dtcsFile.DTCs.Add(dtc);
						}

						App.SerializeToXML(fileStream, dtcsFile);
						App.DisplayStatusMessage("Successfully saved DTCs to file.", StatusMessageType.USER);
					}
				}
				catch
				{
					App.DisplayStatusMessage("Failed to save DTCs to file.", StatusMessageType.USER);
				}
			}
			else
			{
				App.DisplayStatusMessage("Cancelling saving of DTCs.", StatusMessageType.USER);
			}
		}

		private void OnLoadDTCs()
		{
			App.DisplayStatusMessage("Starting loading of DTCs.", StatusMessageType.USER);

			var dialog = new OpenFileDialog();
			dialog.Filter = DTCsFile.FILTER;
			dialog.CheckFileExists = true;
			dialog.CheckPathExists = true;
			dialog.Title = "Select DTCs File to Load";
			dialog.InitialDirectory = Directory.GetCurrentDirectory();//TODO: remember last used directory

			if (dialog.ShowDialog() == true)
			{
				ECUDTCs.Clear();

				try
				{
					using (var fileSteam = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read))
					{
						var formatter = new XmlSerializer(typeof(DTCsFile));
						var dtcFile = (DTCsFile)formatter.Deserialize(fileSteam);

						foreach(var dtc in dtcFile.DTCs)
						{
							ECUDTCs.Add(dtc);
						}

						App.DisplayStatusMessage("Successfully loaded DTCs from file.", StatusMessageType.USER);
					}
				}
				catch(Exception e)
				{
					App.DisplayStatusMessage("Failed to load DTCs from file: " + e.Message, StatusMessageType.USER);
				}
			}
			else
			{
				App.DisplayStatusMessage("Cancelling loading of DTCs.", StatusMessageType.USER);
			}
		}

		public ICommand ReadDTCsCommand
		{
			get
			{
				if (_ReadDTCsCommand == null)
				{
					_ReadDTCsCommand = new ReactiveCommand(this.ReadAllDTCs);
					_ReadDTCsCommand.Name = "Read DTCs";
					_ReadDTCsCommand.Description = "Read the DTCs stored in the ECU";

					if (App != null)
					{
						_ReadDTCsCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
						_ReadDTCsCommand.AddWatchedProperty(App, "OperationInProgress");
						_ReadDTCsCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
					}

					_ReadDTCsCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (!App.CommInterface.IsConnected())
						{
							reasonsDisabled.Add("Not connected to ECU");
							result = false;
						}

						if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000)
						{
							reasonsDisabled.Add("Not connected with KWP2000 protocol");
							result = false;
						}

						if (App.OperationInProgress)
						{
							reasonsDisabled.Add("Another operation is in progress");
							result = false;
						}

						return result;
					};
				}

				return _ReadDTCsCommand;
			}
		}
		private ReactiveCommand _ReadDTCsCommand;

		public ICommand ClearDTCsCommand
		{
			get
			{
				if (_ClearDTCsCommand == null)
				{
					_ClearDTCsCommand = new ReactiveCommand(this.OnClearDTCs);
					_ClearDTCsCommand.Name = "Clear DTCs";
					_ClearDTCsCommand.Description = "Clear the DTCs stored in the ECU";

					if (App != null)
					{
						_ClearDTCsCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
						_ClearDTCsCommand.AddWatchedProperty(App, "OperationInProgress");
						_ClearDTCsCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
					}

					_ClearDTCsCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (!App.CommInterface.IsConnected())
						{
							reasonsDisabled.Add("Not connected to ECU");
							result = false;
						}

						if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000)
						{
							reasonsDisabled.Add("Not connected with KWP2000 protocol");
							result = false;
						}

						if (App.OperationInProgress)
						{
							reasonsDisabled.Add("Another operation is in progress");
							result = false;
						}

						return result;
					};
				}

				return _ClearDTCsCommand;
			}
		}
		private ReactiveCommand _ClearDTCsCommand;

		private void OnClearDTCs()
		{
			string confirmationMessage = "Are you sure you want to clear diagnostic information?";
			confirmationMessage += "\n\nClick OK to confirm, otherwise Cancel.";

			if(App.DisplayUserPrompt("Confirm Clear Diagnostic Information", confirmationMessage, UserPromptType.OK_CANCEL) == UserPromptResult.OK)			
			{
				ClearDiagnosticInformation();
			}
		}

		public ICommand SaveInfoCommand
		{
			get
			{
				if (_SaveInfoCommand == null)
				{
					_SaveInfoCommand = new ReactiveCommand(this.OnSaveInfo);
					_SaveInfoCommand.Name = "Save ECU Info";
					_SaveInfoCommand.Description = "Save ECU info to a file";

					_SaveInfoCommand.AddWatchedCollection(this, "ECUInfo", ECUInfo);

					_SaveInfoCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						bool result = true;

						if (ECUInfo.Count <= 0)
						{
							reasonsDisabled.Add("There is no info to save");
							result = false;
						}

						return result;
					};
				}

				return _SaveInfoCommand;
			}
		}
		private ReactiveCommand _SaveInfoCommand;

		public ICommand LoadInfoCommand
		{
			get
			{
				if (_LoadInfoCommand == null)
				{
					_LoadInfoCommand = new ReactiveCommand(this.OnLoadInfo);
					_LoadInfoCommand.Name = "Load ECU Info";
					_LoadInfoCommand.Description = "Load ECU info from a file";
				}

				return _LoadInfoCommand;
			}
		}
		private ReactiveCommand _LoadInfoCommand;

		public ICommand RemoveInfoEntryCommand
		{
			get
			{
				if(_RemoveInfoEntryCommand == null)
				{
					_RemoveInfoEntryCommand = new ReactiveCommand();
					_RemoveInfoEntryCommand.Name = "Remove Entry";
					_RemoveInfoEntryCommand.Description = "Remove selected entries from the list";
					_RemoveInfoEntryCommand.ExecuteMethod = delegate(object param)
					{
						if (param is KWP2000IdentificationOptionValue)
						{
							ECUInfo.Remove(param as KWP2000IdentificationOptionValue);
						}
						else if (param is IEnumerable)
						{
							var collection = param as IEnumerable;
							var collectionCopy = new List<KWP2000IdentificationOptionValue>();

							//need to copy before we change the collection
							foreach (var variable in collection)
							{
								collectionCopy.Add(variable as KWP2000IdentificationOptionValue);
							}

							foreach (var entry in collectionCopy)
							{
								ECUInfo.Remove(entry);
							}
						}
					};
				}

				return _RemoveInfoEntryCommand;
			}
		}
		private ReactiveCommand _RemoveInfoEntryCommand;

		private void OnSaveInfo()
		{
			App.DisplayStatusMessage("Starting saving of info.", StatusMessageType.USER);

			var dialog = new SaveFileDialog();
			dialog.DefaultExt = IdentificationFile.EXT;//gets long extensions to work properly when they are added to a filename when saved
			dialog.Filter = IdentificationFile.FILTER;
			dialog.OverwritePrompt = true;
			dialog.Title = "Select Location to Save Info File";
			dialog.InitialDirectory = Directory.GetCurrentDirectory();//TODO: remember last used directory

			if (dialog.ShowDialog() == true)
			{
				//replace .xml with .Info.xml
				var actualFileName = ExtensionFixer.SwitchToLongExtension(dialog.FileName, IdentificationFile.SHORT_EXT, IdentificationFile.EXT);

				try
				{
					using (var fileStream = new FileStream(actualFileName, FileMode.Create, FileAccess.Write))
					{
						var infoFile = new IdentificationFile();

						foreach (var entry in ECUInfo)
						{
							infoFile.IdentificationValues.Add(entry);
						}

						App.SerializeToXML(fileStream, infoFile);
						App.DisplayStatusMessage("Successfully saved info to file.", StatusMessageType.USER);
					}
				}
				catch
				{
					App.DisplayStatusMessage("Failed to save info to file.", StatusMessageType.USER);
				}
			}
			else
			{
				App.DisplayStatusMessage("Cancelling saving of info.", StatusMessageType.USER);
			}
		}

		private void OnLoadInfo()
		{
			App.DisplayStatusMessage("Starting loading of info.", StatusMessageType.USER);

			var dialog = new OpenFileDialog();
			dialog.Filter = IdentificationFile.FILTER;
			dialog.CheckFileExists = true;
			dialog.CheckPathExists = true;
			dialog.Title = "Select Info File to Load";
			dialog.InitialDirectory = Directory.GetCurrentDirectory();//TODO: remember last used directory

			if (dialog.ShowDialog() == true)
			{
				ECUInfo.Clear();

				try
				{
					using (var fileSteam = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read))
					{
						var formatter = new XmlSerializer(typeof(IdentificationFile));
						var infoFile = (IdentificationFile)formatter.Deserialize(fileSteam);

						foreach (var entry in infoFile.IdentificationValues)
						{
							ECUInfo.Add(entry);
						}

						App.DisplayStatusMessage("Successfully loaded info from file.", StatusMessageType.USER);
					}
				}
				catch (Exception e)
				{
					App.DisplayStatusMessage("Failed to load info from file: " + e.Message, StatusMessageType.USER);
				}
			}
			else
			{
				App.DisplayStatusMessage("Cancelling loading of info.", StatusMessageType.USER);
			}
		}

		public ReadOnlyCollection<KWP2000DiagnosticSessionType> AvailableReadInfoSessionTypes
		{
			get
			{
				if (_AvailableReadInfoSessionTypes == null)
				{
					_AvailableReadInfoSessionTypes = new ReadOnlyCollection<KWP2000DiagnosticSessionType>( new List<KWP2000DiagnosticSessionType>() 
						{ KWP2000DiagnosticSessionType.StandardSession, KWP2000DiagnosticSessionType.ProgrammingSession } );
				}

				return _AvailableReadInfoSessionTypes;
			}

		}
		private ReadOnlyCollection<KWP2000DiagnosticSessionType> _AvailableReadInfoSessionTypes;

		public KWP2000DiagnosticSessionType ReadInfoSessionType
		{
			get
			{
				return _ReadInfoSessionType;
			}
			set
			{
				if (_ReadInfoSessionType != value)
				{
					_ReadInfoSessionType = value;

					OnPropertyChanged(new PropertyChangedEventArgs("ReadInfoSessionType"));
				}
			}
		}
		private KWP2000DiagnosticSessionType _ReadInfoSessionType = KWP2000DiagnosticSessionType.StandardSession;

		public ICommand ReadInfoCommand
		{
			get
			{
				if (_ReadInfoCommand == null)
				{
					_ReadInfoCommand = new ReactiveCommand();
					_ReadInfoCommand.Name = "Read ECU Info";
					_ReadInfoCommand.Description = "Read the ECU information for the specified diagnostic session type";

					if (App != null)
					{
						_ReadInfoCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
						_ReadInfoCommand.AddWatchedProperty(App, "OperationInProgress");
						_ReadInfoCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
					}

					_ReadInfoCommand.ExecuteMethod = delegate
					{
						this.ReadAllECUIdentificationInfo(ReadInfoSessionType);
					};

					_ReadInfoCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (!App.CommInterface.IsConnected())
						{
							reasonsDisabled.Add("Not connected to ECU");
							result = false;
						}

						if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000)
						{
							reasonsDisabled.Add("Not connected with KWP2000 protocol");
							result = false;
						}

						if (App.OperationInProgress)
						{
							reasonsDisabled.Add("Another operation is in progress");
							result = false;
						}

						return result;
					};
				}

				return _ReadInfoCommand;
			}
		}
		private ReactiveCommand _ReadInfoCommand;

		private void ReadAllECUIdentificationInfo(KWP2000DiagnosticSessionType sessionType)
		{
			App.OperationInProgress = true;
			App.PercentOperationComplete = -1.0f;

			App.DisplayStatusMessage("Reading all ECU info.", StatusMessageType.USER);

			ECUInfo.Clear();

			var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

			App.CurrentOperation = new ReadAllECUIdentificationOptionsOperation(App.CommInterface as KWP2000Interface, KWP2000CommViewModel.DesiredBaudRates, sessionType);
			App.CurrentOperation.CompletedOperationEvent += this.ReadAllECUIdentificationInfoCompleted;			

			App.CurrentOperation.Start();
		}

		private void ReadAllECUIdentificationInfoCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				operation.CompletedOperationEvent -= this.ReadAllECUIdentificationInfoCompleted;

				if (success)
				{
					var readInfoOperation = operation as ReadAllECUIdentificationOptionsOperation;

					var newECUInfo = new ObservableCollection<KWP2000IdentificationOptionValue>();

					foreach (var identOption in readInfoOperation.ECUInfo.IdentOptionData.Keys)
					{
						KWP2000IdentificationOptionValue identOptionValue;
						if (readInfoOperation.ECUInfo.GetIdentificationOptionValue(identOption, out identOptionValue))
						{
							newECUInfo.Add(identOptionValue);
						}
					}

					ECUInfo = newECUInfo;

					DisplayReadECUInfo(readInfoOperation.ECUInfo);
				}
				else
				{
					App.DisplayStatusMessage("Reading all ECU info failed.", StatusMessageType.USER);
				}

				App.PercentOperationComplete = 100.0f;
				App.OperationInProgress = false;
			}), null);
		}

		public static string GetIdentOptionName(byte identOption)
		{
			var identOptionName = "Unknown";

			var identOptionEnum = (KWP2000IdentificationOption)Enum.ToObject(typeof(KWP2000IdentificationOption), identOption);

			identOptionName = identOptionEnum.ToString();

			var identOptionEnumDesc = DescriptionAttributeConverter.GetDescriptionAttribute(identOptionEnum);

			if (identOptionEnumDesc != null)
			{
				identOptionName = identOptionEnumDesc.ToString();
			}

			return identOptionName;
		}

		private void DisplayReadECUInfo(KWP2000IdentificationInfo identInfo)
		{
			if (identInfo.IdentOptionData.Count > 0)
			{
				App.DisplayStatusMessage("Read " + identInfo.IdentOptionData.Count + " ECU info entries:", StatusMessageType.USER);

				foreach (var identOption in identInfo.IdentOptionData.Keys)
				{
					var identOptionName = GetIdentOptionName(identOption);

					string identOptionString = "";

					KWP2000IdentificationOptionValue optionValue;
					if (identInfo.GetIdentificationOptionValue(identOption, out optionValue))
					{
						identOptionString = optionValue.ToString();
					}

					App.DisplayStatusMessage("\t0x" + identOption.ToString("X2") + ", " + identOptionName + ": " + identOptionString, StatusMessageType.USER);
				}
			}
			else
			{
				App.DisplayStatusMessage("No ECU info read.", StatusMessageType.USER);
			}			
		}

		private void ReadAllDTCs()
		{
			App.OperationInProgress = true;
			App.PercentOperationComplete = -1.0f;

			App.DisplayStatusMessage("Reading all ECU DTCs.", StatusMessageType.USER);

			ECUDTCs.Clear();

			var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;
			
			App.CurrentOperation = new ReadDiagnosticTroubleCodesOperation(App.CommInterface as KWP2000Interface, KWP2000CommViewModel.DesiredBaudRates);
			App.CurrentOperation.CompletedOperationEvent += this.ReadAllDTCsCompleted;
			
			App.CurrentOperation.Start();
		}

		private void ReadAllDTCsCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				operation.CompletedOperationEvent -= this.ReadAllDTCsCompleted;

				if (success)
				{
					var readDTCOperation = operation as ReadDiagnosticTroubleCodesOperation;				

					App.DisplayStatusMessage("Read " + readDTCOperation.DTCsRead.Count() + " of " + readDTCOperation.ExpectedNumDTCs + " DTCs.", StatusMessageType.USER);

					ECUDTCs = new ObservableCollection<KWP2000DTCInfo>(readDTCOperation.DTCsRead);
					DisplayDTCsRead(ECUDTCs);

					App.DisplayStatusMessage("Reading all ECU DTCs succeeded.", StatusMessageType.USER);
				}
				else
				{
					App.DisplayStatusMessage("Reading all ECU DTCs failed.", StatusMessageType.USER);
				}

				App.PercentOperationComplete = 100.0f;
				App.OperationInProgress = false;
			}), null);
		}

		public static string GetDTCName(ushort DTCnum)
		{
			string dtcString = "Unknown DTC";

			if (KWP2000VAGDTCDictionary.DTCStrings.ContainsKey(DTCnum))
			{
				dtcString = KWP2000VAGDTCDictionary.DTCStrings[DTCnum];
			}

			return dtcString;
		}

		private void DisplayDTCsRead(IEnumerable<KWP2000DTCInfo> dtcs)
		{
			string dtcOutput = "";

			foreach (var dtc in dtcs)
			{
				if (dtcOutput.Length > 0)
				{
					dtcOutput += "\n";
				}

				dtcOutput += "DTC: P" + dtc.DTC.ToString("X4") + " Status: 0x" + dtc.Status.ToString("X2") + " " + GetDTCName(dtc.DTC);
			}

			App.DisplayStatusMessage(dtcOutput, StatusMessageType.USER);
		}

		private void ClearDiagnosticInformation()
		{
			App.OperationInProgress = true;
			App.PercentOperationComplete = -1.0f;

			var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

			App.CurrentOperation = new ClearDiagnosticInformationOperation(App.CommInterface as KWP2000Interface, KWP2000CommViewModel.DesiredBaudRates);
			App.CurrentOperation.CompletedOperationEvent += this.ClearDiagnosticInformationCompleted;

			App.DisplayStatusMessage("Clearing ECU diagnostic information.", StatusMessageType.USER);

			App.CurrentOperation.Start();
		}

		private void ClearDiagnosticInformationCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				if (success)
				{
					App.DisplayStatusMessage("Clearing ECU diagnostic information succeeded.", StatusMessageType.USER);
				}
				else
				{
					App.DisplayStatusMessage("Clearing ECU diagnostic information failed.", StatusMessageType.USER);
				}

				operation.CompletedOperationEvent -= this.ClearDiagnosticInformationCompleted;

				App.PercentOperationComplete = 100.0f;
				App.OperationInProgress = false;
			}), null);
		}

		public ObservableCollection<KWP2000DTCInfo> ECUDTCs
		{
			get
			{
				if (_ECUDTCs == null)
				{
					_ECUDTCs = new ObservableCollection<KWP2000DTCInfo>();
				}

				return _ECUDTCs;
			}
			set
			{
				if (_ECUDTCs != value)
				{
					_ECUDTCs = value;

					OnPropertyChanged(new PropertyChangedEventArgs("ECUDTCs"));
				}
			}
		}
		private ObservableCollection<KWP2000DTCInfo> _ECUDTCs;

		public ObservableCollection<KWP2000IdentificationOptionValue> ECUInfo
		{
			get
			{
				if (_ECUInfo == null)
				{
					_ECUInfo = new ObservableCollection<KWP2000IdentificationOptionValue>();
				}

				return _ECUInfo;
			}
			set
			{
				if (_ECUInfo != value)
				{
					_ECUInfo = value;

					OnPropertyChanged(new PropertyChangedEventArgs("ECUInfo"));
				}
			}
		}
		private ObservableCollection<KWP2000IdentificationOptionValue> _ECUInfo;
	}

	public class IdentOptionNameConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value != null)
			{
				return ECUInfoControl.GetIdentOptionName((byte)value);
			}

			return null;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class DTCNameConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value != null)
			{
				if ((Application.Current != null) && (Application.Current is App))//TODO: I don't like this dependence on grabbing the app...
				{
					var app = Application.Current as App;

					return ECUInfoControl.GetDTCName((ushort)value);
				}
			}

			return null;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
