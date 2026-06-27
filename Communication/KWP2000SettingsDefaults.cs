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

using Shared;

namespace Communication
{
    /// <summary>
    /// Default values for KWP2000 connection and communication settings.
    /// </summary>
    public static class KWP2000SettingsDefaults
    {
        /// <summary>UI/settings default connect address (same as fast-init physical). Slow init on ME7 bench typically uses this (KWP1281-first path).</summary>
        public const byte ConnectAddress = KWP2000Interface.DEFAULT_ECU_FASTINIT_KWP2000_PHYSICAL_ADDRESS;
        public const KWP2000AddressMode ConnectAddressMode = KWP2000AddressMode.Physical;

        public const uint NumConnectionAttempts = 3;
        public const double FastInitLowHighTimeOffsetMS = 0.0;
        public const double SlowInitFiveBaudBitTimeOffsetMS = -0.6;
        public const long TimeBetweenSlowInitForKWP2000MS = 1500;//galletto waits 1.5 seconds after kwp1281
        public const long TimeAfterSlowInitBeforeStartCommMessageMS = 280;

        public const uint KWP1281_TesterMinTimeToSendByteComplementMS = 2;
        public const uint KWP1281_TesterMinTimeToSendResponseMessageMS = 25;//55
        public const uint KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS = 1;

        // EDC15 ECUs do not seem to work with the min time of 5ms, at least when connecting
        // 20, 19, 17 worked, 12 didn't work
        public const long P4TesterInterByteTimeMinMsWhenConnecting = 17;

        public const bool ShouldVerifyDumbMode = true;
        public const bool EnableSlowInitTimingLog = false;

        public const uint DeviceReadTimeOutMs = 1000;
        public const uint DeviceWriteTimeOutMs = 1000;

        public const byte SecuritySeedRequest = SecurityAccessAction.DEFAULT_REQUEST_SEED;
        public const bool SecurityUseExtendedSeedRequest = false;
        public const bool SecuritySupportSpecialKey = false;

        // TimingParameters uses 1000 instead of P2_DEFAULT_ECU_RESPONSE_MAX_TIME so reconnect works after fast timing negotiation
        public const long P2DefaultECUResponseMaxTimeMs = 1000;

        public const string Me75Pin121Hint = "Bench ME7.5 (121-pin ECU): connect pin 121 to +12 V for KWP read/write."
            + " Use the same switched +12 V as pins 3, 21, and 62. Pin 121 is the top right pin on the small ECU connector.";

        public static void LogMe75Pin121Hint(CommunicationInterface commInterface)
        {
            if (commInterface != null)
            {
                commInterface.DisplayStatusMessage(Me75Pin121Hint, StatusMessageType.USER);
            }
        }

        public static TimingParameters CreateDefaultTimingParameters()
        {
            return new TimingParameters();
        }

        public static void ApplyTo(KWP2000Interface commInterface)
        {
            commInterface.NumConnectionAttempts = NumConnectionAttempts;
            commInterface.FastInitLowHighTimeOffsetMS = FastInitLowHighTimeOffsetMS;
            commInterface.SlowInitFiveBaudBitTimeOffsetMS = SlowInitFiveBaudBitTimeOffsetMS;
            commInterface.TimeBetweenSlowInitForKWP2000MS = TimeBetweenSlowInitForKWP2000MS;
            commInterface.TimeAfterSlowInitBeforeStartCommMessageMS = TimeAfterSlowInitBeforeStartCommMessageMS;
            commInterface.KWP1281_TesterMinTimeToSendByteComplementMS = KWP1281_TesterMinTimeToSendByteComplementMS;
            commInterface.KWP1281_TesterMinTimeToSendResponseMessageMS = KWP1281_TesterMinTimeToSendResponseMessageMS;
            commInterface.KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS = KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS;
            commInterface.P4TesterInterByteTimeMinMsWhenConnecting = P4TesterInterByteTimeMinMsWhenConnecting;
            commInterface.ShouldVerifyDumbMode = ShouldVerifyDumbMode;
            commInterface.EnableSlowInitTimingLog = EnableSlowInitTimingLog;
            commInterface.FTDIDeviceReadTimeOutMs = DeviceReadTimeOutMs;
            commInterface.FTDIDeviceWriteTimeOutMs = DeviceWriteTimeOutMs;
            commInterface.DefaultTimingParameters = CreateDefaultTimingParameters();
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
