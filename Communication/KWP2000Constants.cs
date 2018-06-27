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
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Remoting.Messaging;
using System.Diagnostics;
using System.ComponentModel;
using System.Xml.Serialization;
using Shared;

namespace Communication
{
	//Tested on 8D0907551 T and A
    //10400 baud - works - actually 10417, standard session only accepts this rate
    //38400 baud - works
    //57600 baud - works - setzi says not on 32mhz ecus
    //59200 baud - invalid format
    //60800 baud - invalid format
    //62400 baud - invalid format
    //64000 baud - invalid format
    //65600 baud - invalid format
    //67200 baud - invalid format
    //68800 baud - invalid format
    //70400 baud - invalid format
    //72000 baud - invalid format
    //73600 baud - invalid format
    //75200 baud - invalid format
    //76800 baud - invalid format - setzi says this only works on 32mhz ecu
    //78400 baud - invalid format
    //80000 baud - invalid format
    //81600 baud - invalid format
    //83200 baud - invalid format
    //84800 baud - invalid format
    //86400 baud - invalid format
    //88000 baud - invalid format
    //89600 baud - invaild format, but programming session seems to use this as a default? checks for 0x78
    //91200 baud - invalid format
    //92800 baud - works
    //94400 baud - works
    //96000 baud - invalid format
    //97600 baud - invalid format
    //99200 baud - works
    //100800 baud - 127 - works
    //128 - invalid format
	//105600 - 129 - works
	//108800 - 130 - works - setzi says only for 24mhz ecus
	//131 to 134 - invalid format
	//124800 baud - 135 - works
	//136 to 142 - invalid format
	//150400 baud - 143 - works
	//144 to 147 - invalid format
	//166400 baud - 148 - works
	//149 to 153 - invalid format
	//185600 baud - 154 - works
    //188800 baud - 155 - works
	//156 to 166 - invalid format
	//249600 baud - 167 - works
	//168 to 255 - invalid format
    //260000 baud - invalid format - any rate above 0x2FFFF == 196607 the ecu sets to 0x3F7A0 == 260000

	//Only includes common baud rates supported by all VAG ECUs
    public enum KWP2000BaudRates : uint
    {
        BAUD_UNSPECIFIED = 0,

		BAUD_9600 = 9600,
        BAUD_10400 = 10400,
		BAUD_38400 = 38400,
		BAUD_52800 = 52800,
		BAUD_124800 = 124800,

        BAUD_DEFAULT = BAUD_10400
    };

	//From setzis tests
	//public enum KWP2000CommonSupportedBaudRateBytes : byte
	//{
	//    000,
	//    001,
	//    002,
	//    003,
	//    004,
	//    005,
	//    006,
	//    007,
	//    008,
	//    009,
	//    010,
	//    011,
	//    012,
	//    013,
	//    014,
	//    015,
	//    016,
	//    017,
	//    018,
	//    019,
	//    020,
	//    021,
	//    022,
	//    023,
	//    024,
	//    025,
	//    026,
	//    027,
	//    028,
	//    029,
	//    030,
	//    031,
	//    032,
	//    033,
	//    034,
	//    035,
	//    036,
	//    037,
	//    038,
	//    039,
	//    040,
	//    041,
	//    042,
	//    043,
	//    044,
	//    045,
	//    046,
	//    047,
	//    048,
	//    049,
	//    050,
	//    051,
	//    052,
	//    053,
	//    054,
	//    055,
	//    056,
	//    057,
	//    058,
	//    060,
	//    061,
	//    062,
	//    063,
	//    065,
	//    066,
	//    067,
	//    068,
	//    069,
	//    071,
	//    073,
	//    075,
	//    076,
	//    079,
	//    080,
	//    081,
	//    084,
	//    087,
	//    088,
	//    089,
	//    097,
	//    107,
	//    135
	//}

    public enum KWP2000ConnectionMethod : uint
    {        
        SlowInit = 0,     
        FastInit
    };

    //only the upper two bits are used
    public enum KWP2000AddressMode : byte
    {
        None = 0x00,
        CARB_Exception = 0x40,//ISO 9141-2 and SAE J1979
        Physical = 0x80,
        Functional = 0xC0
    };

    public enum KWP2000DiagnosticSessionType : byte
    {
        [Description("Unknown Session")]
        InternalUndefined = 0x00,
        [Description("Standard Session")]
        StandardSession = 0x81,
        EndOfLineFIAT = 0x83,
        EndOfLineSystemSupplier = 0x84,
        [Description("Programming Session")]
        ProgrammingSession = 0x85,
        [Description("Development Session")]
        DevelopmentSession = 0x86,
        [Description("Adjustment Session")]
        AdjustmentSession = 0x87,
        DedicatedSessionForComponentStarting = 0x89
    };

    //iso14230 codes
    public enum KWP2000ServiceID : byte
    {
        //00-0F are SAE1979 requests
        //40-4F are SAE1979 responses to requests

        SAEJ1979_ShowCurrentData = 0x01,
        SAEJ1979_ShowFreezeFrameData = 0x02,
        SAEJ1979_ShowDiagnosticTroubleCodes = 0x03,
        SAEJ1979_ClearTroubleCodesAndStoredValues = 0x04,
        SAEJ1979_TestResultOxygenSenors = 0x05,
        SAEJ1979_TestResultsNonContinuouslyMonitored = 0x06,
        SAEJ1979_ShowPendingTroubleCodes = 0x07,
        SAEJ1979_SpecialControlMode = 0x08,
        SAEJ1979_RequestVehicleInformation = 0x09,
        SAEJ1979_RequestPermanentTroubleCodes = 0x0A,
        StartDiagnosticSession = 0x10,
        EcuReset = 0x11,
        ReadFreezeFrameData = 0x12,
        ReadDiagnosticTroubleCodes = 0x13,//KWP2000 spec forbids this
        ClearDiagnosticInformation = 0x14,
        ReadStatusOfDiagnosticTroubleCodes = 0x17,
        ReadDiagnosticTroubleCodesByStatus = 0x18,
        ReadECUIdentification = 0x1A,
        StopDiagnosticSession = 0x20,//KWP2000 spec forbids this
        ReadDataByLocalIdentifier = 0x21,
        ReadDataByCommonIdentifier = 0x22,
        ReadMemoryByAddress = 0x23,
        StopRepeatedDataTransmission = 0x25,
        SetDataRates = 0x26,
        SecurityAccess = 0x27,
        DynamicallyDefineLocalIdentifier = 0x2C,
        WriteDataByCommonIdentifier = 0x2E,
        InputOutputControlByCommonIdentifier = 0x2F,
        InputOutputControlByLocalIdentifier = 0x30,
        StartRoutineByLocalIdentifier = 0x31,
        StopRoutineByLocalIdentifier = 0x32,
        RequestRoutineResultsByLocalIdentifier = 0x33,
        RequestDownload = 0x34,
        RequestUpload = 0x35,
        TransferData = 0x36,
        RequestTransferExit = 0x37,
        StartRoutineByAddress = 0x38,
        StopRoutineByAddress = 0x39,
        RequestRoutineResultsByAddress = 0x3A,
        WriteDataByLocalIdentifier = 0x3B,
        WriteMemoryByAddress = 0x3D,
        TesterPresent = 0x3E,
        StartDiagnosticSessionPositiveResponse = 0x50,
        ReadDiagnosticTroubleCodesPositiveResponse = 0x53,
        ClearDiagnosticInformationPositiveResponse = 0x54,
        ReadStatusOfDiagnosticTroubleCodesPositiveResponse = 0x57,
        ReadDiagnosticTroubleCodesByStatusPositiveResponse = 0x58,
        ReadECUIdentificationPositiveResponse = 0x5A,
        StopDiagnosticSessionPositiveResponse = 0x60,
        ReadDataByLocalIdentifierPositiveResponse = 0x61,
        ReadMemoryByAddressPositiveResponse = 0x63,
        SecurityAccessPositiveResponse = 0x67,
        StartRoutineByLocalIdentifierPositiveResponse = 0x71,
        RequestRoutineResultsByLocalIdentifierPositiveResponse = 0x73,
        RequestDownloadPositiveResponse = 0x74,
        RequestUploadPositiveResponse = 0x75,
        TransferDataPositiveResponse = 0x76,
        RequestTransferExitPositiveResponse = 0x77,
        WriteMemoryByAddressPositiveResponse = 0x7D,
        TesterPresentPositiveReponse = 0x7E,
        NegativeResponse = 0x7F,
        EscapeCode = 0x80,
        StartCommunication = 0x81,
        StopCommunication = 0x82,
        AccessTimingParameters = 0x83,
        StartCommunicationPositiveResponse = 0xC1,
        StopCommunicationPositiveResponse = 0xC2,
        AccessTimingParametersPositiveResponse = 0xC3
    };

    public enum KWP2000ResponseCode : byte
    {
        //80-FF are manufacturer specific codes

        GeneralReject = 0x10,
        ServiceNotSupported = 0x11,
        SubFunctionNotSupported_InvalidFormat = 0x12,
//TODO: what is this?
UnknownEDC15ResponseCode = 0x13,
        Busy_RepeastRequest = 0x21,
        ConditionsNotCorrectOrRequestSequenceError = 0x22,
        RoutineNotCompleteOrServiceInProgress = 0x23,
        RequestOutOfRange = 0x31,
        SecurityAccessDenied_SecurityAccessRequested = 0x33,
        //0x34 is not defined, but VAG seems to use this number to indicate a successful security login
        InvalidKey = 0x35,//for security access
        ExceedNumberOfAttempts = 0x36,//for security access
        RequiredTimeDelayNotExpired = 0x37,//for security access
        DownloadNotAccepted = 0x40,
        ImproperDownloadType = 0x41,
        CanNotDownloadToSpecifiedAddress = 0x42,
        CanNotDownloadNumberOfBytesRequested = 0x43,
        UploadNotAccepted = 0x50,
        ImproperUploadType = 0x51,
        CanNotUploadFromSpecifiedAddress = 0x52,
        CanNotUploadNumberOfBytesRequested = 0x53,
        TransferSuspended = 0x71,
        TransferAborted = 0x72,
        IllegalAddressInBlockTransfer = 0x74,
        IllegalByteCountInBlockTransfer = 0x75,
        IllegalBlockTransferType = 0x76,
        BlockTransferDataChecksumError = 0x77,
        RequestCorrectlyReceived_ResponsePending = 0x78,
        IncorrectByteCountDuringBlockTransfer = 0x79,
        ServiceNotSupportedInActiveDiagnosticSession = 0x80,
        NoProgram = 0x90//VAG Specific
    };

    public enum KWP2000IdentificationOption : byte
    {
		[Description("ECU Identification Data Table")]
        ECUIdentificationDataTable = 0x80,//supported//gets a table containing identification options 82 to BF
		[Description("ECU Identification Scaling Table")]
        ECUIdentificationScalingTable = 0x81,//supported		
        vehicleManufacturerSpecific = 0x86,//not supported//Extended ECU identification number, serial number? SCA insertion requires this field
		[Description("Vehicle Manufacturer Spare Part Number")]
        vehicleManufacturerSparePartNumber = 0x87,
		[Description("Vehicle Manufacturer ECU Software Number")]
        vehicleManufacturerECUSoftwareNumber = 0x88,
		[Description("Vehicle Manufacturer ECU Software Version Number")]
        vehicleManufacturerECUSoftwareVersionNumber = 0x89,
		[Description("System Supplier")]
        systemSupplier = 0x8A,
		[Description("ECU Manufacturing Date")]
        ECUManufacturingDate = 0x8B,
		[Description("ECU Serial Number")]
        ECUSerialNumber = 0x8C,		
        systemSupplierSpecific1 = 0x8D,
        systemSupplierSpecific2 = 0x8E,
        systemSupplierSpecific3 = 0x8F,
		[Description("Vehicle Identification Number")]
        vehicleIdentificationNumber = 0x90,//supported, not set
		[Description("Vehicle Manufacturer ECU Hardware Number")]
        vehicleManufacturerECUHardwareNumber = 0x91,//supported, sometimes set, depending on ECU version and diagnostic mode?
		[Description("System Supplier ECU Hardware Number")]
        systemSupplierECUHardwareNumber = 0x92,//supported, set
		[Description("System Supplier ECU Hardware Version Number")]
        systemSupplierECUHardwareVersionNumber = 0x93,//supported, not set
		[Description("System Supplier ECU Software Number")]
        systemSupplierECUSoftwareNumber = 0x94,//supported, set
		[Description("System Supplier ECU Software Version Number")]
        systemSupplierECUSoftwareVersionNumber = 0x95,//supported, not set
		[Description("Exhaust Regulation or Type Approval Number")]
        exhaustRegulationOrTypeApprovalNumber = 0x96,//supported, not set
		[Description("System Name or Engine Type")]
        systemNameOrEngineType = 0x97,//supported, not set
		[Description("Repair Shop Code or Tester Serial Number")]
        repairShopCodeOrTesterSerialNumber = 0x98,//supported, not set
		[Description("Programming Date")]
        programmingDate = 0x99,//supported, not set
		[Description("Calibration Repair Shop Code or Calibration Equipment Serial Number")]
        calibrationRepairShopCodeOrCalibrationEquipmentSerialNumber = 0x9A,
		[Description("Calibration Date")]
        calibrationDate = 0x9B,//supported//ECU identification number.  SCA insertion requires this field
		[Description("Calibration Equiment Software Number")]
        calibrationEquipmentSoftwareNumber = 0x9C,//supported//flash status -  byte:flash status, byte:num flash attempts, byte:num successful flash attempts, byte:status of flash preconditions
		[Description("ECU Installation Date")]
        ECUInstallationDate = 0x9D,
        vehicleManufacturerSpecific1 = 0x9E,
        vehicleManufacturerSpecific2 = 0x9F,
        vehicleManufacturerSpecific3 = 0xA0,
        vehicleManufacturerSpecific4 = 0xA1,
        vehicleManufacturerSpecific5 = 0xA2,
        vehicleManufacturerSpecific6 = 0xA3,
        vehicleManufacturerSpecific7 = 0xA4,
        vehicleManufacturerSpecific8 = 0xA5,
        vehicleManufacturerSpecific9 = 0xA6,
        vehicleManufacturerSpecific10 = 0xA7,
        vehicleManufacturerSpecific11 = 0xA8,
        vehicleManufacturerSpecific12 = 0xA9,
        vehicleManufacturerSpecific13 = 0xAA,
        vehicleManufacturerSpecific14 = 0xAB,
        vehicleManufacturerSpecific15 = 0xAC,
        vehicleManufacturerSpecific16 = 0xAD,
        vehicleManufacturerSpecific17 = 0xAE,
        vehicleManufacturerSpecific18 = 0xAF,
        systemSupplierSpecific4 = 0xB0,
        systemSupplierSpecific5 = 0xB1,
        systemSupplierSpecific6 = 0xB2,
        systemSupplierSpecific7 = 0xB3,
        systemSupplierSpecific8 = 0xB4,
        systemSupplierSpecific9 = 0xB5,
        systemSupplierSpecific10 = 0xB6,
        systemSupplierSpecific11 = 0xB7,
        systemSupplierSpecific12 = 0xB8,
        systemSupplierSpecific13 = 0xB9,
        systemSupplierSpecific14 = 0xBA,
        systemSupplierSpecific15 = 0xBB,
        systemSupplierSpecific16 = 0xBC,
        systemSupplierSpecific17 = 0xBD,
        systemSupplierSpecific18 = 0xBE,
        systemSupplierSpecific19 = 0xBF
    };

    public enum KWP2000VAGLocalIdentifierRoutine : byte
    {
        EraseFlash = 0xC4,
        ValidateFlashChecksum = 0xC5
    };
}
