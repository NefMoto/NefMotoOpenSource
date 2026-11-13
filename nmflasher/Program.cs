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

using System.Diagnostics;
using System.Reflection;
using System.Xml.Serialization;

using Communication;
using Shared;

namespace Nmflasher
{
    public static class Program
    {
        const uint DefaultKwpBaud = 124800;
        const uint DefaultBootmodeBaud = 57600;
        const int OperationWaitMinutes = 60;

        static readonly object LogLock = new object();
        static readonly uint[] KnownBaudRates = { 9600, 10400, 38400, 52800, 124800 };

        public static int Main(string[] args)
        {
            foreach (string arg in args)
            {
                if ((arg == "-h") || (arg == "--help"))
                {
                    Usage(Console.Out);
                    return 0;
                }

                if ((arg == "-V") || (arg == "--version"))
                {
                    Console.WriteLine("nmflasher " + VersionText());
                    return 0;
                }
            }

            if (!TryParse(args, out Request? request, out string? error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    Console.Error.WriteLine("nmflasher: " + error);
                }

                Console.Error.WriteLine("Try 'nmflasher --help' for more information.");
                return 2;
            }

            if (request!.UsbList)
            {
                return ListUsb();
            }

            if (request.LayoutList)
            {
                return ListLayouts();
            }

            return Run(request);
        }

        static int Run(Request request)
        {
            if (request.Info)
            {
                return RunInfo(request);
            }

            byte[]? image = null;
            if (request.InputPath != null)
            {
                if (!File.Exists(request.InputPath))
                {
                    Log("Image file was not found: " + request.InputPath);
                    return 2;
                }

                image = File.ReadAllBytes(request.InputPath);
            }

            MemoryLayout? layout = null;
            if (request.Kwp)
            {
                if (!TryLoadKwpLayout(request, image, out layout))
                {
                    return 2;
                }
            }

            if (!Confirm(request))
            {
                Log("Cancelled. Nothing was sent.");
                return 1;
            }

            DeviceInfo? device = ClaimDevice(request, out int selectStatus);
            if (device == null)
            {
                return selectStatus;
            }

            if (request.Kwp)
            {
                return RunKwp(request, device, layout!, image);
            }

            return RunBootmode(request, device, image);
        }

        static int RunInfo(Request request)
        {
            DeviceInfo? device = ClaimDevice(request, out int selectStatus);
            if (device == null)
            {
                return selectStatus;
            }

            if (request.Kwp)
            {
                return RunKwpInfo(request, device);
            }

            return RunBootmodeInfo(request, device);
        }

        static DeviceInfo? ClaimDevice(Request request, out int status)
        {
            if (Process.GetProcessesByName("ECUFlasher").Length > 0)
            {
                Log("ECUFlasher is running. Close it. It owns the FTDI device.");
                status = 1;
                return null;
            }

            return SelectDevice(request.UsbIndex, out status);
        }

        static void WatchConnection(CommunicationInterface comm, ManualResetEvent connected, ManualResetEvent terminated)
        {
            comm.ConnectionStatusChangedEvent += (c, status, willReconnect) =>
            {
                if (status == CommunicationInterface.ConnectionStatusType.Connected)
                {
                    connected.Set();
                }

                if (status == CommunicationInterface.ConnectionStatusType.CommunicationTerminated)
                {
                    terminated.Set();
                }
            };
        }

        static int WithKwp(Request request, DeviceInfo device, Func<KWP2000Interface, ManualResetEvent, int> body)
        {
            string initName = request.Slow ? "Slow" : "Fast";
            Log("Opening FTDI " + device.Description + " " + device.SerialNumber + ". " + initName + " init.");
            Log("Session baud list: " + string.Join(", ", SessionBaudRates(request.Baud)) + ".");

            var kwp = new KWP2000Interface(LogStatus);
            HookPrompts(kwp);
            using var connected = new ManualResetEvent(false);
            using var terminated = new ManualResetEvent(false);
            WatchConnection(kwp, connected, terminated);
            kwp.SelectedDeviceInfo = device;
            byte connectAddress = request.Slow
                ? KWP2000Interface.DEFAULT_ECU_SLOWINIT_KWP1281_ADDRESS
                : KWP2000Interface.DEFAULT_ECU_FASTINIT_KWP2000_PHYSICAL_ADDRESS;
            bool started = request.Slow
                ? kwp.ConnectToECUSlowInit(connectAddress)
                : kwp.ConnectToECUFastInit(KWP2000AddressMode.Physical, connectAddress);
            if (!started)
            {
                Log(initName + " init did not start.");
                return 1;
            }

            int connectSeconds = request.Slow ? 120 : 60;
            if (WaitHandle.WaitAny(new WaitHandle[] { connected, terminated }, TimeSpan.FromSeconds(connectSeconds)) != 0)
            {
                Log(initName + " init was not answered.");
                ReleaseKwp(kwp, terminated);
                return 1;
            }

            try
            {
                return body(kwp, terminated);
            }
            finally
            {
                ReleaseKwp(kwp, terminated);
            }
        }

        static int WithBoot(Request request, DeviceInfo device, Func<BootstrapInterface, int> body)
        {
            uint baud = request.HasBaud ? request.Baud : DefaultBootmodeBaud;
            Log("Opening FTDI " + device.Description + " " + device.SerialNumber + " at " + baud + ".");
            Log("The ECU must already be in boot mode.");

            var boot = new BootstrapInterface(LogStatus);
            HookPrompts(boot);
            using var connected = new ManualResetEvent(false);
            using var terminated = new ManualResetEvent(false);
            WatchConnection(boot, connected, terminated);
            boot.SelectedDeviceInfo = device;
            if (!boot.OpenConnection(baud))
            {
                Log("Boot mode connection did not start.");
                return 1;
            }

            if (WaitHandle.WaitAny(new WaitHandle[] { connected, terminated }, TimeSpan.FromSeconds(30)) != 0)
            {
                Log("Boot mode did not answer.");
                CloseBoot(boot);
                return 1;
            }

            try
            {
                return body(boot);
            }
            finally
            {
                CloseBoot(boot);
            }
        }

        static int RunKwpInfo(Request request, DeviceInfo device)
        {
            return WithKwp(request, device, (kwp, terminated) =>
            {
                var operation = new ReadAllECUIdentificationOptionsOperation(kwp, SessionBaudRates(request.Baud), KWP2000DiagnosticSessionType.StandardSession);
                bool success = WaitForOperation(operation, "Reading all ECU info.", () => ReleaseKwp(kwp, terminated));
                if (!success)
                {
                    Log("Reading all ECU info failed.");
                    return 1;
                }

                PrintKwpInfo(operation.ECUInfo);
                return 0;
            });
        }

        static void PrintKwpInfo(KWP2000IdentificationInfo identInfo)
        {
            if (identInfo.IdentOptionData.Count == 0)
            {
                Log("No ECU info read.");
                return;
            }

            Log("Read " + identInfo.IdentOptionData.Count + " ECU info entries:");
            bool sawRamProgramIdent = false;
            foreach (byte identOption in identInfo.IdentOptionData.Keys)
            {
                string identOptionString = "";
                KWP2000IdentificationOptionValue optionValue;
                if (identInfo.GetIdentificationOptionValue(identOption, out optionValue))
                {
                    identOptionString = optionValue.ToString();
                }

                Log("\t0x" + identOption.ToString("X2") + ", " + IdentOptionName(identOption) + ": " + identOptionString);
                if (!sawRamProgramIdent && KWP2000RamProgramIdent.ContainsRamProgramIdent(identOptionString))
                {
                    sawRamProgramIdent = true;
                }
            }

            if (sawRamProgramIdent)
            {
                Log(KWP2000RamProgramIdent.UserStatusMessage);
            }
        }

        static string IdentOptionName(byte identOption)
        {
            var identOptionEnum = (KWP2000IdentificationOption)identOption;
            object description = DescriptionAttributeConverterLogic.GetDescriptionAttribute(identOptionEnum);
            if (description != null)
            {
                return description.ToString() ?? identOptionEnum.ToString();
            }

            return identOptionEnum.ToString();
        }

        static int RunBootmodeInfo(Request request, DeviceInfo device)
        {
            return WithBoot(request, device, boot =>
            {
                Log("Reading bootmode ECU information...");
                var entries = BootmodeEcuInfo.Read(boot);
                foreach (BootmodeEcuInfo.Entry entry in entries)
                {
                    Log(entry.Name + ": " + entry.Value);
                }

                Log("Read " + entries.Count + " bootmode ECU info entries.");
                return 0;
            });
        }

        static int ListUsb()
        {
            var devices = FtdiDevices();
            if (devices.Count == 0)
            {
                Log("No FTDI device.");
                return 1;
            }

            PrintDevices(devices);
            return 0;
        }

        static int ListLayouts()
        {
            string dir = MemoryLayout.GetLayoutsDirectory();
            if (string.IsNullOrEmpty(dir))
            {
                Log("No layouts.");
                return 1;
            }

            string[] paths;
            try
            {
                paths = Directory.GetFiles(dir, "*" + MemoryLayout.MEMORY_LAYOUT_FILE_EXT);
            }
            catch
            {
                Log("No layouts.");
                return 1;
            }

            var names = new List<string>();
            foreach (string path in paths)
            {
                string name = LayoutBasename(Path.GetFileName(path));
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                Log("No layouts.");
                return 1;
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                Log(name);
            }

            return 0;
        }

        static bool TryLoadKwpLayout(Request request, byte[]? image, out MemoryLayout? layout)
        {
            layout = null;
            string? name = request.LayoutName;
            if ((name == null) && (image != null))
            {
                name = LayoutNameForLength(image.Length);
                if (name == null)
                {
                    Log("Image is " + image.Length + " bytes. Pass --layout.");
                    return false;
                }

                Log("Image is " + image.Length + " bytes. Using layout " + name + ".");
            }

            if (string.IsNullOrEmpty(name))
            {
                Log("Pass --layout.");
                return false;
            }

            layout = LoadLayout(name, out string? error);
            if (layout == null)
            {
                Log(error ?? "Layout was not loaded.");
                return false;
            }

            if ((image != null) && ((uint)image.Length != layout.Size))
            {
                Log("Image is " + image.Length + " bytes. Layout " + name + " is " + layout.Size + " bytes.");
                return false;
            }

            Log("Layout " + name + ", " + layout.Size + " bytes, base 0x" + layout.BaseAddress.ToString("X6") + ".");
            return true;
        }

        static int RunKwp(Request request, DeviceInfo device, MemoryLayout layout, byte[]? image)
        {
            return WithKwp(request, device, (kwp, terminated) =>
            {
                uint[] baudRates = SessionBaudRates(request.Baud);
                Operation operation = request.Read
                    ? CreateKwpRead(kwp, baudRates, layout, image, request)
                    : CreateKwpWrite(kwp, baudRates, layout, image!, request);
                string status = request.Read ? "Reading ECU flash memory." : "Writing ECU flash memory.";
                bool success = WaitForOperation(operation, status, () => ReleaseKwp(kwp, terminated));
                if (request.Read)
                {
                    return FinishKwpRead(operation, success, layout, request.OutputPath!);
                }

                Log(KwpWriteResult((WriteExternalFlashOperation)operation, success));
                return success ? 0 : 1;
            });
        }

        static Operation CreateKwpRead(KWP2000Interface kwp, uint[] baudRates, MemoryLayout layout, byte[]? image, Request request)
        {
            bool verify = true;
            int checksumCount = 0;
            if (!request.Diff)
            {
                FullReadVerify(layout, request.NoVerify, out verify, out checksumCount);
            }

            byte[] readImage = request.Diff ? image! : BlankImage(layout);
            var sectors = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(readImage, layout));
            var settings = new ReadExternalFlashOperation.ReadExternalFlashSettings();
            settings.CheckIfSectorReadRequired = request.Diff;
            settings.OnlyReadNonMatchingSectors = request.Diff;
            settings.VerifyReadData = verify;
            settings.ChecksumSectorCount = checksumCount;

            return new ReadExternalFlashOperation(kwp, baudRates, settings, sectors);
        }

        static Operation CreateKwpWrite(KWP2000Interface kwp, uint[] baudRates, MemoryLayout layout, byte[] image, Request request)
        {
            var sectors = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(image, layout));
            var settings = new WriteExternalFlashOperation.WriteExternalFlashSettings();
            settings.CheckIfWriteRequired = request.Diff;
            settings.OnlyWriteNonMatchingSectors = request.Diff;
            settings.VerifyWrittenData = !request.NoVerify;
            settings.EraseEntireFlashAtOnce = false;
            settings.FlashMemoryLayout = layout;
            return new WriteExternalFlashOperation(kwp, baudRates, settings, sectors);
        }

        static int FinishKwpRead(Operation operation, bool success, MemoryLayout layout, string outputPath)
        {
            if (!success)
            {
                Log("Reading ECU flash memory failed.");
                return 1;
            }

            var read = (ReadExternalFlashOperation)operation;
            var sectorData = new List<byte[]>();
            foreach (MemoryImage sector in read.FlashBlockList)
            {
                sectorData.Add(sector.RawData);
            }

            if (!MemoryUtils.CombineMemorySectorsIntoImage(sectorData, layout, out MemoryImage combined))
            {
                Log("Failed to combine memory sectors into one memory image.");
                return 1;
            }

            return SaveRead(combined, outputPath);
        }

        static int RunBootmode(Request request, DeviceInfo device, byte[]? image)
        {
            return WithBoot(request, device, boot =>
            {
                if (!boot.GetBootmodeFlashLayout(out MemoryLayout layout, out string error) || (layout == null))
                {
                    Log(string.IsNullOrEmpty(error) ? "Flash layout was not read." : error);
                    return 1;
                }

                if ((layout.SectorSizes == null) || (layout.SectorSizes.Count == 0) || (layout.Size == 0))
                {
                    Log("The flash layout has no sectors.");
                    return 1;
                }

                Log("Chip layout is " + layout.Size + " bytes, base 0x" + layout.BaseAddress.ToString("X6") + ".");
                if (request.Read)
                {
                    return FinishBootmodeRead(boot, layout, request.OutputPath!);
                }

                if ((image == null) || ((uint)image.Length != layout.Size))
                {
                    Log("Image is " + (image == null ? 0 : image.Length) + " bytes. The chip layout is " + layout.Size + " bytes.");
                    return 1;
                }

                var settings = new BootmodeWriteExternalFlashOperation.BootmodeWriteExternalFlashSettings();
                settings.Variant = InferBootmodeVariant(layout);
                settings.FlashMemoryLayout = layout;
                var sectors = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(image, layout));
                var operation = new BootmodeWriteExternalFlashOperation(boot, settings, sectors);
                bool success = WaitForOperation(operation, "Writing ECU flash memory (bootmode).", () => CloseBoot(boot));
                Log(BootmodeWriteResult(operation, success));
                return success ? 0 : 1;
            });
        }

        static int FinishBootmodeRead(BootstrapInterface boot, MemoryLayout layout, string outputPath)
        {
            byte[] readImage = BlankImage(layout);
            var sectors = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(readImage, layout));
            var settings = new BootmodeReadExternalFlashOperation.BootmodeReadExternalFlashSettings();
            settings.Variant = InferBootmodeVariant(layout);
            settings.StartAddress = layout.BaseAddress;
            settings.Size = layout.Size;
            var operation = new BootmodeReadExternalFlashOperation(boot, settings, sectors);
            bool success = WaitForOperation(operation, "Reading ECU flash memory via BootMode.", () => CloseBoot(boot));
            if (!success || (operation.mReadFlashMemory == null))
            {
                Log("Reading ECU flash memory failed.");
                return 1;
            }

            return SaveRead(operation.mReadFlashMemory, outputPath);
        }

        static int SaveRead(MemoryImage image, string outputPath)
        {
            if (!image.SaveToFile(outputPath))
            {
                Log("The read finished, but the file was not saved: " + outputPath);
                return 1;
            }

            Log("Reading ECU flash memory succeeded.");
            Log("Wrote " + image.Size + " bytes to " + Path.GetFullPath(outputPath) + ".");
            return 0;
        }

        static bool WaitForOperation(Operation operation, string status, Action release)
        {
            Log(status);
            using var done = new ManualResetEvent(false);
            bool operationSucceeded = false;
            operation.CompletedOperationEvent += (op, success) =>
            {
                operationSucceeded = success;
                try
                {
                    done.Set();
                }
                catch (ObjectDisposedException)
                {
                }
            };

            operation.Start();
            if (!done.WaitOne(TimeSpan.FromMinutes(OperationWaitMinutes)))
            {
                Log("The operation did not finish within " + OperationWaitMinutes + " minutes.");
                operation.Abort();
                done.WaitOne(TimeSpan.FromSeconds(5));
                release();
                return false;
            }

            return operationSucceeded;
        }

        static string KwpWriteResult(WriteExternalFlashOperation write, bool success)
        {
            if (success)
            {
                return "Writing ECU flash memory succeeded. Wrote " + write.NumSuccessfullyFlashedSectors + " of " + write.NumSectors + " sectors in flash memory."
                    + "\n" + KWP2000RamProgramIdent.WriteSuccessAdvice;
            }

            if (write.WasFailureCausedByPreviousIncompleteDownload)
            {
                return "Writing ECU flash memory failed because a previous programming operation was incomplete. Please reconnect and retry."
                    + " Do not power cycle.";
            }

            return "Writing ECU flash memory failed."
                + "\n" + KWP2000RamProgramIdent.WriteFailureAdvice;
        }

        static string BootmodeWriteResult(BootmodeWriteExternalFlashOperation write, bool success)
        {
            if (success)
            {
                return "Writing ECU flash memory succeeded. Wrote " + write.NumSuccessfullyFlashedSectors + " of " + write.NumSectors + " sectors in flash memory.";
            }

            return "Writing ECU flash memory failed.";
        }

        static void FullReadVerify(MemoryLayout layout, bool noVerify, out bool verify, out int checksumCount)
        {
            verify = false;
            checksumCount = 0;
            var sizes = layout.SectorSizes;
            bool bottomBoot = (sizes != null)
                && (sizes.Count > 1)
                && (layout.BaseAddress == ChecksumFlashBlocksOperation.SectorAddress)
                && (sizes[0] == ChecksumFlashBlocksOperation.SectorSize);
            if (!bottomBoot || (sizes == null))
            {
                return;
            }

            verify = true;
            if (!noVerify)
            {
                return;
            }

            if (sizes.Count < 4)
            {
                verify = false;
                return;
            }

            checksumCount = 4;
        }

        static BootstrapInterface.ECUFlashVariant InferBootmodeVariant(MemoryLayout layout)
        {
            return layout.BaseAddress == 0x400000
                ? BootstrapInterface.ECUFlashVariant.Simos3
                : BootstrapInterface.ECUFlashVariant.ME7;
        }

        static byte[] BlankImage(MemoryLayout layout)
        {
            var buffer = new byte[checked((int)layout.Size)];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0xFF;
            }

            return buffer;
        }

        static string? LayoutNameForLength(int length)
        {
            if (length == 1024 * 1024)
            {
                return "29F800BB";
            }

            if (length == 512 * 1024)
            {
                return "29F400BB";
            }

            if (length == 256 * 1024)
            {
                return "29F200";
            }

            return null;
        }

        static MemoryLayout? LoadLayout(string name, out string? error)
        {
            error = null;
            string baseName = LayoutBasename(name);
            string path = Path.Combine(MemoryLayout.GetLayoutsDirectory(), baseName + MemoryLayout.MEMORY_LAYOUT_FILE_EXT);
            if (!File.Exists(path))
            {
                error = "Layout was not found: " + path;
                return null;
            }

            using FileStream stream = File.OpenRead(path);
            var serializer = new XmlSerializer(typeof(MemoryLayout));
            var layout = serializer.Deserialize(stream) as MemoryLayout;
            if ((layout == null) || !layout.Validate())
            {
                error = "Layout is not valid: " + baseName;
                return null;
            }

            return layout;
        }

        static string LayoutBasename(string name)
        {
            string fileName = Path.GetFileName(name);
            string ext = MemoryLayout.MEMORY_LAYOUT_FILE_EXT;
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - ext.Length);
            }

            return fileName;
        }

        static uint[] SessionBaudRates(uint desired)
        {
            if (desired == 0)
            {
                desired = DefaultKwpBaud;
            }

            var rates = new List<uint>();
            bool knownDesired = false;
            for (int i = KnownBaudRates.Length - 1; i >= 0; i--)
            {
                if (KnownBaudRates[i] == desired)
                {
                    knownDesired = true;
                }

                if (KnownBaudRates[i] <= desired)
                {
                    rates.Add(KnownBaudRates[i]);
                }
            }

            if (!knownDesired)
            {
                rates.Insert(0, desired);
            }

            return rates.ToArray();
        }

        static bool Confirm(Request request)
        {
            foreach (string line in Checklist(request))
            {
                Log(line);
            }

            if (request.Yes)
            {
                Log("Continuing because --yes was passed.");
                return true;
            }

            Log("Type ok or cancel.");
            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null)
                {
                    return false;
                }

                if (line.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (line.Trim().Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                Log("Type ok or cancel.");
            }
        }

        static List<string> Checklist(Request request)
        {
            var lines = new List<string>();
            if (request.Read && !request.Diff)
            {
                lines.Add("If you are ready to read, confirm the following things:");
                lines.Add("1) You have loaded a valid memory layout for the ECU.");
                lines.Add("2) The engine is not running.");
                lines.Add("Note: Some non-standard flash memory chips may prevent reading the flash memory.");
            }
            else if (request.Read)
            {
                lines.Add("If you are ready to read, confirm the following things:");
                lines.Add("1) You have loaded a valid file and memory layout for the ECU.");
                lines.Add("2) The engine is not running.");
                lines.Add("Note: Diff read will only read sectors that differ from the loaded file.");
                lines.Add("Note: Some non-standard flash memory chips may prevent reading the flash memory.");
            }
            else
            {
                lines.Add("If you are ready to write, confirm the following things:");
                lines.Add("1) You have loaded a valid file and memory layout for the ECU (top-boot vs bottom-boot must match the flash chip).");
                lines.Add("2) The engine is not running.");
                lines.Add("3) Battery voltage is at least 12 volts.");
                lines.Add("4) It is OK the ECU adaptation channels will be reset to defaults");
                lines.Add("5) Flashing process can run uninterrupted until complete.");
                lines.Add("6) You agree to release Nefarious Motorsports Inc from all liability.");
                if (request.Diff)
                {
                    lines.Add("7) You agree to not use this tool commercially, as the nefmoto karma gods shall smite you if you do.");
                }

                lines.Add("Note: Some non-standard flash memory chips may prevent writing the flash memory.");
            }

            if (request.Kwp)
            {
                lines.Add(KWP2000SettingsDefaults.Me75Pin121Hint);
            }
            else
            {
                lines.Add("The ECU must already be in boot mode. Ground the boot pin as power comes up, then release it.");
            }

            lines.Add(string.Empty);
            lines.Add("Click OK to confirm, otherwise Cancel.");
            return lines;
        }

        static void HookPrompts(CommunicationInterface comm)
        {
            comm.mBeginDisplayUserPrompt += BeginPrompt;
        }

        static void BeginPrompt(string title, string message, UserPromptType promptType, Action<UserPromptResult> completed)
        {
            var thread = new Thread(() =>
            {
                UserPromptResult result;
                try
                {
                    result = Ask(title, message, promptType);
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                    result = UserPromptResult.CANCEL;
                }

                completed(result);
            });
            thread.IsBackground = true;
            thread.Name = "nmflasher prompt";
            thread.Start();
        }

        static UserPromptResult Ask(string title, string message, UserPromptType promptType)
        {
            while (true)
            {
                lock (LogLock)
                {
                    Console.Error.WriteLine(title);
                    if (!string.IsNullOrEmpty(message))
                    {
                        Console.Error.WriteLine(message);
                    }

                    Console.Error.WriteLine(PromptHint(promptType));
                }

                string? line = Console.ReadLine();
                if (line == null)
                {
                    return UserPromptResult.CANCEL;
                }

                if (TryMapAnswer(line.Trim(), promptType, out UserPromptResult result))
                {
                    return result;
                }

                Log("Answer with " + PromptHint(promptType) + ".");
            }
        }

        static string PromptHint(UserPromptType promptType)
        {
            switch (promptType)
            {
                case UserPromptType.OK:
                    return "ok";
                case UserPromptType.OK_CANCEL:
                    return "ok, cancel";
                case UserPromptType.YES_NO_CANCEL:
                    return "yes, no, cancel";
                case UserPromptType.CONTINUE_CHECK_CANCEL:
                    return "yes (check), no (continue), check, ok (continue), cancel";
                default:
                    return "cancel";
            }
        }

        static bool TryMapAnswer(string answer, UserPromptType promptType, out UserPromptResult result)
        {
            result = UserPromptResult.NONE;
            if (answer.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (promptType == UserPromptType.OK)
                {
                    return false;
                }

                result = UserPromptResult.CANCEL;
                return true;
            }

            if (answer.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                if ((promptType == UserPromptType.YES_NO_CANCEL))
                {
                    return false;
                }

                result = UserPromptResult.OK;
                return true;
            }

            if (answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                if (promptType == UserPromptType.YES_NO_CANCEL)
                {
                    result = UserPromptResult.YES;
                    return true;
                }

                if (promptType == UserPromptType.CONTINUE_CHECK_CANCEL)
                {
                    result = UserPromptResult.CHECK;
                    return true;
                }

                return false;
            }

            if (answer.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                if (promptType == UserPromptType.YES_NO_CANCEL)
                {
                    result = UserPromptResult.NO;
                    return true;
                }

                if (promptType == UserPromptType.CONTINUE_CHECK_CANCEL)
                {
                    result = UserPromptResult.OK;
                    return true;
                }

                return false;
            }

            if (answer.Equals("check", StringComparison.OrdinalIgnoreCase) && (promptType == UserPromptType.CONTINUE_CHECK_CANCEL))
            {
                result = UserPromptResult.CHECK;
                return true;
            }

            return false;
        }

        static void ReleaseKwp(KWP2000Interface kwp, ManualResetEvent terminated)
        {
            var status = kwp.ConnectionStatus;
            if (status == CommunicationInterface.ConnectionStatusType.CommunicationTerminated)
            {
                return;
            }

            if (status != CommunicationInterface.ConnectionStatusType.Disconnected)
            {
                kwp.ConnectionStatus = CommunicationInterface.ConnectionStatusType.Disconnected;
            }

            terminated.WaitOne(TimeSpan.FromSeconds(5));
        }

        static void CloseBoot(BootstrapInterface boot)
        {
            if (boot.ConnectionStatus != CommunicationInterface.ConnectionStatusType.CommunicationTerminated)
            {
                boot.CloseConnection();
            }
        }

        static List<DeviceInfo> FtdiDevices()
        {
            var devices = new List<DeviceInfo>();
            foreach (DeviceInfo info in DeviceManager.EnumerateAllDevices())
            {
                if (info.Type == DeviceType.FTDI)
                {
                    devices.Add(info);
                }
            }

            return devices;
        }

        static void PrintDevices(IReadOnlyList<DeviceInfo> devices)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                Log(i + "  " + devices[i].Description + "  " + devices[i].SerialNumber);
            }
        }

        static DeviceInfo? SelectDevice(int? index, out int status)
        {
            var devices = FtdiDevices();
            if (devices.Count == 0)
            {
                Log("No FTDI device.");
                status = 1;
                return null;
            }

            if (index.HasValue)
            {
                if ((index.Value < 0) || (index.Value >= devices.Count))
                {
                    Log("USB index " + index.Value + " is not in the list.");
                    PrintDevices(devices);
                    status = 2;
                    return null;
                }

                status = 0;
                return devices[index.Value];
            }

            if (devices.Count == 1)
            {
                status = 0;
                return devices[0];
            }

            Log("More than one FTDI device. Pass --usb N.");
            PrintDevices(devices);
            status = 2;
            return null;
        }

        static bool TryParse(string[] args, out Request? request, out string? error)
        {
            request = new Request();
            error = null;
            if (args.Length == 0)
            {
                error = "missing command";
                return false;
            }

            var positionals = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if ((arg == "-i") || (arg == "-o") || (arg == "--layout") || (arg == "--baud") || (arg == "--usb"))
                {
                    if ((i + 1) >= args.Length)
                    {
                        error = "Missing value for " + arg + ".";
                        return false;
                    }

                    string value = args[++i];
                    if (arg == "-i")
                    {
                        if (request.InputPath != null)
                        {
                            error = "-i was given twice.";
                            return false;
                        }

                        request.InputPath = value;
                    }
                    else if (arg == "-o")
                    {
                        if (request.OutputPath != null)
                        {
                            error = "-o was given twice.";
                            return false;
                        }

                        request.OutputPath = value;
                    }
                    else if (arg == "--layout")
                    {
                        if (request.LayoutName != null)
                        {
                            error = "--layout was given twice.";
                            return false;
                        }

                        request.LayoutName = value;
                    }
                    else if (arg == "--baud")
                    {
                        if (request.HasBaud || !uint.TryParse(value, out uint baud) || (baud == 0))
                        {
                            error = request.HasBaud ? "--baud was given twice." : "Baud must be a positive number.";
                            return false;
                        }

                        request.Baud = baud;
                        request.HasBaud = true;
                    }
                    else if (arg == "--usb")
                    {
                        if (request.UsbIndex.HasValue || !int.TryParse(value, out int usb) || (usb < 0))
                        {
                            error = request.UsbIndex.HasValue ? "--usb was given twice." : "USB index must be a number from the list.";
                            return false;
                        }

                        request.UsbIndex = usb;
                    }

                    continue;
                }

                if (arg == "--slow")
                {
                    if (request.Slow)
                    {
                        error = "--slow was given twice.";
                        return false;
                    }

                    request.Slow = true;
                    continue;
                }

                if (arg == "--no-verify")
                {
                    if (request.NoVerify)
                    {
                        error = "--no-verify was given twice.";
                        return false;
                    }

                    request.NoVerify = true;
                    continue;
                }

                if (arg == "--yes")
                {
                    if (request.Yes)
                    {
                        error = "--yes was given twice.";
                        return false;
                    }

                    request.Yes = true;
                    continue;
                }

                if (arg.StartsWith("-"))
                {
                    error = "Unknown option " + arg + ".";
                    return false;
                }

                positionals.Add(arg);
            }

            if (positionals.Count == 1)
            {
                string command = positionals[0];
                if (command.Equals("usb", StringComparison.OrdinalIgnoreCase) || command.Equals("layouts", StringComparison.OrdinalIgnoreCase))
                {
                    if (HasFlashOption(request))
                    {
                        error = command + " does not take options.";
                        return false;
                    }

                    if (command.Equals("usb", StringComparison.OrdinalIgnoreCase))
                    {
                        request.UsbList = true;
                    }
                    else
                    {
                        request.LayoutList = true;
                    }

                    return true;
                }
            }

            if (positionals.Count == 2)
            {
                return ParseInfo(positionals, request, out error);
            }

            if (positionals.Count != 3)
            {
                error = "Pass usb, layouts, or kwp or bootmode, then info, read, or write.";
                return false;
            }

            string protocol = positionals[0];
            string verb = positionals[1];
            string mode = positionals[2];
            if (protocol.Equals("kwp", StringComparison.OrdinalIgnoreCase))
            {
                request.Kwp = true;
            }
            else if (!protocol.Equals("bootmode", StringComparison.OrdinalIgnoreCase))
            {
                error = "Protocol must be kwp or bootmode.";
                return false;
            }

            if (verb.Equals("read", StringComparison.OrdinalIgnoreCase))
            {
                request.Read = true;
            }
            else if (!verb.Equals("write", StringComparison.OrdinalIgnoreCase))
            {
                error = "Command must be read or write.";
                return false;
            }

            if (mode.Equals("diff", StringComparison.OrdinalIgnoreCase))
            {
                request.Diff = true;
            }
            else if (!mode.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                error = "Mode must be full or diff.";
                return false;
            }

            if (!request.Kwp && request.Diff)
            {
                error = "Diff read and diff write are not available in boot mode.";
                return false;
            }

            if (!request.Kwp && request.Slow)
            {
                error = "--slow applies to KWP.";
                return false;
            }

            if (!request.Kwp && (request.LayoutName != null))
            {
                error = "Boot mode reads the chip id. Do not pass --layout.";
                return false;
            }

            if (request.Read)
            {
                if (request.OutputPath == null)
                {
                    error = "A read needs -o out.bin.";
                    return false;
                }

                if (request.Diff)
                {
                    if (request.InputPath == null)
                    {
                        error = "A diff read needs -i base.bin.";
                        return false;
                    }
                }
                else if (request.InputPath != null)
                {
                    error = "A full read does not take -i.";
                    return false;
                }
                else if (request.Kwp && (request.LayoutName == null))
                {
                    error = "A full KWP read needs --layout.";
                    return false;
                }
            }
            else
            {
                if (request.InputPath == null)
                {
                    error = "A write needs -i image.bin.";
                    return false;
                }

                if (request.OutputPath != null)
                {
                    error = "A write does not take -o.";
                    return false;
                }
            }

            return true;
        }

        static bool ParseInfo(List<string> positionals, Request request, out string? error)
        {
            error = null;
            if (!positionals[1].Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                error = "Pass usb, layouts, or kwp or bootmode, then info, read, or write.";
                return false;
            }

            if (positionals[0].Equals("kwp", StringComparison.OrdinalIgnoreCase))
            {
                request.Kwp = true;
            }
            else if (!positionals[0].Equals("bootmode", StringComparison.OrdinalIgnoreCase))
            {
                error = "Protocol must be kwp or bootmode.";
                return false;
            }

            if (!request.Kwp && request.Slow)
            {
                error = "--slow applies to KWP.";
                return false;
            }

            if ((request.InputPath != null) || (request.OutputPath != null) || (request.LayoutName != null) || request.NoVerify || request.Yes)
            {
                error = "info does not take -i, -o, --layout, --no-verify, or --yes.";
                return false;
            }

            request.Info = true;
            return true;
        }

        static bool HasFlashOption(Request request)
        {
            return (request.InputPath != null)
                || (request.OutputPath != null)
                || (request.LayoutName != null)
                || request.HasBaud
                || request.UsbIndex.HasValue
                || request.Slow
                || request.NoVerify
                || request.Yes;
        }

        static void Usage(TextWriter writer)
        {
            writer.WriteLine("Usage: nmflasher [-h|--help] [-V|--version]");
            writer.WriteLine("       nmflasher usb");
            writer.WriteLine("       nmflasher layouts");
            writer.WriteLine("       nmflasher kwp info");
            writer.WriteLine("       nmflasher bootmode info");
            writer.WriteLine("       nmflasher kwp read full --layout NAME -o FILE");
            writer.WriteLine("       nmflasher kwp read diff -i FILE -o FILE");
            writer.WriteLine("       nmflasher kwp write full -i FILE");
            writer.WriteLine("       nmflasher kwp write diff -i FILE");
            writer.WriteLine("       nmflasher bootmode read full -o FILE");
            writer.WriteLine("       nmflasher bootmode write full -i FILE");
            writer.WriteLine();
            writer.WriteLine("Read ECU information, or read or write flash over KWP or boot mode.");
            writer.WriteLine("A write erases sectors as part of the write. There is no erase command.");
            writer.WriteLine();
            writer.WriteLine("Commands:");
            Command(writer, "usb", "Print each FTDI device as index, description, and serial.");
            Command(writer, "layouts", "Print each layout basename under MemoryLayouts.");
            Command(writer, "kwp info", "Read ECU identification in the standard session.");
            Command(writer, "bootmode info", "Read boot-mode device, bus, and flash layout information.");
            Command(writer, "kwp read full", "Read the whole layout. Requires --layout and -o.");
            Command(writer, "kwp read diff", "Read sectors that differ from -i. Requires -i and -o.");
            Command(writer, "kwp write full", "Write every sector from -i.");
            Command(writer, "kwp write diff", "Write sectors that differ from -i.");
            Command(writer, "bootmode read full", "Read the chip. The ECU must already be in boot mode.");
            Command(writer, "bootmode write full", "Write -i. The ECU must already be in boot mode.");
            writer.WriteLine("Options:");
            Option(writer, "-h, --help", "show this help and exit");
            Option(writer, "-V, --version", "print the build version and exit");
            Option(writer, "-i FILE", "image to write, or the file a diff read compares");
            Option(writer, "-o FILE", "file a read saves");
            Option(writer, "--layout NAME", "layout basename under MemoryLayouts");
            Option(writer, "--usb N", "device index from usb; one device is used if omitted");
            Option(writer, "--baud RATE", "KWP session baud, or the boot-mode baud");
            Option(writer, "--slow", "use KWP slow init; fast init is the default");
            Option(writer, "--no-verify", "match clearing Verify Read or Verify Write");
            Option(writer, "--yes", "print the confirmation and do not wait");
            writer.WriteLine();
            writer.WriteLine("With -i and no --layout, 1 MB selects 29F800BB, 512 KB selects 29F400BB,");
            writer.WriteLine("and 256 KB selects 29F200. Any other length asks for --layout. A full KWP");
            writer.WriteLine("read has no image, so it requires --layout. The image length must match");
            writer.WriteLine("the layout before a write starts.");
            writer.WriteLine();
            writer.WriteLine("The default KWP session baud list is 124800, 52800, 38400, 10400, 9600.");
            writer.WriteLine("Boot mode defaults to 57600 and reads the chip id. Diff read and diff write");
            writer.WriteLine("are not available in boot mode. Boot-mode write does not verify.");
            writer.WriteLine();
            writer.WriteLine("On a full KWP read of a bottom-boot layout whose first sector is 16 KB at");
            writer.WriteLine("0x800000, --no-verify checksums the first four sectors instead of every sector.");
            writer.WriteLine("A cancelled write does not connect.");
        }

        static void Command(TextWriter writer, string name, string text)
        {
            writer.WriteLine("  " + name);
            writer.WriteLine("          " + text);
            writer.WriteLine();
        }

        static void Option(TextWriter writer, string spec, string text)
        {
            const int column = 24;
            string prefix = "  " + spec;
            if (prefix.Length >= column)
            {
                writer.WriteLine(prefix);
                writer.WriteLine(new string(' ', column) + text);
                return;
            }

            writer.WriteLine(prefix.PadRight(column) + text);
        }

        static string VersionText()
        {
            var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string version = attribute?.InformationalVersion ?? "";
            if (string.IsNullOrEmpty(version))
            {
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
            }

            return version;
        }

        static void LogStatus(string message, StatusMessageType messageType)
        {
            if ((messageType == StatusMessageType.USER) || (messageType == StatusMessageType.LOG))
            {
                Log(message);
            }
        }

        static void Log(string message)
        {
            lock (LogLock)
            {
                Console.Error.WriteLine(message);
            }
        }

        sealed class Request
        {
            public bool UsbList;
            public bool LayoutList;
            public bool Info;
            public bool Kwp;
            public bool Read;
            public bool Diff;
            public string? LayoutName;
            public string? InputPath;
            public string? OutputPath;
            public uint Baud;
            public bool HasBaud;
            public int? UsbIndex;
            public bool Slow;
            public bool NoVerify;
            public bool Yes;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
