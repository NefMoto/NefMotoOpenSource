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
using Path = System.IO.Path;

using Communication;
using Shared;
using ApplicationShared;

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

            AvailableMemoryLayouts = new ObservableCollection<string>();
            SelectedMemoryLayout = null;

            AvailableEepromPresets = new ObservableCollection<BootmodeEepromPresetOption>(BootmodeEepromPresetOption.All);
            SelectedEepromPreset = AvailableEepromPresets[0];

            InitializeComponent();

#if DEBUG
            AddDebugFlashButtons();
#endif

            PopulateMemoryLayouts();
            if (App?.Preferences != null)
            {
                string layoutFile = App.Preferences.MemoryLayoutFile;
                if (!String.IsNullOrEmpty(layoutFile) && !File.Exists(layoutFile))
                {
                    string renamed = layoutFile.Replace("ME7 29F", "29F");
                    if (File.Exists(renamed))
                    {
                        layoutFile = renamed;
                    }
                }
                MemoryLayoutFileName = layoutFile;
                FileNameToFlash = App.Preferences.FlashFile;
            }

            // Watch for protocol changes to update IsMemoryLayoutEnabled
            if (App != null)
            {
                App.PropertyChanged += App_PropertyChanged;
                _watchedCommInterface = App.CommInterface;
                if (_watchedCommInterface != null)
                {
                    _watchedCommInterface.PropertyChanged += CommInterface_PropertyChanged;
                }
            }
        }

        private CommunicationInterface _watchedCommInterface;

        private void App_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CommInterface")
            {
                if (_watchedCommInterface != null)
                {
                    _watchedCommInterface.PropertyChanged -= CommInterface_PropertyChanged;
                }

                _watchedCommInterface = App.CommInterface;
                if (_watchedCommInterface != null)
                {
                    _watchedCommInterface.PropertyChanged += CommInterface_PropertyChanged;
                }

                OnPropertyChanged(new PropertyChangedEventArgs("IsMemoryLayoutEnabled"));
                OnPropertyChanged(new PropertyChangedEventArgs("MemoryLayoutToolTip"));
                OnPropertyChanged(new PropertyChangedEventArgs("IsVerifyWriteEnabled"));
                OnPropertyChanged(new PropertyChangedEventArgs("IsVerifyReadEnabled"));
                OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));

                // Switching protocol replaces CommInterface. CurrentProtocol does not change on the old object.
                if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode &&
                    App.CommInterface.IsConnected() && IsFlashFileOK)
                {
                    TryAutoDetectLayoutForBootmode();
                }
                else
                {
                    ApplyDetectedLayoutIfAny();
                }
            }
        }

        private void CommInterface_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CurrentProtocol")
            {
                OnPropertyChanged(new PropertyChangedEventArgs("IsMemoryLayoutEnabled"));
                OnPropertyChanged(new PropertyChangedEventArgs("MemoryLayoutToolTip"));
                OnPropertyChanged(new PropertyChangedEventArgs("IsVerifyWriteEnabled"));
                OnPropertyChanged(new PropertyChangedEventArgs("IsVerifyReadEnabled"));
                OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));

                // Re-validate memory layout when protocol changes (to clear errors for bootmode)
                LoadMemoryLayoutFile();
                if (App.CommInterface == null || App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode)
                {
                    ApplyDetectedLayoutIfAny();
                }

                // For bootmode, try to auto-detect layout if file is already selected
                if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode &&
                    App.CommInterface.IsConnected() && IsFlashFileOK)
                {
                    TryAutoDetectLayoutForBootmode();
                }
            }
            else if (e.PropertyName == "ConnectionStatus")
            {
                if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                {
                    var status = App.CommInterface.ConnectionStatus;
                    if (status == CommunicationInterface.ConnectionStatusType.Disconnected || status == CommunicationInterface.ConnectionStatusType.CommunicationTerminated)
                    {
                        // Drop the in-memory layout so the next bootmode connect detects again.
                        // Keep the detected file name; disconnect-then-KWP reloads it.
                        // Must run on UI thread - PropertyChanged can fire from SendReceive thread
                        if (FlashMemoryLayout != null || IsMemoryLayoutOK)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (App.CommInterface == null || App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode)
                                {
                                    return;
                                }

                                FlashMemoryLayout = null;
                                IsMemoryLayoutOK = false;
                                string placeholder = "Auto-detected (pending)";
                                if (!AvailableMemoryLayouts.Contains(placeholder))
                                {
                                    AvailableMemoryLayouts.Insert(0, placeholder);
                                }
                                mSelectedMemoryLayout = placeholder;
                                OnPropertyChanged(new PropertyChangedEventArgs("FlashMemoryLayout"));
                                OnPropertyChanged(new PropertyChangedEventArgs("IsMemoryLayoutOK"));
                                OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
                                OnPropertyChanged(new PropertyChangedEventArgs("MemoryLayoutToolTip"));
                            }));
                        }
                    }
                    else if (App.CommInterface.IsConnected())
                    {
                        // Auto-detect layout so read/write are enabled; no flash file required for read
                        TryAutoDetectLayoutForBootmode();
                    }
                }
            }
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
                if (App?.Preferences != null)
                {
                    App.Preferences.FlashFile = FileNameToFlash;
                }

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

            // For bootmode, try to auto-detect layout when file is selected
            if (IsFlashFileOK && App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode && App.CommInterface.IsConnected())
            {
                TryAutoDetectLayoutForBootmode();
            }
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
            // For bootmode, layout is auto-detected - don't require a layout file
            bool isBootMode = App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode;

            // For bootmode, preserve auto-detected layout if it exists
            MemoryLayout preservedAutoDetectedLayout = null;
            bool preservedIsMemoryLayoutOK = false;
            if (isBootMode && FlashMemoryLayout != null && FlashMemoryLayout.Validate() && IsMemoryLayoutOK)
            {
                // Preserve the auto-detected layout
                preservedAutoDetectedLayout = FlashMemoryLayout;
                preservedIsMemoryLayoutOK = IsMemoryLayoutOK;
            }

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

                if (App?.Preferences != null)
                {
                    App.Preferences.MemoryLayoutFile = MemoryLayoutFileName;
                }
            }
            else
            {
                // Only show error if not bootmode (bootmode auto-detects layout)
                if (!isBootMode)
                {
                    error = "No file selected";
                }
                else if (preservedAutoDetectedLayout != null)
                {
                    // Restore the auto-detected layout for bootmode
                    FlashMemoryLayout = preservedAutoDetectedLayout;
                    IsMemoryLayoutOK = preservedIsMemoryLayoutOK;
                }
            }

            //force and update without setting the property
            OnPropertyChanged(new PropertyChangedEventArgs("FlashMemoryLayout"));

            this["MemoryLayoutFileName"] = error;
        }

        /// <summary>
        /// Auto-detects flash layout for bootmode via BootstrapInterface.GetBootmodeFlashLayout.
        /// Updates FlashMemoryLayout, IsMemoryLayoutOK. GetBootmodeFlashLayout populates BootstrapInterface layout cache.
        /// No flash file required; runs on connect for read-before-save and write.
        /// </summary>
        private void TryAutoDetectLayoutForBootmode()
        {
            if (App.CommInterface == null || App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode || !App.CommInterface.IsConnected())
            {
                return;
            }

            if (IsMemoryLayoutOK && FlashMemoryLayout != null && FlashMemoryLayout.Validate()
                && (!String.IsNullOrEmpty(mDetectedLayoutBasename) || FlashMemoryLayout == mDetectedBootmodeLayout))
            {
                return;
            }

            var bootstrapInterface = App.CommInterface as BootstrapInterface;
            if (bootstrapInterface == null)
            {
                return;
            }

            MemoryLayout layout;
            string errorMessage;
            if (!bootstrapInterface.GetBootmodeFlashLayout(out layout, out errorMessage))
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    string placeholder = "Auto-detected (requires bootmode)";
                    if (!AvailableMemoryLayouts.Contains(placeholder))
                    {
                        AvailableMemoryLayouts.Insert(0, placeholder);
                    }
                    mSelectedMemoryLayout = placeholder;
                    OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
                    OnPropertyChanged(new PropertyChangedEventArgs("MemoryLayoutToolTip"));
                }));
                return;
            }

            ushort deviceID = bootstrapInterface.LastKnownFlashDeviceID;
            MemoryLayout layoutForClosure = layout;
            Dispatcher.Invoke((Action)(() => ApplyBootmodeDetection(deviceID, layoutForClosure)));
        }

        private void ApplyBootmodeDetection(ushort deviceID, MemoryLayout generated)
        {
            string basename = BootstrapInterface.GetLayoutBasenameFromDeviceID(deviceID);
            string memoryLayoutsDir = GetMemoryLayoutsDirectory();
            string fullPath = null;
            if (!String.IsNullOrEmpty(basename) && !String.IsNullOrEmpty(memoryLayoutsDir))
            {
                fullPath = Path.Combine(memoryLayoutsDir, basename + MemoryLayout.MEMORY_LAYOUT_FILE_EXT);
                if (!File.Exists(fullPath))
                {
                    fullPath = null;
                }
            }

            if (fullPath != null)
            {
                if (!AvailableMemoryLayouts.Contains(basename))
                {
                    AvailableMemoryLayouts.Add(basename);
                }

                mDetectedLayoutBasename = basename;
                mDetectedBootmodeLayout = null;
                MemoryLayoutFileName = fullPath;
                OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
                OnPropertyChanged(new PropertyChangedEventArgs("MemoryLayoutToolTip"));
                if (IsMemoryLayoutOK && FlashMemoryLayout != null)
                {
                    App.DisplayStatusMessage("Bootmode-detected layout: " + basename, StatusMessageType.USER);
                    return;
                }

                mDetectedLayoutBasename = null;
                mMemoryLayoutFileName = null;
            }

            mDetectedBootmodeLayout = generated;
            ApplyDetectedLayout(generated);
            App.DisplayStatusMessage(
                "Bootmode-detected layout: 0x" + generated.BaseAddress.ToString("X6") + ", " + generated.SectorSizes.Count + " sectors",
                StatusMessageType.USER);
        }

        private void ApplyDetectedLayoutIfAny()
        {
            if (!String.IsNullOrEmpty(mDetectedLayoutBasename))
            {
                if (FlashMemoryLayout == null || !IsMemoryLayoutOK)
                {
                    LoadMemoryLayoutFile();
                }
            }
            else if (mDetectedBootmodeLayout != null && mDetectedBootmodeLayout.Validate())
            {
                ApplyDetectedLayout(mDetectedBootmodeLayout);
                return;
            }

            string basename = MemoryLayoutBasename(mMemoryLayoutFileName);
            if (String.IsNullOrEmpty(basename) || !AvailableMemoryLayouts.Contains(basename) || mSelectedMemoryLayout == basename)
            {
                return;
            }

            mSelectedMemoryLayout = basename;
            OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
        }

        private void ApplyDetectedLayout(MemoryLayout layout)
        {
            FlashMemoryLayout = layout;
            IsMemoryLayoutOK = true;

            string autoDetectedString = $"Auto-detected: {layout.Size / 1024}KB, {layout.SectorSizes.Count} sectors";
            if (!AvailableMemoryLayouts.Contains(autoDetectedString))
            {
                AvailableMemoryLayouts.Insert(0, autoDetectedString);
            }
            mSelectedMemoryLayout = autoDetectedString;

            string tempError;
            IsFlashFileOK = ValidateFileToFlash(FlashMemoryImage, FlashMemoryLayout, out tempError);
            this["FileNameToFlash"] = tempError;

            OnPropertyChanged(new PropertyChangedEventArgs("FlashMemoryLayout"));
            OnPropertyChanged(new PropertyChangedEventArgs("IsMemoryLayoutOK"));
            OnPropertyChanged(new PropertyChangedEventArgs("MemoryLayoutToolTip"));
            OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
            OnPropertyChanged(new PropertyChangedEventArgs("FileNameToFlash"));
        }

        public string MemoryLayoutFileName
        {
            get { return mMemoryLayoutFileName; }
            set
            {
                mMemoryLayoutFileName = value;

                // Update the selected memory layout in the dropdown if the file path matches
                if (!String.IsNullOrEmpty(value))
                {
                    string memoryLayoutsDir = GetMemoryLayoutsDirectory();
                    string basename = MemoryLayoutBasename(value);
                    if (!String.IsNullOrEmpty(memoryLayoutsDir) && !String.IsNullOrEmpty(basename)
                        && AvailableMemoryLayouts.Contains(basename) && mSelectedMemoryLayout != basename)
                    {
                        mSelectedMemoryLayout = basename;
                        OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
                    }
                }
                else
                {
                    // For bootmode, don't clear mSelectedMemoryLayout if it's an auto-detected layout
                    // (it will be updated by TryAutoDetectLayoutForBootmode if needed)
                    bool isBootMode = App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode;
                    if (!isBootMode || (mSelectedMemoryLayout == null || !mSelectedMemoryLayout.StartsWith("Auto-detected")))
                    {
                        if (mSelectedMemoryLayout != null)
                        {
                            mSelectedMemoryLayout = null;
                            OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
                        }
                    }
                }

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
        private string mDetectedLayoutBasename;
        private MemoryLayout mDetectedBootmodeLayout;

        private static string MemoryLayoutBasename(string path)
        {
            string fileName = Path.GetFileName(path);
            if (String.IsNullOrEmpty(fileName))
            {
                return null;
            }

            if (fileName.EndsWith(MemoryLayout.MEMORY_LAYOUT_FILE_EXT))
            {
                return fileName.Substring(0, fileName.Length - MemoryLayout.MEMORY_LAYOUT_FILE_EXT.Length);
            }

            if (fileName.EndsWith(MemoryLayout.MEMORY_LAYOUT_FILE_SHORT_EXT))
            {
                return fileName.Substring(0, fileName.Length - MemoryLayout.MEMORY_LAYOUT_FILE_SHORT_EXT.Length);
            }

            return fileName;
        }

        public ObservableCollection<string> AvailableMemoryLayouts { get; private set; }

        public string SelectedMemoryLayout
        {
            get
            {
                // For bootmode, show auto-detected layout or placeholder
                if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                {
                    if (!String.IsNullOrEmpty(mSelectedMemoryLayout)
                        && AvailableMemoryLayouts.Contains(mSelectedMemoryLayout)
                        && !mSelectedMemoryLayout.StartsWith("Auto-detected"))
                    {
                        return mSelectedMemoryLayout;
                    }

                    var bootstrapInterface = App.CommInterface as BootstrapInterface;
                    const byte DEVICE_ID_CORE_RUNNING = 0xAA;

                    // Check if core is already running - try to use stored flash device ID
                    if (bootstrapInterface != null && bootstrapInterface.DeviceID == DEVICE_ID_CORE_RUNNING)
                    {
                        // Try to use stored flash device ID if available and layout is already generated
                        if (bootstrapInterface.LastKnownFlashDeviceID != 0 && FlashMemoryLayout != null && FlashMemoryLayout.Validate() && IsMemoryLayoutOK)
                        {
                            // Layout is available from stored device ID
                            string storedString = $"Auto-detected (stored): {FlashMemoryLayout.Size / 1024}KB, {FlashMemoryLayout.SectorSizes.Count} sectors";
                            if (!AvailableMemoryLayouts.Contains(storedString))
                            {
                                AvailableMemoryLayouts.Insert(0, storedString);
                            }
                            return storedString;
                        }

                        // No stored flash device ID available
                        string placeholder = "Auto-detected (requires bootmode)";
                        if (!AvailableMemoryLayouts.Contains(placeholder))
                        {
                            AvailableMemoryLayouts.Insert(0, placeholder);
                        }
                        return placeholder;
                    }

                    if (FlashMemoryLayout != null && FlashMemoryLayout.Validate() && IsMemoryLayoutOK)
                    {
                        // Layout is detected, return the auto-detected string
                        if (!String.IsNullOrEmpty(mSelectedMemoryLayout) && mSelectedMemoryLayout.StartsWith("Auto-detected:"))
                        {
                            return mSelectedMemoryLayout;
                        }
                        // Create the string if it doesn't exist
                        string autoDetectedString = $"Auto-detected: {FlashMemoryLayout.Size / 1024}KB, {FlashMemoryLayout.SectorSizes.Count} sectors";
                        if (!AvailableMemoryLayouts.Contains(autoDetectedString))
                        {
                            AvailableMemoryLayouts.Insert(0, autoDetectedString);
                        }
                        return autoDetectedString;
                    }
                    else
                    {
                        // Layout not detected yet, show placeholder
                        string placeholder = "Auto-detected (pending)";
                        if (!AvailableMemoryLayouts.Contains(placeholder))
                        {
                            AvailableMemoryLayouts.Insert(0, placeholder);
                        }
                        return placeholder;
                    }
                }
                return mSelectedMemoryLayout;
            }
            set
            {
                // Ignore changes for bootmode (ComboBox is disabled anyway)
                if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                {
                    return;
                }

                if (mSelectedMemoryLayout != value)
                {
                    mSelectedMemoryLayout = value;

                    if (!String.IsNullOrEmpty(value))
                    {
                        // Construct the full path to the memory layout file
                        string memoryLayoutsDir = GetMemoryLayoutsDirectory();
                        if (!String.IsNullOrEmpty(memoryLayoutsDir))
                        {
                            // Find the file with this basename
                            string fullPath = Path.Combine(memoryLayoutsDir, value + MemoryLayout.MEMORY_LAYOUT_FILE_EXT);
                            if (File.Exists(fullPath))
                            {
                                MemoryLayoutFileName = fullPath;
                            }
                            else
                            {
                                // Try without extension in case the basename already includes it
                                fullPath = Path.Combine(memoryLayoutsDir, value);
                                if (File.Exists(fullPath))
                                {
                                    MemoryLayoutFileName = fullPath;
                                }
                            }
                        }
                    }
                    else
                    {
                        MemoryLayoutFileName = "";
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("SelectedMemoryLayout"));
                }
            }
        }
        private string mSelectedMemoryLayout;

        /// <summary>
        /// Returns true if memory layout selection should be enabled.
        /// Disabled for bootmode (auto-detected from device ID).
        /// </summary>
        public bool IsMemoryLayoutEnabled
        {
            get
            {
                // Disable for bootmode - layout is auto-detected from device ID
                if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Returns tooltip text for memory layout ComboBox.
        /// Shows different message for bootmode with auto-detected layout details or unavailable reason.
        /// </summary>
        public string MemoryLayoutToolTip
        {
            get
            {
                if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                {
                    if (FlashMemoryLayout != null && FlashMemoryLayout.Validate())
                    {
                        string tooltip = "Auto-detected layout (BootMode):\n";
                        tooltip += $"Base Address: 0x{FlashMemoryLayout.BaseAddress:X6}\n";
                        tooltip += $"Size: {FlashMemoryLayout.Size} bytes ({FlashMemoryLayout.Size / 1024} KB)\n";
                        tooltip += $"Sectors: {FlashMemoryLayout.SectorSizes.Count}";
                        return tooltip;
                    }
                    // Show specific reason when layout detection failed
                    var bootstrapInterface = App.CommInterface as BootstrapInterface;
                    if (bootstrapInterface != null)
                    {
                        var state = bootstrapInterface.GetBootmodeConnectionState();
                        if (state.FlashLayoutStatus == BootstrapInterface.BootmodeFlashLayoutStatus.Unavailable && !string.IsNullOrEmpty(state.FlashLayoutUnavailableReason))
                        {
                            return "Memory layout unavailable: " + state.FlashLayoutUnavailableReason;
                        }
                    }
                    return "Memory layout will be auto-detected from flash device ID when read/write operation starts";
                }
                if (mDetectedBootmodeLayout != null && FlashMemoryLayout == mDetectedBootmodeLayout)
                {
                    return $"Bootmode-detected layout: {FlashMemoryLayout.Size / 1024}KB, {FlashMemoryLayout.SectorSizes.Count} sectors";
                }
                return MemoryLayoutFileName;
            }
        }

        public bool IsVerifyWriteEnabled
        {
            get { return IsNotBootmode(); }
        }

        public bool IsVerifyReadEnabled
        {
            get { return IsNotBootmode() && IsBottomBootFirstBlock(); }
        }

        private bool IsNotBootmode()
        {
            return App.CommInterface == null || App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode;
        }

        private string GetMemoryLayoutsDirectory()
        {
            return MemoryLayout.GetLayoutsDirectory();
        }

        private void PopulateMemoryLayouts()
        {
            AvailableMemoryLayouts.Clear();

            string memoryLayoutsDir = GetMemoryLayoutsDirectory();
            if (!String.IsNullOrEmpty(memoryLayoutsDir) && Directory.Exists(memoryLayoutsDir))
            {
                try
                {
                    var files = Directory.GetFiles(memoryLayoutsDir, "*" + MemoryLayout.MEMORY_LAYOUT_FILE_EXT);
                    foreach (var file in files)
                    {
                        string basename = MemoryLayoutBasename(file);
                        if (!String.IsNullOrEmpty(basename) && !AvailableMemoryLayouts.Contains(basename))
                        {
                            AvailableMemoryLayouts.Add(basename);
                        }
                    }
                }
                catch
                {
                    // If we can't read the directory, just leave the list empty
                }
            }

            OnPropertyChanged(new PropertyChangedEventArgs("AvailableMemoryLayouts"));
        }

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

                    _ChooseFlashFileCommand.CanExecuteMethod = delegate (List<string> reasonsDisabled)
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

            if (App.ShowFileDialog(dialog, FileNameToFlash) == true)
            {
                FileNameToFlash = dialog.FileName;
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

                    _VerifyChecksumsCommand.CanExecuteMethod = delegate (List<string> reasonsDisabled)
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
                        _CheckIfFlashMatchesCommand.WatchConnection(App);
                        _CheckIfFlashMatchesCommand.AddWatchedProperty(App, "OperationInProgress");
                        _CheckIfFlashMatchesCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
                        _CheckIfFlashMatchesCommand.AddWatchedProperty(this, "IsFlashFileOK");
                    }

                    _CheckIfFlashMatchesCommand.CanExecuteMethod = delegate (List<string> reasonsDisabled)
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
                if (ConfirmFlashOperation("Confirm Check if Flash Matches", FlashConfirmationKind.CheckIfFlashMatches))
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
                        _WriteEntireFlashCommand.WatchConnection(App);
                        _WriteEntireFlashCommand.AddWatchedProperty(App, "OperationInProgress");
                        _WriteEntireFlashCommand.AddWatchedProperty(this, "IsFlashFileOK");
                        _WriteEntireFlashCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
                    }

                    _WriteEntireFlashCommand.CanExecuteMethod = delegate (List<string> reasonsDisabled)
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

            // For bootmode, layout is auto-detected via GetBootmodeFlashLayout
            if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
            {
                if (!App.CommInterface.IsConnected())
                {
                    reasonsDisabled.Add("Not connected to ECU");
                    result = false;
                }
                else
                {
                    var bootstrapInterface = App.CommInterface as BootstrapInterface;
                    if (bootstrapInterface != null && !bootstrapInterface.CanGetBootmodeFlashLayout())
                    {
                        reasonsDisabled.Add(bootstrapInterface.GetBootmodeFlashLayoutUnavailableReason()
                            ?? "Flash layout not available (BootMode).");
                        result = false;
                    }
                }

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
            }

            // For KWP2000, require memory layout
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

            if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000 &&
                App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode)
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
        }
        private void OnWriteEntireFlash()
        {
            //done to trigger a reload of the memory layout and flash files and cause them to revalidate
            FileNameToFlash = FileNameToFlash;
            MemoryLayoutFileName = MemoryLayoutFileName;

            if (WriteEntireFlashCommand.IsEnabled)
            {
                if (ConfirmFlashOperation("Confirm Full Write ECU Flash Memory", FlashConfirmationKind.WriteEntire))
                {
                    LogLoadedFlashFile("Full write");
                    // Force verify to false for bootmode
                    bool verify = (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                        ? false
                        : chkVerifyWrite.IsChecked.Value;
                    OnWriteExternalFlashStarted(FlashMemoryImage.RawData, FlashMemoryLayout, this.OnWriteFlashCompleted, false, verify);
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
                        _WriteDiffFlashCommand.WatchConnection(App);
                        _WriteDiffFlashCommand.AddWatchedProperty(App, "OperationInProgress");
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

            // Diff write is not supported for BootMode (same reason as diff read)
            if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
            {
                reasonsDisabled.Add("Diff write is not supported for BootMode protocol");
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

            if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000 &&
                App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode)
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
        }
        private void OnWriteDiffFlash()
        {
            //done to trigger a reload of the memory layout and flash files and cause them to revalidate
            FileNameToFlash = FileNameToFlash;
            MemoryLayoutFileName = MemoryLayoutFileName;

            if (WriteDiffFlashCommand.IsEnabled)
            {
                if (ConfirmFlashOperation("Confirm Diff Write ECU Flash Memory", FlashConfirmationKind.WriteDiff))
                {
                    LogLoadedFlashFile("Diff write");
                    // Force verify to false for bootmode
                    bool verify = (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                        ? false
                        : chkVerifyWrite.IsChecked.Value;
                    OnWriteExternalFlashStarted(FlashMemoryImage.RawData, FlashMemoryLayout, this.OnWriteFlashCompleted, true, verify);
                }
            }
        }

        private void OnWriteFlashCompleted(Operation operation, bool success)
        {
            //UI should occur on the UI thread...
            Dispatcher.Invoke((Action)(() =>
            {
                string statusMessage = OnWriteExternalFlashCompleted(operation, success);

                if (success)
                {
                    var flashingTime = operation.OperationElapsedTime;
                    statusMessage += "\nFlashing time was " + flashingTime.Hours.ToString("D2") + ":" + flashingTime.Minutes.ToString("D2") + ":" + flashingTime.Seconds.ToString("D2") + ".";
                }

                App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
                App.DisplayUserPrompt("Writing ECU Flash Memory Complete", statusMessage, UserPromptType.OK);

                if (success)
                    App.PercentOperationComplete = 100.0f;
                App.CurrentOperation = null;
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
                        _ReadEntireFlashCommand.WatchConnection(App);
                        _ReadEntireFlashCommand.AddWatchedProperty(App, "OperationInProgress");
                        _ReadEntireFlashCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
                    }

                    _ReadEntireFlashCommand.CanExecuteMethod = delegate (List<string> reasonsDisabled)
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

            // For bootmode, layout is auto-detected via GetBootmodeFlashLayout
            if (App.CommInterface != null && App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
            {
                if (!App.CommInterface.IsConnected())
                {
                    reasonsDisabled.Add("Not connected to ECU");
                    result = false;
                }
                else
                {
                    var bootstrapInterface = App.CommInterface as BootstrapInterface;
                    if (bootstrapInterface != null && !bootstrapInterface.CanGetBootmodeFlashLayout())
                    {
                        reasonsDisabled.Add(bootstrapInterface.GetBootmodeFlashLayoutUnavailableReason()
                            ?? "Flash layout not available (BootMode).");
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

            // For KWP2000, require memory layout
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

            if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000 &&
                App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.BootMode)
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
        }

        private void OnReadEntireFlash()
        {
            //done to trigger a reload of the memory layout and cause it to revalidate
            MemoryLayoutFileName = MemoryLayoutFileName;

            if (ReadEntireFlashCommand.IsEnabled)
            {
                if (ConfirmFlashOperation("Confirm Full Read ECU Flash Memory", FlashConfirmationKind.ReadEntire))
                {
                    App.OperationInProgress = true;
                    App.PercentOperationComplete = 0.0f;

                    bool verifyRead = false;
                    ReadExternalFlashOperation.UploadRange[] checksumRanges = null;
                    if (IsNotBootmode() && IsBottomBootFirstBlock())
                    {
                        verifyRead = true;
                        if (chkVerifyRead.IsChecked != true)
                        {
                            checksumRanges = FirstFourSectorChecksumRanges(FlashMemoryLayout);
                            if (checksumRanges == null)
                            {
                                verifyRead = false;
                            }
                        }
                    }

                    OnReadExternalFlashStarted(false, false, verifyRead, FlashMemoryImage.RawData, FlashMemoryLayout, this.OnReadFlashCompleted, checksumRanges);
                }
            }
        }

        private static ReadExternalFlashOperation.UploadRange[] FirstFourSectorChecksumRanges(MemoryLayout layout)
        {
            if ((layout == null) || (layout.SectorSizes == null) || (layout.SectorSizes.Count < 4))
            {
                return null;
            }

            var ranges = new ReadExternalFlashOperation.UploadRange[4];
            uint address = layout.BaseAddress;
            for (int i = 0; i < ranges.Length; i++)
            {
                uint size = layout.SectorSizes[i];
                ranges[i] = new ReadExternalFlashOperation.UploadRange(address, size);
                address += size;
            }

            return ranges;
        }

        public ObservableCollection<BootmodeEepromPresetOption> AvailableEepromPresets { get; private set; }

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

        public ReactiveCommand ReadBootmodeEepromCommand
        {
            get
            {
                if (_ReadBootmodeEepromCommand == null)
                {
                    _ReadBootmodeEepromCommand = CreateBootmodeEepromCommand(
                        this.OnReadBootmodeEeprom,
                        "Read EEPROM (Bootmode)",
                        "Read physical SPI EEPROM (95040) via bootmode; overwrites flash driver until reloaded",
                        CanExecuteBootmodeEepromCommand);
                }

                return _ReadBootmodeEepromCommand;
            }
        }
        private ReactiveCommand _ReadBootmodeEepromCommand;

        public ReactiveCommand WriteBootmodeEepromCommand
        {
            get
            {
                if (_WriteBootmodeEepromCommand == null)
                {
                    _WriteBootmodeEepromCommand = CreateBootmodeEepromCommand(
                        this.OnWriteBootmodeEeprom,
                        "Write EEPROM (Bootmode)",
                        "Write physical SPI EEPROM (95040) via bootmode; immo/security risk - backup first",
                        CanExecuteBootmodeEepromCommand);
                }

                return _WriteBootmodeEepromCommand;
            }
        }
        private ReactiveCommand _WriteBootmodeEepromCommand;

        private ReactiveCommand CreateBootmodeEepromCommand(
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
                command.AddWatchedProperty(this, "SelectedEepromPreset");
            }

            command.CanExecuteMethod = canExecute;
            return command;
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

        private void FinishBootmodeEepromUi(bool success, string promptTitle, string statusMessage)
        {
            App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
            App.DisplayUserPrompt(promptTitle, statusMessage, UserPromptType.OK);

            if (success)
            {
                App.PercentOperationComplete = 100.0f;
            }

            App.CurrentOperation = null;
            App.OperationInProgress = false;
        }

        private void OnReadBootmodeEeprom()
        {
            if (!ReadBootmodeEepromCommand.IsEnabled || SelectedEepromPreset == null)
            {
                return;
            }

            var bootstrap = TryGetBootstrapInterface();
            if (bootstrap == null)
            {
                return;
            }

            string confirm =
                "Read physical SPI EEPROM (not the KWP mirror).\n\n"
                + "Preset: " + SelectedEepromPreset.DisplayName + "\n"
                + "This uploads the EEPROM driver over the flash driver at 0xF600.\n"
                + "Reload/re-detect flash before any flash read/write afterward.\n\n"
                + "Continue?";

            if (App.DisplayUserPrompt("Confirm Bootmode EEPROM Read", confirm, UserPromptType.OK_CANCEL) != UserPromptResult.OK)
            {
                return;
            }

            var operation = new BootmodeReadEepromOperation(bootstrap, SelectedEepromPreset.Settings);
            StartBootmodeEepromOperation(operation, OnReadBootmodeEepromCompleted);
        }

        private void OnReadBootmodeEepromCompleted(Operation operation, bool success)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                operation.CompletedOperationEvent -= OnReadBootmodeEepromCompleted;

                var readOp = operation as BootmodeReadEepromOperation;
                string statusMessage;

                if (success && readOp != null && readOp.ReadMemory != null)
                {
                    success = SaveBootmodeEepromFile(readOp.ReadMemory);
                    if (success)
                    {
                        statusMessage = "Reading EEPROM succeeded in: "
                            + FormatOperationElapsed(operation.OperationElapsedTime) + ".";
                    }
                    else
                    {
                        statusMessage = "EEPROM was read but not saved.";
                    }
                }
                else
                {
                    statusMessage = "Reading EEPROM failed.";
                    success = false;
                }

                FinishBootmodeEepromUi(success, "Bootmode EEPROM Read Finished", statusMessage);
            }), null);
        }

        private bool SaveBootmodeEepromFile(MemoryImage readMemory)
        {
            var dialog = new SaveFileDialog();
            dialog.DefaultExt = ".bin";
            dialog.Filter = "EEPROM binary (*.bin)|*.bin|All files (*.*)|*.*";
            dialog.AddExtension = true;
            dialog.OverwritePrompt = true;
            dialog.Title = "Save Bootmode EEPROM Dump";
            dialog.FileName = "eeprom-95040.bin";

            if (App.ShowFileDialog(dialog) != true)
            {
                return false;
            }

            if (!readMemory.SaveToFile(dialog.FileName))
            {
                App.DisplayStatusMessage("Failed to save EEPROM dump to: " + dialog.FileName, StatusMessageType.USER);
                return false;
            }

            App.DisplayStatusMessage("Saved EEPROM dump to: " + dialog.FileName, StatusMessageType.USER);
            return true;
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

            var openDialog = new OpenFileDialog();
            openDialog.DefaultExt = ".bin";
            openDialog.Filter = "EEPROM binary (*.bin)|*.bin|All files (*.*)|*.*";
            openDialog.CheckFileExists = true;
            openDialog.Title = "Select EEPROM Image to Write";

            if (App.ShowFileDialog(openDialog) != true)
            {
                return;
            }

            byte[] dataToWrite;
            try
            {
                dataToWrite = File.ReadAllBytes(openDialog.FileName);
            }
            catch (Exception ex)
            {
                App.DisplayStatusMessage("Failed to read EEPROM file: " + ex.Message, StatusMessageType.USER);
                return;
            }

            if (dataToWrite.Length == 0)
            {
                App.DisplayStatusMessage("EEPROM file is empty.", StatusMessageType.USER);
                return;
            }

            if (dataToWrite.Length > SelectedEepromPreset.Settings.Size)
            {
                App.DisplayStatusMessage(
                    $"EEPROM file is {dataToWrite.Length} bytes; preset allows at most {SelectedEepromPreset.Settings.Size}.",
                    StatusMessageType.USER);
                return;
            }

            if (dataToWrite.Length != SelectedEepromPreset.Settings.Size)
            {
                string sizeWarn =
                    $"File is {dataToWrite.Length} bytes; preset size is {SelectedEepromPreset.Settings.Size}.\n"
                    + "Only the file length will be written (not a full-chip pad).\n\nContinue?";
                if (App.DisplayUserPrompt("EEPROM Size Mismatch", sizeWarn, UserPromptType.OK_CANCEL) != UserPromptResult.OK)
                {
                    return;
                }
            }

            if (SelectedEepromPreset.Settings.EepromType == BootstrapInterface.BootmodeEepromType.Type95040
                && dataToWrite.Length >= Me7Eeprom95040Checksum.EepromSize)
            {
                var checksum = Me7Eeprom95040Checksum.Validate(dataToWrite);
                if (!checksum.AllChecksumPagesValid)
                {
                    string csPrompt =
                        $"ME7 95040 data-page checksums are invalid ({checksum.PagesInvalid} bad / {checksum.PagesChecked} checked).\n"
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
                            $"Corrected {pagesUpdated} EEPROM data-page checksum(s); pages 28-29 left unchanged.",
                            StatusMessageType.USER);
                    }
                }
            }

            bool verify = chkVerifyWrite.IsChecked == true;

            string confirm =
                "WRITE physical SPI EEPROM (not KWP mirror).\n\n"
                + "File: " + openDialog.FileName + "\n"
                + "Size: " + dataToWrite.Length + " bytes\n"
                + "Preset: " + SelectedEepromPreset.DisplayName + "\n"
                + "Verify after write: " + (verify ? "yes" : "no") + "\n\n"
                + "This can affect immobilizer / VIN / adaptations.\n"
                + "Backup the chip first. Wrong image can brick the ECU.\n"
                + "Flash driver at 0xF600 will be overwritten.\n\n"
                + "Continue?";

            if (App.DisplayUserPrompt("Confirm Bootmode EEPROM Write", confirm, UserPromptType.OK_CANCEL) != UserPromptResult.OK)
            {
                return;
            }

            var operation = new BootmodeWriteEepromOperation(
                bootstrap,
                SelectedEepromPreset.Settings,
                dataToWrite,
                verify);
            StartBootmodeEepromOperation(operation, OnWriteBootmodeEepromCompleted);
        }

        private void OnWriteBootmodeEepromCompleted(Operation operation, bool success)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                operation.CompletedOperationEvent -= OnWriteBootmodeEepromCompleted;

                string statusMessage = success
                    ? "Writing EEPROM succeeded in: " + FormatOperationElapsed(operation.OperationElapsedTime) + "."
                    : "Writing EEPROM failed.";

                FinishBootmodeEepromUi(success, "Bootmode EEPROM Write Finished", statusMessage);
            }), null);
        }

        public ReactiveCommand ReadDiffFlashCommand
        {
            get
            {
                if (_ReadDiffFlashCommand == null)
                {
                    _ReadDiffFlashCommand = new ReactiveCommand(this.OnReadDiffFlash);
                    _ReadDiffFlashCommand.Name = "Diff Read Flash";
                    _ReadDiffFlashCommand.Description = "Read only changed sectors of ECU flash memory, using the loaded file to skip matching sectors";

                    if (App != null)
                    {
                        _ReadDiffFlashCommand.WatchConnection(App);
                        _ReadDiffFlashCommand.AddWatchedProperty(App, "OperationInProgress");
                        _ReadDiffFlashCommand.AddWatchedProperty(this, "IsFlashFileOK");
                        _ReadDiffFlashCommand.AddWatchedProperty(this, "IsMemoryLayoutOK");
                    }

                    _ReadDiffFlashCommand.CanExecuteMethod = delegate (List<string> reasonsDisabled)
                    {
                        return CanExecuteReadDiffFlashCommand(reasonsDisabled);
                    };
                }

                return _ReadDiffFlashCommand;
            }
        }
        private ReactiveCommand _ReadDiffFlashCommand;

        private bool CanExecuteReadDiffFlashCommand(List<string> reasonsDisabled)
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
                if (App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
                {
                    reasonsDisabled.Add("Diff read is not yet supported for BootMode protocol");
                }
                else
                {
                    reasonsDisabled.Add("Not connected with KWP2000 protocol");
                }
                result = false;
            }

            if (App.OperationInProgress)
            {
                reasonsDisabled.Add("Another operation is in progress");
                result = false;
            }

            return result;
        }

        private void OnReadDiffFlash()
        {
            //done to trigger a reload of the memory layout and flash files and cause them to revalidate
            FileNameToFlash = FileNameToFlash;
            MemoryLayoutFileName = MemoryLayoutFileName;

            if (ReadDiffFlashCommand.IsEnabled)
            {
                if (ConfirmFlashOperation("Confirm Diff Read ECU Flash Memory", FlashConfirmationKind.ReadDiff))
                {
                    LogLoadedFlashFile("Diff read");
                    App.OperationInProgress = true;
                    App.PercentOperationComplete = 0.0f;

                    OnReadExternalFlashStarted(true, true, true, FlashMemoryImage.RawData, FlashMemoryLayout, this.OnReadFlashCompleted);
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
                    statusMessage = (readFlashOperation?.WasCancelled == true) ? "Reading ECU flash memory cancelled." : "Reading ECU flash memory failed.";
                }

                App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
                App.DisplayUserPrompt("Reading ECU Flash Memory Finished", statusMessage, UserPromptType.OK);

                // Clear operation state before showing the modal so user can cancel/close/disconnect
                // while the completion dialog is open (e.g. after a write failure).
                if (success)
                    App.PercentOperationComplete = 100.0f;
                App.CurrentOperation = null;
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

            if (App.ShowFileDialog(dialog, FileNameToFlash) == true)
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

        internal const string ProgrammingFlagClearBlockList = "0x804000-0x805FFF, 0x806000-0x807FFF, and 0x808000-0x80FFFF";

        public ReactiveCommand ChecksumRestOfFirst64KCommand
        {
            get
            {
                if (_ChecksumRestOfFirst64KCommand == null)
                {
                    var command = new ReactiveCommand(this.OnChecksumRestOfFirst64K);
                    command.Name = "Checksum 8K+8K+32K";
                    command.Description = "Checksum " + ProgrammingFlagClearBlockList + " from the loaded file. No upload and no erase.";

                    if (App != null)
                    {
                        command.WatchConnection(App);
                        command.AddWatchedProperty(App, "OperationInProgress");
                        command.AddWatchedProperty(this, "IsFlashFileOK");
                        command.AddWatchedProperty(this, "IsMemoryLayoutOK");
                    }

                    command.CanExecuteMethod = CanExecuteChecksumRestOfFirst64KCommand;
                    _ChecksumRestOfFirst64KCommand = command;
                }

                return _ChecksumRestOfFirst64KCommand;
            }
        }
        private ReactiveCommand _ChecksumRestOfFirst64KCommand;

        private const string ProgrammingFlagClearStatus = "Checksumming 0x804000-0x80FFFF as 8KB, 8KB, and 32KB. No upload and no erase.";
#if DEBUG
        private void AddDebugFlashButtons()
        {
            AddDebugFlashButton(CheckIfFlashMatchesCommand, 0);
            AddDebugFlashButton(ChecksumRestOfFirst64KCommand, 1);
        }

        private void AddDebugFlashButton(ReactiveCommand command, int column)
        {
            var button = new Button();
            button.SetResourceReference(FrameworkElement.StyleProperty, "ReactiveCommandButtonStyle");
            button.Command = command;
            Grid.SetColumn(button, column);
            Grid.SetRow(button, 2);
            FlashActionGrid.Children.Add(button);
        }
#endif

        private void OnChecksumRestOfFirst64K()
        {
            TryStartProgrammingFlagClear(null, true);
        }

        internal bool TryStartProgrammingFlagClear(Action<bool> afterComplete, bool confirm = false)
        {
            List<MemoryImage> blocks;
            if (!TryGetProgrammingFlagClearBlocks(out blocks))
            {
                return false;
            }

            if (confirm)
            {
                string prompt = "Checksum 3 blocks from the loaded flash file:\n" + ProgrammingFlagClearBlockList + ".\n\n"
                    + "0x800000-0x803FFF is not checksummed.\nNo block from 0x810000 on is checksummed.\n\n"
                    + "No upload and no erase.\n\nContinue?";
                if (App.DisplayUserPrompt("Checksum 8K+8K+32K", prompt, UserPromptType.OK_CANCEL) != UserPromptResult.OK)
                {
                    return false;
                }
            }

            var kwpViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;
            if (kwpViewModel == null)
            {
                App.DisplayStatusMessage("Checksumming the flash blocks requires a KWP2000 connection.", StatusMessageType.USER);
                return false;
            }

            mAfterProgrammingFlagClear = afterComplete;
            var operation = new ChecksumFlashBlocksOperation(kwpViewModel.KWP2000CommInterface, kwpViewModel.DesiredBaudRates, GetSecuritySettings(kwpViewModel), blocks);
            StartFlashOperation(operation, this.ChecksumRestOfFirst64KCompleted, ProgrammingFlagClearStatus);
            return true;
        }

        private static SecurityAccessAction.SecurityAccessSettings GetSecuritySettings(KWP2000Interface_ViewModel kwpViewModel)
        {
            var security = new SecurityAccessAction.SecurityAccessSettings();
            security.RequestSeed = kwpViewModel.SeedRequest;
            security.SupportSpecialKey = kwpViewModel.ShouldSupportSpecialKey;
            security.UseExtendedSeedRequest = kwpViewModel.ShouldUseExtendedSeedRequest;
            return security;
        }

        private bool TryGetProgrammingFlagClearBlocks(out List<MemoryImage> blocks)
        {
            blocks = null;

            if (!IsBottomBootFirstBlock() || FlashMemoryImage == null || FlashMemoryImage.RawData == null)
            {
                App.DisplayStatusMessage("Load the bottom-boot layout and the matching flash file first.", StatusMessageType.USER);
                return false;
            }

            var sectorImages = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(FlashMemoryImage.RawData, FlashMemoryLayout));
            if (sectorImages.Count < 4
                || sectorImages[1].StartAddress != 0x804000 || sectorImages[1].Size != 0x2000
                || sectorImages[2].StartAddress != 0x806000 || sectorImages[2].Size != 0x2000
                || sectorImages[3].StartAddress != 0x808000 || sectorImages[3].Size != 0x8000)
            {
                App.DisplayStatusMessage("The loaded layout does not have the 8KB, 8KB, and 32KB blocks after 0x803FFF.", StatusMessageType.USER);
                return false;
            }

            blocks = sectorImages.GetRange(1, 3);
            return true;
        }

        private Action<bool> mAfterProgrammingFlagClear;

        private void ChecksumRestOfFirst64KCompleted(Operation operation, bool success)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                App.OperationInProgress = false;

                Action<bool> after = mAfterProgrammingFlagClear;
                mAfterProgrammingFlagClear = null;

                if (success)
                {
                    App.DisplayStatusMessage("The 8KB, 8KB, and 32KB checksums matched." + (after == null ? " Key-cycle before reading DTCs again." : ""), StatusMessageType.USER);
                }
                else
                {
                    App.DisplayStatusMessage("The 8KB, 8KB, and 32KB checksums did not all match.", StatusMessageType.USER);
                }

                after?.Invoke(success);
            }));
        }

        private bool CanExecuteChecksumRestOfFirst64KCommand(List<string> reasonsDisabled)
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
                reasonsDisabled.Add("Requires a KWP2000 connection");
                result = false;
            }

            if (App.OperationInProgress)
            {
                reasonsDisabled.Add("Another operation is in progress");
                result = false;
            }

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

            if (!IsBottomBootFirstBlock())
            {
                reasonsDisabled.Add("First sector must be 16KB at 0x800000");
                result = false;
            }

            return result;
        }

        private bool IsBottomBootFirstBlock()
        {
            return FlashMemoryLayout != null
                && FlashMemoryLayout.SectorSizes != null
                && FlashMemoryLayout.SectorSizes.Count > 1
                && FlashMemoryLayout.BaseAddress == ChecksumFlashBlocksOperation.SectorAddress
                && FlashMemoryLayout.SectorSizes[0] == ChecksumFlashBlocksOperation.SectorSize;
        }

        private void CheckIfFlashMatches()
        {
            var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

            if (KWP2000CommViewModel == null)
            {
                App.DisplayStatusMessage("Checking if flash matches flash file requires a KWP2000 connection.", StatusMessageType.USER);
                return;
            }

            var sectors = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(FlashMemoryImage.RawData, FlashMemoryLayout));
            var operation = new ChecksumFlashBlocksOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates, GetSecuritySettings(KWP2000CommViewModel), sectors);
            StartFlashOperation(operation, this.CheckIfFlashMatchesOperationCompleted, "Checking if flash matches flash file, one checksum per sector.");
        }

        private void CheckIfFlashMatchesOperationCompleted(Operation operation, bool success)
        {
            //UI should occur on the UI thread...
            Dispatcher.Invoke((Action)(() =>
            {
                string statusMessage = success
                    ? "Flash memory matches flash file."
                    : "One or more sectors differ (see the list above) or the checksum failed.";

                operation.CompletedOperationEvent -= this.CheckIfFlashMatchesOperationCompleted;

                App.PercentOperationComplete = 100.0f;

                App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
                App.DisplayUserPrompt("Checking If Flash Matches Complete", statusMessage, UserPromptType.OK);

                App.OperationInProgress = false;
            }), null);
        }

        private void OnWriteExternalFlashStarted(byte[] flashMemoryImage, MemoryLayout flashMemoryLayout, Operation.CompletedOperationDelegate onOperationComplete, bool diffWrite, bool verify)
        {
            if (App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
            {
                OnWriteExternalFlashStarted_Bootmode(flashMemoryImage, flashMemoryLayout, onOperationComplete, diffWrite, verify);
            }
            else
            {
                OnWriteExternalFlashStarted_KWP2000(flashMemoryImage, flashMemoryLayout, onOperationComplete, diffWrite, verify);
            }
        }

        private void OnWriteExternalFlashStarted_Bootmode(byte[] flashMemoryImage, MemoryLayout flashMemoryLayout, Operation.CompletedOperationDelegate onOperationComplete, bool diffWrite, bool verify)
        {
            BootstrapInterface bootstrap;
            MemoryLayout layout;
            if (!TryGetBootmodeLayoutAndUpdateUI(out bootstrap, out layout, "write"))
            {
                return;
            }

            var writeSettings = new BootmodeWriteExternalFlashOperation.BootmodeWriteExternalFlashSettings();
            writeSettings.Variant = InferBootmodeVariantFromLayout(layout, bootstrap);
            writeSettings.FlashMemoryLayout = layout;

            var sectorImages = MemoryUtils.SplitMemoryImageIntoSectors(flashMemoryImage, layout);

            var operation = new BootmodeWriteExternalFlashOperation(bootstrap, writeSettings, sectorImages);
            StartFlashOperation(operation, onOperationComplete, "Writing ECU flash memory (bootmode).");
        }

        private void OnWriteExternalFlashStarted_KWP2000(byte[] flashMemoryImage, MemoryLayout flashMemoryLayout, Operation.CompletedOperationDelegate onOperationComplete, bool diffWrite, bool verify)
        {
            var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

            var settings = new WriteExternalFlashOperation.WriteExternalFlashSettings();
            settings.CheckIfWriteRequired = diffWrite;
            settings.OnlyWriteNonMatchingSectors = diffWrite;
            settings.VerifyWrittenData = verify;
            settings.EraseEntireFlashAtOnce = false;
            settings.FlashMemoryLayout = flashMemoryLayout;
            settings.SecuritySettings = GetSecuritySettings(KWP2000CommViewModel);

            var sectorImages = MemoryUtils.SplitMemoryImageIntoSectors(flashMemoryImage, flashMemoryLayout);

            var operation = new WriteExternalFlashOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates, settings, sectorImages);
            StartFlashOperation(operation, onOperationComplete, "Writing ECU flash memory.");
        }

        private string OnWriteExternalFlashCompleted(Operation operation, bool success)
        {
            string statusMesage = "";

            var bootmodeWrite = operation as BootmodeWriteExternalFlashOperation;
            if (bootmodeWrite != null)
            {
                if (success)
                {
                    int numSectors = bootmodeWrite.NumSectors;
                    int numSuccessfullyFlashedSectors = bootmodeWrite.NumSuccessfullyFlashedSectors;
                    statusMesage = "Writing ECU flash memory succeeded. Wrote " + numSuccessfullyFlashedSectors + " of " + numSectors + " sectors in flash memory.";
                }
                else
                {
                    statusMesage = "Writing ECU flash memory failed.";
                }
                return statusMesage;
            }

            var writeOperation = (WriteExternalFlashOperation)operation;
            if (success)
            {
                int numSectors = writeOperation.NumSectors;
                int numSuccessfullyFlashedSectors = writeOperation.NumSuccessfullyFlashedSectors;

                statusMesage = "Writing ECU flash memory succeeded. Wrote " + numSuccessfullyFlashedSectors + " of " + numSectors + " sectors in flash memory."
                    + "\n" + KWP2000RamProgramIdent.WriteSuccessAdvice;
            }
            else
            {
                if (writeOperation.WasFailureCausedByPreviousIncompleteDownload)
                {
                    statusMesage = "Writing ECU flash memory failed because a previous programming operation was incomplete. Please reconnect and retry."
                        + " Do not power cycle.";
                }
                else
                {
                    statusMesage = "Writing ECU flash memory failed."
                        + "\n" + KWP2000RamProgramIdent.WriteFailureAdvice;
                }
            }

            return statusMesage;
        }

        private static BootstrapInterface.ECUFlashVariant InferBootmodeVariantFromLayout(MemoryLayout layout, BootstrapInterface bootstrap)
        {
            if (layout == null) return BootstrapInterface.ECUFlashVariant.ME7;
            // M5.9.2: 256KB at 0x800000 (flash ID 0x22BA)
            if (bootstrap != null && BootstrapInterface.IsM59FlashDevice(bootstrap.LastKnownFlashDeviceID))
                return BootstrapInterface.ECUFlashVariant.M59;
            // ME7: 0x800000, Simos3/EDC15: 0x400000
            return layout.BaseAddress == 0x400000 ? BootstrapInterface.ECUFlashVariant.Simos3 : BootstrapInterface.ECUFlashVariant.ME7;
        }

        /// <summary>Gets bootmode layout via GetBootmodeFlashLayout and updates UI. Returns false on failure.</summary>
        private bool TryGetBootmodeLayoutAndUpdateUI(out BootstrapInterface bootstrap, out MemoryLayout layout, string operationLabel)
        {
            bootstrap = null;
            layout = null;
            bootstrap = App.CommInterface as BootstrapInterface;
            if (bootstrap == null)
            {
                App.DisplayStatusMessage($"Failed to get bootstrap interface for flash {operationLabel}.", StatusMessageType.USER);
                App.OperationInProgress = false;
                return false;
            }
            App.DisplayStatusMessage("Loading flash driver for device identification...", StatusMessageType.USER);
            string errorMessage;
            if (!bootstrap.GetBootmodeFlashLayout(out layout, out errorMessage))
            {
                App.DisplayStatusMessage(errorMessage ?? "Cannot auto-detect flash layout.", StatusMessageType.USER);
                App.OperationInProgress = false;
                return false;
            }
            ApplyBootmodeDetection(bootstrap.LastKnownFlashDeviceID, layout);
            layout = FlashMemoryLayout;
            return true;
        }

        /// <summary>Returns baseImage if valid for layout, otherwise allocates layout.Size bytes filled with 0xFF.</summary>
        private static byte[] PrepareReadImage(byte[] baseImage, MemoryLayout layout)
        {
            if ((baseImage != null) && (baseImage.Length == layout.Size))
            {
                return baseImage;
            }
            var buffer = new byte[layout.Size];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0xFF;
            }
            return buffer;
        }

        /// <summary>Wires completion, sets operation state, displays message, and starts the operation.</summary>
        private void LogLoadedFlashFile(string operation)
        {
            App.DisplayStatusMessage(operation + " loaded file: " + FileNameToFlash, StatusMessageType.LOG);
        }

        private void StartFlashOperation(Operation operation, Operation.CompletedOperationDelegate onComplete, string statusMessage)
        {
            operation.CompletedOperationEvent += onComplete;
            App.CurrentOperation = operation;
            App.OperationInProgress = true;
            App.PercentOperationComplete = 0.0f;
            App.DisplayStatusMessage(statusMessage, StatusMessageType.USER);
            operation.Start();
        }

        private void OnReadExternalFlashStarted(bool checkIfReadRequired, bool onlyReadRequiredSectors, bool shouldVerifyReadData, byte[] baseImage, MemoryLayout flashLayout, Operation.CompletedOperationDelegate operationCompletedDel, ReadExternalFlashOperation.UploadRange[] checksumRanges = null)
        {
            if (App.CommInterface.CurrentProtocol == CommunicationInterface.Protocol.BootMode)
            {
                OnReadExternalFlashStarted_Bootmode(baseImage, operationCompletedDel);
            }
            else
            {
                OnReadExternalFlashStarted_KWP2000(checkIfReadRequired, onlyReadRequiredSectors, shouldVerifyReadData, baseImage, flashLayout, operationCompletedDel, checksumRanges);
            }
        }

        private void OnReadExternalFlashStarted_Bootmode(byte[] baseImage, Operation.CompletedOperationDelegate operationCompletedDel)
        {
            BootstrapInterface bootstrap;
            MemoryLayout layout;
            if (!TryGetBootmodeLayoutAndUpdateUI(out bootstrap, out layout, "read"))
            {
                return;
            }

            var readImage = PrepareReadImage(baseImage, layout);
            var sectorImages = MemoryUtils.SplitMemoryImageIntoSectors(readImage, layout);

            var settings = new BootmodeReadExternalFlashOperation.BootmodeReadExternalFlashSettings();
            settings.Variant = InferBootmodeVariantFromLayout(layout, bootstrap);
            settings.StartAddress = layout.BaseAddress;
            settings.Size = (uint)layout.Size;

            var operation = new BootmodeReadExternalFlashOperation(bootstrap, settings, sectorImages);
            StartFlashOperation(operation, operationCompletedDel, "Reading ECU flash memory via BootMode.");
        }

        private void OnReadExternalFlashStarted_KWP2000(bool checkIfReadRequired, bool onlyReadRequiredSectors, bool shouldVerifyReadData, byte[] baseImage, MemoryLayout flashLayout, Operation.CompletedOperationDelegate operationCompletedDel, ReadExternalFlashOperation.UploadRange[] checksumRanges = null)
        {
            if (flashLayout == null)
            {
                App.DisplayStatusMessage("Memory layout is required for read. Select a layout file (KWP2000) or use BootMode for auto-detect.", StatusMessageType.USER);
                App.OperationInProgress = false;
                return;
            }

            var readImage = PrepareReadImage(baseImage, flashLayout);
            var sectorImages = MemoryUtils.SplitMemoryImageIntoSectors(readImage, flashLayout);

            var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

            var settings = new ReadExternalFlashOperation.ReadExternalFlashSettings();
            settings.CheckIfSectorReadRequired = checkIfReadRequired;
            settings.OnlyReadNonMatchingSectors = onlyReadRequiredSectors;
            settings.VerifyReadData = shouldVerifyReadData;
            settings.ChecksumRanges = checksumRanges;
            settings.SecuritySettings = GetSecuritySettings(KWP2000CommViewModel);

            var operation = new ReadExternalFlashOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates, settings, sectorImages);
            StartFlashOperation(operation, operationCompletedDel, "Reading ECU flash memory.");
        }

        private bool OnReadExternalFlashCompleted(Operation operation, bool success, out MemoryImage readMemory)
        {
            readMemory = null;

            if (success)
            {
                // Handle BootMode operation
                var bootmodeOperation = operation as BootmodeReadExternalFlashOperation;
                if (bootmodeOperation != null)
                {
                    readMemory = bootmodeOperation.mReadFlashMemory;
                    return readMemory != null;
                }

                // Handle KWP2000 operation
                var readFlashOperation = operation as ReadExternalFlashOperation;
                if (readFlashOperation != null)
                {
                    //we only want the data out of the memory images read, since we already know which memory layout is being used
                    var readSectorData = new List<byte[]>();
                    foreach (var sector in readFlashOperation.FlashBlockList)
                    {
                        readSectorData.Add(sector.RawData);
                    }

                    if (!MemoryUtils.CombineMemorySectorsIntoImage(readSectorData, FlashMemoryLayout, out readMemory))
                    {
                        App.DisplayStatusMessage("Failed to combine memory sectors into one memory image", StatusMessageType.USER);
                        return false;
                    }
                    return true;
                }

                // Neither bootmode nor KWP2000 operation
                App.DisplayStatusMessage("Unknown operation type for flash read completion", StatusMessageType.USER);
                return false;
            }

            return false;
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
                    OnPropertyChanged(new PropertyChangedEventArgs("IsVerifyReadEnabled"));
                }
            }
        }
        private MemoryLayout _FlashMemoryLayout;

        private enum FlashConfirmationKind
        {
            CheckIfFlashMatches,
            ReadEntire,
            ReadDiff,
            WriteEntire,
            WriteDiff,
        }

        private const string Me75BenchPin121Note = KWP2000SettingsDefaults.Me75Pin121Hint;

        private bool ConfirmFlashOperation(string title, FlashConfirmationKind kind)
        {
            return App.DisplayUserPrompt(title, BuildFlashConfirmationMessage(kind, App.CommInterface), UserPromptType.OK_CANCEL) == UserPromptResult.OK;
        }

        private static string BuildFlashConfirmationMessage(FlashConfirmationKind kind, CommunicationInterface commInterface)
        {
            var lines = new List<string>();

            switch (kind)
            {
                case FlashConfirmationKind.CheckIfFlashMatches:
                    lines.Add("If you are ready to check if flash matches, confirm the following things:");
                    lines.Add("1) You have loaded a valid file and memory layout for the ECU.");
                    lines.Add("2) The engine is not running.");
                    break;

                case FlashConfirmationKind.ReadEntire:
                    lines.Add("If you are ready to read, confirm the following things:");
                    lines.Add("1) You have loaded a valid memory layout for the ECU.");
                    lines.Add("2) The engine is not running.");
                    lines.Add("Note: Some non-standard flash memory chips may prevent reading the flash memory.");
                    AddMe75BenchPin121NoteIfKwp(lines, commInterface);
                    break;

                case FlashConfirmationKind.ReadDiff:
                    lines.Add("If you are ready to read, confirm the following things:");
                    lines.Add("1) You have loaded a valid file and memory layout for the ECU.");
                    lines.Add("2) The engine is not running.");
                    lines.Add("Note: Diff read will only read sectors that differ from the loaded file.");
                    lines.Add("Note: Some non-standard flash memory chips may prevent reading the flash memory.");
                    AddMe75BenchPin121NoteIfKwp(lines, commInterface);
                    break;

                case FlashConfirmationKind.WriteEntire:
                    AddWriteChecklistLines(lines, includeCommercialDisclaimer: false);
                    lines.Add("Note: Some non-standard flash memory chips may prevent writing the flash memory.");
                    AddMe75BenchPin121NoteIfKwp(lines, commInterface);
                    break;

                case FlashConfirmationKind.WriteDiff:
                    AddWriteChecklistLines(lines, includeCommercialDisclaimer: true);
                    lines.Add("Note: Some non-standard flash memory chips may prevent writing the flash memory.");
                    AddMe75BenchPin121NoteIfKwp(lines, commInterface);
                    break;

                default:
                    Debug.Fail("Unknown FlashConfirmationKind");
                    break;
            }

            AddConfirmationFooter(lines);
            return JoinConfirmationLines(lines);
        }

        private static string JoinConfirmationLines(IEnumerable<string> lines)
        {
            return string.Join(Environment.NewLine, lines);
        }

        private static void AddConfirmationFooter(List<string> lines)
        {
            lines.Add(string.Empty);
            lines.Add("Click OK to confirm, otherwise Cancel.");
        }

        private static void AddWriteChecklistLines(List<string> lines, bool includeCommercialDisclaimer)
        {
            lines.Add("If you are ready to write, confirm the following things:");
            lines.Add("1) You have loaded a valid file and memory layout for the ECU (top-boot vs bottom-boot must match the flash chip).");
            lines.Add("2) The engine is not running.");
            lines.Add("3) Battery voltage is at least 12 volts.");
            lines.Add("4) It is OK the ECU adaptation channels will be reset to defaults");
            lines.Add("5) Flashing process can run uninterrupted until complete.");
            lines.Add("6) You agree to release Nefarious Motorsports Inc from all liability.");
            if (includeCommercialDisclaimer)
            {
                lines.Add("7) You agree to not use this tool commercially, as the nefmoto karma gods shall smite you if you do.");
            }
        }

        private static void AddMe75BenchPin121NoteIfKwp(List<string> lines, CommunicationInterface commInterface)
        {
            if (commInterface != null && commInterface.CurrentProtocol == CommunicationInterface.Protocol.KWP2000)
            {
                lines.Add(string.Empty);
                lines.Add(Me75BenchPin121Note);
            }
        }
    }

    /// <summary>UI option for bootmode SPI EEPROM presets (ME7.1/7.5, SSC/XSSC).</summary>
    public sealed class BootmodeEepromPresetOption
    {
        public string DisplayName { get; private set; }
        public BootstrapInterface.BootmodeEepromSettings Settings { get; private set; }

        public BootmodeEepromPresetOption(string displayName, BootstrapInterface.BootmodeEepromSettings settings)
        {
            DisplayName = displayName;
            Settings = settings;
        }

        public static IReadOnlyList<BootmodeEepromPresetOption> All { get; } = new List<BootmodeEepromPresetOption>
        {
            new BootmodeEepromPresetOption(
                "ME7.1 - 95040 SSC P4.7 (512 B)",
                BootstrapInterface.BootmodeEepromSettings.ForMe71()),
            new BootmodeEepromPresetOption(
                "ME7.5 - 95040 SSC P4.7 (512 B)",
                BootstrapInterface.BootmodeEepromSettings.ForMe75()),
            new BootmodeEepromPresetOption(
                "ME7.1 - 95040 XSSC P4.7 (512 B)",
                MakeXssc(BootstrapInterface.BootmodeEepromSettings.ForMe71())),
            new BootmodeEepromPresetOption(
                "ME7.5 - 95040 XSSC P4.7 (512 B)",
                MakeXssc(BootstrapInterface.BootmodeEepromSettings.ForMe75())),
        };

        private static BootstrapInterface.BootmodeEepromSettings MakeXssc(BootstrapInterface.BootmodeEepromSettings baseSettings)
        {
            return new BootstrapInterface.BootmodeEepromSettings
            {
                Periph = BootstrapInterface.BootmodeEepromPeriph.XSSC,
                EepromType = baseSettings.EepromType,
                PortNumber = baseSettings.PortNumber,
                PinNumber = baseSettings.PinNumber,
                Size = baseSettings.Size
            };
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
