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
using System.Windows;
using System.Windows.Controls;

using Communication;

namespace ECUFlasher
{
    public class CommunicationInterface_ViewModel : BaseViewModel
    {
        public CommunicationInterface_ViewModel()
        {
            if ((Application.Current != null) && (Application.Current is App))
            {
                App = (App)Application.Current;
            }
        }

        public App App
        {
            get
            {
                return _App;
            }
            private set
            {
                if (value != _App)
                {
                    _App = value;

                    ConnectCommInterfaceEvents();
                }
            }
        }
        private App _App;

        public CommunicationInterface CommInterface
        {
            get
            {
                return _CommInterface;
            }
            protected set
            {
                if (value != _CommInterface)
                {
                    _CommInterface = value;

                    ConnectCommInterfaceEvents();
                }
            }
        }
        private CommunicationInterface _CommInterface;

        private void ConnectCommInterfaceEvents()
        {
            if ((App != null) && (CommInterface != null))
            {
                CommInterface.mDisplayStatusMessage += App.DisplayStatusMessage;
                CommInterface.mDisplayUserPrompt += App.DisplayUserPrompt;
            }
        }

        public delegate void FailedToStartConnectingDelegate();
        public event FailedToStartConnectingDelegate FailedToStartConnectingEvent;

        protected void OnFailedToStartConnecting()
        {
            if (FailedToStartConnectingEvent != null)
            {
                FailedToStartConnectingEvent();
            }
        }
    }    
}
