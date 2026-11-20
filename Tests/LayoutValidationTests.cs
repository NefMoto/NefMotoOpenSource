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
        public void Programming_session_NoProgram_continues_write_without_layout_validation()
        {
            Assert.True(LayoutValidationText.ContinueWriteWithoutLayoutValidation(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported));
        }

        [Fact]
        public void Programming_session_upload_reject_continues_write_without_layout_validation()
        {
            Assert.True(LayoutValidationText.ContinueWriteWithoutLayoutValidation(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadRejected));
        }

        [Theory]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession)]
        [InlineData(KWP2000DiagnosticSessionType.StandardSession)]
        public void Upload_unavailable_outside_programming_does_not_continue_write_without_layout(KWP2000DiagnosticSessionType session)
        {
            Assert.False(LayoutValidationText.ContinueWriteWithoutLayoutValidation(
                session,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported));
        }

        [Theory]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.StartIsntLowest)]
        [InlineData(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.EndIsntHighest)]
        public void Address_rejects_stay_on_the_layout_probe(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result)
        {
            Assert.False(LayoutValidationText.ContinueWriteWithoutLayoutValidation(KWP2000DiagnosticSessionType.ProgrammingSession, result));
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
            Assert.False(LayoutValidationText.ContinueWriteWithoutLayoutValidation(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.Valid));
            Assert.False(LayoutValidationText.MustAbort(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.Valid));
        }

        [Fact]
        public void Development_session_general_reject_does_not_continue_write_without_layout()
        {
            Assert.False(LayoutValidationText.ContinueWriteWithoutLayoutValidation(
                KWP2000DiagnosticSessionType.DevelopmentSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadRejected));
        }

        [Fact]
        public void Security_refusal_opens_programming_and_does_not_continue_write_without_layout()
        {
            Assert.False(LayoutValidationText.ContinueWriteWithoutLayoutValidation(
                KWP2000DiagnosticSessionType.ProgrammingSession,
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted));
            Assert.True(LayoutValidationText.MustAbort(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted));
            Assert.True(LayoutValidationText.UploadProbeOpensProgramming(
                ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted));
        }

        [Theory]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession, KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError, true, ValidateFlashChecksumAction.ChecksumReadStep.OpenProgramming)]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession, KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError, false, ValidateFlashChecksumAction.ChecksumReadStep.ContinueWithoutChecksum)]
        [InlineData(KWP2000DiagnosticSessionType.ProgrammingSession, KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError, true, ValidateFlashChecksumAction.ChecksumReadStep.UnavailablePrompt)]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession, KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat, true, ValidateFlashChecksumAction.ChecksumReadStep.UnavailablePrompt)]
        [InlineData(KWP2000DiagnosticSessionType.ProgrammingSession, KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat, true, ValidateFlashChecksumAction.ChecksumReadStep.UnavailablePrompt)]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession, KWP2000ResponseCode.NoProgram, true, ValidateFlashChecksumAction.ChecksumReadStep.ContinueWithoutChecksum)]
        [InlineData(KWP2000DiagnosticSessionType.ProgrammingSession, KWP2000ResponseCode.NoProgram, true, ValidateFlashChecksumAction.ChecksumReadStep.ContinueWithoutChecksum)]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession, KWP2000ResponseCode.ServiceNotSupported, true, ValidateFlashChecksumAction.ChecksumReadStep.ContinueWithoutChecksum)]
        [InlineData(KWP2000DiagnosticSessionType.ProgrammingSession, KWP2000ResponseCode.ServiceNotSupported, true, ValidateFlashChecksumAction.ChecksumReadStep.ContinueWithoutChecksum)]
        [InlineData(KWP2000DiagnosticSessionType.DevelopmentSession, KWP2000ResponseCode.GeneralReject, true, ValidateFlashChecksumAction.ChecksumReadStep.ContinueWithoutChecksum)]
        [InlineData(KWP2000DiagnosticSessionType.ProgrammingSession, KWP2000ResponseCode.GeneralReject, true, ValidateFlashChecksumAction.ChecksumReadStep.ContinueWithoutChecksum)]
        public void Checksum_read_step(KWP2000DiagnosticSessionType session, KWP2000ResponseCode nrc, bool mayOpenProgramming, ValidateFlashChecksumAction.ChecksumReadStep step)
        {
            Assert.Equal(step, ValidateFlashChecksumAction.ChecksumReadStepFor(session, (byte)nrc, mayOpenProgramming));
        }

        [Fact]
        public void Read_switches_on_ChecksumReadStepFor()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while ((dir != null) && !File.Exists(Path.Combine(dir.FullName, "Communication", "KWP2000Operations.cs")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            string text = File.ReadAllText(Path.Combine(dir.FullName, "Communication", "KWP2000Operations.cs"));
            Assert.Contains("ChecksumReadStepFor", text);
        }

        [Fact]
        public void Write_keeps_checksum_and_skips_flag_upload_after_NoProgram()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while ((dir != null) && !File.Exists(Path.Combine(dir.FullName, "Communication", "KWP2000Operations.cs")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            string text = File.ReadAllText(Path.Combine(dir.FullName, "Communication", "KWP2000Operations.cs"));
            Assert.Contains("ContinueWriteWithoutLayoutValidation", text);
            Assert.Contains("Sector checksums will still be tried", text);
            Assert.Contains("Flash checksum got no reply", text);
            Assert.Contains("new ProgrammingFlagCloser(KWP2000CommInterface, mFlashBlockList.First().mMemoryImage.StartAddress, clearFlagBlock.mMemoryImage, mLayoutUploadAccepted)", text);
            Assert.Contains("Trying matching checksums only", text);
            Assert.DoesNotContain("AskBeforeErase", text);
            Assert.DoesNotContain("ShouldCloseOpenUpload", text);
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
