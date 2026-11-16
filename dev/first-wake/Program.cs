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

using Communication;

namespace FirstWakeProbe
{
    // One fast init at 0x11, then one speed byte 0x14. No erase, no 0x82, no ECU reset.
    public static class Program
    {
        const string CableSerial = "A6000001";

        static readonly object LogLock = new object();

        public static int Main(string[] args)
        {
            bool clearOnSilent = false;
            bool connectOnly = false;
            bool releaseBeforeSpeed = false;
            bool standardSession = false;
            bool timingAfterAccept = false;
            bool afterSpeed = false;
            bool beforeKey = false;
            bool keyFirst = false;
            bool sweep = false;
            int sweepFrom = 0;
            bool slowInit = false;
            bool readDtcs = false;
            bool readEeprom = false;
            byte[]? instead = null;
            System.Collections.Generic.List<byte[]>? thenChain = null;
            uint insteadBaud = 0;
            byte speed = FirstWakeFlashCodec.SlowSpeed;
            byte connectAddress = KWP2000Interface.DEFAULT_ECU_SLOWINIT_KWP2000_ADDRESS;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "recover")
                {
                    clearOnSilent = true;
                    continue;
                }

                if (arg == "standard")
                {
                    standardSession = true;
                    continue;
                }

                if (arg == "release")
                {
                    releaseBeforeSpeed = true;
                    continue;
                }

                if (arg == "timing")
                {
                    timingAfterAccept = true;
                    continue;
                }

                if (arg == "after14")
                {
                    afterSpeed = true;
                    continue;
                }

                if (arg == "beforekey")
                {
                    beforeKey = true;
                    continue;
                }

                if (arg == "keyfirst")
                {
                    keyFirst = true;
                    continue;
                }

                if (arg == "sweep")
                {
                    sweep = true;
                    continue;
                }

                if (arg == "from")
                {
                    if (((i + 1) >= args.Length) || !int.TryParse(args[i + 1], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out sweepFrom) || (sweepFrom < 0) || (sweepFrom > 0xFF))
                    {
                        Log("Usage: FirstWakeProbe sweep from <byte hex>");
                        return 2;
                    }

                    i++;
                    continue;
                }

                if (arg == "slow")
                {
                    slowInit = true;
                    continue;
                }

                if (arg == "dtc")
                {
                    readDtcs = true;
                    continue;
                }

                if (arg == "eeprom")
                {
                    readEeprom = true;
                    continue;
                }

                if (arg == "at")
                {
                    if (((i + 1) >= args.Length) || !byte.TryParse(args[i + 1], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out connectAddress))
                    {
                        Log("Usage: FirstWakeProbe at <address hex>");
                        return 2;
                    }

                    i++;
                    continue;
                }

                if (arg == "baud")
                {
                    if (((i + 1) >= args.Length) || !uint.TryParse(args[i + 1], out uint parsedBaud) || (parsedBaud == 0))
                    {
                        Log("Usage: FirstWakeProbe baud <rate> request <hex bytes>");
                        return 2;
                    }

                    i++;
                    insteadBaud = parsedBaud;
                    continue;
                }

                if (arg == "request")
                {
                    var bytes = new System.Collections.Generic.List<byte>();
                    while ((i + 1) < args.Length && args[i + 1] != "then")
                    {
                        i++;
                        if (!byte.TryParse(args[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte requestByte))
                        {
                            Log("Usage: FirstWakeProbe request <hex bytes>");
                            return 2;
                        }

                        bytes.Add(requestByte);
                    }

                    if (bytes.Count == 0)
                    {
                        Log("Usage: FirstWakeProbe request <hex bytes>");
                        return 2;
                    }

                    instead = bytes.ToArray();
                    continue;
                }

                if (arg == "then")
                {
                    var bytes = new System.Collections.Generic.List<byte>();
                    while (((i + 1) < args.Length) && (args[i + 1] != "then"))
                    {
                        i++;
                        if (!byte.TryParse(args[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte requestByte))
                        {
                            Log("Usage: FirstWakeProbe request <hex bytes> then <hex bytes>");
                            return 2;
                        }

                        bytes.Add(requestByte);
                    }

                    if (bytes.Count == 0)
                    {
                        Log("Usage: FirstWakeProbe request <hex bytes> then <hex bytes>");
                        return 2;
                    }

                    if (thenChain == null)
                    {
                        thenChain = new System.Collections.Generic.List<byte[]>();
                    }

                    thenChain.Add(bytes.ToArray());
                    continue;
                }

                if (arg == "connect")
                {
                    connectOnly = true;
                    if ((i + 1) < args.Length)
                    {
                        i++;
                        if (!byte.TryParse(args[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out connectAddress))
                        {
                            Log("Usage: FirstWakeProbe connect <address hex>");
                            return 2;
                        }
                    }

                    continue;
                }

                if (!byte.TryParse(arg, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte parsed))
                {
                    Log("Usage: FirstWakeProbe [recover] [speed byte hex]");
                    return 2;
                }

                speed = parsed;
            }

            if (readDtcs || readEeprom)
            {
                slowInit = true;
                connectAddress = KWP2000Interface.DEFAULT_ECU_SLOWINIT_KWP1281_ADDRESS;
            }

            bool rateTest = (speed == FirstWakeFlashCodec.FastSpeed) || (speed == FirstWakeFlashCodec.TopSpeed) || (speed == 0x64);
            if (timingAfterAccept && (speed != FirstWakeFlashCodec.SlowSpeed) && !rateTest)
            {
                Log("timing after a non-10400 byte is only 0x50, 0x63, or 0x64.");
                return 2;
            }

            if (!timingAfterAccept && ((speed == FirstWakeFlashCodec.FastSpeed) || (speed == FirstWakeFlashCodec.TopSpeed)))
            {
                Log("0x50 and 0x63 are not sent. An accepted 0x50 moves the ECU to 38400, and that is the lockup this probe is avoiding.");
                return 2;
            }

            if (beforeKey && (instead == null))
            {
                Log("Usage: FirstWakeProbe beforekey request <hex bytes>");
                return 2;
            }

            if (keyFirst && beforeKey)
            {
                Log("keyfirst and beforekey are separate requests.");
                return 2;
            }

            if ((thenChain != null) && (instead == null))
            {
                Log("Usage: FirstWakeProbe request <hex bytes> then <hex bytes>");
                return 2;
            }

            if (beforeKey && afterSpeed)
            {
                Log("beforekey and after14 are separate requests.");
                return 2;
            }

            if (afterSpeed && (instead == null))
            {
                Log("Usage: FirstWakeProbe after14 request <hex bytes>");
                return 2;
            }

            if (afterSpeed && (insteadBaud != 0))
            {
                Log("after14 sends the follow-up at 10400.");
                return 2;
            }

            if ((insteadBaud != 0) && (instead == null))
            {
                Log("Usage: FirstWakeProbe baud <rate> request <hex bytes>");
                return 2;
            }

            if ((instead != null) && (instead.Length >= 3) && (instead[0] == 0x10) && (instead[1] == 0x85)
                && ((instead[2] == FirstWakeFlashCodec.FastSpeed) || (instead[2] == FirstWakeFlashCodec.TopSpeed))
                && !beforeKey)
            {
                Log("0x50 and 0x63 are not sent. An accepted 0x50 moves the ECU to 38400, and that is the lockup this probe is avoiding.");
                return 2;
            }

            if (Process.GetProcessesByName("ECUFlasher").Length > 0)
            {
                Log("ECUFlasher is running. Close it before this probe. It owns the FTDI device.");
                return Report(FirstWakeProbeResult.WakeFail);
            }

            if (sweep)
            {
                return SweepSpeedBytes(sweepFrom);
            }

            DeviceInfo? chosen = null;
            foreach (DeviceInfo info in DeviceManager.EnumerateAllDevices())
            {
                if (info.Type != DeviceType.FTDI)
                {
                    continue;
                }

                if (string.Equals(info.SerialNumber, CableSerial, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = info;
                    break;
                }

                if (chosen == null)
                {
                    chosen = info;
                }
            }

            if (chosen == null)
            {
                Log("No FTDI device.");
                return Report(FirstWakeProbeResult.WakeFail);
            }

            string initName = slowInit ? "Slow" : "Fast";
            Log("Opening FTDI " + chosen.SerialNumber + " (" + chosen.Description + "). " + initName + " init address 0x" + connectAddress.ToString("X2") + ".");

            var kwp = new KWP2000Interface(LogStatus);
            using var connected = new ManualResetEvent(false);
            using var terminated = new ManualResetEvent(false);
            kwp.ConnectionStatusChangedEvent += (comm, status, willReconnect) =>
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

            kwp.SelectedDeviceInfo = chosen;
            kwp.NumConnectionAttempts = 1;
            kwp.FastInitUseConnectAddressOnly = true;

            bool started = slowInit
                ? kwp.ConnectToECUSlowInit(connectAddress)
                : kwp.ConnectToECUFastInit(KWP2000AddressMode.Physical, connectAddress);
            if (!started)
            {
                Log(initName + " init did not start. No speed byte was sent.");
                Release(kwp, terminated);
                return Report(FirstWakeProbeResult.WakeFail);
            }

            int waitSeconds = slowInit ? 45 : 20;
            int which = WaitHandle.WaitAny(new WaitHandle[] { connected, terminated }, TimeSpan.FromSeconds(waitSeconds));
            if (which != 0)
            {
                Log("Address 0x" + connectAddress.ToString("X2") + " was not answered. No speed byte was sent.");
                Release(kwp, terminated);
                Console.Out.WriteLine("result wake-fail");
                Console.Out.WriteLine("speed-byte not-sent");
                return 1;
            }

            if (connectOnly)
            {
                Log("Address 0x" + connectAddress.ToString("X2") + " answered. No speed byte was sent.");
                Release(kwp, terminated);
                Console.Out.WriteLine("result connected");
                Console.Out.WriteLine("speed-byte not-sent");
                return 0;
            }

            if (readDtcs)
            {
                return ReadHeldDtcs(kwp, terminated);
            }

            if (readEeprom)
            {
                return ReadHeldEeprom(kwp, terminated);
            }

            var operation = FirstWakeFlashOperation.CreateProbe(kwp, speed, clearOnSilent);
            if (releaseBeforeSpeed)
            {
                operation.StopBeforeSpeedByte();
            }

            if (standardSession)
            {
                operation.RequestStandardSession();
            }

            if (instead != null)
            {
                if (afterSpeed)
                {
                    operation.SendFollowAfterSpeed(instead);
                }
                else
                {
                    operation.SendInsteadOfSpeedByte(instead);
                    if (insteadBaud != 0)
                    {
                        operation.SendInsteadAtBaud(insteadBaud);
                    }
                }
            }

            if (beforeKey)
            {
                operation.RequestBeforeKey();
            }

            if (keyFirst)
            {
                operation.RequestKeyFirst();
            }

            if (thenChain != null)
            {
                operation.SendThenChain(thenChain);
            }

            if (timingAfterAccept)
            {
                operation.RequestTimingAfterAccept();
            }
            using var done = new ManualResetEvent(false);
            operation.CompletedOperationEvent += (op, success) => done.Set();
            operation.Start();

            FirstWakeProbeResult result;
            if (!done.WaitOne(TimeSpan.FromSeconds(90)))
            {
                Log("Probe timed out.");
                result = operation.ProbeResult;
                if (result == FirstWakeProbeResult.None)
                {
                    result = FirstWakeProbeResult.WakeFail;
                }
            }
            else
            {
                result = operation.ProbeResult;
                if ((result == FirstWakeProbeResult.None) && (instead == null) && !afterSpeed && !releaseBeforeSpeed && !standardSession)
                {
                    result = FirstWakeProbeResult.WakeFail;
                }

                if (!string.IsNullOrEmpty(operation.ResultText))
                {
                    Log(operation.ResultText);
                }
            }

            Release(kwp, terminated);
            if (beforeKey)
            {
                bool speedSent = (result != FirstWakeProbeResult.WakeFail)
                    && (instead != null)
                    && (instead.Length >= 3)
                    && (instead[0] == 0x10)
                    && (instead[1] == 0x85);
                Console.Out.WriteLine("result beforekey");
                Console.Out.WriteLine("key not-sent");
                Console.Out.WriteLine(speedSent ? "speed-byte 14 sent" : "speed-byte not-sent");
                return (result == FirstWakeProbeResult.WakeFail) ? 1 : 0;
            }

            if (timingAfterAccept)
            {
                Console.Out.WriteLine(FirstWakeProbeReport.ResultLine(result));
                string timing = string.IsNullOrEmpty(operation.TimingText) ? "not-sent" : operation.TimingText;
                Console.Out.WriteLine("timing " + timing);
                return (result == FirstWakeProbeResult.Accepted) ? 0 : 1;
            }

            if (afterSpeed)
            {
                Console.Out.WriteLine("result after14");
                Console.Out.WriteLine((result == FirstWakeProbeResult.WakeFail) ? "speed-byte not-sent" : "speed-byte 14 sent");
                return (result == FirstWakeProbeResult.WakeFail) ? 1 : 0;
            }

            if (releaseBeforeSpeed || standardSession || (instead != null))
            {
                string label = releaseBeforeSpeed ? "result release" : (standardSession ? "result standard" : "result request");
                Console.Out.WriteLine(label);
                if (insteadBaud != 0)
                {
                    Console.Out.WriteLine("baud " + insteadBaud);
                }

                Console.Out.WriteLine("speed-byte not-sent");
                return (result == FirstWakeProbeResult.WakeFail) ? 1 : 0;
            }

            return Report(result);
        }

        static int ReadHeldDtcs(KWP2000Interface kwp, ManualResetEvent terminated)
        {
            if (!kwp.IsHeldKWP1281Session)
            {
                Log("This connect is not the held KWP1281 session. No DTC request was sent. No speed byte was sent.");
                Release(kwp, terminated);
                Console.Out.WriteLine("result not-kwp1281");
                Console.Out.WriteLine("speed-byte not-sent");
                return 1;
            }

            bool ok = false;
            string report = "";
            using var done = new ManualResetEvent(false);
            kwp.RequestHeldKWP1281Dtcs(true, (success, text) =>
            {
                ok = success;
                report = text;
                done.Set();
            });

            if (!done.WaitOne(TimeSpan.FromSeconds(30)))
            {
                Log("DTC request timed out. No speed byte was sent.");
                Release(kwp, terminated);
                Console.Out.WriteLine("result dtc-fail");
                Console.Out.WriteLine("speed-byte not-sent");
                return 1;
            }

            Log(report);
            Release(kwp, terminated);
            Console.Out.WriteLine(ok ? "result dtc" : "result dtc-fail");
            Console.Out.WriteLine("dtc " + report);
            Console.Out.WriteLine("speed-byte not-sent");
            return ok ? 0 : 1;
        }

        static int ReadHeldEeprom(KWP2000Interface kwp, ManualResetEvent terminated)
        {
            if (!kwp.IsHeldKWP1281Session)
            {
                Log("This connect is not the held KWP1281 session. No EEPROM read was sent. No speed byte was sent.");
                Release(kwp, terminated);
                Console.Out.WriteLine("result not-kwp1281");
                Console.Out.WriteLine("speed-byte not-sent");
                return 1;
            }

            bool ok = false;
            byte[]? eeprom = null;
            using var done = new ManualResetEvent(false);
            kwp.RequestHeldKWP1281Eeprom((success, bytes) =>
            {
                ok = success;
                eeprom = bytes;
                done.Set();
            });

            if (!done.WaitOne(TimeSpan.FromMinutes(4)))
            {
                Log("EEPROM read timed out. No speed byte was sent.");
                Release(kwp, terminated);
                Console.Out.WriteLine("result eeprom-fail");
                Console.Out.WriteLine("speed-byte not-sent");
                return 1;
            }

            Release(kwp, terminated);
            if (!ok || (eeprom == null))
            {
                Log("EEPROM read failed. No speed byte was sent.");
                Console.Out.WriteLine("result eeprom-fail");
                Console.Out.WriteLine("speed-byte not-sent");
                return 1;
            }

            Log(KWP1281CalibrationRead.Describe(eeprom));
            Console.Out.WriteLine("result eeprom");
            Console.Out.WriteLine("eeprom " + KWP1281CalibrationRead.Describe(eeprom));
            for (int i = 0; i < eeprom.Length; i += 16)
            {
                string line = i.ToString("X2") + ":";
                int n = Math.Min(16, eeprom.Length - i);
                for (int j = 0; j < n; j++)
                {
                    line += " " + eeprom[i + j].ToString("X2");
                }

                Console.Out.WriteLine(line);
            }

            Console.Out.WriteLine("speed-byte not-sent");
            return 0;
        }

        // One fresh wake per byte: idents, then 10 85 XX before the key. No proof, no erase, no 0x82.
        // Stops on silence or a dead wake. 0x14 and 0x50 are the bytes this ECU has already accepted.
        static int SweepSpeedBytes(int start)
        {
            DeviceInfo? chosen = null;
            foreach (DeviceInfo info in DeviceManager.EnumerateAllDevices())
            {
                if (info.Type != DeviceType.FTDI)
                {
                    continue;
                }

                if (string.Equals(info.SerialNumber, CableSerial, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = info;
                    break;
                }

                if (chosen == null)
                {
                    chosen = info;
                }
            }

            if (chosen == null)
            {
                Log("No FTDI device.");
                return 1;
            }

            string path = Path.Combine("plans", "speed-byte-sweep.txt");
            Directory.CreateDirectory("plans");
            var kwp = new KWP2000Interface(LogStatus);
            using var connected = new ManualResetEvent(false);
            using var terminated = new ManualResetEvent(false);
            kwp.ConnectionStatusChangedEvent += (comm, status, willReconnect) =>
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

            kwp.SelectedDeviceInfo = chosen;
            kwp.NumConnectionAttempts = 1;
            kwp.FastInitUseConnectAddressOnly = true;
            int accepted = 0;
            int refused = 0;

            using (var record = new StreamWriter(path, start > 0))
            {
                if (start == 0)
                {
                    record.WriteLine("byte result detail");
                    record.Flush();
                }

                bool identRetry = false;
                for (int i = start; i <= 0xFF; i++)
                {
                    byte speedByte = (byte)i;
                    string hex = speedByte.ToString("X2");
                    connected.Reset();
                    terminated.Reset();
                    bool started = kwp.ConnectToECUFastInit(KWP2000AddressMode.Physical, KWP2000Interface.DEFAULT_ECU_SLOWINIT_KWP2000_ADDRESS);
                    if (!started)
                    {
                        WriteSweep(record, hex + " wake-fail connect");
                        return 1;
                    }

                    int which = WaitHandle.WaitAny(new WaitHandle[] { connected, terminated }, TimeSpan.FromSeconds(20));
                    if (which != 0)
                    {
                        WriteSweep(record, hex + " wake-fail");
                        Release(kwp, terminated);
                        return 1;
                    }

                    var operation = FirstWakeFlashOperation.CreateProbe(kwp, speedByte, false);
                    operation.SendInsteadOfSpeedByte(new byte[] { 0x10, 0x85, speedByte });
                    operation.RequestBeforeKey();
                    operation.SkipSessionProof();
                    using var done = new ManualResetEvent(false);
                    operation.CompletedOperationEvent += (op, success) => done.Set();
                    operation.Start();
                    string line;
                    if (!done.WaitOne(TimeSpan.FromSeconds(15)))
                    {
                        line = hex + " timeout";
                        WriteSweep(record, line);
                        Release(kwp, terminated);
                        return 1;
                    }

                    line = hex + " " + ClassifySweep(operation.ProbeResult, operation.ResultText);
                    bool identMiss = (operation.ResultText != null)
                        && (operation.ResultText.IndexOf("was not sent", StringComparison.Ordinal) >= 0)
                        && line.EndsWith(" fail");
                    Release(kwp, terminated);
                    if (identMiss && !identRetry)
                    {
                        WriteSweep(record, hex + " ident-miss");
                        identRetry = true;
                        i--;
                        Thread.Sleep(500);
                        continue;
                    }

                    identRetry = false;
                    WriteSweep(record, line);
                    if (line.Contains(" accept"))
                    {
                        accepted++;
                    }
                    else if (line.Contains(" refuse"))
                    {
                        refused++;
                    }
                    else
                    {
                        Console.Out.WriteLine("stopped " + line + " accepted " + accepted + " refused " + refused);
                        Console.Out.Flush();
                        return 1;
                    }

                    Thread.Sleep(500);
                }
            }

            Console.Out.WriteLine("sweep done accepted " + accepted + " refused " + refused);
            Console.Out.Flush();
            return 0;
        }

        static void WriteSweep(StreamWriter record, string line)
        {
            record.WriteLine(line);
            record.Flush();
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }

        static string ClassifySweep(FirstWakeProbeResult result, string text)
        {
            if (result == FirstWakeProbeResult.WakeFail)
            {
                return "fail";
            }

            if (result == FirstWakeProbeResult.Silent)
            {
                return "silent";
            }

            if (string.IsNullOrEmpty(text))
            {
                return "no-result";
            }

            if (text.IndexOf("got no reply", StringComparison.Ordinal) >= 0)
            {
                return "silent";
            }

            if (text.IndexOf("FAILURE", StringComparison.Ordinal) >= 0)
            {
                return "fail";
            }

            int dataAt = text.IndexOf("Data ", StringComparison.Ordinal);
            if (dataAt >= 0)
            {
                string payload = text.Substring(dataAt + 5).Trim().TrimEnd('.');
                if (payload.StartsWith("10 ", StringComparison.Ordinal))
                {
                    return "refuse " + payload.Substring(3);
                }

                return "accept " + payload;
            }

            if (text.IndexOf("reply received", StringComparison.Ordinal) >= 0)
            {
                return "accept";
            }

            return "other";
        }

        static void Release(KWP2000Interface kwp, ManualResetEvent terminated)
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

        static int Report(FirstWakeProbeResult result)
        {
            Console.Out.WriteLine(FirstWakeProbeReport.ResultLine(result));
            Console.Out.WriteLine(FirstWakeProbeReport.NextLine(result));
            return (result == FirstWakeProbeResult.Accepted) ? 0 : 1;
        }

        static void LogStatus(string message, Shared.StatusMessageType messageType)
        {
            Log(message);
        }

        static void Log(string message)
        {
            lock (LogLock)
            {
                Console.Error.WriteLine(message);
            }
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
