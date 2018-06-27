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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.ComponentModel;

using Shared;
using Communication;
using FTD2XX_NET;

namespace ECUFlasher
{
    public class BootstrapInterface_ViewModel : CommunicationInterface_ViewModel
    {
        public BootstrapInterface_ViewModel()
        {
            CommInterface = new BootstrapInterface();

            AvailableBaudRates = new ObservableCollection<uint>();
			AvailableBaudRates.Add(9600);
            AvailableBaudRates.Add(19200);
			AvailableBaudRates.Add(38400);

            //get the last used baud rate
            var previousRate = ECUFlasher.Properties.Settings.Default.DesiredBootModeBaudRate;

            //try to find the last used baud rate in the available rates list, if it isn't there, then use the last baud rate
			DesiredBaudRate = AvailableBaudRates.DefaultIfEmpty(AvailableBaudRates.Last()).FirstOrDefault(baudRate => (baudRate == previousRate));
        }

        public BootstrapInterface BootstrapCommInterface
        {
            get
            {
                return CommInterface as BootstrapInterface;
            }
        }

        public uint DesiredBaudRate
        {
            get { return mDesiredBaudRate; }
            set
            {
                if (mDesiredBaudRate != value)
                {
                    mDesiredBaudRate = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("DesiredBaudRate"));

                    ECUFlasher.Properties.Settings.Default.DesiredBootModeBaudRate = mDesiredBaudRate;
                }
            }
        }
        private uint mDesiredBaudRate;

        private bool DesiredBaudRatePredicate(uint baudRate)
        {
            return (DesiredBaudRate == baudRate);
        }

        public ObservableCollection<uint> AvailableBaudRates { get; private set; }

        #region ConnectCommand
        public ReactiveCommand ConnectCommand
        {
            get
            {
                if (_ConnectCommand == null)
                {
                    _ConnectCommand = new ReactiveCommand(OnBootstrapConnect);
                    _ConnectCommand.Name = "Connect Boot Mode";
                    _ConnectCommand.Description = "Connect using boot mode";
                    _ConnectCommand.AddWatchedProperty(App, "SelectedDeviceInfo");
                    _ConnectCommand.AddWatchedProperty(CommInterface, "ConnectionStatus");

                    _ConnectCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (App.SelectedDeviceInfo == null)
                        {
                            reasonsDisabled.Add("No FTDI USB device selected");
                            result = false;
                        }

                        if (CommInterface.ConnectionStatus != CommunicationInterface.ConnectionStatusType.CommunicationTerminated)
                        {
                            reasonsDisabled.Add("Connection is already open");
                            result = false;
                        }

                        return result;
                    };
                }

                return _ConnectCommand;
            }
        }
        private ReactiveCommand _ConnectCommand;

        private void OnBootstrapConnect()
        {
            bool result = BootstrapCommInterface.OpenConnection(DesiredBaudRate);

            if (!result)
            {
                OnFailedToStartConnecting();
            }
        }
        #endregion

        #region DisconnectCommand
        public ReactiveCommand DisconnectCommand
        {
            get
            {
                if (_DisconnectCommand == null)
                {
                    _DisconnectCommand = new ReactiveCommand(OnDisconnect);
                    _DisconnectCommand.Name = "Disconnect";
                    _DisconnectCommand.Description = "Disconnect from the ECU";
                    _DisconnectCommand.AddWatchedProperty(CommInterface, "ConnectionStatus");
                    _DisconnectCommand.AddWatchedProperty(App, "OperationInProgress");

                    _DisconnectCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (!CommInterface.IsConnected())
                        {
                            reasonsDisabled.Add("Not currently connected");
                            result = false;
                        }

                        if (App.OperationInProgress)
                        {
                            reasonsDisabled.Add("Operation is in progress");
                            result = false;
                        }

                        return result;
                    };
                }

                return _DisconnectCommand;
            }
        }
        private ReactiveCommand _DisconnectCommand;

        private void OnDisconnect()
        {
            BootstrapCommInterface.CloseConnection();
        }
        #endregion
    }
}
