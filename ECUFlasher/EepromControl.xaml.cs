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
            AvailableChipSelects = new ObservableCollection<BootmodeEepromCatalog.ChipSelect>(BootmodeEepromCatalog.ChipSelects);
            AvailableParts = new ObservableCollection<BootmodeEepromCatalog.Part>();
            _SscPeripheral = new EepromPeripheralOption(BootstrapInterface.BootmodeEepromPeriph.SSC);
            AvailablePeripherals = new ObservableCollection<EepromPeripheralOption>();
            AvailablePeripherals.Add(_SscPeripheral);
#if DEBUG
            _XsscPeripheral = new EepromPeripheralOption(BootstrapInterface.BootmodeEepromPeriph.XSSC);
            AvailablePeripherals.Add(_XsscPeripheral);
#endif
            _loadingSelection = true;
            ApplySavedSelection();
            _loadingSelection = false;
            if (App?.Preferences != null)
            {
                _BackupBeforeWrite = App.Preferences.BackupBeforeWrite;
            }

            ShowEmptyLegend();
            ShowPlaceholder();

            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public ObservableCollection<EepromPageRow> Pages { get; private set; }

        public ObservableCollection<EepromLegendItem> Legend { get; private set; }

        public ObservableCollection<BootmodeEepromCatalog.ChipSelect> AvailableChipSelects { get; private set; }

        public ObservableCollection<BootmodeEepromCatalog.Part> AvailableParts { get; private set; }

        public ObservableCollection<EepromPeripheralOption> AvailablePeripherals { get; private set; }

        public string PartSizeText
        {
            get { return _SelectedPart == null ? "" : _SelectedPart.Size + " B"; }
        }

        public bool ShowPeripheral
        {
            get
            {
#if DEBUG
                return _SelectedChipSelect != null
                    && !_SelectedChipSelect.I2c
                    && _SelectedPart != null
                    && _SelectedPart.AllowsXssc;
#else
                return false;
#endif
            }
        }

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

        public BootmodeEepromCatalog.ChipSelect SelectedChipSelect
        {
            get { return _SelectedChipSelect; }
            set
            {
                if (value == null || ReferenceEquals(_SelectedChipSelect, value))
                {
                    return;
                }

                bool wasI2c = _SelectedChipSelect != null && _SelectedChipSelect.I2c;
                _SelectedChipSelect = value;
                OnPropertyChanged(new PropertyChangedEventArgs("SelectedChipSelect"));
                if (wasI2c != value.I2c)
                {
                    FillPartList(value.I2c);
                    SelectedPart = value.I2c
                        ? BootmodeEepromCatalog.DefaultI2cPart
                        : BootmodeEepromCatalog.DefaultSpiPart;
                    return;
                }

                PersistSelection();
            }
        }
        private BootmodeEepromCatalog.ChipSelect _SelectedChipSelect;
        private bool _loadingSelection;
        private EepromPeripheralOption _SscPeripheral;
#if DEBUG
        private EepromPeripheralOption _XsscPeripheral;
#endif

        public BootmodeEepromCatalog.Part SelectedPart
        {
            get { return _SelectedPart; }
            set
            {
                if (value == null || ReferenceEquals(_SelectedPart, value))
                {
                    return;
                }

                _SelectedPart = value;
                OnPropertyChanged(new PropertyChangedEventArgs("SelectedPart"));
                OnPropertyChanged(new PropertyChangedEventArgs("PartSizeText"));
                SnapPeripheral();
                OnPropertyChanged(new PropertyChangedEventArgs("ShowPeripheral"));
                PersistSelection();
                RefreshGrid();
            }
        }
        private BootmodeEepromCatalog.Part _SelectedPart;

        public EepromPeripheralOption SelectedPeripheral
        {
            get { return _SelectedPeripheral; }
            set
            {
                if (value == null || ReferenceEquals(_SelectedPeripheral, value))
                {
                    return;
                }

                _SelectedPeripheral = value;
                OnPropertyChanged(new PropertyChangedEventArgs("SelectedPeripheral"));
                PersistSelection();
            }
        }
        private EepromPeripheralOption _SelectedPeripheral;

        private void ApplySavedSelection()
        {
            var prefs = App?.Preferences;
            BootmodeEepromCatalog.ChipSelect chip = BootmodeEepromCatalog.FindChipSelect(prefs?.EepromChipSelect)
                ?? BootmodeEepromCatalog.ChipSelects[0];
            bool i2c = chip.I2c;
            BootmodeEepromCatalog.Part part = BootmodeEepromCatalog.FindPart(i2c, prefs?.EepromPart)
                ?? (i2c ? BootmodeEepromCatalog.DefaultI2cPart : BootmodeEepromCatalog.DefaultSpiPart);
            FillPartList(i2c);
            _SelectedChipSelect = chip;
            _SelectedPart = part;
            _SelectedPeripheral = _SscPeripheral;
#if DEBUG
            if (!i2c
                && part.AllowsXssc
                && prefs != null
                && prefs.EepromPeriph == BootstrapInterface.BootmodeEepromPeriph.XSSC)
            {
                _SelectedPeripheral = _XsscPeripheral;
            }
#endif
        }

        private void FillPartList(bool i2c)
        {
            AvailableParts.Clear();
            foreach (BootmodeEepromCatalog.Part part in i2c
                ? BootmodeEepromCatalog.I2cParts
                : BootmodeEepromCatalog.SpiParts)
            {
                AvailableParts.Add(part);
            }
        }

        private void SnapPeripheral()
        {
            if (_SelectedPeripheral != null
                && _SelectedPeripheral.Periph == BootstrapInterface.BootmodeEepromPeriph.XSSC
                && !ShowPeripheral)
            {
                SelectedPeripheral = _SscPeripheral;
            }
        }

        private BootstrapInterface.BootmodeEepromPeriph CurrentPeriph()
        {
            if (_SelectedChipSelect != null && _SelectedChipSelect.I2c)
            {
                return BootstrapInterface.BootmodeEepromPeriph.I2C;
            }

            if (ShowPeripheral
                && _SelectedPeripheral != null
                && _SelectedPeripheral.Periph == BootstrapInterface.BootmodeEepromPeriph.XSSC)
            {
                return BootstrapInterface.BootmodeEepromPeriph.XSSC;
            }

            return BootstrapInterface.BootmodeEepromPeriph.SSC;
        }

        private void PersistSelection()
        {
            if (_loadingSelection || App?.Preferences == null || _SelectedChipSelect == null || _SelectedPart == null)
            {
                return;
            }

            App.Preferences.EepromChipSelect = _SelectedChipSelect.DisplayName;
            App.Preferences.EepromPeriph = CurrentPeriph();
            App.Preferences.EepromPart = _SelectedPart.DisplayName;
        }

        private BootstrapInterface.BootmodeEepromSettings CurrentSettings()
        {
            if (SelectedChipSelect == null || SelectedPart == null)
            {
                return null;
            }

            return BootmodeEepromCatalog.ToSettings(SelectedChipSelect, CurrentPeriph(), SelectedPart);
        }

        private bool Is95040Layout(byte[] image)
        {
            return IsNamedPart(image, "95040");
        }

        private bool Is95P08Layout(byte[] image)
        {
            return IsNamedPart(image, "95P08");
        }

        private bool HasPageChecksum(byte[] image)
        {
            return Is95040Layout(image) || Is95P08Layout(image);
        }

        private bool IsNamedPart(byte[] image, string name)
        {
            return SelectedPart != null
                && SelectedPart.DisplayName == name
                && image != null
                && image.Length == (int)SelectedPart.Size;
        }

        private bool ImmoIsOff(byte[] image)
        {
            if (Is95P08Layout(image))
            {
                return Me7Eeprom95P08Immo.IsOff(image);
            }

            return Me7Eeprom95040Immo.IsOff(image);
        }

        public ReactiveCommand OpenEepromCommand
        {
            get
            {
                if (_OpenEepromCommand == null)
                {
                    _OpenEepromCommand = new ReactiveCommand(this.OnOpenEeprom);
                    _OpenEepromCommand.Name = "Load EEPROM File";
                    _OpenEepromCommand.Description = "Load an EEPROM image of the selected part size. Does not write the chip.";

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

        public ReactiveCommand ReadEepromCommand
        {
            get
            {
                if (_ReadEepromCommand == null)
                {
                    _ReadEepromCommand = CreateBootmodeEepromCommand(
                        this.OnReadEeprom,
                        "Read EEPROM",
                        "Bootmode: read the physical EEPROM; overwrites flash driver until reloaded. KWP2000: read the 95040 through the mirror at 0x600000 and fill withheld pages from the RAM shadow. Held KWP1281: read the 24C02.");
                    _ReadEepromCommand.CanExecuteMethod = reasons => (App?.CommInterface?.CurrentProtocol == CommunicationInterface.Protocol.KWP2000)
                        ? CanExecuteOnProtocol(reasons, CommunicationInterface.Protocol.KWP2000)
                        : CanExecuteBootmodeEepromCommand(reasons);
                }

                return _ReadEepromCommand;
            }
        }
        private ReactiveCommand _ReadEepromCommand;

        public ReactiveCommand SaveEepromCommand
        {
            get
            {
                if (_SaveEepromCommand == null)
                {
                    _SaveEepromCommand = new ReactiveCommand(this.OnSaveEeprom);
                    _SaveEepromCommand.Name = "Save EEPROM File";
                    _SaveEepromCommand.Description = "Save the image on screen. Does not write the chip.";

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
        private FlashingControl mFlashing;

        // Pages of the on-screen image a KWP read could not get; they are FF, not chip data.
        private List<int> mMissingPages = new List<int>();

        public ReactiveCommand WriteBootmodeEepromCommand
        {
            get
            {
                if (_WriteBootmodeEepromCommand == null)
                {
                    _WriteBootmodeEepromCommand = CreateBootmodeEepromCommand(
                        this.OnWriteBootmodeEeprom,
                        "Write EEPROM",
                        "Write the image on screen to the physical EEPROM.");
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
                            ImmoIsOff(mDisplayedImage) ? "Immo On" : "Immo Off",
                            ImmoIsOff(mDisplayedImage) ? "Immo on (not written)" : "Immo off (not written)",
                            EditImmo),
                        "Immo Off",
                        "Set the immobilizer status bytes on the loaded image. Does not read or write the chip.",
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
                command.AddWatchedProperty(this, "SelectedChipSelect");
                command.AddWatchedProperty(this, "SelectedPart");
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
                command.WatchConnection(App);
                command.AddWatchedProperty(App, "OperationInProgress");
                command.AddWatchedProperty(this, "HasEepromImage");
                command.AddWatchedProperty(this, "SelectedPart");
            }

            command.CanExecuteMethod = canExecute;
            return command;
        }

        private bool CanUseLoadedImage(List<string> reasonsDisabled)
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

            if (mDisplayedImage == null)
            {
                reasonsDisabled.Add("No EEPROM image loaded");
                return false;
            }

            return true;
        }

        private bool CanEditLoadedImage(List<string> reasonsDisabled)
        {
            if (!CanUseLoadedImage(reasonsDisabled))
            {
                return false;
            }

            var kwpViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;
            if ((kwpViewModel != null) && kwpViewModel.BlocksMe7Commands())
            {
                reasonsDisabled.Add("Lockout and P0602 apply to an ME7 EEPROM.");
                return false;
            }

            if (!HasPageChecksum(mDisplayedImage))
            {
                reasonsDisabled.Add("Lockout and P0602 apply to a 95040 or the first 512 bytes of a 95P08");
                return false;
            }

            if (Me7Eeprom95040Checksum.MapOffset(mDisplayedImage) != 0)
            {
                reasonsDisabled.Add("Lockout and P0602 stay off when the map starts at byte 1.");
                return false;
            }

            return true;
        }

        private bool CanApplyImmo(List<string> reasonsDisabled)
        {
            if (!CanUseLoadedImage(reasonsDisabled))
            {
                return false;
            }

            var kwpViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;
            if ((kwpViewModel != null) && kwpViewModel.BlocksMe7Commands())
            {
                reasonsDisabled.Add("Immo applies to an ME7 EEPROM.");
                return false;
            }

            bool turnOn = false;
            string offsetDescription = "";

            if (Is95040Layout(mDisplayedImage))
            {
                turnOn = Me7Eeprom95040Immo.IsOff(mDisplayedImage);
                offsetDescription = "0x012 and 0x022";
            }
            else if (Is95P08Layout(mDisplayedImage))
            {
                turnOn = Me7Eeprom95P08Immo.IsOff(mDisplayedImage);
                offsetDescription = "0x012 and 0x022";
            }
            else
            {
                reasonsDisabled.Add("Immo applies to a 95040 512-byte image or a 95P08 1024-byte image");
                return false;
            }

            int mapOffset = Me7Eeprom95040Checksum.MapOffset(mDisplayedImage);
            if (mapOffset != 0 && !Me7Eeprom95040Checksum.ImmoPagesMatch(mDisplayedImage, mapOffset, out _, out _))
            {
                reasonsDisabled.Add("Immo stays off when page 1 or page 2 does not match.");
                return false;
            }

            if (_ImmoOffCommand != null)
            {
                _ImmoOffCommand.Name = turnOn ? "Immo On" : "Immo Off";
                _ImmoOffCommand.Description = turnOn
                    ? $"Set {offsetDescription} to 0x01 on the loaded image. Does not read or write the chip."
                    : $"Set {offsetDescription} to 0x02 on the loaded image. Does not read or write the chip.";
            }

            return CanApplyEdit(reasonsDisabled, EditImmo, false);
        }

        private bool CanApplyP0602(List<string> reasonsDisabled)
        {
            if (!CanEditLoadedImage(reasonsDisabled))
            {
                return false;
            }

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

        private bool CanApplyEdit(List<string> reasonsDisabled, Func<byte[], string> edit, bool require95040Layout = true)
        {
            bool ready = require95040Layout
                ? CanEditLoadedImage(reasonsDisabled)
                : CanUseLoadedImage(reasonsDisabled);
            if (!ready)
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
                reasonsDisabled.Add("No EEPROM image loaded");
                return false;
            }

            return true;
        }

        private bool CanExecuteBootmodeEepromCommand(List<string> reasonsDisabled)
        {
            bool result = CanExecuteOnProtocol(reasonsDisabled, CommunicationInterface.Protocol.BootMode);

            if (SelectedChipSelect == null || SelectedPart == null)
            {
                reasonsDisabled.Add("No EEPROM part selected");
                result = false;
            }

            return result;
        }

        private bool CanExecuteOnProtocol(List<string> reasonsDisabled, CommunicationInterface.Protocol protocol)
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

            if (App.CommInterface.CurrentProtocol != protocol)
            {
                reasonsDisabled.Add("Requires " + protocol + " protocol");
                result = false;
            }

            if ((App.CommInterface as KWP2000Interface)?.CurrentDiagnosticSessionType == KWP2000DiagnosticSessionType.ProgrammingSession)
            {
                reasonsDisabled.Add("ECU is in a programming session. Turn the ignition off first.");
                result = false;
            }

            if (protocol == CommunicationInterface.Protocol.KWP2000)
            {
                var kwpViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;
                if ((kwpViewModel != null) && kwpViewModel.BlocksMe7Commands() && !kwpViewModel.IsHeldKWP1281Session())
                {
                    reasonsDisabled.Add("EEPROM mirror read is for ME7.");
                    result = false;
                }
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
            if (mDisplayedImage == null)
            {
                reasonsDisabled.Add("No EEPROM image loaded");
                result = false;
            }
            else if (mMissingPages.Count > 0)
            {
                // Writing FF over the pages the ECU withheld would erase the SKC.
                reasonsDisabled.Add("Image is missing page(s) " + string.Join(", ", mMissingPages) + " from the KWP read. Read the chip in bootmode to write it");
                result = false;
            }
            else if (SelectedPart == null || mDisplayedImage.Length != SelectedPart.Size)
            {
                reasonsDisabled.Add("EEPROM image size does not match the selected part");
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

            BootstrapInterface.BootmodeEepromSettings settings = CurrentSettings();
            if (settings == null)
            {
                App.DisplayStatusMessage("No EEPROM device selected.", StatusMessageType.USER);
                return;
            }

            if (image.Length != settings.Size)
            {
                App.DisplayStatusMessage(
                    "EEPROM file is " + image.Length + " bytes; the selected device is " + settings.Size + " bytes.",
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
            string defaultName = Is95040Layout(mDisplayedImage) ? "eeprom-95040.bin" : "eeprom.bin";
            TrySaveBytes(mDisplayedImage, "Save EEPROM File", defaultName, "EEPROM", out _);
        }

        private void OnReadEeprom()
        {
            if (App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.KWP2000)
            {
                if ((App.CommInterfaceViewModel is KWP2000Interface_ViewModel kwpViewModel) && kwpViewModel.IsHeldKWP1281Session())
                {
                    StartKWP1281EepromRead();
                }
                else
                {
                    StartKwpRead();
                }
            }
            else
            {
                StartRead();
            }
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

        private string EditImmo(byte[] image)
        {
            if (Is95P08Layout(image))
            {
                return ImmoEdit(
                    Me7Eeprom95P08Immo.IsOff(image),
                    () => Me7Eeprom95P08Immo.TryEnable(image),
                    () => Me7Eeprom95P08Immo.TryDisable(image));
            }

            return ImmoEdit(
                Me7Eeprom95040Immo.IsOff(image),
                () => Me7Eeprom95040Immo.TryEnable(image),
                () => Me7Eeprom95040Immo.TryDisable(image));
        }

        private static string ImmoEdit(
            bool isOff,
            Func<Me7Eeprom95040Immo.Result> enable,
            Func<Me7Eeprom95040Immo.Result> disable)
        {
            if (isOff)
            {
                return enable() == Me7Eeprom95040Immo.Result.Applied ? null : "Immo bytes do not match";
            }

            Me7Eeprom95040Immo.Result result = disable();
            if (result == Me7Eeprom95040Immo.Result.AlreadyOff)
            {
                return "Immo is already off";
            }

            return result == Me7Eeprom95040Immo.Result.Applied ? null : "Immo bytes do not match";
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
            BootstrapInterface.BootmodeEepromSettings settings = CurrentSettings();
            if (settings == null || !ReadEepromCommand.IsEnabled)
            {
                return;
            }

            var bootstrap = TryGetBootstrapInterface();
            if (bootstrap == null)
            {
                return;
            }

            var operation = new BootmodeReadEepromOperation(bootstrap, settings, CurrentImageTable());
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

        private void StartKWP1281EepromRead()
        {
            if (!ReadEepromCommand.IsEnabled || !(App.CommInterfaceViewModel is KWP2000Interface_ViewModel kwpViewModel))
            {
                return;
            }

            App.DisplayStatusMessage("Reading 24C02.", StatusMessageType.USER);
            StartBootmodeEepromOperation(new KWP1281CalibrationReadOperation(kwpViewModel.KWP2000CommInterface, true), OnReadKWP1281EepromCompleted);
        }

        private void OnReadKWP1281EepromCompleted(Operation operation, bool success)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                operation.CompletedOperationEvent -= OnReadKWP1281EepromCompleted;

                var readOp = operation as KWP1281CalibrationReadOperation;
                if (!success || (readOp == null) || (readOp.Eeprom == null))
                {
                    FinishBootmodeEepromUi(false, "Reading 24C02 failed.");
                    return;
                }

                ShowImage(readOp.Eeprom, null);
                FinishBootmodeEepromUi(true, "Reading 24C02 succeeded in: " + FormatOperationElapsed(operation.OperationElapsedTime) + ".");
            }), null);
        }

        private void StartKwpRead()
        {
            if (ReadEepromCommand.IsEnabled && (App.CommInterfaceViewModel is KWP2000Interface_ViewModel kwpViewModel))
            {
                App.DisplayStatusMessage("Reading EEPROM through the KWP mirror at 0x600000.", StatusMessageType.USER);
                StartBootmodeEepromOperation(new ReadEntireSerialEEPROMOperation(kwpViewModel.KWP2000CommInterface, kwpViewModel.DesiredBaudRates), OnReadKwpEepromCompleted);
                App.PercentOperationComplete = -1.0f;   // indeterminate, like the page 30 read
            }
        }

        private void OnReadKwpEepromCompleted(Operation operation, bool success)
        {
            // BeginInvoke so the completion does not block the KWP send thread.
            Dispatcher.BeginInvoke((Action)(() =>
            {
                operation.CompletedOperationEvent -= OnReadKwpEepromCompleted;

                if (!success || !(operation is ReadEntireSerialEEPROMOperation readOp))
                {
                    FinishBootmodeEepromUi(false, "Reading EEPROM over KWP failed.");
                    return;
                }

                List<int> missing = readOp.MissingPages;
                ShowImage(readOp.Image, null, false, missing);
                FinishBootmodeEepromUi(true, "Reading EEPROM over KWP succeeded in: " + FormatOperationElapsed(operation.OperationElapsedTime) + "."
                    + ((missing.Count > 0) ? " Page(s) " + string.Join(", ", missing) + " are FF (not read). Save keeps them as FF; writing this image is blocked." : ""));
            }), null);
        }

        private void OnWriteBootmodeEeprom()
        {
            if (!WriteBootmodeEepromCommand.IsEnabled || CurrentSettings() == null)
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
                string backupName = Is95040Layout(mPendingOriginal) ? "eeprom-95040-backup.bin" : "eeprom-backup.bin";
                if (!TrySaveBytes(mPendingOriginal, "Save EEPROM backup before write", backupName, "EEPROM backup", out backupPath))
                {
                    App.DisplayStatusMessage("Write cancelled. Backup was not saved. Chip was not modified.", StatusMessageType.USER);
                    return;
                }
            }

            byte[] dataToWrite = (byte[])mDisplayedImage.Clone();
            string sourceName = mPendingSummary == null
                ? "On-screen image"
                : "On-screen image (" + mPendingSummary + ")";

            BootstrapInterface.BootmodeEepromSettings settings = CurrentSettings();
            if (settings == null)
            {
                return;
            }

            ushort[] imageTable = null;
            if (HasPageChecksum(dataToWrite))
            {
                imageTable = CurrentImageTable();
                int mapOffset = Me7Eeprom95040Checksum.MapOffset(dataToWrite, imageTable);
                if (mapOffset != 0)
                {
                    if (!ConfirmByte1Checksums(dataToWrite, mapOffset))
                    {
                        return;
                    }
                }
                else if (!ConfirmShippedChecksums(dataToWrite, imageTable))
                {
                    return;
                }
            }

            bool verify = chkVerifyWrite.IsChecked == true;

            string confirm =
                "WRITE physical SPI EEPROM (not KWP mirror).\n\n"
                + "Source: " + sourceName + "\n"
                + "Size: " + dataToWrite.Length + " bytes\n"
                + "Preset: " + SelectedChipSelect.DisplayName + ", " + CurrentPeriph() + ", " + SelectedPart.DisplayName + "\n"
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

            mWriteImage = dataToWrite;
            mWriteCaption = verify ? "Written and verified" : "Written (not verified)";
            var operation = new BootmodeWriteEepromOperation(
                bootstrap,
                settings,
                dataToWrite,
                verify,
                imageTable);
            StartBootmodeEepromOperation(operation, OnWriteBootmodeEepromCompleted);
        }

        private bool ConfirmShippedChecksums(byte[] dataToWrite, ushort[] imageTable)
        {
            var checksum = Me7Eeprom95040Checksum.Validate(dataToWrite, 0, imageTable);
            if (checksum.AllChecksumPagesValid)
            {
                return true;
            }

            string device = Is95P08Layout(dataToWrite) ? "95P08" : "95040";
            string csPrompt =
                "ME7 " + device + " data-page checksums are invalid (" + checksum.PagesInvalid + " bad / " + checksum.PagesChecked + " checked).\n"
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
                return false;
            }

            if (csResult == UserPromptResult.YES)
            {
                int pagesUpdated = Me7Eeprom95040Checksum.CorrectChecksums(dataToWrite, 0, imageTable);
                App.DisplayStatusMessage(
                    "Corrected " + pagesUpdated + " EEPROM data-page checksum(s); pages 28-29 left unchanged.",
                    StatusMessageType.USER);
            }

            return true;
        }

        private bool ConfirmByte1Checksums(byte[] dataToWrite, int mapOffset)
        {
            if (mPendingOriginal == null)
            {
                return true;
            }

            string device = Is95P08Layout(dataToWrite) ? "95P08" : "95040";
            int matches = Me7Eeprom95040Checksum.CountFormulaMatches(dataToWrite, mapOffset);
            string csPrompt =
                "ME7 " + device + " page checksums match (" + matches + " pages) starting at byte 1.\n"
                + "Pages that did not match are left unchanged.\n\n"
                + "Yes = correct checksums on pages this edit changed, then continue\n"
                + "No = write the file as-is\n"
                + "Cancel = abort";

            UserPromptResult csResult = App.DisplayUserPrompt(
                "EEPROM Checksums",
                csPrompt,
                UserPromptType.YES_NO_CANCEL);

            if (csResult == UserPromptResult.CANCEL || csResult == UserPromptResult.NONE)
            {
                return false;
            }

            if (csResult == UserPromptResult.YES)
            {
                int pagesUpdated = Me7Eeprom95040Checksum.CorrectEditedPages(dataToWrite, mPendingOriginal, mapOffset);
                App.DisplayStatusMessage(
                    "Corrected " + pagesUpdated + " EEPROM data-page checksum(s); unmatched pages left unchanged.",
                    StatusMessageType.USER);
            }

            return true;
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

        private void ShowImage(byte[] image, string caption, bool keepPending = false, List<int> missingPages = null)
        {
            if (!keepPending)
            {
                mPendingOriginal = null;
                mPendingSummary = null;
                WritePendingText = "";
                mMissingPages = missingPages ?? new List<int>();
            }

            if (!string.IsNullOrEmpty(caption))
            {
                App.DisplayStatusMessage(caption, StatusMessageType.USER);
            }
            Pages.Clear();
            mDisplayedImage = (image != null && image.Length > 0) ? image : null;
            OnPropertyChanged(new PropertyChangedEventArgs("HasEepromImage"));
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            if (mDisplayedImage == null)
            {
                ShowEmptyLegend();
                ShowPlaceholder();
                return;
            }

            Pages.Clear();
            if (HasPageChecksum(mDisplayedImage))
            {
                ushort[] imageTable = CurrentImageTable();
                int mapOffset = Me7Eeprom95040Checksum.MapOffset(mDisplayedImage, imageTable);
                int windowLength = Math.Min(
                    Me7Eeprom95040Checksum.EepromSize,
                    mDisplayedImage.Length - mapOffset);
                var window = new byte[windowLength];
                Array.Copy(mDisplayedImage, mapOffset, window, 0, windowLength);
                Me7Eeprom95040Checksum.PageChecksumKind[] kinds;
                bool checksumsOk;
                string checksumText;
                if (mapOffset == 0)
                {
                    var validation = Me7Eeprom95040Checksum.Validate(window, 0, imageTable);
                    kinds = validation.PageChecksumKinds;
                    checksumsOk = validation.AllChecksumPagesValid;
                    checksumText = null;
                }
                else
                {
                    kinds = FormulaKinds(window);
                    checksumsOk = true;
                    checksumText = Me7Eeprom95040Checksum.CountFormulaMatches(window, 0) + " pages";
                }

                byte[] legendImage = window;
                if (window.Length < Me7Eeprom95040Checksum.EepromSize)
                {
                    legendImage = new byte[Me7Eeprom95040Checksum.EepromSize];
                    Array.Copy(window, legendImage, window.Length);
                }

                ShowLegend(legendImage, checksumsOk, checksumText);
                int pageSize = Me7Eeprom95040Checksum.PageSize;
                for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
                {
                    int pageOffset = page * pageSize;
                    if (pageOffset >= window.Length)
                    {
                        break;
                    }

                    int count = Math.Min(pageSize, window.Length - pageOffset);
                    Pages.Add(count == pageSize
                        ? BuildRow(window, page, kinds[page], imageTable)
                        : BuildPlainRow(window, pageOffset, count));
                }

                if (Is95P08Layout(mDisplayedImage))
                {
                    int tail = mapOffset + window.Length;
                    while (tail < mDisplayedImage.Length)
                    {
                        int count = Math.Min(pageSize, mDisplayedImage.Length - tail);
                        Pages.Add(BuildPlainRow(mDisplayedImage, tail, count));
                        tail += count;
                    }
                }

                return;
            }

            ShowEmptyLegend();
            AddPlainRows(mDisplayedImage);
        }

        private void ShowEmptyLegend()
        {
            SetLegend(false, "--", "--", "--", "--");
        }

        private void ShowLegend(byte[] image, bool checksumsOk, string checksumText = null)
        {
            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            SetLegend(
                true,
                Me7Eeprom95040Fields.FormatImmoValue(ee),
                Me7Eeprom95040Fields.FormatSkcValue(ee),
                Me7Eeprom95040Fields.FormatP0602Value(ee),
                Me7Eeprom95040Fields.FormatLockoutValue(ee),
                checksumsOk,
                checksumText);
        }

        private void SetLegend(
            bool loaded,
            string immo,
            string skc,
            string p0602,
            string lockout,
            bool checksumsOk = false,
            string checksumText = null)
        {
            string kind = loaded ? null : "Empty";
            Legend.Clear();
            Legend.Add(Item(loaded ? "ImmoStatus" : kind, immo, "Immo"));
            Legend.Add(Item(loaded ? "Skc" : kind, skc, "SKC"));
            Legend.Add(Item(loaded ? "P0602Flag" : kind, p0602, "P0602"));
            Legend.Add(Item(loaded ? "Lockout" : kind, lockout, "Lockout"));
            if (loaded)
            {
                string text = checksumText ?? (checksumsOk ? "ok" : "not ok");
                Legend.Add(Item(checksumsOk ? "ChecksumOk" : "ChecksumBad", text, "Checksum"));
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
            int size = SelectedPart != null ? (int)SelectedPart.Size : Me7Eeprom95040Checksum.EepromSize;
            AddByteRows(size, true, null);
        }

        private void AddPlainRows(byte[] image)
        {
            Pages.Clear();
            AddByteRows(image.Length, false, image);
        }

        private void AddByteRows(int size, bool empty, byte[] image)
        {
            int pageSize = Me7Eeprom95040Checksum.PageSize;
            if (size <= 0)
            {
                return;
            }

            int pages = (size + pageSize - 1) / pageSize;
            for (int page = 0; page < pages; page++)
            {
                int count = Math.Min(pageSize, size - (page * pageSize));
                Pages.Add(empty ? BuildEmptyRow(page, count) : BuildPlainRow(image, page * pageSize, count));
            }
        }

        private static EepromPageRow BuildEmptyRow(int page, int count)
        {
            int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
            var cells = new List<EepromByteCell>(count);
            var ascii = new List<EepromAsciiChar>(count);
            for (int index = 0; index < count; index++)
            {
                cells.Add(new EepromByteCell
                {
                    Hex = "00",
                    Kind = "Empty",
                    ToolTip = "No EEPROM loaded"
                });
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

        private static Me7Eeprom95040Checksum.PageChecksumKind[] FormulaKinds(byte[] window)
        {
            var kinds = new Me7Eeprom95040Checksum.PageChecksumKind[Me7Eeprom95040Checksum.PageCount];
            int pageSize = Me7Eeprom95040Checksum.PageSize;
            for (int page = 0; page < kinds.Length; page++)
            {
                if (((page + 1) * pageSize) > window.Length)
                {
                    continue;
                }

                if (Me7Eeprom95040Checksum.IsChecksumExemptPage(page))
                {
                    kinds[page] = Me7Eeprom95040Checksum.PageChecksumKind.Exempt;
                    continue;
                }

                if (Me7Eeprom95040Checksum.TryMatchBit6(window, page, 0, out _))
                {
                    kinds[page] = Me7Eeprom95040Checksum.PageChecksumKind.Ok;
                }
            }

            return kinds;
        }

        private static EepromPageRow BuildPlainRow(byte[] image, int offset, int count)
        {
            var cells = new List<EepromByteCell>(count);
            var ascii = new List<EepromAsciiChar>(count);
            for (int index = 0; index < count; index++)
            {
                int address = offset + index;
                byte value = image[address];
                bool printable = value >= 32 && value <= 126;
                cells.Add(new EepromByteCell
                {
                    Hex = value.ToString("X2"),
                    Kind = "Plain",
                    ToolTip = "0x" + address.ToString("X3")
                });
                ascii.Add(new EepromAsciiChar
                {
                    Text = (printable ? ((char)value).ToString() : "."),
                    Kind = "Plain",
                    ToolTip = "0x" + address.ToString("X3")
                });
            }

            return new EepromPageRow
            {
                Offset = "0x" + offset.ToString("X3"),
                Ascii = ascii,
                Cells = cells
            };
        }

        private static EepromPageRow BuildRow(
            byte[] image,
            int page,
            Me7Eeprom95040Checksum.PageChecksumKind pageKind,
            ushort[] imageTable)
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
                    ToolTip = CellToolTip(image, page, index, pageKind, imageTable)
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
            Me7Eeprom95040Checksum.PageChecksumKind pageKind,
            ushort[] imageTable)
        {
            int offset = (page * Me7Eeprom95040Checksum.PageSize) + indexInPage;
            Me7Eeprom95040Fields.Role role = Me7Eeprom95040Fields.RoleAt(offset);

            if (indexInPage >= 14 && pageKind != Me7Eeprom95040Checksum.PageChecksumKind.None)
            {
                int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
                ushort descriptor = Me7Eeprom95040Checksum.ActiveDescriptor(page, image, pageOffset, imageTable);
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

            if (role == Me7Eeprom95040Fields.Role.ImmoState)
            {
                return "Immobiliser state word, written by the ECU at runtime. Not the SKC";
            }

            if (role == Me7Eeprom95040Fields.Role.Skc)
            {
                return "SKC, little-endian, shown in the legend as 5 decimal digits. FF FF when none is stored";
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

            App.CurrentOperation = null;
            App.OperationInProgress = false;

            if (success)
            {
                // Set after the operation ends, so App does not log a "100% complete." line.
                App.PercentOperationComplete = 100.0f;
            }
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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            WatchFlashImage();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (mFlashing != null)
            {
                mFlashing.PropertyChanged -= OnFlashImageChanged;
                mFlashing = null;
            }
        }

        private void WatchFlashImage()
        {
            FlashingControl flashing = FindFlashingControl();
            if (flashing == mFlashing)
            {
                return;
            }

            if (mFlashing != null)
            {
                mFlashing.PropertyChanged -= OnFlashImageChanged;
            }

            mFlashing = flashing;
            if (mFlashing != null)
            {
                mFlashing.PropertyChanged += OnFlashImageChanged;
            }
        }

        private void OnFlashImageChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "FlashMemoryImage")
            {
                RefreshGrid();
            }
        }

        private ushort[] CurrentImageTable()
        {
            WatchFlashImage();
            byte[] flash = mFlashing != null && mFlashing.FlashMemoryImage != null
                ? mFlashing.FlashMemoryImage.RawData
                : null;
            return Me7Eeprom95040Checksum.TryCopyImageTable(flash);
        }

        private FlashingControl FindFlashingControl()
        {
            DependencyObject root = Window.GetWindow(this);
            if (root == null)
            {
                return null;
            }

            return FindLogicalChild<FlashingControl>(root);
        }

        private static T FindLogicalChild<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                var dep = child as DependencyObject;
                if (dep == null)
                {
                    continue;
                }

                var match = dep as T;
                if (match != null)
                {
                    return match;
                }

                match = FindLogicalChild<T>(dep);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
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

    public sealed class EepromPeripheralOption
    {
        public EepromPeripheralOption(BootstrapInterface.BootmodeEepromPeriph periph)
        {
            Periph = periph;
            DisplayName = periph.ToString();
        }

        public BootstrapInterface.BootmodeEepromPeriph Periph { get; private set; }
        public string DisplayName { get; private set; }
    }
}

// vi: set sw=4 ts=8 expandtab:
