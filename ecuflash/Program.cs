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

namespace Ecuflash
{
    public static class Program
    {
        const uint DefaultKwpBaud = 124800;
        const uint DefaultBootmodeBaud = 57600;
        const int OperationWaitMinutes = 60;

        static readonly object LogLock = new object();
        static readonly uint[] KnownBaudRates = { 9600, 10400, 38400, 52800, 124800 };
        static bool Force;

        static readonly (int Bytes, string Name, string Word)[] SizeLayouts =
        {
            (1024 * 1024, "29F800BB", "1m"),
            (512 * 1024, "29F400BB", "512k"),
            (256 * 1024, "29F200", "256k"),
        };

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
                    Console.WriteLine("ecuflash " + VersionText());
                    return 0;
                }
            }

            if (!TryParse(args, out Request? request, out string? error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    Console.Error.WriteLine("ecuflash: " + error);
                }

                Console.Error.WriteLine("Try 'ecuflash --help' for more information.");
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
            Force = request.Force;
            if (request.Info)
            {
                return RunInfo(request);
            }

            if (request.DtcRead || request.DtcClear)
            {
                return RunDtc(request);
            }

            if (request.ImmoOff)
            {
                return RunImmoOff(request);
            }

            if (request.Reset)
            {
                return RunBootmodeReset(request);
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
            if (request.Kwp && !KwpReadWithoutLayout(request))
            {
                if (!TryLoadKwpLayout(request, image, out layout))
                {
                    return 2;
                }
            }

            if (!request.Verify && !Confirm(request))
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
                return RunKwp(request, device, layout, image);
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

        static int RunBootmodeReset(Request request)
        {
            DeviceInfo? device = ClaimDevice(request, out int selectStatus);
            if (device == null)
            {
                return selectStatus;
            }

            return WithBoot(request, device, boot =>
            {
                if (!boot.TrySoftwareReset())
                {
                    Log("Software reset was not sent.");
                    return 1;
                }

                Log("Software reset sent. The port is closed. The boot pin chooses what starts.");
                Log("Pin held: the chip waits for the loader. Pin released: the chip starts from flash.");
                return 0;
            });
        }

        static DeviceInfo? ClaimDevice(Request request, out int status)
        {
            if (Process.GetProcessesByName("ECUFlasher").Length > 0)
            {
                Log("ECUFlasher is running. Close it. It owns the USB serial device.");
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
            Log("Opening " + OpenLabel(device) + ". " + initName + " init.");
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
            Log("Opening " + OpenLabel(device) + " at " + baud + ".");
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

                return PrintKwpInfo(operation.ECUInfo, request.OutputPath);
            });
        }

        static int PrintKwpInfo(KWP2000IdentificationInfo identInfo, string? outputPath)
        {
            List<string> lines;
            if (identInfo.IdentOptionData.Count == 0)
            {
                Log("No ECU info read.");
                lines = new List<string>();
            }
            else
            {
                Log("Read " + identInfo.IdentOptionData.Count + " ECU info entries:");
                lines = identInfo.OptionLines(out bool ramProgram);
                foreach (string line in lines)
                {
                    Log(line);
                }

                if (ramProgram)
                {
                    Log(KWP2000RamProgramIdent.UserStatusMessage);
                }
            }

            return (outputPath != null) && !SaveLines(outputPath, lines, "ECU info entries") ? 1 : 0;
        }

        static int RunBootmodeInfo(Request request, DeviceInfo device)
        {
            return WithBoot(request, device, boot =>
            {
                Log("Reading bootmode ECU information...");
                var entries = BootmodeEcuInfo.Read(boot);
                var lines = new List<string>();
                foreach (BootmodeEcuInfo.Entry entry in entries)
                {
                    string line = entry.Name + ": " + entry.Value;
                    lines.Add(line);
                    Log(line);
                }

                Log("Read " + entries.Count + " bootmode ECU info entries.");
                return (request.OutputPath != null) && !SaveLines(request.OutputPath, lines, "bootmode ECU info entries") ? 1 : 0;
            });
        }

        static int RunDtc(Request request)
        {
            return RunOnDevice(request, device => WithKwp(request, device, (kwp, terminated) =>
            {
                if (kwp.IsHeldKWP1281Session)
                {
                    return FinishHeldDtcs(kwp, request.DtcClear, request.OutputPath);
                }

                if (kwp.CurrentDiagnosticSessionType == KWP2000DiagnosticSessionType.ProgrammingSession)
                {
                    Log("ECU is in a programming session. Turn the ignition off first.");
                    return 1;
                }

                uint[] baudRates = SessionBaudRates(request.Baud);
                if (request.DtcClear)
                {
                    var clear = new ClearDiagnosticInformationOperation(kwp, baudRates);
                    bool cleared = WaitForOperation(clear, "Clearing ECU diagnostic information.", () => ReleaseKwp(kwp, terminated));
                    Log(cleared
                        ? "Clearing ECU diagnostic information succeeded."
                        : "Clearing ECU diagnostic information failed.");
                    return cleared ? 0 : 1;
                }

                var read = new ReadDiagnosticTroubleCodesOperation(kwp, baudRates);
                bool success = WaitForOperation(read, "Reading all ECU DTCs.", () => ReleaseKwp(kwp, terminated));
                if (!success)
                {
                    Log("Reading all ECU DTCs failed.");
                    return 1;
                }

                return FinishDtcRead(read.DTCsRead, read.ExpectedNumDTCs, request.OutputPath);
            }));
        }

        static int FinishHeldDtcs(KWP2000Interface kwp, bool clear, string? outputPath)
        {
            Log(clear ? "Clearing ECU DTCs." : "Reading ECU DTCs.");
            using var done = new ManualResetEvent(false);
            bool ok = false;
            string report = "";
            kwp.RequestHeldKWP1281Dtcs(clear, (success, text) =>
            {
                ok = success;
                report = text ?? "";
                try
                {
                    done.Set();
                }
                catch (ObjectDisposedException)
                {
                }
            });

            if (!done.WaitOne(TimeSpan.FromMinutes(OperationWaitMinutes)))
            {
                Log("The operation did not finish within " + OperationWaitMinutes + " minutes.");
                return 1;
            }

            Log(ok ? report : ("DTC request failed. " + report));
            if (!ok || clear)
            {
                return ok ? 0 : 1;
            }

            var held = KWP1281CalibrationRead.ParseFaultReport(report);
            return FinishDtcRead(held, held.Count, outputPath);
        }

        static int FinishDtcRead(IEnumerable<KWP2000DTCInfo> dtcs, int expected, string? outputPath)
        {
            var lines = new List<string>();
            foreach (KWP2000DTCInfo dtc in dtcs)
            {
                lines.Add(dtc.DisplayLine());
            }

            Log("Read " + lines.Count + " of " + expected + " DTCs.");
            if (lines.Count == 0)
            {
                Log("No DTCs.");
            }

            foreach (string line in lines)
            {
                Log(line);
            }

            if ((outputPath != null) && !SaveLines(outputPath, lines, "DTCs"))
            {
                return 1;
            }

            Log("Reading all ECU DTCs succeeded.");
            return 0;
        }

        static bool SaveLines(string path, IReadOnlyList<string> lines, string label)
        {
            try
            {
                string text = (lines.Count == 0) ? "" : string.Join("\n", lines) + "\n";
                File.WriteAllText(path, text);
            }
            catch (Exception ex)
            {
                Log("The read finished, but the file was not saved: " + ex.Message);
                return false;
            }

            Log("Wrote " + lines.Count + " " + label + " to " + Path.GetFullPath(path) + ".");
            return true;
        }

        static int RunImmoOff(Request request)
        {
            return RunOnDevice(request, device => WithBoot(request, device, boot => FinishImmoOff(request, boot)));
        }

        static int RunOnDevice(Request request, Func<DeviceInfo, int> body)
        {
            DeviceInfo? device = ClaimDevice(request, out int status);
            return device == null ? status : body(device);
        }

        static int FinishImmoOff(Request request, BootstrapInterface boot)
        {
            BootstrapInterface.BootmodeEepromSettings settings = ImmoSettings(request);
            Log("Preset: " + ImmoPreset(request) + ".");
            var read = new BootmodeReadEepromOperation(boot, settings);
            bool readOk = WaitForOperation(read, "Reading EEPROM.", () => CloseBoot(boot));
            if (!readOk || (read.ReadMemory == null) || (read.ReadMemory.RawData == null))
            {
                Log("Reading EEPROM failed. Chip was not written.");
                return 1;
            }

            byte[] original = (byte[])read.ReadMemory.RawData.Clone();
            if ((uint)original.Length != settings.Size)
            {
                Log("EEPROM read is " + original.Length + " bytes. Preset is " + settings.Size + " bytes. Chip was not written.");
                return 1;
            }

            byte[] image = (byte[])original.Clone();
            Me7Eeprom95040Immo.Result result = request.Eeprom95P08
                ? Me7Eeprom95P08Immo.TryDisable(image)
                : Me7Eeprom95040Immo.TryDisable(image);
            if (result != Me7Eeprom95040Immo.Result.Applied)
            {
                bool already = result == Me7Eeprom95040Immo.Result.AlreadyOff;
                Log(already ? "Immo is already off. Chip was not written." : "Immo bytes do not match. Chip was not written.");
                if ((request.OutputPath != null) && !SaveImmoBytes(request.OutputPath, original))
                {
                    return 1;
                }

                return already ? 0 : 1;
            }

            string backupPath = request.OutputPath ?? DefaultImmoBackupName(request);
            if (!SaveImmoBytes(backupPath, original))
            {
                Log("Chip was not written.");
                return 1;
            }

            bool verify = !request.NoVerify;
            var write = new BootmodeWriteEepromOperation(boot, settings, image, verify);
            bool wrote = WaitForOperation(write, "Writing EEPROM.", () => CloseBoot(boot));
            if (!wrote)
            {
                Log("Writing EEPROM failed. The backup from before the edit is " + Path.GetFullPath(backupPath) + ".");
                return 1;
            }

            Log(verify ? "Immo off. Written and verified." : "Immo off. Written (not verified).");
            return 0;
        }

        static bool SaveImmoBytes(string path, byte[] data)
        {
            if (File.Exists(path))
            {
                Log("File already exists: " + Path.GetFullPath(path) + ". Pass -o with a new name.");
                return false;
            }

            try
            {
                File.WriteAllBytes(path, data);
            }
            catch (Exception ex)
            {
                Log("The file was not saved: " + ex.Message);
                return false;
            }

            Log("Wrote " + data.Length + " bytes to " + Path.GetFullPath(path) + ".");
            return true;
        }

        static string DefaultImmoBackupName(Request request)
        {
            return request.Eeprom95P08 ? "eeprom-95p08-before-immo.bin" : "eeprom-95040-before-immo.bin";
        }

        static string ImmoPreset(Request request)
        {
            return request.Eeprom95P08
                ? "ME7.1.1 P6.3, 95P08 SSC 1024 B"
                : "ME7.5 P4.7, 95040 SSC 512 B";
        }

        static BootstrapInterface.BootmodeEepromSettings ImmoSettings(Request request)
        {
            if (!request.Eeprom95P08)
            {
                return BootstrapInterface.BootmodeEepromSettings.ForMe75();
            }

            return new BootstrapInterface.BootmodeEepromSettings
            {
                Periph = BootstrapInterface.BootmodeEepromPeriph.SSC,
                EepromType = BootstrapInterface.BootmodeEepromType.Type95040,
                PortNumber = 6,
                PinNumber = 3,
                Size = (uint)Me7Eeprom95P08Immo.EepromSize
            };
        }

        static int ListUsb()
        {
            var devices = ListedDevices();
            if (devices.Count == 0)
            {
                Log("No USB serial device.");
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

            string resolved = ResolveLayoutName(name);
            if (!resolved.Equals(LayoutBasename(name), StringComparison.OrdinalIgnoreCase))
            {
                Log(name + " selects layout " + resolved + ".");
            }

            name = resolved;
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

        static int RunKwp(Request request, DeviceInfo device, MemoryLayout? layout, byte[]? image)
        {
            return WithKwp(request, device, (kwp, terminated) =>
            {
                if (kwp.IsHeldKWP1281Session)
                {
                    if (request.Read && !request.Diff)
                    {
                        if (!string.IsNullOrEmpty(request.LayoutName))
                        {
                            Log("Layout is not used on this session.");
                        }

                        return FinishCalibrationRead(kwp, terminated, request.OutputPath!);
                    }

                    Log("This is the M5.9.2 read session. It copies 0x0000-0xBFFF."
                        + (request.Read ? " Diff read is not available." : " It does not write."));
                    return 1;
                }

                if (layout == null)
                {
                    Log("This is not the M5.9.2 read session. Pass --layout.");
                    Log(SizeLayoutText());
                    int listed = ListLayouts();
                    return listed == 0 ? 2 : listed;
                }

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

        static int FinishCalibrationRead(KWP2000Interface kwp, ManualResetEvent terminated, string outputPath)
        {
            var operation = new KWP1281CalibrationReadOperation(kwp);
            bool success = WaitForOperation(
                operation,
                "Reading calibration 0x0000-0xBFFF. This is not a full flash read.",
                () => ReleaseKwp(kwp, terminated));
            if (!success || (operation.Calibration == null))
            {
                Log("Calibration read failed.");
                return 1;
            }

            return SaveRead(
                new MemoryImage(operation.Calibration, 0),
                outputPath,
                "Calibration read succeeded. 49152 bytes, 0x0000-0xBFFF. Not a full flash image.");
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
                if (request.Verify)
                {
                    return FinishBootmodeVerify(boot, layout, image);
                }

                if (request.Read)
                {
                    if (request.Diff && !ImageFits(layout, image))
                    {
                        return 1;
                    }

                    return FinishBootmodeRead(boot, layout, request.OutputPath!, request.Diff ? image : null, !request.NoVerify);
                }

                if (!ImageFits(layout, image))
                {
                    return 1;
                }

                var settings = new BootmodeWriteExternalFlashOperation.BootmodeWriteExternalFlashSettings();
                settings.Variant = InferBootmodeVariant(layout);
                settings.FlashMemoryLayout = layout;
                settings.SkipMatchingSectors = request.Diff;
                settings.VerifyWrittenData = !request.NoVerify;
                var sectors = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(image!, layout));
                var operation = new BootmodeWriteExternalFlashOperation(boot, settings, sectors);
                bool success = WaitForOperation(operation, "Writing ECU flash memory (bootmode).", () => CloseBoot(boot));
                Log(BootmodeWriteResult(operation, success));
                return success ? 0 : 1;
            });
        }

        static int FinishBootmodeRead(BootstrapInterface boot, MemoryLayout layout, string outputPath, byte[]? baseImage, bool verify)
        {
            byte[] readImage = baseImage ?? BlankImage(layout);
            var sectors = new List<MemoryImage>(MemoryUtils.SplitMemoryImageIntoSectors(readImage, layout));
            var settings = new BootmodeReadExternalFlashOperation.BootmodeReadExternalFlashSettings();
            settings.Variant = InferBootmodeVariant(layout);
            settings.StartAddress = layout.BaseAddress;
            settings.Size = layout.Size;
            settings.SkipMatchingSectors = baseImage != null;
            settings.VerifyReadData = verify;
            var operation = new BootmodeReadExternalFlashOperation(boot, settings, sectors);
            bool success = WaitForOperation(operation, "Reading ECU flash memory via BootMode.", () => CloseBoot(boot));
            if (!success || (operation.mReadFlashMemory == null))
            {
                Log("Reading ECU flash memory failed.");
                return 1;
            }

            return SaveRead(operation.mReadFlashMemory, outputPath);
        }

        static int FinishBootmodeVerify(BootstrapInterface boot, MemoryLayout layout, byte[]? image)
        {
            if (!ImageFits(layout, image))
            {
                return 1;
            }

            var operation = new BootmodeVerifyFlashOperation(boot, layout, image!);
            bool success = WaitForOperation(operation, BootmodeVerifyFlashOperation.UserStatus, () => CloseBoot(boot));
            Log(BootmodeVerifyFlashOperation.ResultMessage(success));
            return success ? 0 : 1;
        }

        static bool ImageFits(MemoryLayout layout, byte[]? image)
        {
            if ((image != null) && ((uint)image.Length == layout.Size))
            {
                return true;
            }

            Log(BootmodeVerifyFlashOperation.LengthMessage(layout.Size, image!));
            return false;
        }

        static int SaveRead(MemoryImage image, string outputPath, string? succeeded = null)
        {
            if (!image.SaveToFile(outputPath))
            {
                Log("The read finished, but the file was not saved: " + outputPath);
                return 1;
            }

            Log(succeeded ?? "Reading ECU flash memory succeeded.");
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

        static bool KwpReadWithoutLayout(Request request)
        {
            return request.Kwp && request.Read && !request.Diff && string.IsNullOrEmpty(request.LayoutName);
        }

        internal static string ResolveLayoutName(string name)
        {
            string compact = name.Trim().ToLowerInvariant();
            foreach (var size in SizeLayouts)
            {
                if ((compact == size.Word) || (compact == size.Word + "b"))
                {
                    return size.Name;
                }
            }

            return LayoutBasename(name);
        }

        static string? LayoutNameForLength(int length)
        {
            foreach (var size in SizeLayouts)
            {
                if (length == size.Bytes)
                {
                    return size.Name;
                }
            }

            return null;
        }

        static string SizeLayoutText()
        {
            return string.Join(", ", SizeLayouts.Select(size => size.Word + "/" + size.Word + "b selects " + size.Name)) + ".";
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

            if (request.Force)
            {
                Log("Continuing because --force was passed.");
                return true;
            }

            return ReadChoice(UserPromptType.OK_CANCEL) == UserPromptResult.OK;
        }

        static List<string> Checklist(Request request)
        {
            var lines = new List<string>();
            if (KwpReadWithoutLayout(request))
            {
                lines.Add("If you are ready to read, confirm the following things:");
                lines.Add("1) The engine is not running.");
                lines.Add("2) On M5.9.2, pin 43 is off +12 V.");
                lines.Add("A held M5.9.2 session reads 0x0000-0xBFFF (49152 bytes), not the whole flash.");
                lines.Add("Any other ECU needs --layout. Nothing is read until the session type is known.");
            }
            else if (request.Read && !request.Diff)
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
            thread.Name = "ecuflash prompt";
            thread.Start();
        }

        static UserPromptResult Ask(string title, string message, UserPromptType promptType)
        {
            if (Force)
            {
                LogPrompt(title, message);
                if (TryForcedAnswer(promptType, out UserPromptResult forced))
                {
                    Log(forced == UserPromptResult.CHECK
                        ? "Checking the layout mirror because --force was passed."
                        : "Continuing because --force was passed.");
                    return forced;
                }

                Log("Stopping. --force does not erase the entire flash.");
                return UserPromptResult.CANCEL;
            }

            LogPrompt(title, message);
            return ReadChoice(promptType);
        }

        static UserPromptResult ReadChoice(UserPromptType promptType)
        {
            while (true)
            {
                Log(PromptHint(promptType));
                string? line = Console.ReadLine();
                if ((line != null) && TryMapAnswer(line.Trim(), promptType, out UserPromptResult result))
                {
                    return result;
                }

                if (line == null)
                {
                    return UserPromptResult.CANCEL;
                }
            }
        }

        static void LogPrompt(string title, string message)
        {
            Log(title);
            if (!string.IsNullOrEmpty(message))
            {
                Log(message);
            }
        }

        static bool TryForcedAnswer(UserPromptType promptType, out UserPromptResult result)
        {
            switch (promptType)
            {
                case UserPromptType.OK:
                case UserPromptType.OK_CANCEL:
                    result = UserPromptResult.OK;
                    return true;
                case UserPromptType.CONTINUE_CHECK_CANCEL:
                    result = UserPromptResult.CHECK;
                    return true;
                default:
                    result = UserPromptResult.NONE;
                    return false;
            }
        }

        static string PromptHint(UserPromptType promptType)
        {
            switch (promptType)
            {
                case UserPromptType.OK:
                case UserPromptType.OK_CANCEL:
                    return "Press Enter to continue. Ctrl-C aborts.";
                case UserPromptType.YES_NO_CANCEL:
                    return "yes, no, or cancel";
                case UserPromptType.CONTINUE_CHECK_CANCEL:
                    return "yes checks, no continues, cancel aborts";
                default:
                    return "Ctrl-C aborts.";
            }
        }

        static bool TryMapAnswer(string answer, UserPromptType promptType, out UserPromptResult result)
        {
            result = UserPromptResult.NONE;
            if ((answer.Length == 0)
                && ((promptType == UserPromptType.OK) || (promptType == UserPromptType.OK_CANCEL)))
            {
                result = UserPromptResult.OK;
                return true;
            }

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

        static List<DeviceInfo> ListedDevices()
        {
            var devices = new List<DeviceInfo>();
            foreach (DeviceInfo info in DeviceManager.EnumerateAllDevices())
            {
                if ((info.Type == DeviceType.FTDI) || (info.Type == DeviceType.CH340))
                {
                    devices.Add(info);
                }
            }

            return devices;
        }

        static string OpenLabel(DeviceInfo device)
        {
            return device.Type + " " + device.Description + " " + device.SerialNumber;
        }

        static void PrintDevices(IReadOnlyList<DeviceInfo> devices)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                Log(i + "  " + devices[i].Type + "  " + devices[i].Description + "  " + devices[i].SerialNumber);
            }
        }

        static DeviceInfo? SelectDevice(int? index, out int status)
        {
            var devices = ListedDevices();
            if (devices.Count == 0)
            {
                Log("No USB serial device.");
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

            Log("More than one USB serial device. Pass --usb N.");
            PrintDevices(devices);
            status = 2;
            return null;
        }

        internal static bool TryParse(string[] args, out Request? request, out string? error)
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
                if (arg == "--layout")
                {
                    if (request.LayoutName != null)
                    {
                        error = "--layout was given twice.";
                        return false;
                    }

                    if (((i + 1) >= args.Length) || args[i + 1].StartsWith("-"))
                    {
                        request.LayoutList = true;
                        continue;
                    }

                    request.LayoutName = args[++i];
                    continue;
                }

                if ((arg == "-i") || (arg == "-o") || (arg == "--baud") || (arg == "--usb"))
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

                if ((arg == "-f") || (arg == "--force"))
                {
                    if (request.Force)
                    {
                        error = "--force was given twice.";
                        return false;
                    }

                    request.Force = true;
                    continue;
                }

                if (arg == "--95p08")
                {
                    if (request.Eeprom95P08)
                    {
                        error = "--95p08 was given twice.";
                        return false;
                    }

                    request.Eeprom95P08 = true;
                    continue;
                }

                if (arg.StartsWith("-"))
                {
                    error = "Unknown option " + arg + ".";
                    return false;
                }

                positionals.Add(arg);
            }

            if (request.LayoutList)
            {
                return Accept(request, out error);
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

                    return Accept(request, out error);
                }
            }

            if (positionals.Count == 2)
            {
                if (positionals[1].Equals("verify", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseVerify(positionals, request, out error);
                }

                if (positionals[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseReset(positionals, request, out error);
                }

                return ParseInfo(positionals, request, out error);
            }

            if (positionals.Count != 3)
            {
                error = CommandError();
                return false;
            }

            if (positionals[1].Equals("dtc", StringComparison.OrdinalIgnoreCase))
            {
                return ParseDtc(positionals, request, out error);
            }

            if (positionals[1].Equals("immo", StringComparison.OrdinalIgnoreCase))
            {
                return ParseImmo(positionals, request, out error);
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

            if (verb.Equals("verify", StringComparison.OrdinalIgnoreCase))
            {
                error = "verify does not take full or diff. Pass bootmode verify -i FILE.";
                return false;
            }

            if (verb.Equals("read", StringComparison.OrdinalIgnoreCase))
            {
                request.Read = true;
            }
            else if (!verb.Equals("write", StringComparison.OrdinalIgnoreCase))
            {
                error = "Command must be read, write, dtc, or immo.";
                return false;
            }

            if (mode.Equals("dtc", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("immo", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                error = "Pass kwp dtc read, kwp dtc clear, or bootmode immo off.";
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

            if (!request.Kwp)
            {
                error = BootmodeOptionError(request);
                if (error != null)
                {
                    return false;
                }
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

            return Accept(request, out error);
        }

        static bool ParseDtc(List<string> positionals, Request request, out string? error)
        {
            if (!positionals[0].Equals("kwp", StringComparison.OrdinalIgnoreCase))
            {
                error = "DTC read and clear are KWP commands.";
                return false;
            }

            bool read = positionals[2].Equals("read", StringComparison.OrdinalIgnoreCase);
            bool clear = positionals[2].Equals("clear", StringComparison.OrdinalIgnoreCase);
            if (!read && !clear)
            {
                error = "DTC command must be read or clear.";
                return false;
            }

            if ((request.InputPath != null) || (request.LayoutName != null) || request.NoVerify || request.Eeprom95P08)
            {
                error = "dtc does not take -i, --layout, --no-verify, or --95p08.";
                return false;
            }

            if (clear && (request.OutputPath != null))
            {
                error = "dtc clear does not take -o.";
                return false;
            }

            request.Kwp = true;
            request.DtcRead = read;
            request.DtcClear = clear;
            error = null;
            return true;
        }

        static bool ParseImmo(List<string> positionals, Request request, out string? error)
        {
            if (!positionals[0].Equals("bootmode", StringComparison.OrdinalIgnoreCase))
            {
                error = "Immo off is a bootmode command.";
                return false;
            }

            if (!positionals[2].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                error = "Immo command must be off.";
                return false;
            }

            if (request.Slow)
            {
                error = "--slow applies to KWP.";
                return false;
            }

            if (request.LayoutName != null)
            {
                error = "immo off does not take --layout.";
                return false;
            }

            if (request.InputPath != null)
            {
                error = "immo off does not take -i.";
                return false;
            }

            request.ImmoOff = true;
            error = null;
            return true;
        }

        static bool Accept(Request request, out string? error)
        {
            if (request.Eeprom95P08 && !request.ImmoOff)
            {
                error = "--95p08 applies to bootmode immo off.";
                return false;
            }

            error = null;
            return true;
        }

        static string CommandError()
        {
            return "Pass usb, layouts, or kwp or bootmode, then info, verify, reset, read, write, dtc, or immo.";
        }

        static bool BeginBootmode(List<string> positionals, Request request, string kwpError, out string? error)
        {
            if (positionals[0].Equals("kwp", StringComparison.OrdinalIgnoreCase))
            {
                error = kwpError;
                return false;
            }

            if (!positionals[0].Equals("bootmode", StringComparison.OrdinalIgnoreCase))
            {
                error = "Protocol must be kwp or bootmode.";
                return false;
            }

            error = BootmodeOptionError(request);
            return error == null;
        }

        static bool ParseReset(List<string> positionals, Request request, out string? error)
        {
            if (!BeginBootmode(positionals, request, "Reset is a bootmode command.", out error))
            {
                return false;
            }

            if ((request.InputPath != null) || (request.OutputPath != null) || request.NoVerify || request.Eeprom95P08)
            {
                error = "reset does not take -i, -o, --no-verify, or --95p08.";
                return false;
            }

            request.Reset = true;
            return Accept(request, out error);
        }

        static bool ParseVerify(List<string> positionals, Request request, out string? error)
        {
            if (!BeginBootmode(positionals, request, "Full-chip verify is a bootmode command.", out error))
            {
                return false;
            }

            error = request.NoVerify ? "verify does not take --no-verify." : null
                ?? (request.OutputPath != null ? "verify does not take -o." : null)
                ?? (request.InputPath == null ? "verify needs -i image.bin." : null);
            if (error != null)
            {
                return false;
            }

            request.Verify = true;
            return Accept(request, out error);
        }

        static string? BootmodeOptionError(Request request)
        {
            if (request.Slow)
            {
                return "--slow applies to KWP.";
            }

            if (request.LayoutName != null)
            {
                return "Boot mode reads the chip id. Do not pass --layout.";
            }

            return null;
        }

        static bool ParseInfo(List<string> positionals, Request request, out string? error)
        {
            error = null;
            if (!positionals[1].Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                error = CommandError();
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

            if ((request.InputPath != null) || (request.LayoutName != null) || request.NoVerify || request.Force)
            {
                error = "info does not take -i, --layout, --no-verify, or --force.";
                return false;
            }

            request.Info = true;
            return Accept(request, out error);
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
                || request.Force;
        }

        static void Usage(TextWriter writer)
        {
            writer.WriteLine("Usage: ecuflash [-h|--help] [-V|--version]");
            writer.WriteLine("       ecuflash usb");
            writer.WriteLine("       ecuflash layouts");
            writer.WriteLine("       ecuflash --layout");
            writer.WriteLine("       ecuflash kwp info [-o FILE]");
            writer.WriteLine("       ecuflash kwp dtc read [-o FILE]");
            writer.WriteLine("       ecuflash kwp dtc clear");
            writer.WriteLine("       ecuflash bootmode info [-o FILE]");
            writer.WriteLine("       ecuflash bootmode immo off");
            writer.WriteLine("       ecuflash bootmode verify -i FILE");
            writer.WriteLine("       ecuflash bootmode reset");
            writer.WriteLine("       ecuflash kwp read full --layout NAME -o FILE");
            writer.WriteLine("       ecuflash kwp read full -o FILE");
            writer.WriteLine("       ecuflash kwp read diff -i FILE -o FILE");
            writer.WriteLine("       ecuflash kwp write full -i FILE");
            writer.WriteLine("       ecuflash kwp write diff -i FILE");
            writer.WriteLine("       ecuflash bootmode read full -o FILE");
            writer.WriteLine("       ecuflash bootmode read diff -i FILE -o FILE");
            writer.WriteLine("       ecuflash bootmode write full -i FILE");
            writer.WriteLine("       ecuflash bootmode write diff -i FILE");
            writer.WriteLine();
            writer.WriteLine("Read ECU information, read or clear DTCs, turn immo off, or read or write flash.");
            writer.WriteLine("A write erases sectors as part of the write. There is no erase command.");
            writer.WriteLine();
            writer.WriteLine("Commands:");
            Command(writer, "usb", "Print each FTDI or CH340 device as index, description, and serial.");
            Command(writer, "layouts", "Print each layout basename under MemoryLayouts.");
            Command(writer, "kwp info", "Read ECU identification in the standard session. -o writes the same lines to a file.");
            Command(writer, "kwp dtc read", "Read diagnostic trouble codes. -o writes the same lines to a file.");
            Command(writer, "kwp dtc clear", "Clear diagnostic information.");
            Command(writer, "bootmode info", "Read boot-mode device, bus, and flash layout information. -o writes the same lines to a file.");
            Command(writer, "bootmode immo off", "Read the EEPROM, set immo off, save a backup, and write it back.");
            Command(writer, "bootmode verify", "Checksum each sector of -i on the ECU. Does not read or write the chip.");
            Command(writer, "bootmode reset", "Software reset. The port closes. The boot pin chooses the loader or flash.");
            Command(writer, "kwp read full", "Read the layout, or the M5.9.2 calibration. Requires -o.");
            Command(writer, "kwp read diff", "Read sectors that differ from -i. Requires -i and -o.");
            Command(writer, "kwp write full", "Write every sector from -i.");
            Command(writer, "kwp write diff", "Write sectors that differ from -i.");
            Command(writer, "bootmode read full", "Read the chip. The ECU must already be in boot mode.");
            Command(writer, "bootmode read diff", "Read sectors that differ from -i. The ECU must already be in boot mode.");
            Command(writer, "bootmode write full", "Write -i. The ECU must already be in boot mode.");
            Command(writer, "bootmode write diff", "Write sectors that differ from -i. The ECU must already be in boot mode.");
            writer.WriteLine("Options:");
            Option(writer, "-h, --help", "show this help and exit");
            Option(writer, "-V, --version", "print the build version and exit");
            Option(writer, "-i FILE", "image to write, or the file a diff read compares");
            Option(writer, "-o FILE", "file a read saves, the info or DTC lines, or the immo-off backup");
            Option(writer, "--layout [NAME]", "layout basename, 1m, 512k, or 256k; no NAME lists them");
            Option(writer, "--usb N", "device index from usb; one device is used if omitted");
            Option(writer, "--baud RATE", "KWP session baud, or the boot-mode baud");
            Option(writer, "--slow", "use KWP slow init; fast init is the default");
            Option(writer, "--no-verify", "match clearing Verify Read or Verify Write");
            Option(writer, "--95p08", "ME7.1.1 95P08 on P6.3; default immo preset is 95040 on P4.7");
            Option(writer, "-f, --force", "answer confirmation prompts and do not wait");
            writer.WriteLine();
            writer.WriteLine("--layout with no name prints the layout names and does not connect.");
            writer.WriteLine(SizeLayoutText());
            writer.WriteLine("With -i and no --layout, 1 MB selects 29F800BB, 512 KB selects 29F400BB,");
            writer.WriteLine("and 256 KB selects 29F200. Any other length asks for --layout. A full KWP");
            writer.WriteLine("read with no --layout reads 0x0000-0xBFFF on a held M5.9.2 session.");
            writer.WriteLine("Any other ECU still needs --layout. 29F800BB is not assumed. The image");
            writer.WriteLine("length must match the layout before a write.");
            writer.WriteLine();
            writer.WriteLine("Press Enter to continue a prompt. Ctrl-C aborts. -f and --force answer");
            writer.WriteLine("confirmation prompts and do not wait. They do not erase the entire flash");
            writer.WriteLine("when a sector erase fails.");
            writer.WriteLine();
            writer.WriteLine("The default KWP session baud list is 124800, 52800, 38400, 10400, 9600.");
            writer.WriteLine("Boot mode defaults to 57600 and reads the chip id. Diff read and diff write");
            writer.WriteLine("checksum each sector on the ECU. A boot-mode write checksums each programmed");
            writer.WriteLine("sector unless --no-verify is set.");
            writer.WriteLine();
            writer.WriteLine("kwp info, bootmode info, and kwp dtc read print each entry. -o writes those lines to a file.");
            writer.WriteLine("Without -o the list is only printed. A DTC line is Pxxxx, the status byte, and the name.");
            writer.WriteLine("A held M5.9.2 session uses the KWP1281 fault list. kwp dtc clear sends the");
            writer.WriteLine("tester clear for group 0x0000. It does not checksum the programming flag.");
            writer.WriteLine("Leave a programming session with an ignition cycle before either command.");
            writer.WriteLine();
            writer.WriteLine("bootmode immo off uses 95040 SSC, 512 bytes, chip select P4.7 (ME7.1 and");
            writer.WriteLine("ME7.5). --95p08 uses 95P08 SSC, 1024 bytes, chip select P6.3 (ME7.1.1).");
            writer.WriteLine("The bytes at 0x012 and 0x022 must match. They are set to 0x02. On a 95040");
            writer.WriteLine("the page 1 and page 2 checksums are corrected. The image from before that");
            writer.WriteLine("edit is saved as eeprom-95040-before-immo.bin, or eeprom-95p08-before-immo.bin");
            writer.WriteLine("with --95p08. -o chooses that file. An existing file is left in place and");
            writer.WriteLine("the chip is not written. The write is verified unless --no-verify is set.");
            writer.WriteLine("If immo is already off, the chip is not written.");
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

        internal sealed class Request
        {
            public bool UsbList;
            public bool LayoutList;
            public bool Info;
            public bool Kwp;
            public bool Read;
            public bool Verify;
            public bool Diff;
            public string? LayoutName;
            public string? InputPath;
            public string? OutputPath;
            public uint Baud;
            public bool HasBaud;
            public int? UsbIndex;
            public bool Slow;
            public bool NoVerify;
            public bool Force;
            public bool DtcRead;
            public bool DtcClear;
            public bool ImmoOff;
            public bool Eeprom95P08;
            public bool Reset;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
