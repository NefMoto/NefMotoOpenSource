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
						AddWatchedPropertySafe(_ReadInfoCommand, App.CommInterface, "ConnectionStatus", "CommInterface");
						_ReadInfoCommand.AddWatchedProperty(App, "OperationInProgress");
						_ReadInfoCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
					}

					_ReadInfoCommand.ExecuteMethod = delegate
					{
						if (App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
						{
							this.ReadBootmodeECUInfo();
						}
						else
						{
							this.ReadAllECUIdentificationInfo(ReadInfoSessionType);
						}
					};

					_ReadInfoCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						if (App.CommInterface == null)
						{
							reasonsDisabled.Add("Communication interface not available");
							return false;
						}

						bool result = true;

						if (!App.CommInterface.IsConnected())
						{
							reasonsDisabled.Add("Not connected to ECU");
							result = false;
						}

						if ((App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000) &&
						    (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode))
						{
							reasonsDisabled.Add("Not connected with KWP2000 or BootMode protocol");
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


		private void ReadBootmodeECUInfo()
		{
			App.OperationInProgress = true;
			App.PercentOperationComplete = -1.0f;

			App.DisplayStatusMessage("Reading bootmode ECU information...", StatusMessageType.USER);

			ECUInfo.Clear();
			BootmodeInfo.Clear();

			var bootstrapInterface = App.CommInterface as BootstrapInterface;
			if (bootstrapInterface == null)
			{
				App.DisplayStatusMessage("Failed to get bootstrap interface.", StatusMessageType.USER);
				App.OperationInProgress = false;
				return;
			}

			// Read device ID
			byte deviceID = bootstrapInterface.DeviceID;
			byte actualDeviceID = deviceID;
			const byte CORE_ALREADY_RUNNING = 0xAA;

			// If core is already running (0xAA), try to use the last known device ID
			if (deviceID == CORE_ALREADY_RUNNING && bootstrapInterface.LastKnownDeviceID != 0)
			{
				actualDeviceID = bootstrapInterface.LastKnownDeviceID;
				AddBootmodeInfoEntry("Device ID (Current)", $"0x{deviceID:X2} (Core Already Running)");
				AddBootmodeInfoEntry("Device ID (Last Known)", $"0x{actualDeviceID:X2} ({GetDeviceIDName(actualDeviceID)})");
			}
			else
			{
				AddBootmodeInfoEntry("Device ID", $"0x{deviceID:X2} ({GetDeviceIDName(deviceID)})");
			}

			string cpuFamily = GetCPUFamily(actualDeviceID);
			AddBootmodeInfoEntry("CPU Family", cpuFamily);
			App.DisplayStatusMessage($"Bootmode Info: Device ID = 0x{deviceID:X2} ({GetDeviceIDName(deviceID)})", StatusMessageType.LOG);
			if (deviceID == CORE_ALREADY_RUNNING && bootstrapInterface.LastKnownDeviceID != 0)
			{
				App.DisplayStatusMessage($"Bootmode Info: Last Known Device ID = 0x{actualDeviceID:X2} ({GetDeviceIDName(actualDeviceID)})", StatusMessageType.LOG);
			}
			App.DisplayStatusMessage($"Bootmode Info: CPU Family = {cpuFamily}", StatusMessageType.LOG);

			// Read SYSCON register (0x00FF12) - System Configuration Register
			uint sysconValue;
			if (bootstrapInterface.MiniMon_ReadWord(0x00FF12, out sysconValue))
			{
				string sysconInfo = FormatSYSCON(sysconValue);
				AddBootmodeInfoEntry("SYSCON Register (0x00FF12)", sysconInfo);
				App.DisplayStatusMessage($"Bootmode Info: SYSCON Register (0x00FF12) = {sysconInfo}", StatusMessageType.LOG);
			}

			// Read BUSCON0 register (0x00FF0C) - Bus Configuration Register 0
			uint buscon0Value;
			if (bootstrapInterface.MiniMon_ReadWord(0x00FF0C, out buscon0Value))
			{
				string buscon0Info = FormatBUSCON0(buscon0Value);
				AddBootmodeInfoEntry("BUSCON0 Register (0x00FF0C)", buscon0Info);
				App.DisplayStatusMessage($"Bootmode Info: BUSCON0 Register (0x00FF0C) = {buscon0Info}", StatusMessageType.LOG);
			}

			// Read BUSCON1 register (0x00FF14) - Bus Configuration Register 1
			uint buscon1Value;
			if (bootstrapInterface.MiniMon_ReadWord(0x00FF14, out buscon1Value))
			{
				string buscon1Info = FormatBUSCON1(buscon1Value);
				AddBootmodeInfoEntry("BUSCON1 Register (0x00FF14)", buscon1Info);
				App.DisplayStatusMessage($"Bootmode Info: BUSCON1 Register (0x00FF14) = {buscon1Info}", StatusMessageType.LOG);
			}

			// Read Port 2 register (0xFFC0) - Port 2 Data Register
			uint port2Value;
			if (bootstrapInterface.MiniMon_ReadWord(0xFFC0, out port2Value))
			{
				string port2Info = $"0x{port2Value:X4} (Binary: {Convert.ToString(port2Value, 2).PadLeft(16, '0')})";
				AddBootmodeInfoEntry("Port 2 Register (0xFFC0)", port2Info);
				App.DisplayStatusMessage($"Bootmode Info: Port 2 Register (0xFFC0) = {port2Info}", StatusMessageType.LOG);
			}

			// Read ADDRSEL registers to understand memory mapping
			uint addrsel1Value;
			if (bootstrapInterface.MiniMon_ReadWord(0x00FE18, out addrsel1Value))
			{
				string addrsel1Info = FormatADDRSEL(addrsel1Value, 1);
				AddBootmodeInfoEntry("ADDRSEL1 Register (0x00FE18)", addrsel1Info);
				App.DisplayStatusMessage($"Bootmode Info: ADDRSEL1 Register (0x00FE18) = {addrsel1Info}", StatusMessageType.LOG);
			}

			uint addrsel2Value;
			if (bootstrapInterface.MiniMon_ReadWord(0x00FE1A, out addrsel2Value))
			{
				string addrsel2Info = FormatADDRSEL(addrsel2Value, 2);
				AddBootmodeInfoEntry("ADDRSEL2 Register (0x00FE1A)", addrsel2Info);
				App.DisplayStatusMessage($"Bootmode Info: ADDRSEL2 Register (0x00FE1A) = {addrsel2Info}", StatusMessageType.LOG);
			}

			uint addrsel3Value;
			if (bootstrapInterface.MiniMon_ReadWord(0x00FE1C, out addrsel3Value))
			{
				if (addrsel3Value != 0)
				{
					string addrsel3Info = FormatADDRSEL(addrsel3Value, 3);
					AddBootmodeInfoEntry("ADDRSEL3 Register (0x00FE1C)", addrsel3Info);
					App.DisplayStatusMessage($"Bootmode Info: ADDRSEL3 Register (0x00FE1C) = {addrsel3Info}", StatusMessageType.LOG);
				}
			}

			// Read internal RAM to check if driver area is initialized
			byte[] ramData;
			if (bootstrapInterface.MiniMon_ReadBlock(0x00F600, 16, out ramData))
			{
				bool allZero = ramData.All(b => b == 0);
				bool allFF = ramData.All(b => b == 0xFF);
				string ramStatus = allZero ? "All zeros (uninitialized)" : (allFF ? "All 0xFF (erased)" : "Contains data");
				AddBootmodeInfoEntry("Internal RAM (0xF600, 16 bytes)", ramStatus);
				App.DisplayStatusMessage($"Bootmode Info: Internal RAM (0xF600, 16 bytes) = {ramStatus}", StatusMessageType.LOG);
			}

			// Flash layout status (updates cache via GetBootmodeFlashLayout, then reads from GetBootmodeConnectionState)
			bootstrapInterface.GetBootmodeFlashLayout(out _, out _);
			var connState = bootstrapInterface.GetBootmodeConnectionState();
			switch (connState.FlashLayoutStatus)
			{
				case BootstrapInterface.BootmodeFlashLayoutStatus.Available:
					var layout = connState.FlashLayout;
					string layoutDetails = layout != null && layout.Validate()
						? $"Available: Base 0x{layout.BaseAddress:X6}, {layout.Size / 1024} KB, {layout.SectorSizes.Count} sectors"
						: "Available";
					AddBootmodeInfoEntry("Flash Layout", layoutDetails);
					App.DisplayStatusMessage($"Bootmode Info: Flash Layout = {layoutDetails}", StatusMessageType.LOG);
					break;
				case BootstrapInterface.BootmodeFlashLayoutStatus.Unavailable:
					string reason = connState.FlashLayoutUnavailableReason ?? "Unknown reason";
					AddBootmodeInfoEntry("Flash Layout", $"Unavailable: {reason}");
					App.DisplayStatusMessage($"Bootmode Info: Flash Layout = Unavailable: {reason}", StatusMessageType.LOG);
					break;
				default:
					AddBootmodeInfoEntry("Flash Layout", "Not yet detected");
					App.DisplayStatusMessage("Bootmode Info: Flash Layout = Not yet detected", StatusMessageType.LOG);
					break;
			}

			App.DisplayStatusMessage($"Read {BootmodeInfo.Count} bootmode ECU info entries.", StatusMessageType.USER);
			App.DisplayStatusMessage($"Bootmode Info Summary: {BootmodeInfo.Count} entries read successfully", StatusMessageType.LOG);
			App.PercentOperationComplete = 100.0f;
			App.OperationInProgress = false;
		}

		private void AddBootmodeInfoEntry(string name, string value)
		{
			BootmodeInfo.Add(new BootmodeInfoEntry(name, value));
		}

		private string GetDeviceIDName(byte deviceID)
		{
			switch (deviceID)
			{
				case 0x55: return "C166";
				case 0xA5: return "C167 (Old)";
				case 0xB5: return "C165";
				case 0xC5: return "C167";
				case 0xD5: return "C167 (With ID) / ST10";
				case 0xAA: return "Core Already Running";
				default: return "Unknown";
			}
		}

		private string GetCPUFamily(byte deviceID)
		{
			switch (deviceID)
			{
				case 0x55: return "C16x Family (C166)";
				case 0xA5: return "C16x Family (C167 Old)";
				case 0xB5: return "C16x Family (C165)";
				case 0xC5: return "C16x Family (C167)";
				case 0xD5: return "C16x/ST10 Family (C167 with ID or ST10)";
				case 0xAA: return "C16x/ST10 Family (Cannot determine specific variant - Core Already Running)";
				default: return "Unknown CPU Family";
			}
		}

		private string FormatADDRSEL(uint value, int addrselNumber)
		{
			// ADDRSEL register format:
			// Bits 15-12: Segment address (A23-A20)
			// Bits 11-8: Window size (0=32KB, 1=64KB, 2=128KB, 3=256KB, 4=512KB, 5=1MB, 6=2MB, 7=4MB, 8=1024KB, etc.)
			// Bits 7-0: Reserved/configuration

			if (value == 0)
			{
				return $"0x{value:X4} - Disabled/Not configured";
			}

			uint segmentAddr = (value >> 12) & 0xF;
			uint windowSize = (value >> 8) & 0xF;

			// Calculate window size in KB
			string windowSizeStr;
			switch (windowSize)
			{
				case 0: windowSizeStr = "32 KB"; break;
				case 1: windowSizeStr = "64 KB"; break;
				case 2: windowSizeStr = "128 KB"; break;
				case 3: windowSizeStr = "256 KB"; break;
				case 4: windowSizeStr = "512 KB"; break;
				case 5: windowSizeStr = "1 MB"; break;
				case 6: windowSizeStr = "2 MB"; break;
				case 7: windowSizeStr = "4 MB"; break;
				case 8: windowSizeStr = "1024 KB"; break;
				default: windowSizeStr = $"{windowSize} (unknown)"; break;
			}

			uint baseAddress = segmentAddr << 20; // Convert segment to address (A23-A20)

			return $"0x{value:X4} - Base: 0x{baseAddress:X6}, Window: {windowSizeStr}";
		}

		private string FormatSYSCON(uint value)
		{
			// SYSCON register bit fields (C167):
			// Bits 15-13: Stack size (STKSZ)
			// Bit 12: ROMS1 (ROM mapping)
			// Bit 11: SGTDIS (Segment disable)
			// Bit 10: ROMEN (ROM enable)
			// Bit 9: BYTDIS (Byte disable)
			// Bit 8: CLKEN (Clock enable)
			// Bit 7: WRCFG (Write config)
			// Bit 6: CSCFG (Chip select config)
			// Bit 5: Reserved
			// Bit 4: OWDDIS (Watchdog disable)
			// Bit 3: BDRSTEN (Brown-out reset enable)
			// Bit 2: XPEN (External bus enable)
			// Bit 1: VISIBLE (Visible mode)
			// Bit 0: SPER-SHARE (Special function share)

			uint stackSize = (value >> 13) & 0x7;
			bool romEnabled = ((value >> 10) & 0x1) != 0;
			bool byteDisable = ((value >> 9) & 0x1) != 0;
			bool extBusEnabled = ((value >> 2) & 0x1) != 0;
			bool visibleMode = ((value >> 1) & 0x1) != 0;

			return $"0x{value:X4} - Stack: {stackSize}, ROM: {(romEnabled ? "On" : "Off")}, " +
			       $"ByteMode: {(byteDisable ? "Word" : "Byte")}, ExtBus: {(extBusEnabled ? "On" : "Off")}, " +
			       $"Visible: {(visibleMode ? "Yes" : "No")}";
		}

		private string FormatBUSCON0(uint value)
		{
			// BUSCON0 register bit fields (C167):
			// Bits 15-12: Wait states (WTC)
			// Bits 11-8: Bus configuration
			// Bits 7-4: Address setup/hold
			// Bits 3-0: Chip select configuration

			uint waitStates = (value >> 12) & 0xF;
			uint busConfig = (value >> 8) & 0xF;
			bool extBusActive = ((value >> 2) & 0x1) != 0;

			string busType = "Unknown";
			if ((busConfig & 0x8) != 0)
			{
				busType = ((busConfig & 0x4) != 0) ? "16-bit Demux" : "8-bit Demux";
			}

			return $"0x{value:X4} - Wait States: {waitStates}, Bus: {busType}, " +
			       $"ExtBus: {(extBusActive ? "Active" : "Inactive")}";
		}

		private string FormatBUSCON1(uint value)
		{
			// BUSCON1 register bit fields (similar to BUSCON0 but for different address range)
			uint waitStates = (value >> 12) & 0xF;
			uint busConfig = (value >> 8) & 0xF;
			bool extBusActive = ((value >> 2) & 0x1) != 0;

			string busType = "Unknown";
			if ((busConfig & 0x8) != 0)
			{
				busType = ((busConfig & 0x4) != 0) ? "16-bit Demux" : "8-bit Demux";
			}

			return $"0x{value:X4} - Wait States: {waitStates}, Bus: {busType}, " +
			       $"ExtBus: {(extBusActive ? "Active" : "Inactive")}";
		}

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
			// Handle bootmode info entries (0xFF)
			if (identOption == 0xFF)
			{
				return "Bootmode Info";
			}

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
					_ECUInfo.CollectionChanged += ECUInfo_CollectionChanged;
				}

				return _ECUInfo;
			}
			set
			{
				if (_ECUInfo != value)
				{
					if (_ECUInfo != null)
					{
						_ECUInfo.CollectionChanged -= ECUInfo_CollectionChanged;
					}

					_ECUInfo = value;

					if (_ECUInfo != null)
					{
						_ECUInfo.CollectionChanged += ECUInfo_CollectionChanged;
					}

					OnPropertyChanged(new PropertyChangedEventArgs("ECUInfo"));
					OnPropertyChanged(new PropertyChangedEventArgs("CombinedECUInfo"));
				}
			}
		}
		private ObservableCollection<KWP2000IdentificationOptionValue> _ECUInfo;

		private void ECUInfo_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			OnPropertyChanged(new PropertyChangedEventArgs("CombinedECUInfo"));
		}

		public ObservableCollection<BootmodeInfoEntry> BootmodeInfo
		{
			get
			{
				if (_BootmodeInfo == null)
				{
					_BootmodeInfo = new ObservableCollection<BootmodeInfoEntry>();
					_BootmodeInfo.CollectionChanged += BootmodeInfo_CollectionChanged;
				}

				return _BootmodeInfo;
			}
			set
			{
				if (_BootmodeInfo != value)
				{
					if (_BootmodeInfo != null)
					{
						_BootmodeInfo.CollectionChanged -= BootmodeInfo_CollectionChanged;
					}

					_BootmodeInfo = value;

					if (_BootmodeInfo != null)
					{
						_BootmodeInfo.CollectionChanged += BootmodeInfo_CollectionChanged;
					}

					OnPropertyChanged(new PropertyChangedEventArgs("BootmodeInfo"));
					OnPropertyChanged(new PropertyChangedEventArgs("CombinedECUInfo"));
				}
			}
		}
		private ObservableCollection<BootmodeInfoEntry> _BootmodeInfo;

		private void BootmodeInfo_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			OnPropertyChanged(new PropertyChangedEventArgs("CombinedECUInfo"));
		}

		/// <summary>
		/// Combined collection of ECUInfo and BootmodeInfo for display in the ListBox
		/// </summary>
		public IEnumerable<object> CombinedECUInfo
		{
			get
			{
				var combined = new List<object>();
				if (ECUInfo != null)
				{
					foreach (var item in ECUInfo)
					{
						combined.Add(item);
					}
				}
				if (BootmodeInfo != null)
				{
					foreach (var item in BootmodeInfo)
					{
						combined.Add(item);
					}
				}
				return combined;
			}
		}
	}

	/// <summary>
	/// Represents a bootmode information entry for display in the ECU Info UI
	/// </summary>
	public class BootmodeInfoEntry : INotifyPropertyChanged
	{
		private string _name;
		private string _value;

		public string Name
		{
			get { return _name; }
			set
			{
				if (_name != value)
				{
					_name = value;
					OnPropertyChanged("Name");
				}
			}
		}

		public string Value
		{
			get { return _value; }
			set
			{
				if (_value != value)
				{
					_value = value;
					OnPropertyChanged("Value");
				}
			}
		}

		public BootmodeInfoEntry(string name, string value)
		{
			_name = name;
			_value = value;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
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
