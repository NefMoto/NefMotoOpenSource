/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

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

Contact by Email: nyet@nyet.org
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Communication;
using Shared;

namespace ECUFlasher
{
    public partial class EepromControl : BaseUserControl
    {
        public EepromControl()
        {
            Pages = new ObservableCollection<EepromPageRow>();
            Legend = new ObservableCollection<EepromLegendItem>();
            AvailableEepromPresets = new ObservableCollection<BootmodeEepromPresetOption>(BootmodeEepromPresetOption.All);
            SelectedEepromPreset = AvailableEepromPresets[0];
            if (App?.Preferences != null)
            {
                _BackupBeforeWrite = App.Preferences.BackupBeforeWrite;
            }

            ShowEmptyLegend();
            ShowPlaceholder();

            InitializeComponent();
        }

        public ObservableCollection<EepromPageRow> Pages { get; private set; }

        public ObservableCollection<EepromLegendItem> Legend { get; private set; }

        public ObservableCollection<BootmodeEepromPresetOption> AvailableEepromPresets { get; private set; }

        public bool BackupBeforeWrite
        {
            get { return _BackupBeforeWrite; }
            set
            {
                if (_BackupBeforeWrite == value)
                {
                    return;
                }

                _BackupBeforeWrite = value;
                if (App?.Preferences != null)
                {
                    App.Preferences.BackupBeforeWrite = value;
                }

                OnPropertyChanged(new PropertyChangedEventArgs("BackupBeforeWrite"));
            }
        }
        private bool _BackupBeforeWrite = true;

        public string WritePendingText
        {
            get { return _WritePendingText; }
            private set
            {
                if (_WritePendingText != value)
                {
                    _WritePendingText = value ?? "";
                    OnPropertyChanged(new PropertyChangedEventArgs("WritePendingText"));
                    OnPropertyChanged(new PropertyChangedEventArgs("HasWritePending"));
                }
            }
        }
        private string _WritePendingText = "";

        public bool HasWritePending
        {
            get { return _WritePendingText.Length > 0; }
        }

        public BootmodeEepromPresetOption SelectedEepromPreset
        {
            get { return _SelectedEepromPreset; }
            set
            {
                if (_SelectedEepromPreset != value)
                {
                    _SelectedEepromPreset = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("SelectedEepromPreset"));
                }
            }
        }
        private BootmodeEepromPresetOption _SelectedEepromPreset;

        public ReactiveCommand OpenEepromCommand
        {
            get
            {
                if (_OpenEepromCommand == null)
                {
                    _OpenEepromCommand = new ReactiveCommand(this.OnOpenEeprom);
                    _OpenEepromCommand.Name = "Load EEPROM File";
                    _OpenEepromCommand.Description = "Load a 512-byte 95040 image. Does not write the chip.";

                    if (App != null)
                    {
                        _OpenEepromCommand.AddWatchedProperty(App, "OperationInProgress");
                    }

                    _OpenEepromCommand.CanExecuteMethod = CanOpenEeprom;
                }

                return _OpenEepromCommand;
            }
        }
        private ReactiveCommand _OpenEepromCommand;

        public ReactiveCommand ReadBootmodeEepromCommand
        {
            get
            {
                if (_ReadBootmodeEepromCommand == null)
                {
                    _ReadBootmodeEepromCommand = CreateBootmodeEepromCommand(
                        this.OnReadBootmodeEeprom,
                        "Read EEPROM",
                        "Read physical SPI EEPROM (95040) via bootmode; overwrites flash driver until reloaded");
                }

                return _ReadBootmodeEepromCommand;
            }
        }
        private ReactiveCommand _ReadBootmodeEepromCommand;

        public ReactiveCommand SaveEepromCommand
        {
            get
            {
                if (_SaveEepromCommand == null)
                {
                    _SaveEepromCommand = new ReactiveCommand(this.OnSaveEeprom);
                    _SaveEepromCommand.Name = "Save EEPROM File";
                    _SaveEepromCommand.Description = "Save the 512-byte image on screen. Does not write the chip.";

                    if (App != null)
                    {
                        _SaveEepromCommand.AddWatchedProperty(App, "OperationInProgress");
                        _SaveEepromCommand.AddWatchedProperty(this, "HasEepromImage");
                    }

                    _SaveEepromCommand.CanExecuteMethod = CanSaveEeprom;
                }

                return _SaveEepromCommand;
            }
        }
        private ReactiveCommand _SaveEepromCommand;

        public bool HasEepromImage
        {
            get { return mDisplayedImage != null; }
        }

        private byte[] mDisplayedImage;

        public ReactiveCommand WriteBootmodeEepromCommand
        {
            get
            {
                if (_WriteBootmodeEepromCommand == null)
                {
                    _WriteBootmodeEepromCommand = CreateBootmodeEepromCommand(
                        this.OnWriteBootmodeEeprom,
                        "Write EEPROM",
                        "Write the image on screen to the physical SPI EEPROM (95040).");
                    if (App != null)
                    {
                        _WriteBootmodeEepromCommand.AddWatchedProperty(this, "HasEepromImage");
                    }

                    _WriteBootmodeEepromCommand.CanExecuteMethod = CanWriteBootmodeEeprom;
                }

                return _WriteBootmodeEepromCommand;
            }
        }
        private ReactiveCommand _WriteBootmodeEepromCommand;

        public ReactiveCommand ImmoOffCommand
        {
            get
            {
                if (_ImmoOffCommand == null)
                {
                    _ImmoOffCommand = CreateImageEditCommand(
                        () => ApplyLoadedEdit(
                            Me7Eeprom95040Immo.IsOff(mDisplayedImage) ? "Immo On" : "Immo Off",
                            Me7Eeprom95040Immo.IsOff(mDisplayedImage) ? "Immo on (not written)" : "Immo off (not written)",
                            EditImmo),
                        "Immo Off",
                        "Set 0x012 and 0x022 to 0x02 on the loaded image. Does not read or write the chip.",
                        CanApplyImmo);
                }

                return _ImmoOffCommand;
            }
        }
        private ReactiveCommand _ImmoOffCommand;

        public ReactiveCommand ResetLockoutCommand
        {
            get
            {
                if (_ResetLockoutCommand == null)
                {
                    _ResetLockoutCommand = CreateImageEditCommand(
                        () => ApplyLoadedEdit("Reset Lockout", "Reset lockout (not written)", EditLockout),
                        "Reset Lockout",
                        "Zero 0x1EC-0x1ED and 0x1FC-0x1FD on the loaded image. Does not read or write the chip.",
                        reasons => CanApplyEdit(reasons, EditLockout));
                }

                return _ResetLockoutCommand;
            }
        }
        private ReactiveCommand _ResetLockoutCommand;

        public ReactiveCommand ClearP0602Command
        {
            get
            {
                if (_ClearP0602Command == null)
                {
                    _ClearP0602Command = CreateImageEditCommand(
                        () => ApplyLoadedEdit(
                            Me7Eeprom95040Page3031.IsClear(mDisplayedImage) ? "Set P0602" : "Clear P0602",
                            Me7Eeprom95040Page3031.IsClear(mDisplayedImage) ? "Set P0602 (not written)" : "Clear P0602 (not written)",
                            EditP0602),
                        "Clear P0602",
                        "Clear bit 7 at 0x1E8 and 0x1F8 on the loaded image. Does not read or write the chip.",
                        CanApplyP0602);
                }

                return _ClearP0602Command;
            }
        }
        private ReactiveCommand _ClearP0602Command;

        private byte[] mPendingOriginal;
        private string mPendingSummary;
        private byte[] mWriteImage;
        private string mWriteCaption;

        private ReactiveCommand CreateBootmodeEepromCommand(Action execute, string name, string description)
        {
            var command = new ReactiveCommand(execute);
            command.Name = name;
            command.Description = description;

            if (App != null)
            {
                command.WatchConnection(App);
                command.AddWatchedProperty(App, "OperationInProgress");
                command.AddWatchedProperty(this, "SelectedEepromPreset");
            }

            command.CanExecuteMethod = CanExecuteBootmodeEepromCommand;
            return command;
        }

        private ReactiveCommand CreateImageEditCommand(
            Action execute,
            string name,
            string description,
            ReactiveCommand.CanExecuteDelegate canExecute)
        {
            var command = new ReactiveCommand(execute);
            command.Name = name;
            command.Description = description;

            if (App != null)
            {
                command.AddWatchedProperty(App, "OperationInProgress");
                command.AddWatchedProperty(this, "HasEepromImage");
            }

            command.CanExecuteMethod = canExecute;
            return command;
        }

        private bool CanEditLoadedImage(List<string> reasonsDisabled)
        {
            if (App == null)
            {
                reasonsDisabled.Add("Internal program error");
                return false;
            }

            if (App.OperationInProgress)
            {
                reasonsDisabled.Add("Another operation is in progress");
                return false;
            }

            if (mDisplayedImage == null || mDisplayedImage.Length != Me7Eeprom95040Checksum.EepromSize)
            {
                reasonsDisabled.Add("No EEPROM image loaded");
                return false;
            }

            return true;
        }

        private bool CanApplyImmo(List<string> reasonsDisabled)
        {
            bool turnOn = Me7Eeprom95040Immo.IsOff(mDisplayedImage);
            if (_ImmoOffCommand != null)
            {
                _ImmoOffCommand.Name = turnOn ? "Immo On" : "Immo Off";
                _ImmoOffCommand.Description = turnOn
                    ? "Set 0x012 and 0x022 to 0x01 on the loaded image. Does not read or write the chip."
                    : "Set 0x012 and 0x022 to 0x02 on the loaded image. Does not read or write the chip.";
            }

            return CanApplyEdit(reasonsDisabled, EditImmo);
        }

        private bool CanApplyP0602(List<string> reasonsDisabled)
        {
            bool set = Me7Eeprom95040Page3031.IsClear(mDisplayedImage);
            if (_ClearP0602Command != null)
            {
                _ClearP0602Command.Name = set ? "Set P0602" : "Clear P0602";
                _ClearP0602Command.Description = set
                    ? "Set bit 7 at 0x1E8 and 0x1F8 on the loaded image. Does not read or write the chip."
                    : "Clear bit 7 at 0x1E8 and 0x1F8 on the loaded image. Does not read or write the chip.";
            }

            return CanApplyEdit(reasonsDisabled, EditP0602);
        }

        private bool CanApplyEdit(List<string> reasonsDisabled, Func<byte[], string> edit)
        {
            if (!CanEditLoadedImage(reasonsDisabled))
            {
                return false;
            }

            string blocked = edit((byte[])mDisplayedImage.Clone());
            if (blocked != null)
            {
                reasonsDisabled.Add(blocked);
                return false;
            }

            return true;
        }

        private bool CanOpenEeprom(List<string> reasonsDisabled)
        {
            if (App == null)
            {
                reasonsDisabled.Add("Internal program error");
                return false;
            }

            if (App.OperationInProgress)
            {
                reasonsDisabled.Add("Another operation is in progress");
                return false;
            }

            return true;
        }

        private bool CanSaveEeprom(List<string> reasonsDisabled)
        {
            if (!CanOpenEeprom(reasonsDisabled))
            {
                return false;
            }

            if (!HasEepromImage)
            {
                reasonsDisabled.Add("No 512-byte EEPROM image loaded");
                return false;
            }

            return true;
        }

        private bool CanExecuteBootmodeEepromCommand(List<string> reasonsDisabled)
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

            if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode)
            {
                reasonsDisabled.Add("Requires BootMode protocol (not KWP)");
                result = false;
            }

            if (SelectedEepromPreset == null)
            {
                reasonsDisabled.Add("No EEPROM preset selected");
                result = false;
            }

            if (App.OperationInProgress)
            {
                reasonsDisabled.Add("Another operation is in progress");
                result = false;
            }

            return result;
        }

        private bool CanWriteBootmodeEeprom(List<string> reasonsDisabled)
        {
            bool result = CanExecuteBootmodeEepromCommand(reasonsDisabled);
            if (!HasEepromImage)
            {
                reasonsDisabled.Add("No EEPROM image loaded");
                result = false;
            }

            return result;
        }

        private void OnOpenEeprom()
        {
            if (!OpenEepromCommand.IsEnabled)
            {
                return;
            }

            var dialog = new OpenFileDialog();
            dialog.DefaultExt = ".bin";
            dialog.Filter = "EEPROM binary (*.bin)|*.bin|All files (*.*)|*.*";
            dialog.CheckFileExists = true;
            dialog.Title = "Load EEPROM File";

            if (App.ShowFileDialog(dialog) != true)
            {
                return;
            }

            byte[] image;
            try
            {
                image = File.ReadAllBytes(dialog.FileName);
            }
            catch (Exception ex)
            {
                App.DisplayStatusMessage("Failed to read EEPROM file: " + ex.Message, StatusMessageType.USER);
                return;
            }

            if (image.Length != Me7Eeprom95040Checksum.EepromSize)
            {
                App.DisplayStatusMessage(
                    "EEPROM file is " + image.Length + " bytes; a 95040 image is 512.",
                    StatusMessageType.USER);
                return;
            }

            ShowImage(image, "Opened EEPROM image: " + dialog.FileName);
        }

        private void OnSaveEeprom()
        {
            if (!SaveEepromCommand.IsEnabled || mDisplayedImage == null)
            {
                return;
            }

            // This button saves the image on screen. Backup before write applies only to Write EEPROM.
            TrySaveBytes(mDisplayedImage, "Save EEPROM File", "eeprom-95040.bin", "EEPROM", out _);
        }

        private void OnReadBootmodeEeprom()
        {
            StartRead();
        }

        private void ApplyLoadedEdit(string title, string caption, Func<byte[], string> edit)
        {
            if (mDisplayedImage == null)
            {
                return;
            }

            if (mPendingOriginal == null)
            {
                mPendingOriginal = (byte[])mDisplayedImage.Clone();
            }

            byte[] patched = (byte[])mDisplayedImage.Clone();
            string blocked = edit(patched);
            if (blocked != null)
            {
                App.DisplayStatusMessage(blocked, StatusMessageType.USER);
                return;
            }

            mPendingSummary = string.IsNullOrEmpty(mPendingSummary) ? title : mPendingSummary + ", " + title;
            ShowImage(patched, caption, true);
            WritePendingText = "Write pending: " + mPendingSummary + ". Click Write EEPROM to program the chip.";
        }

        private static string EditImmo(byte[] image)
        {
            if (Me7Eeprom95040Immo.IsOff(image))
            {
                return Me7Eeprom95040Immo.TryEnable(image) == Me7Eeprom95040Immo.Result.Applied
                    ? null
                    : "Immo bytes do not match";
            }

            Me7Eeprom95040Immo.Result result = Me7Eeprom95040Immo.TryDisable(image);
            if (result == Me7Eeprom95040Immo.Result.AlreadyOff)
            {
                return "Immo is already off";
            }

            if (result != Me7Eeprom95040Immo.Result.Applied)
            {
                return "Immo bytes do not match";
            }

            return null;
        }

        private static string EditLockout(byte[] image)
        {
            return Me7Eeprom95040Page3031.TryResetLockout(image) == Me7Eeprom95040Page3031.Result.Applied
                ? null
                : "Lockout bytes are already 00 00";
        }

        private static string EditP0602(byte[] image)
        {
            if (Me7Eeprom95040Page3031.IsClear(image))
            {
                return Me7Eeprom95040Page3031.TrySetP0602(image) == Me7Eeprom95040Page3031.Result.Applied
                    ? null
                    : "P0602 bit 7 is already set";
            }

            return Me7Eeprom95040Page3031.TryClearP0602(image) == Me7Eeprom95040Page3031.Result.Applied
                ? null
                : "P0602 bit 7 is already clear";
        }

        private void StartRead()
        {
            if (SelectedEepromPreset == null || !ReadBootmodeEepromCommand.IsEnabled)
            {
                return;
            }

            var bootstrap = TryGetBootstrapInterface();
            if (bootstrap == null)
            {
                return;
            }

            var operation = new BootmodeReadEepromOperation(bootstrap, SelectedEepromPreset.Settings);
            StartBootmodeEepromOperation(operation, OnReadBootmodeEepromCompleted);
        }

        private void OnReadBootmodeEepromCompleted(Operation operation, bool success)
        {
            // BeginInvoke so a dialog in the completion does not block the comm thread.
            Dispatcher.BeginInvoke((Action)(() =>
            {
                operation.CompletedOperationEvent -= OnReadBootmodeEepromCompleted;

                var readOp = operation as BootmodeReadEepromOperation;
                byte[] image = null;
                if (success && readOp != null && readOp.ReadMemory != null && readOp.ReadMemory.RawData != null)
                {
                    image = (byte[])readOp.ReadMemory.RawData.Clone();
                }

                if (image == null)
                {
                    FinishBootmodeEepromUi(false, "Reading EEPROM failed.");
                    return;
                }

                ShowImage(image, null);
                string statusMessage = "Reading EEPROM succeeded in: "
                    + FormatOperationElapsed(operation.OperationElapsedTime) + ".";
                FinishBootmodeEepromUi(true, statusMessage);
            }), null);
        }

        private void OnWriteBootmodeEeprom()
        {
            if (!WriteBootmodeEepromCommand.IsEnabled || SelectedEepromPreset == null)
            {
                return;
            }

            var bootstrap = TryGetBootstrapInterface();
            if (bootstrap == null)
            {
                return;
            }

            if (mDisplayedImage == null)
            {
                App.DisplayStatusMessage("No EEPROM image loaded.", StatusMessageType.USER);
                return;
            }

            if (mPendingOriginal != null && chkBackupBeforeWrite.IsChecked == true)
            {
                string backupPath;
                if (!TrySaveBytes(mPendingOriginal, "Save EEPROM backup before write", "eeprom-95040-backup.bin", "EEPROM backup", out backupPath))
                {
                    App.DisplayStatusMessage("Write cancelled. Backup was not saved. Chip was not modified.", StatusMessageType.USER);
                    return;
                }
            }

            byte[] dataToWrite = (byte[])mDisplayedImage.Clone();
            string sourceName = mPendingSummary == null
                ? "On-screen image"
                : "On-screen image (" + mPendingSummary + ")";

            if (SelectedEepromPreset.Settings.EepromType == BootstrapInterface.BootmodeEepromType.Type95040
                && dataToWrite.Length >= Me7Eeprom95040Checksum.EepromSize)
            {
                var checksum = Me7Eeprom95040Checksum.Validate(dataToWrite);
                if (!checksum.AllChecksumPagesValid)
                {
                    string csPrompt =
                        "ME7 95040 data-page checksums are invalid (" + checksum.PagesInvalid + " bad / " + checksum.PagesChecked + " checked).\n"
                        + "Pages 28-29 (HW/SW ID) are ignored and will not be changed.\n\n"
                        + "Yes = correct data-page checksums, then continue\n"
                        + "No = write the file as-is\n"
                        + "Cancel = abort";

                    UserPromptResult csResult = App.DisplayUserPrompt(
                        "EEPROM Checksums Invalid",
                        csPrompt,
                        UserPromptType.YES_NO_CANCEL);

                    if (csResult == UserPromptResult.CANCEL || csResult == UserPromptResult.NONE)
                    {
                        return;
                    }

                    if (csResult == UserPromptResult.YES)
                    {
                        int pagesUpdated = Me7Eeprom95040Checksum.CorrectChecksums(dataToWrite);
                        App.DisplayStatusMessage(
                            "Corrected " + pagesUpdated + " EEPROM data-page checksum(s); pages 28-29 left unchanged.",
                            StatusMessageType.USER);
                    }
                }
            }

            bool verify = chkVerifyWrite.IsChecked == true;

            string confirm =
                "WRITE physical SPI EEPROM (not KWP mirror).\n\n"
                + "Source: " + sourceName + "\n"
                + "Size: " + dataToWrite.Length + " bytes\n"
                + "Preset: " + SelectedEepromPreset.DisplayName + "\n"
                + "Verify after write: " + (verify ? "yes" : "no") + "\n\n"
                + "This can affect immobilizer / VIN / adaptations.\n"
                + (chkBackupBeforeWrite.IsChecked == true
                    ? "Backup before write is on.\n"
                    : "Backup before write is off. No backup file will be saved.\n")
                + "Wrong image can brick the ECU.\n"
                + "Flash driver at 0xF600 will be overwritten.\n\n"
                + "Continue?";

            if (App.DisplayUserPrompt("Confirm Bootmode EEPROM Write", confirm, UserPromptType.OK_CANCEL) != UserPromptResult.OK)
            {
                return;
            }

            mWriteImage = dataToWrite.Length == Me7Eeprom95040Checksum.EepromSize ? dataToWrite : null;
            mWriteCaption = verify ? "Written and verified" : "Written (not verified)";
            var operation = new BootmodeWriteEepromOperation(
                bootstrap,
                SelectedEepromPreset.Settings,
                dataToWrite,
                verify);
            StartBootmodeEepromOperation(operation, OnWriteBootmodeEepromCompleted);
        }

        private void OnWriteBootmodeEepromCompleted(Operation operation, bool success)
        {
            // BeginInvoke so a failure dialog does not block the comm thread.
            Dispatcher.BeginInvoke((Action)(() =>
            {
                operation.CompletedOperationEvent -= OnWriteBootmodeEepromCompleted;

                byte[] written = mWriteImage;
                string caption = mWriteCaption;
                mWriteImage = null;
                mWriteCaption = null;

                if (written != null)
                {
                    ShowImage(written, success ? caption : null, !success);
                }

                string statusMessage = success
                    ? "Writing EEPROM succeeded in: " + FormatOperationElapsed(operation.OperationElapsedTime) + "."
                    : "Writing EEPROM failed.";

                FinishBootmodeEepromUi(success, statusMessage, "Bootmode EEPROM Write Finished");
            }), null);
        }

        private void ShowImage(byte[] image, string caption, bool keepPending = false)
        {
            if (!keepPending)
            {
                mPendingOriginal = null;
                mPendingSummary = null;
                WritePendingText = "";
            }

            if (!string.IsNullOrEmpty(caption))
            {
                App.DisplayStatusMessage(caption, StatusMessageType.USER);
            }
            Pages.Clear();
            mDisplayedImage = (image != null && image.Length == Me7Eeprom95040Checksum.EepromSize)
                ? image
                : null;
            OnPropertyChanged(new PropertyChangedEventArgs("HasEepromImage"));

            if (mDisplayedImage == null)
            {
                ShowEmptyLegend();
                ShowPlaceholder();
                return;
            }

            var validation = Me7Eeprom95040Checksum.Validate(image);
            ShowLegend(image, validation.AllChecksumPagesValid);

            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                Pages.Add(BuildRow(image, page, validation.PageChecksumKinds[page]));
            }
        }

        private void ShowEmptyLegend()
        {
            SetLegend(false, "--", "--", "--", "--");
        }

        private void ShowLegend(byte[] image, bool checksumsOk)
        {
            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            SetLegend(
                true,
                Me7Eeprom95040Fields.FormatImmoValue(ee),
                Me7Eeprom95040Fields.FormatSkcValue(ee),
                Me7Eeprom95040Fields.FormatP0602Value(ee),
                Me7Eeprom95040Fields.FormatLockoutValue(ee),
                checksumsOk);
        }

        private void SetLegend(bool loaded, string immo, string skc, string p0602, string lockout, bool checksumsOk = false)
        {
            string kind = loaded ? null : "Empty";
            Legend.Clear();
            Legend.Add(Item(loaded ? "ImmoStatus" : kind, immo, "Immo"));
            Legend.Add(Item(loaded ? "Skc" : kind, skc, "SKC"));
            Legend.Add(Item(loaded ? "P0602Flag" : kind, p0602, "P0602"));
            Legend.Add(Item(loaded ? "Lockout" : kind, lockout, "Lockout"));
            if (loaded)
            {
                Legend.Add(Item(checksumsOk ? "ChecksumOk" : "ChecksumBad", checksumsOk ? "ok" : "not ok", "Checksum"));
            }
            else
            {
                Legend.Add(Item("Empty", "--", "Checksum"));
            }
        }

        private static EepromLegendItem Item(string kind, string swatch, string caption)
        {
            return new EepromLegendItem
            {
                Kind = kind,
                Swatch = swatch,
                Caption = caption
            };
        }

        private void ShowPlaceholder()
        {
            Pages.Clear();
            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                Pages.Add(BuildEmptyRow(page));
            }
        }

        private static EepromPageRow BuildEmptyRow(int page)
        {
            int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
            var cells = new List<EepromByteCell>(Me7Eeprom95040Checksum.PageSize);
            for (int index = 0; index < Me7Eeprom95040Checksum.PageSize; index++)
            {
                cells.Add(new EepromByteCell
                {
                    Hex = "00",
                    Kind = "Empty",
                    ToolTip = "No EEPROM loaded"
                });
            }

            var ascii = new List<EepromAsciiChar>(Me7Eeprom95040Checksum.PageSize);
            for (int index = 0; index < Me7Eeprom95040Checksum.PageSize; index++)
            {
                ascii.Add(new EepromAsciiChar
                {
                    Text = ".",
                    Kind = "Empty",
                    ToolTip = "No EEPROM loaded"
                });
            }

            return new EepromPageRow
            {
                Offset = "0x" + pageOffset.ToString("X3"),
                Ascii = ascii,
                Kind = "Empty",
                Cells = cells
            };
        }

        private static EepromPageRow BuildRow(byte[] image, int page, Me7Eeprom95040Checksum.PageChecksumKind pageKind)
        {
            int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
            var cells = new List<EepromByteCell>(Me7Eeprom95040Checksum.PageSize);
            var ascii = new List<EepromAsciiChar>(Me7Eeprom95040Checksum.PageSize);

            for (int index = 0; index < Me7Eeprom95040Checksum.PageSize; index++)
            {
                int offset = pageOffset + index;
                byte value = image[offset];
                ascii.Add(AsciiChar(offset, value));
                cells.Add(new EepromByteCell
                {
                    Hex = value.ToString("X2"),
                    Kind = CellKind(pageKind, index, Me7Eeprom95040Fields.RoleAt(offset)),
                    ToolTip = CellToolTip(image, page, index, pageKind)
                });
            }

            return new EepromPageRow
            {
                Offset = "0x" + pageOffset.ToString("X3"),
                Ascii = ascii,
                Cells = cells
            };
        }

        private static EepromAsciiChar AsciiChar(int offset, byte value)
        {
            bool printable = value >= 32 && value <= 126;
            char text = printable ? (char)value : '.';
            string kind = "Plain";
            string toolTip = null;
            if (Me7Eeprom95040Fields.IsEcuPartNumber(offset))
            {
                kind = "EcuPn";
                toolTip = "ECU part number";
            }
            else if (Me7Eeprom95040Fields.IsToolId(offset))
            {
                kind = "ToolId";
                toolTip = "Tool ID";
            }
            else if (Me7Eeprom95040Fields.IsVin(offset))
            {
                kind = "Vin";
                toolTip = "VIN";
            }
            else if (Me7Eeprom95040Fields.IsImmoId(offset))
            {
                kind = "ImmoId";
                toolTip = "Immobilizer ID";
            }

            return new EepromAsciiChar
            {
                Text = text.ToString(),
                Kind = kind,
                ToolTip = toolTip
            };
        }

        private static string CellKind(
            Me7Eeprom95040Checksum.PageChecksumKind pageKind,
            int indexInPage,
            Me7Eeprom95040Fields.Role role)
        {
            if (indexInPage >= 14)
            {
                if (pageKind == Me7Eeprom95040Checksum.PageChecksumKind.Ok)
                {
                    return "ChecksumOk";
                }

                if (pageKind == Me7Eeprom95040Checksum.PageChecksumKind.Bad)
                {
                    return "ChecksumBad";
                }

                if (pageKind == Me7Eeprom95040Checksum.PageChecksumKind.Exempt)
                {
                    return "ChecksumExempt";
                }
            }

            if (role != Me7Eeprom95040Fields.Role.None)
            {
                return role.ToString();
            }

            return "Plain";
        }

        private static string CellToolTip(
            byte[] image,
            int page,
            int indexInPage,
            Me7Eeprom95040Checksum.PageChecksumKind pageKind)
        {
            int offset = (page * Me7Eeprom95040Checksum.PageSize) + indexInPage;
            Me7Eeprom95040Fields.Role role = Me7Eeprom95040Fields.RoleAt(offset);

            if (indexInPage >= 14 && pageKind != Me7Eeprom95040Checksum.PageChecksumKind.None)
            {
                int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
                ushort descriptor = Me7Eeprom95040Checksum.GetPageDescriptor(page);
                ushort expected = Me7Eeprom95040Checksum.CalculatePageChecksum(
                    image,
                    pageOffset,
                    (ushort)page,
                    descriptor);
                ushort stored = (ushort)(image[pageOffset + 14] | (image[pageOffset + 15] << 8));

                if (pageKind == Me7Eeprom95040Checksum.PageChecksumKind.Exempt)
                {
                    return "Page " + page + " HW/SW ID. Checksum not enforced (stored 0x" + stored.ToString("X4") + ")";
                }

                if (pageKind == Me7Eeprom95040Checksum.PageChecksumKind.Ok)
                {
                    return "Page " + page + " checksum OK (0x" + stored.ToString("X4") + ")";
                }

                return "Page " + page + " checksum bad (stored 0x" + stored.ToString("X4")
                    + ", expected 0x" + expected.ToString("X4") + ")";
            }

            if (role == Me7Eeprom95040Fields.Role.ImmoStatus)
            {
                return "Immo status at 0x" + offset.ToString("X3") + ". 0x01 on, 0x02 off";
            }

            if (role == Me7Eeprom95040Fields.Role.Skc)
            {
                return "SKC";
            }

            if (role == Me7Eeprom95040Fields.Role.P0602Flag)
            {
                bool set = (image[offset] & 0x80) != 0;
                return "P0602 flag, bit 7 " + (set ? "set" : "clear");
            }

            if (role == Me7Eeprom95040Fields.Role.Lockout)
            {
                return "Level-3 lockout";
            }

            return "0x" + offset.ToString("X3");
        }

        private BootstrapInterface TryGetBootstrapInterface()
        {
            var bootstrap = App.CommInterface as BootstrapInterface;
            if (bootstrap == null)
            {
                App.DisplayStatusMessage("Bootmode interface not available.", StatusMessageType.USER);
            }

            return bootstrap;
        }

        private void StartBootmodeEepromOperation(CommunicationOperation operation, Operation.CompletedOperationDelegate completedHandler)
        {
            App.OperationInProgress = true;
            App.PercentOperationComplete = 0.0f;
            operation.CompletedOperationEvent += completedHandler;
            App.CurrentOperation = operation;
            operation.Start();
        }

        private static string FormatOperationElapsed(TimeSpan elapsed)
        {
            return elapsed.Hours.ToString("D2") + ":"
                + elapsed.Minutes.ToString("D2") + ":"
                + elapsed.Seconds.ToString("D2");
        }

        private void ClearOperationState()
        {
            App.CurrentOperation = null;
            App.OperationInProgress = false;
        }

        private void FinishBootmodeEepromUi(bool success, string statusMessage, string failureTitle = null)
        {
            App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
            if (!success && failureTitle != null)
            {
                App.DisplayUserPrompt(failureTitle, statusMessage, UserPromptType.OK);
            }

            if (success)
            {
                App.PercentOperationComplete = 100.0f;
            }

            App.CurrentOperation = null;
            App.OperationInProgress = false;
        }

        private bool TrySaveBytes(byte[] data, string title, string defaultName, string savedKind, out string path)
        {
            path = null;

            var dialog = new SaveFileDialog();
            dialog.DefaultExt = ".bin";
            dialog.Filter = "EEPROM binary (*.bin)|*.bin|All files (*.*)|*.*";
            dialog.AddExtension = true;
            dialog.OverwritePrompt = true;
            dialog.Title = title;
            dialog.FileName = defaultName;

            if (App.ShowFileDialog(dialog) != true)
            {
                return false;
            }

            try
            {
                File.WriteAllBytes(dialog.FileName, data);
            }
            catch (Exception ex)
            {
                App.DisplayStatusMessage("Failed to save " + savedKind + ": " + ex.Message, StatusMessageType.USER);
                return false;
            }

            path = dialog.FileName;
            App.DisplayStatusMessage("Saved " + savedKind + " to: " + path, StatusMessageType.USER);
            return true;
        }

    }

    public sealed class EepromLegendItem
    {
        public string Kind { get; set; }
        public string Swatch { get; set; }
        public string Caption { get; set; }
    }

    public sealed class EepromPageRow
    {
        public string Offset { get; set; }
        public IList<EepromAsciiChar> Ascii { get; set; }
        public string Kind { get; set; }
        public IList<EepromByteCell> Cells { get; set; }
    }

    public sealed class EepromAsciiChar
    {
        public string Text { get; set; }
        public string Kind { get; set; }
        public string ToolTip { get; set; }
    }

    public sealed class EepromByteCell
    {
        public string Hex { get; set; }
        public string Kind { get; set; }
        public string ToolTip { get; set; }
    }
}

// vi: set sw=4 ts=8 expandtab:
