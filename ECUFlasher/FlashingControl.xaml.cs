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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

using Communication;
using Shared;
using ApplicationShared;
using FTD2XX_NET;

namespace ECUFlasher
{
	public partial class FlashingControl : BaseUserControl
	{
		public FlashingControl()
		{
			FlashMemoryImage = new MemoryImage();
			FlashMemoryLayout = null;
			
			IsFlashFileOK = false;
			IsMemoryLayoutOK = false;
			
			FileNameToFlash = "";
			MemoryLayoutFileName = "";

			InitializeComponent();
			
			MemoryLayoutFileName = Properties.Settings.Default.MemoryLayoutFile;
			FileNameToFlash = Properties.Settings.Default.FlashFile;
		}

		private static bool ValidateFileToFlash(MemoryImage flashImage, MemoryLayout flashLayout, out string error)
		{
			error = null;

			if (flashImage != null)
			{
				if (flashImage.EndAddress % 2 != 0)
				{
					error = "File to flash size must be an even number of bytes";
				}
				else if (flashImage.StartAddress % 2 != 0)
				{
					error = "File to flash start address must be an even number of bytes";
				}
				else if ((flashLayout != null) && (flashLayout.Validate()) && (flashImage.Size != flashLayout.EndAddress - flashLayout.BaseAddress))
				{
					error = "File to flash size does not match memory layout size";
				}				
			}
			else
			{
				error = "File to flash is unset";
			}

			return (error == null);
		}

		private void LoadFlashFile()
		{
			if (FlashMemoryImage != null)
			{
				FlashMemoryImage.Reset();
			}

			IsFlashFileOK = false;
			string error = null;

			if (FlashMemoryImage == null)
			{
				error = "Internal program error";
			}
			else if (!String.IsNullOrEmpty(FileNameToFlash))
			{
				Properties.Settings.Default.FlashFile = FileNameToFlash;

				try
				{
					var fileBytes = File.ReadAllBytes(FileNameToFlash);
					FlashMemoryImage = new MemoryImage(fileBytes, 0);

					ValidateFileToFlash(FlashMemoryImage, FlashMemoryLayout, out error);
				}
				catch (Exception)
				{
					error = "Could not read flash file";
				}
			}
			else
			{
				error = "No file selected";
			}

			//force and update without setting the property
			OnPropertyChanged(new PropertyChangedEventArgs("FlashMemoryImage"));

			this["FileNameToFlash"] = error;
			IsFlashFileOK = (error == null);
		}

		public string FileNameToFlash
		{
			get { return mFileNameToFlash; }
			set
			{
				mFileNameToFlash = value;

				LoadFlashFile();

				OnPropertyChanged(new PropertyChangedEventArgs("FileNameToFlash"));
			}
		}
		private string mFileNameToFlash;
		
		private void LoadMemoryLayoutFile()
		{
			if (FlashMemoryLayout != null)
			{
				FlashMemoryLayout.Reset();
				FlashMemoryLayout = null;
			}

			IsMemoryLayoutOK = false;
			string error = null;

			if (!String.IsNullOrEmpty(MemoryLayoutFileName))
			{
				try
				{
					using (var fStream = new FileStream(MemoryLayoutFileName, FileMode.Open, FileAccess.Read))
					{
						var xmlFormat = new XmlSerializer(typeof(MemoryLayout));
						FlashMemoryLayout = (MemoryLayout)xmlFormat.Deserialize(fStream);
					}

					IsMemoryLayoutOK = FlashMemoryLayout.Validate();

					if (!IsMemoryLayoutOK)
					{
						error = FlashMemoryLayout.Error;
					}
				}
				catch
				{
					error = "Error reading memory layout file";
				}

				Properties.Settings.Default.MemoryLayoutFile = MemoryLayoutFileName;
			}
			else
			{
				error = "No file selected";
			}

			//force and update without setting the property
			OnPropertyChanged(new PropertyChangedEventArgs("FlashMemoryLayout"));

			this["MemoryLayoutFileName"] = error;
		}

		public string MemoryLayoutFileName
		{
			get { return mMemoryLayoutFileName; }
			set
			{
				mMemoryLayoutFileName = value;

				LoadMemoryLayoutFile();

				//because changing the memory layout can change if the file to flash is valid
				string tempError;
				IsFlashFileOK = ValidateFileToFlash(FlashMemoryImage, FlashMemoryLayout, out tempError);
				this["FileNameToFlash"] = tempError;

				//force and update without setting the property
				OnPropertyChanged(new PropertyChangedEventArgs("FileNameToFlash"));

				OnPropertyChanged(new PropertyChangedEventArgs("MemoryLayoutFileName"));
			}
		}
		private string mMemoryLayoutFileName;

		public bool IsFlashFileOK
		{
			get { return mIsFlashFileOK; }
			set
			{
				mIsFlashFileOK = value;
				OnPropertyChanged(new PropertyChangedEventArgs("IsFlashFileOK"));
			}
		}
		private bool mIsFlashFileOK;        

		public bool IsMemoryLayoutOK
		{
			get { return mIsMemoryLayoutOK; }
			set
			{
				mIsMemoryLayoutOK = value;
				OnPropertyChanged(new PropertyChangedEventArgs("IsMemoryLayoutOK"));
			}
		}
		private bool mIsMemoryLayoutOK;

		public ICommand ChooseFlashFileCommand
		{
			get
			{
				if (_ChooseFlashFileCommand == null)
				{
					_ChooseFlashFileCommand = new ReactiveCommand(this.OnChooseFlashFile);
					_ChooseFlashFileCommand.Name = "Choose Flash File";
					_ChooseFlashFileCommand.Description = "Choose the flash file";
					_ChooseFlashFileCommand.AddWatchedProperty(App, "OperationInProgress");

					_ChooseFlashFileCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (App.OperationInProgress)
						{
							reasonsDisabled.Add("Operation is in progress");
							result = false;
						}

						return result;
					};
				}

				return _ChooseFlashFileCommand;
			}
		}
		private ReactiveCommand _ChooseFlashFileCommand;

		private readonly string FLASH_FILE_EXT = ".bin";
		private readonly string FLASH_FILE_FILTER = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*";

		private void OnChooseFlashFile()
		{
			OpenFileDialog dialog = new OpenFileDialog();
			dialog.Filter = FLASH_FILE_FILTER;
			dialog.CheckFileExists = true;
			dialog.CheckPathExists = true;
			dialog.Title = "Select File to Flash";

			if (!String.IsNullOrEmpty(FileNameToFlash))
			{
				DirectoryInfo dirInfo = Directory.GetParent(FileNameToFlash);
				
				if(dirInfo != null)
				{
					dialog.InitialDirectory = dirInfo.FullName;
				}
			}

			if (String.IsNullOrEmpty(dialog.InitialDirectory))
			{
				dialog.InitialDirectory = Directory.GetCurrentDirectory();
			}

			if (dialog.ShowDialog() == true)
			{
				FileNameToFlash = dialog.FileName;
			}
		}

		public ICommand ChooseMemoryLayoutCommand
		{
			get
			{
				if (_ChooseMemoryLayoutCommand == null)
				{
					_ChooseMemoryLayoutCommand = new ReactiveCommand(this.OnChooseMemoryLayout);
					_ChooseMemoryLayoutCommand.Name = "Choose Memory Layout";
					_ChooseMemoryLayoutCommand.Description = "Choose the memory layout";

					if (App != null)
					{
						_ChooseMemoryLayoutCommand.AddWatchedProperty(App, "OperationInProgress");
					}

					_ChooseMemoryLayoutCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (App.OperationInProgress)
						{
							reasonsDisabled.Add("Operation is in progress");
							result = false;
						}

						return result;
					};
				}

				return _ChooseMemoryLayoutCommand;
			}
		}
		private ReactiveCommand _ChooseMemoryLayoutCommand;

		private void OnChooseMemoryLayout()
		{
			OpenFileDialog dialog = new OpenFileDialog();
			dialog.Filter = MemoryLayout.MEMORY_LAYOUT_FILE_FILTER;
			dialog.CheckFileExists = true;
			dialog.CheckPathExists = true;
			dialog.Title = "Select Memory Layout File for Flashing";

			if (!String.IsNullOrEmpty(MemoryLayoutFileName))
			{
				DirectoryInfo dirInfo = Directory.GetParent(MemoryLayoutFileName);

				if(dirInfo != null)
				{
					dialog.InitialDirectory = dirInfo.FullName;
				}
			}

			if (String.IsNullOrEmpty(dialog.InitialDirectory))
			{
				dialog.InitialDirectory = Directory.GetCurrentDirectory();
			}

			if (dialog.ShowDialog() == true)
			{
				MemoryLayoutFileName = dialog.FileName;                
			}
		}

		public ReactiveCommand VerifyChecksumsCommand
		{
			get
			{
				if (_VerifyChecksumsCommand == null)
				{
					_VerifyChecksumsCommand = new ReactiveCommand(this.OnVerifyChecksums);
					_VerifyChecksumsCommand.Name = "Verify Checksums";
					_VerifyChecksumsCommand.Description = "Verify the checksums are correct";

					if (App != null)
					{
						_VerifyChecksumsCommand.AddWatchedProperty(App, "OperationInProgress");
						_VerifyChecksumsCommand.AddWatchedProperty(this, "IsFlashFileOK");
					}

					_VerifyChecksumsCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (App.OperationInProgress)
						{
							reasonsDisabled.Add("Another operation is in progress");
							result = false;
						}

						if (!IsFlashFileOK)
						{
							reasonsDisabled.Add("Specified flash file is not correct");
							result = false;
						}

						return result;
					};
				}

				return _VerifyChecksumsCommand;
			}
		}
		private ReactiveCommand _VerifyChecksumsCommand;

		public ReactiveCommand CorrectChecksumsCommand
		{
			get
			{
				if (_CorrectChecksumsCommand == null)
				{
					_CorrectChecksumsCommand = new ReactiveCommand(this.OnCorrectChecksums);
					_CorrectChecksumsCommand.Name = "Correct Checksums";
					_CorrectChecksumsCommand.Description = "Update checksums to correct values";

					if (App != null)
					{
						_CorrectChecksumsCommand.AddWatchedProperty(App, "OperationInProgress");
						_CorrectChecksumsCommand.AddWatchedProperty(this, "IsFlashFileOK");
					}

					_CorrectChecksumsCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (App.OperationInProgress)
						{
							reasonsDisabled.Add("Another operation is in progress");
							result = false;
						}

						if (!IsFlashFileOK)
						{
							reasonsDisabled.Add("Specified flash file is not correct");
							result = false;
						}
						
						return result;
					};
				}

				return _CorrectChecksumsCommand;
			}
		}
		private ReactiveCommand _CorrectChecksumsCommand;

		private void OnVerifyChecksums()
		{
			App.CurrentOperation = new Checksum.ValidateChecksumsOperation(FlashMemoryImage.RawData);
			App.CurrentOperation.CompletedOperationEvent += this.OnVerifyChecksumsCompleted;

			App.OperationInProgress = true;
			App.PercentOperationComplete = -1.0f;

			App.DisplayStatusMessage("Verifying checksums are correct.", StatusMessageType.USER);

			App.CurrentOperation.Start();
		}

		private void OnVerifyChecksumsCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				App.PercentOperationComplete = 100.0f;
				App.OperationInProgress = false;

				if (success)
				{
					var validateOperation = operation as Checksum.ValidateChecksumsOperation;

					uint numCorrect = validateOperation.NumChecksums - validateOperation.NumIncorrectChecksums;

					App.DisplayStatusMessage(numCorrect + " of " + validateOperation.NumChecksums + " checksums are correct.", StatusMessageType.USER);

					if (validateOperation.AreChecksumsCorrect)
					{
						App.DisplayStatusMessage("All checksums are correct.", StatusMessageType.USER);
					}
					else
					{
						App.DisplayStatusMessage("Some checksums are NOT correct.", StatusMessageType.USER);
					}
				}
				else
				{
					App.DisplayStatusMessage("Failed to verify checksums are correct.", StatusMessageType.USER);
				}
			}));
		}

		private void OnCorrectChecksums()
		{
			App.CurrentOperation = new Checksum.CorrectChecksumsOperation(FlashMemoryImage.RawData);
			App.CurrentOperation.CompletedOperationEvent += this.OnCorrectChecksumsCompleted;

			App.OperationInProgress = true;
			App.PercentOperationComplete = -1.0f;

			App.DisplayStatusMessage("Correcting checksums.", StatusMessageType.USER);

			App.CurrentOperation.Start();
		}

		private void OnCorrectChecksumsCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() =>
			{
				App.PercentOperationComplete = 100.0f;
				App.OperationInProgress = false;

				var correctOperation = operation as Checksum.CorrectChecksumsOperation;

				App.DisplayStatusMessage("Corrected " + correctOperation.NumCorrectedChecksums + " of " + correctOperation.NumChecksums + " checksums.", StatusMessageType.USER);

				if (success)
				{
					App.DisplayStatusMessage("Checksums updated to correct values.", StatusMessageType.USER);

					if (FlashMemoryImage.SaveToFile(FileNameToFlash))
					{
						App.DisplayStatusMessage("Saved changes to file: " + FileNameToFlash, StatusMessageType.USER);
					}
					else
					{
						App.DisplayStatusMessage("Failed to save changes to file: " + FileNameToFlash, StatusMessageType.USER);
					}
				}
				else
				{
					App.DisplayStatusMessage("Failed to correct checksums.", StatusMessageType.USER);
				}
			}));
		}

		public ReactiveCommand CheckIfFlashMatchesCommand
		{
			get
			{
				if (_CheckIfFlashMatchesCommand == null)
				{
					_CheckIfFlashMatchesCommand = new ReactiveCommand(this.OnCheckIfFlashMatches);
					_CheckIfFlashMatchesCommand.Name = "Check if Flash Matches";
					_CheckIfFlashMatchesCommand.Description = "Check if the loaded file matches the flash memory on the ECU";

					if (App != null)
					{
						_CheckIfFlashMatchesCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
						_CheckIfFlashMatchesCommand.AddWatchedProperty(App, "OperationInProgress");
						_CheckIfFlashMatchesCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
						_CheckIfFlashMatchesCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
						_CheckIfFlashMatchesCommand.AddWatchedProperty(this, "IsFlashFileOK");
					}

					_CheckIfFlashMatchesCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						if (App == null)
						{
							reasonsDisabled.Add("Internal program error");
							return false;
						}

						bool result = true;

						if (!IsMemoryLayoutOK)
						{
							reasonsDisabled.Add("Specified memory layout is not correct");
							result = false;
						}

						if (!IsFlashFileOK)
						{
							reasonsDisabled.Add("Specified flash file is not correct");
							result = false;
						}

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

				return _CheckIfFlashMatchesCommand;
			}
		}
		private ReactiveCommand _CheckIfFlashMatchesCommand;

		private void OnCheckIfFlashMatches()
		{
			//done to trigger a reload of the memory layout and flash files and cause them to revalidate
			FileNameToFlash = FileNameToFlash;            
			MemoryLayoutFileName = MemoryLayoutFileName;

			if (CheckIfFlashMatchesCommand.IsEnabled)
			{
				string confirmationMessage = "If you are ready to check if flash matches, confirm the following things:";
				confirmationMessage += "\n1) You have loaded a valid file and memory layout for the ECU.";
				confirmationMessage += "\n2) The engine is not running.";
				confirmationMessage += "\n\nClick OK to confirm, otherwise Cancel.";
				
				if (App.DisplayUserPrompt("Confirm Check if Flash Matches", confirmationMessage, UserPromptType.OK_CANCEL) == UserPromptResult.OK)
				{
					CheckIfFlashMatches();
				}
			}
		}

		public ReactiveCommand WriteEntireFlashCommand
		{
			get
			{
				if (_WriteEntireFlashCommand == null)
				{
					_WriteEntireFlashCommand = new ReactiveCommand(this.OnWriteEntireFlash);
					_WriteEntireFlashCommand.Name = "Full Write Flash";
					_WriteEntireFlashCommand.Description = "Write every sector of ECU flash memory with the loaded file";

					if (App != null)
					{
						_WriteEntireFlashCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
						_WriteEntireFlashCommand.AddWatchedProperty(App, "OperationInProgress");
						_WriteEntireFlashCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
						_WriteEntireFlashCommand.AddWatchedProperty(this, "IsFlashFileOK");
						_WriteEntireFlashCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
					}

					_WriteEntireFlashCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						return CanExecuteWriteEntireFlashCommand(reasonsDisabled);
					};
				}

				return _WriteEntireFlashCommand;
			}
		}
		private ReactiveCommand _WriteEntireFlashCommand;

			
		private bool CanExecuteWriteEntireFlashCommand(List<string> reasonsDisabled)
		{
			if (App == null)
			{
				reasonsDisabled.Add("Internal program error");
				return false;
			}

			bool result = true;

			if (!IsMemoryLayoutOK)
			{
				reasonsDisabled.Add("Specified memory layout is not correct");
				result = false;
			}

			if (!IsFlashFileOK)
			{
				reasonsDisabled.Add("Specified flash file is not correct");
				result = false;
			}

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
		}
		private void OnWriteEntireFlash()
		{
			//done to trigger a reload of the memory layout and flash files and cause them to revalidate
			FileNameToFlash = FileNameToFlash;
			MemoryLayoutFileName = MemoryLayoutFileName;

			if (WriteEntireFlashCommand.IsEnabled)
			{
				string confirmationMessage = "If you are ready to write, confirm the following things:";
				confirmationMessage += "\n1) You have loaded a valid file and memory layout for the ECU.";
				confirmationMessage += "\n2) The engine is not running.";
				confirmationMessage += "\n3) Battery voltage is at least 12 volts.";
				confirmationMessage += "\n4) It is OK the ECU adaptation channels will be reset to defaults";
				confirmationMessage += "\n5) Flashing process can run uninterrupted until complete.";
				confirmationMessage += "\n6) You agree to release Nefarious Motorsports Inc from all liability.";
				confirmationMessage += "\nNote: Some non-standard flash memory chips may prevent writing the flash memory.";                
				confirmationMessage += "\n\nClick OK to confirm, otherwise Cancel.";

				if (App.DisplayUserPrompt("Confirm Full Write ECU Flash Memory", confirmationMessage, UserPromptType.OK_CANCEL) == UserPromptResult.OK)
				{
					OnWriteExternalFlashStarted(FlashMemoryImage.RawData, FlashMemoryLayout, this.OnWriteFlashCompleted, false, chkVerifyWrite.IsChecked.Value);
				}
			}
		}


        public ReactiveCommand WriteDiffFlashCommand
        {
            get
            {
                if (_WriteDiffFlashCommand == null)
                {
                    _WriteDiffFlashCommand = new ReactiveCommand(this.OnWriteDiffFlash);
                    _WriteDiffFlashCommand.Name = "Diff Write Flash";
                    _WriteDiffFlashCommand.Description = "Write only changed sectors of ECU flash memory with the loaded file";

                    if (App != null)
                    {
                        _WriteDiffFlashCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
                        _WriteDiffFlashCommand.AddWatchedProperty(App, "OperationInProgress");
                        _WriteDiffFlashCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
                        _WriteDiffFlashCommand.AddWatchedProperty(this, "IsFlashFileOK");
                        _WriteDiffFlashCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
                    }

                    _WriteDiffFlashCommand.CanExecuteMethod = delegate (List<string> reasonsDisabled)
                    {
                        return CanExecuteWriteDiffFlashCommand(reasonsDisabled);
                    };
                }

                return _WriteDiffFlashCommand;
            }
        }
        private ReactiveCommand _WriteDiffFlashCommand;


        private bool CanExecuteWriteDiffFlashCommand(List<string> reasonsDisabled)
        {
            if (App == null)
            {
                reasonsDisabled.Add("Internal program error");
                return false;
            }

            bool result = true;

            if (!IsMemoryLayoutOK)
            {
                reasonsDisabled.Add("Specified memory layout is not correct");
                result = false;
            }

            if (!IsFlashFileOK)
            {
                reasonsDisabled.Add("Specified flash file is not correct");
                result = false;
            }

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
        }
        private void OnWriteDiffFlash()
        {
            //done to trigger a reload of the memory layout and flash files and cause them to revalidate
            FileNameToFlash = FileNameToFlash;
            MemoryLayoutFileName = MemoryLayoutFileName;

            if (WriteDiffFlashCommand.IsEnabled)
            {
                string confirmationMessage = "If you are ready to write, confirm the following things:";
                confirmationMessage += "\n1) You have loaded a valid file and memory layout for the ECU.";
                confirmationMessage += "\n2) The engine is not running.";
                confirmationMessage += "\n3) Battery voltage is at least 12 volts.";
                confirmationMessage += "\n4) It is OK the ECU adaptation channels will be reset to defaults";
                confirmationMessage += "\n5) Flashing process can run uninterrupted until complete.";
                confirmationMessage += "\n6) You agree to release Nefarious Motorsports Inc from all liability.";
                confirmationMessage += "\n7) You agree to not use this tool commercially, as the nefmoto karma gods shall smite you if you do.";
                confirmationMessage += "\nNote: Some non-standard flash memory chips may prevent writing the flash memory.";
                confirmationMessage += "\n\nClick OK to confirm, otherwise Cancel.";

                if (App.DisplayUserPrompt("Confirm Diff Write ECU Flash Memory", confirmationMessage, UserPromptType.OK_CANCEL) == UserPromptResult.OK)
                {
                    OnWriteExternalFlashStarted(FlashMemoryImage.RawData, FlashMemoryLayout, this.OnWriteFlashCompleted, true, chkVerifyWrite.IsChecked.Value);
                }
            }
        }
        
        private void OnWriteFlashCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				var writeOperation = (WriteExternalFlashOperation)operation;

				string statusMessage = OnWriteExternalFlashCompleted(operation, success);

				if (success)
				{
					var flashingTime = operation.OperationElapsedTime;
					statusMessage += "\nFlashing time was " + flashingTime.Hours.ToString("D2") + ":" + flashingTime.Minutes.ToString("D2") + ":" + flashingTime.Seconds.ToString("D2") + ".";
				}
			
				App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);

				App.PercentOperationComplete = 100.0f;
				App.DisplayUserPrompt("Writing ECU Flash Memory Complete", statusMessage, UserPromptType.OK);

				App.OperationInProgress = false;
			}), null);
		}

		public ReactiveCommand ReadEntireFlashCommand
		{
			get
			{
				if (_ReadEntireFlashCommand == null)
				{
					_ReadEntireFlashCommand = new ReactiveCommand(this.OnReadEntireFlash);
					_ReadEntireFlashCommand.Name = "Full Read Flash";
					_ReadEntireFlashCommand.Description = "Read every sector of ECU flash memory";

					if (App != null)
					{
						_ReadEntireFlashCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
						_ReadEntireFlashCommand.AddWatchedProperty(App, "OperationInProgress");
						_ReadEntireFlashCommand.AddWatchedProperty(App, "CommInterface");//listen for protocol changes
						_ReadEntireFlashCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
					}

					_ReadEntireFlashCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
					{
						return CanExecuteReadEntireFlashCommand(reasonsDisabled);
					};

				}

				return _ReadEntireFlashCommand;
			}
		}
		private ReactiveCommand _ReadEntireFlashCommand;

		private bool CanExecuteReadEntireFlashCommand(List<string> reasonsDisabled)
		{
			if (App == null)
			{
				reasonsDisabled.Add("Internal program error");
				return false;
			}

			bool result = true;

			if (!IsMemoryLayoutOK)
			{
				reasonsDisabled.Add("Specified memory layout is not correct");
				result = false;
			}

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
		}

		private void OnReadEntireFlash()
		{
			//done to trigger a reload of the memory layout and cause it to revalidate            
			MemoryLayoutFileName = MemoryLayoutFileName;

			if (ReadEntireFlashCommand.IsEnabled)
			{
				string confirmationMessage = "If you are ready to read, confirm the following things:";
				confirmationMessage += "\n1) You have loaded a valid memory layout for the ECU.";
				confirmationMessage += "\n2) The engine is not running.";
				confirmationMessage += "\nNote: Some non-standard flash memory chips may prevent reading the flash memory.";
				confirmationMessage += "\n\nClick OK to confirm, otherwise Cancel.";

				if (App.DisplayUserPrompt("Confirm Full Read ECU Flash Memory", confirmationMessage, UserPromptType.OK_CANCEL) == UserPromptResult.OK)
				{
					App.OperationInProgress = true;
					App.PercentOperationComplete = 0.0f;
					
					OnReadExternalFlashStarted(false, false, false, FlashMemoryImage.RawData, FlashMemoryLayout, this.OnReadFlashCompleted);
				}
			}
		}		

		private void OnReadFlashCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				var readFlashOperation = operation as ReadExternalFlashOperation;

				operation.CompletedOperationEvent -= this.OnReadFlashCompleted;

				MemoryImage readMemory = null;
				success = OnReadExternalFlashCompleted(operation, success, out readMemory);

				if (success)
				{
					success = SaveECUFlashFile(readMemory);
				}

				string statusMessage = "";

				if (success)
				{
					var readingTime = operation.OperationElapsedTime;

					statusMessage = "Reading ECU flash memory succeeded in: " + readingTime.Hours.ToString("D2") + ":" + readingTime.Minutes.ToString("D2") + ":" + readingTime.Seconds.ToString("D2") + ".";
				}
				else
				{
					statusMessage = "Reading ECU flash memory failed.";
				}

				App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
				App.DisplayUserPrompt("Reading ECU Flash Memory Complete", statusMessage, UserPromptType.OK);

				App.PercentOperationComplete = 100.0f;
				App.OperationInProgress = false;
			}), null);
		}

		private bool SaveECUFlashFile(MemoryImage readMemory)
		{
			//save the read data
			var dialog = new SaveFileDialog();
			dialog.DefaultExt = FLASH_FILE_EXT;//gets long extensions to work properly when they are added to a filename when saved
			dialog.Filter = FLASH_FILE_FILTER;
			dialog.AddExtension = true;
			dialog.OverwritePrompt = true;
			dialog.Title = "Select Where to Save Read Flash File";

			string flashSaveLocation = null;

			if ((FileNameToFlash != null) && (FileNameToFlash.Length > 0))
			{
				var dirInfo = Directory.GetParent(FileNameToFlash);

				if (dirInfo != null)
				{
					flashSaveLocation = dirInfo.FullName;
				}
			}

			if (string.IsNullOrEmpty(flashSaveLocation))
			{
				flashSaveLocation = Directory.GetCurrentDirectory();
			}

			dialog.InitialDirectory = flashSaveLocation;

			if (dialog.ShowDialog() == true)
			{
				if (readMemory.SaveToFile(dialog.FileName))
				{
					App.DisplayStatusMessage("Saved ECU flash memory to: " + dialog.FileName, StatusMessageType.USER);
				}
				else
				{
					App.DisplayStatusMessage("Failed to save ECU flash memory to file", StatusMessageType.USER);
				}
			}
			else
			{
				App.DisplayStatusMessage("Not saving ECU flash memory because user cancelled", StatusMessageType.USER);
			}

			return true;
		}

		private void CheckIfFlashMatches()
		{
			var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

			var settings = new DoesFlashChecksumMatchOperation.DoesFlashChecksumMatchSettings();
			settings.SecuritySettings.RequestSeed = KWP2000CommViewModel.SeedRequest;
			settings.SecuritySettings.SupportSpecialKey = KWP2000CommViewModel.ShouldSupportSpecialKey;
			settings.SecuritySettings.UseExtendedSeedRequest = KWP2000CommViewModel.ShouldUseExtendedSeedRequest;

			App.CurrentOperation = new DoesFlashChecksumMatchOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates, settings, FlashMemoryLayout.BaseAddress, FlashMemoryImage.RawData);
			App.CurrentOperation.CompletedOperationEvent += this.CheckIfFlashMatchesOperationCompleted;

			App.OperationInProgress = true;
			App.PercentOperationComplete = -1.0f;

			App.DisplayStatusMessage("Checking if flash matches flash file.", StatusMessageType.USER);

			App.CurrentOperation.Start();
		}

		private void CheckIfFlashMatchesOperationCompleted(Operation operation, bool success)
		{
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				var matchOperation = (DoesFlashChecksumMatchOperation)operation;

				string statusMessage = "";

				if (success)
				{
					string matchMessage = "";

					if (matchOperation.DoesMatch)
					{
						matchMessage = "Flash memory matches flash file.";
					}
					else
					{
						matchMessage = "Flash memory does not match flash file.";
					}

					statusMessage = "Checking if flash matches succeeded.";
					statusMessage += "\n" + matchMessage;
				}
				else
				{
					statusMessage = "Checking if flash matches failed.";
				}

				operation.CompletedOperationEvent -= this.CheckIfFlashMatchesOperationCompleted;

				App.PercentOperationComplete = 100.0f;

				App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
				App.DisplayUserPrompt("Checking If Flash Matches Complete", statusMessage, UserPromptType.OK);

				App.OperationInProgress = false;
			}), null);
		}

		private void OnWriteExternalFlashStarted(byte[] flashMemoryImage, MemoryLayout flashMemoryLayout, Operation.CompletedOperationDelegate onOperationComplete, bool diffWrite, bool verify)
		{
			var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

			var settings = new WriteExternalFlashOperation.WriteExternalFlashSettings();
			settings.CheckIfWriteRequired = diffWrite;
			settings.OnlyWriteNonMatchingSectors = diffWrite;
			settings.VerifyWrittenData = verify;
			settings.EraseEntireFlashAtOnce = false;
			settings.SecuritySettings.RequestSeed = KWP2000CommViewModel.SeedRequest;
			settings.SecuritySettings.SupportSpecialKey = KWP2000CommViewModel.ShouldSupportSpecialKey;
			settings.SecuritySettings.UseExtendedSeedRequest = KWP2000CommViewModel.ShouldUseExtendedSeedRequest;

			var sectorImages = MemoryUtils.SplitMemoryImageIntoSectors(flashMemoryImage, flashMemoryLayout);

			App.CurrentOperation = new WriteExternalFlashOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates, settings, sectorImages);
			App.CurrentOperation.CompletedOperationEvent += onOperationComplete;

			App.OperationInProgress = true;                
			App.PercentOperationComplete = 0.0f;

			App.DisplayStatusMessage("Writing ECU flash memory.", StatusMessageType.USER);

			App.CurrentOperation.Start();
		}

		private string OnWriteExternalFlashCompleted(Operation operation, bool success)
		{
			var writeOperation = (WriteExternalFlashOperation)operation;

			string statusMesage = "";

			if (success)
			{				
				int numSectors = writeOperation.NumSectors;
				int numSuccessfullyFlashedSectors = writeOperation.NumSuccessfullyFlashedSectors;

				statusMesage = "Writing ECU flash memory succeeded. Wrote " + numSuccessfullyFlashedSectors + " of " + numSectors + " sectors in flash memory.";
			}
			else
			{
				if (writeOperation.WasFailureCausedByPreviousIncompleteDownload)
				{
					statusMesage = "Writing ECU flash memory failed because a previous programming operation was incomplete. Please reconnect and retry.";
				}
				else
				{
					statusMesage = "Writing ECU flash memory failed.";
				}
			}

			return statusMesage;
		}

		private void OnReadExternalFlashStarted(bool checkIfReadRequired, bool onlyReadRequiredSectors, bool shouldVerifyReadData, byte[] baseImage, MemoryLayout flashLayout, Operation.CompletedOperationDelegate operationCompletedDel)
		{
			var readImage = baseImage;

			if ((readImage == null) || (readImage.Length != flashLayout.Size))
			{
				readImage = new byte[flashLayout.Size];

				//fill the memory image with 0xFF because that is what blank data in the flash chip is
				for (int x = 0; x < readImage.Length; x++)
				{
					readImage[x] = 0xFF;
				}
			}
			
			var sectorImages = MemoryUtils.SplitMemoryImageIntoSectors(readImage, flashLayout);

			var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

			var settings = new ReadExternalFlashOperation.ReadExternalFlashSettings();
			settings.CheckIfSectorReadRequired = checkIfReadRequired;
			settings.OnlyReadNonMatchingSectors = onlyReadRequiredSectors;
			settings.VerifyReadData = shouldVerifyReadData;
			settings.SecuritySettings.RequestSeed = KWP2000CommViewModel.SeedRequest;
			settings.SecuritySettings.SupportSpecialKey = KWP2000CommViewModel.ShouldSupportSpecialKey;
			settings.SecuritySettings.UseExtendedSeedRequest = KWP2000CommViewModel.ShouldUseExtendedSeedRequest;

			App.CurrentOperation = new ReadExternalFlashOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates, settings, sectorImages);
			App.CurrentOperation.CompletedOperationEvent += operationCompletedDel;
			
			App.DisplayStatusMessage("Reading ECU flash memory.", StatusMessageType.USER);

			App.CurrentOperation.Start();
		}

		private bool OnReadExternalFlashCompleted(Operation operation, bool success, out MemoryImage readMemory)
		{
			readMemory = null;            

			if (success)
			{
				var readFlashOperation = (ReadExternalFlashOperation)operation;

				//we only want the data out of the memory images read, since we already know which memory layout is being used
				var readSectorData = new List<byte[]>();
				foreach (var sector in readFlashOperation.FlashBlockList)
				{
					readSectorData.Add(sector.RawData);
				}

				if (!MemoryUtils.CombineMemorySectorsIntoImage(readSectorData, FlashMemoryLayout, out readMemory))                
				{
					App.DisplayStatusMessage("Failed to combine memory sectors into one memory image", StatusMessageType.USER);
					success = false;
				}
			}

			return success; 
		}

		public MemoryImage FlashMemoryImage
		{
			get
			{
				return _FlashMemoryImage;
			}
			private set
			{
				if (_FlashMemoryImage != value)
				{
					_FlashMemoryImage = value;

					OnPropertyChanged(new PropertyChangedEventArgs("FlashMemoryImage"));
				}
			}
		}
		private MemoryImage _FlashMemoryImage;

		public MemoryLayout FlashMemoryLayout
		{
			get
			{
				return _FlashMemoryLayout;
			}
			private set
			{
				if (_FlashMemoryLayout != value)
				{
					_FlashMemoryLayout = value;

					OnPropertyChanged(new PropertyChangedEventArgs("FlashMemoryLayout"));
				}
			}
		}
		private MemoryLayout _FlashMemoryLayout;
	}
}

