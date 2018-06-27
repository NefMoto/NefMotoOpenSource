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
using System.Windows.Threading;

using Shared;
using ApplicationShared;
using Communication;
using FTD2XX_NET;

namespace ECUFlasher
{
    public class KWP2000Interface_ViewModel : CommunicationInterface_ViewModel
    {
        public enum ConnectionMethod : uint
        {
            [Description("Slow Init")]
            SlowInit = 0,
            [Description("Fast Init")]
            FastInit
        }

        public KWP2000Interface_ViewModel()
        {
            CommInterface = new KWP2000Interface();

            CommInterface.PropertyChanged += CommInterfacePropertyChanged;
            CopyDefaultTimings();

			CommInterface.ConnectionStatusChangedEvent += ConnectionStatusChangedEvent;

			var defaultBaudRates = ECUFlasher.Properties.Settings.Default.SupportedKWP2000BaudRates;

			var savedAvailableRates = new List<uint>();

			if (defaultBaudRates != null)
			{
				foreach (var rateObj in defaultBaudRates)
				{
					if (rateObj is uint)
					{
						var rate = (uint)rateObj;

						if (!savedAvailableRates.Contains(rate))
						{
							savedAvailableRates.Add(rate);
						}
					}
				}
			}

			savedAvailableRates.Sort();

			AvailableBaudRates = new ObservableCollection<uint>(savedAvailableRates);

			if (!AvailableBaudRates.Any())
			{
				ResetAvailableBaudRatesToDefault();
				SaveAvailableBaudRates();
			}

			//get the last used baud rate
			var previousRate = ECUFlasher.Properties.Settings.Default.DesiredKWP2000BaudRate;

			//try to find the last used baud rate in the available rates list, if it isn't there, then use the last baud rate
			if (AvailableBaudRates.Contains(previousRate))
			{
				DesiredBaudRate = previousRate;
			}
			else
			{
				DesiredBaudRate = AvailableBaudRates.Last();
			}

            //get the last used connection method
            DesiredConnectionMethod = ECUFlasher.Properties.Settings.Default.DesiredKWP2000ConnectionMethod;
        }

		void ConnectionStatusChangedEvent(CommunicationInterface commInterface, CommunicationInterface.ConnectionStatusType status, bool willReconnect)
		{
		}

        public KWP2000Interface KWP2000CommInterface
        {
            get
            {
                return CommInterface as KWP2000Interface;
            }
        }

        #region ConnectionSettings

        public ObservableCollection<uint> AvailableBaudRates { get; private set; }

		private void ResetAvailableBaudRatesToDefault()
		{
			AvailableBaudRates.Clear();

			foreach (uint baudRate in Enum.GetValues(typeof(KWP2000BaudRates)))
			{
				if (!AvailableBaudRates.Contains(baudRate))
				{
					AvailableBaudRates.Add(baudRate);
				}
			}
		}

		private void SaveAvailableBaudRates()
		{
			if (ECUFlasher.Properties.Settings.Default.SupportedKWP2000BaudRates == null)
			{
				ECUFlasher.Properties.Settings.Default.SupportedKWP2000BaudRates = new System.Collections.ArrayList();
			}

			ECUFlasher.Properties.Settings.Default.SupportedKWP2000BaudRates.Clear();

			foreach (var rate in AvailableBaudRates)
			{
				ECUFlasher.Properties.Settings.Default.SupportedKWP2000BaudRates.Add(rate);
			}
		}

		public uint DesiredBaudRate
		{
			get
			{
				return _DesiredBaudRate;
			}
			set
			{
				if (_DesiredBaudRate != value)
				{
					_DesiredBaudRate = value;

					ECUFlasher.Properties.Settings.Default.DesiredKWP2000BaudRate = DesiredBaudRate;

					DesiredBaudRates = new List<uint>(AvailableBaudRates.Where(baudRate => (baudRate <= _DesiredBaudRate)).Reverse());

					OnPropertyChanged(new PropertyChangedEventArgs("DesiredBaudRate"));
				}
			}
		}
		private uint _DesiredBaudRate;

		public List<uint> DesiredBaudRates { get; private set; }
		
        public ConnectionMethod DesiredConnectionMethod
        {
            get { return _DesiredConnectionMethod; }
            set
            {
                if (_DesiredConnectionMethod != value)
                {
                    _DesiredConnectionMethod = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("DesiredConnectionMethod"));

                    ECUFlasher.Properties.Settings.Default.DesiredKWP2000ConnectionMethod = _DesiredConnectionMethod;
                }
            }
        }
        private ConnectionMethod _DesiredConnectionMethod = ConnectionMethod.SlowInit;

		private const byte ConnectAddressDefaultValue = KWP2000Interface.DEFAULT_ECU_FASTINIT_KWP2000_PHYSICAL_ADDRESS;
		[DefaultValue(ConnectAddressDefaultValue)]
        public byte ConnectAddress
        {
            get
            {
                return mConnectAddress;
            }
            set
            {
                if (mConnectAddress != value)
                {
                    mConnectAddress = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("ConnectAddress"));
                }
            }
        }
		private byte mConnectAddress = ConnectAddressDefaultValue;

		private const KWP2000AddressMode ConnectAddressModeDefaultValue = KWP2000AddressMode.Physical;
		[DefaultValue(ConnectAddressModeDefaultValue)]
        public KWP2000AddressMode ConnectAddressMode
        {
            get
            {
                return _ConnectAddressMode;
            }
            set
            {
                if (_ConnectAddressMode != value)
                {
                    _ConnectAddressMode = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("ConnectAddressMode"));
                }
            }
        }
		private KWP2000AddressMode _ConnectAddressMode = ConnectAddressModeDefaultValue;
        #endregion

		#region DefaultTimingParameters
        private void CommInterfacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            //TODO: make this less error prone, doing a string compare is weak
            if (e.PropertyName == "DefaultTimingParameters")
            {
                CopyDefaultTimings();
            }
        }

        private void CopyDefaultTimings()
        {
            var currentTiming = KWP2000CommInterface.DefaultTimingParameters;
            DefaultP1ECUInterByteTimeMaxMS = currentTiming.P1ECUInterByteTimeMaxMs;
            DefaultP2ECUResponseTimeMinMS = currentTiming.P2ECUResponseTimeMinMs;
            DefaultP2ECUResponseTimeMaxMS = currentTiming.P2ECUResponseTimeMaxMs;
            DefaultP3TesterResponseTimeMinMS = currentTiming.P3TesterResponseTimeMinMs;
            DefaultP3TesterResponseTimeMaxMS = currentTiming.P3TesterResponseTimeMaxMs;
            DefaultP4TesterInterByteTimeMinMS = currentTiming.P4TesterInterByteTimeMinMs;
        }

		//TODO: need to add a default value
        public long DefaultP1ECUInterByteTimeMaxMS
        {
            get
            {
                return _DefaultP1ECUInterByteTimeMaxMS;
            }
            set
            {
                if (_DefaultP1ECUInterByteTimeMaxMS != value)
                {
                    _DefaultP1ECUInterByteTimeMaxMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("DefaultP1ECUInterByteTimeMaxMS"));

                    if (App != null)
                    {
                        TimingParameters currentParams = KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P1ECUInterByteTimeMaxMs != _DefaultP1ECUInterByteTimeMaxMS)
                        {
                            currentParams.P1ECUInterByteTimeMaxMs = _DefaultP1ECUInterByteTimeMaxMS;
                            KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP1ECUInterByteTimeMaxMS;

		//TODO: need to add a default value
        public long DefaultP2ECUResponseTimeMinMS
        {
            get
            {
                return _DefaultP2ECUResponseTimeMinMS;
            }
            set
            {
                if (_DefaultP2ECUResponseTimeMinMS != value)
                {
                    _DefaultP2ECUResponseTimeMinMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("DefaultP2ECUResponseTimeMinMS"));

                    if (App != null)
                    {
                        TimingParameters currentParams = KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P2ECUResponseTimeMinMs != _DefaultP2ECUResponseTimeMinMS)
                        {
                            currentParams.P2ECUResponseTimeMinMs = _DefaultP2ECUResponseTimeMinMS;
                            KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP2ECUResponseTimeMinMS;

		//TODO: need to add a default value
        public long DefaultP2ECUResponseTimeMaxMS
        {
            get
            {
                return _DefaultP2ECUResponseTimeMaxMS;
            }
            set
            {
                if (_DefaultP2ECUResponseTimeMaxMS != value)
                {
                    _DefaultP2ECUResponseTimeMaxMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("DefaultP2ECUResponseTimeMaxMS"));

                    if (App != null)
                    {
                        TimingParameters currentParams = KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P2ECUResponseTimeMaxMs != _DefaultP2ECUResponseTimeMaxMS)
                        {
                            currentParams.P2ECUResponseTimeMaxMs = _DefaultP2ECUResponseTimeMaxMS;
                            KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP2ECUResponseTimeMaxMS;

		//TODO: need to add a default value
        public long DefaultP3TesterResponseTimeMinMS
        {
            get
            {
                return _DefaultP3TesterResponseTimeMinMS;
            }
            set
            {
                if (_DefaultP3TesterResponseTimeMinMS != value)
                {
                    _DefaultP3TesterResponseTimeMinMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("DefaultP3TesterResponseTimeMinMS"));

                    if (App != null)
                    {
                        TimingParameters currentParams = KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P3TesterResponseTimeMinMs != _DefaultP3TesterResponseTimeMinMS)
                        {
                            currentParams.P3TesterResponseTimeMinMs = _DefaultP3TesterResponseTimeMinMS;
                            KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP3TesterResponseTimeMinMS;

		//TODO: need to add a default value
        public long DefaultP3TesterResponseTimeMaxMS
        {
            get
            {
                return _DefaultP3TesterResponseTimeMaxMS;
            }
            set
            {
                if (_DefaultP3TesterResponseTimeMaxMS != value)
                {
                    _DefaultP3TesterResponseTimeMaxMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("DefaultP3TesterResponseTimeMaxMS"));

                    if (App != null)
                    {
                        TimingParameters currentParams = KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P3TesterResponseTimeMaxMs != _DefaultP3TesterResponseTimeMaxMS)
                        {
                            currentParams.P3TesterResponseTimeMaxMs = _DefaultP3TesterResponseTimeMaxMS;
                            KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP3TesterResponseTimeMaxMS;

		//TODO: need to add a default value
        public long DefaultP4TesterInterByteTimeMinMS
        {
            get
            {
                return _DefaultP4TesterInterByteTimeMinMS;
            }
            set
            {
                if (_DefaultP4TesterInterByteTimeMinMS != value)
                {
                    _DefaultP4TesterInterByteTimeMinMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("DefaultP4TesterInterByteTimeMinMS"));

                    if (App != null)
                    {
                        TimingParameters currentParams = KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P4TesterInterByteTimeMinMs != _DefaultP4TesterInterByteTimeMinMS)
                        {
                            currentParams.P4TesterInterByteTimeMinMs = _DefaultP4TesterInterByteTimeMinMS;
                            KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP4TesterInterByteTimeMinMS;
		#endregion

		#region SecuritySettings

		private const byte SeedRequestDefaultValue = SecurityAccessAction.DEFAULT_REQUEST_SEED;
		[DefaultValue(SeedRequestDefaultValue)]
		public byte SeedRequest
		{
			get { return _SeedRequest; }
			set
			{
				if (_SeedRequest != value)
				{
					_SeedRequest = value;
					OnPropertyChanged(new PropertyChangedEventArgs("SeedRequest"));
				}
			}
		}
		private byte _SeedRequest = SeedRequestDefaultValue;

		private const bool ShouldUseExtendedSeedRequestDefaultValue = false;
		[DefaultValue(ShouldUseExtendedSeedRequestDefaultValue)]
		public bool ShouldUseExtendedSeedRequest
		{
			get { return _ShouldUseExtendedSeedRequest; }
			set
			{
				if (_ShouldUseExtendedSeedRequest != value)
				{
					_ShouldUseExtendedSeedRequest = value;
					OnPropertyChanged(new PropertyChangedEventArgs("ShouldUseExtendedSeedRequest"));
				}
			}
		}
		private bool _ShouldUseExtendedSeedRequest = ShouldUseExtendedSeedRequestDefaultValue;

		private const bool ShouldSupportSpecialKeyDefaultValue = false;
		[DefaultValue(ShouldSupportSpecialKeyDefaultValue)]
		public bool ShouldSupportSpecialKey
		{
			get { return _ShouldSupportSpecialKey; }
			set
			{
				if (_ShouldSupportSpecialKey != value)
				{
					_ShouldSupportSpecialKey = value;
					OnPropertyChanged(new PropertyChangedEventArgs("ShouldSupportSpecialKey"));
				}
			}
		}
		private bool _ShouldSupportSpecialKey = ShouldSupportSpecialKeyDefaultValue;

		#endregion

		#region ConnectCommands
		private bool ConnectCommandCanExecute(List<string> reasonsDisabled)
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
        }

        public ReactiveCommand ConnectSlowInitCommand
        {
            get
            {
                if (_ConnectSlowInitCommand == null)
                {
                    _ConnectSlowInitCommand = new ReactiveCommand(OnSlowInitConnect, ConnectCommandCanExecute);
                    _ConnectSlowInitCommand.Name = "Connect Slow Init";
                    _ConnectSlowInitCommand.Description = "Connect using slow init";
                    _ConnectSlowInitCommand.AddWatchedProperty(App, "SelectedDeviceInfo");
                    _ConnectSlowInitCommand.AddWatchedProperty(CommInterface, "ConnectionStatus");
                }

                return _ConnectSlowInitCommand;
            }
        }
        private ReactiveCommand _ConnectSlowInitCommand;

        private void OnSlowInitConnect()
        {
            bool result = KWP2000CommInterface.ConnectToECUSlowInit(ConnectAddress);

            if (!result)
            {
                OnFailedToStartConnecting();
            }
        }

        public ReactiveCommand ConnectFastInitCommand
        {
            get
            {
                if (_ConnectFastInitCommand == null)
                {
                    _ConnectFastInitCommand = new ReactiveCommand(OnFastInitConnect, ConnectCommandCanExecute);
                    _ConnectFastInitCommand.Name = "Connect Fast Init";
                    _ConnectFastInitCommand.Description = "Connect using fast init";
                    _ConnectFastInitCommand.AddWatchedProperty(App, "SelectedDeviceInfo");
                    _ConnectFastInitCommand.AddWatchedProperty(CommInterface, "ConnectionStatus");
                }

                return _ConnectFastInitCommand;
            }
        }
        private ReactiveCommand _ConnectFastInitCommand;

        private void OnFastInitConnect()
        {
            bool result = KWP2000CommInterface.ConnectToECUFastInit(ConnectAddressMode, ConnectAddress);

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
            KWP2000CommInterface.DisconnectFromECU();
        }
        #endregion
	}
}
