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
	public enum StatusMessageType
	{
		USER,
		LOG, 
        DEV,
		DEV_USER
	};

    public enum UserPromptType
    {
		OK,
        OK_CANCEL,
        YES_NO_CANCEL
    };

    public enum UserPromptResult
    {
        NONE,
        OK,
        CANCEL,
        YES,
        NO
    };

	public delegate void DisplayStatusMessageDelegate(string message, StatusMessageType messageType);
	public delegate UserPromptResult DisplayUserPrompt(string title, string message, UserPromptType promptType);
}
