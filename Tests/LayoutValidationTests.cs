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

using Communication;
using Xunit;

namespace NefMotoOpenSource.Tests
{
    public class LayoutValidationTests
    {
        [Fact]
        public void Programming_session_NoProgram_asks_before_erase()
        {
            Assert.True(LayoutValidationText.AskBeforeErase(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported));
        }

        [Fact]
        public void Programming_session_upload_reject_asks_before_erase()
        {
            Assert.True(LayoutValidationText.AskBeforeErase(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadRejected));
        }

        [Theory]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession)]
        [InlineData(KWP2000DiagnosticSessionType.StandardSession)]
        public void Upload_unavailable_outside_programming_does_not_ask(KWP2000DiagnosticSessionType session)
        {
            Assert.False(LayoutValidationText.AskBeforeErase(
                session,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported));
        }

        [Theory]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.StartIsntLowest)]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.EndIsntHighest)]
        public void Address_rejects_stay_on_the_layout_probe(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result)
        {
            Assert.False(LayoutValidationText.AskBeforeErase(KWP2000DiagnosticSessionType.ProgrammingSession, result));
            Assert.False(LayoutValidationText.UploadProbeOpensProgramming(result));
        }

        [Theory]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported)]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted)]
        public void Upload_not_served_or_security_refused_opens_programming(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result)
        {
            Assert.True(LayoutValidationText.UploadProbeOpensProgramming(result));
        }

        [Fact]
        public void General_reject_does_not_open_programming()
        {
            Assert.False(LayoutValidationText.UploadProbeOpensProgramming(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadRejected));
        }

        [Fact]
        public void Successful_upload_does_not_open_programming()
        {
            Assert.False(LayoutValidationText.UploadProbeOpensProgramming(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.Valid));
        }

        [Fact]
        public void Served_upload_stays_on_the_layout_probe()
        {
            Assert.False(LayoutValidationText.AskBeforeErase(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.Valid));
            Assert.False(LayoutValidationText.MustAbort(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.Valid));
        }

        [Fact]
        public void Development_session_general_reject_does_not_ask()
        {
            Assert.False(LayoutValidationText.AskBeforeErase(
                KWP2000DiagnosticSessionType.DevelopmentSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadRejected));
        }

        [Fact]
        public void Security_refusal_opens_programming_and_does_not_ask()
        {
            Assert.False(LayoutValidationText.AskBeforeErase(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted));
            Assert.True(LayoutValidationText.MustAbort(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted));
            Assert.True(LayoutValidationText.UploadProbeOpensProgramming(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted));
        }

        [Theory]
        [InlineData((byte)KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError)]
        [InlineData((byte)KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat)]
        public void Checksum_nrc_means_the_routine_is_unavailable(byte nrc)
        {
            Assert.True(ValidateFlashChecksumAction.ChecksumNrcMeansUnavailable(nrc));
        }

        [Theory]
        [InlineData((byte)KWP2000ResponseCode.NoProgram)]
        [InlineData((byte)KWP2000ResponseCode.ServiceNotSupported)]
        [InlineData((byte)KWP2000ResponseCode.GeneralReject)]
        public void Other_checksum_nrc_does_not_continue_the_read(byte nrc)
        {
            Assert.False(ValidateFlashChecksumAction.ChecksumNrcMeansUnavailable(nrc));
        }

        [Theory]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported)]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadRejected)]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted)]
        public void MustAbort_stops_the_read_for_upload_failures(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result)
        {
            Assert.True(LayoutValidationText.MustAbort(result));
        }

        [Theory]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.StartIsntLowest)]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.EndIsntHighest)]
        public void MustAbort_leaves_an_address_reject_on_the_probe(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result)
        {
            Assert.False(LayoutValidationText.MustAbort(result));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
