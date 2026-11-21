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
using System.Linq;
using System.Text;

namespace Shared
{
    // Sink matrix: docs/status-message-sinks.md
    // ERROR — failures; GUI pane + log; CLI stderr at default.
    // PROGRESS — long-op start/%/outcome; GUI pane + log; CLI stderr at default.
    // USER — status-pane narrative; log; CLI stderr at -v.
    // LOG — log file (CLI at -vv); not the GUI pane.
    // DEV / DEV_USER — log file in debug builds; CLI at -vvv.
    // TRACE — debugger/per-frame; CLI stderr with -d/--debug only.
    public enum StatusMessageType
    {
        USER,
        LOG,
        DEV,
        DEV_USER,
        TRACE,
        ERROR,
        PROGRESS
    };

    public enum UserPromptType
    {
        OK,
        OK_CANCEL,
        YES_NO_CANCEL,
        /// <summary>
        /// Yes/No/Cancel MessageBox for "Check for mirror before continuing?":
        /// Yes=Check (CHECK), No=Continue (OK), Cancel=Abort (CANCEL).
        /// </summary>
        CONTINUE_CHECK_CANCEL
    };

    public enum UserPromptResult
    {
        NONE,
        OK,
        CANCEL,
        YES,
        NO,
        CHECK
    };

    public delegate void DisplayStatusMessageDelegate(string message, StatusMessageType messageType);
    public delegate UserPromptResult DisplayUserPrompt(string title, string message, UserPromptType promptType);
    public delegate void BeginDisplayUserPromptDelegate(string title, string message, UserPromptType promptType, Action<UserPromptResult> completed);
}

// vi: set sw=4 ts=8 expandtab:
