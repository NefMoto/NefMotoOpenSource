using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;

using Communication;

namespace ECUFlasher
{
    /// <summary>
    /// Interaction logic for SettingsControl.xaml
    /// </summary>
    public partial class SettingsControl : BaseUserControl, INotifyPropertyChanged
    {
        public SettingsControl()
        {
            if ((Application.Current != null) && (Application.Current is App))
            {
                App = (App)Application.Current;
            }

            if (App != null)
            {
                App.KWP2000CommInterface.PropertyChanged += CommInterfacePropertyChanged;

                CopyDefaultTimings();
            }

            InitializeComponent();
        }

        public App App { get; private set; }

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
            if (App != null)
            {
                TimingParameters currentTiming = App.KWP2000CommInterface.DefaultTimingParameters;
                DefaultP1ECUInterByteTimeMaxMS = currentTiming.P1ECUInterByteTimeMaxMs;
                DefaultP2ECUResponseTimeMinMS = currentTiming.P2ECUResponseTimeMinMs;
                DefaultP2ECUResponseTimeMaxMS = currentTiming.P2ECUResponseTimeMaxMs;
                DefaultP3TesterResponseTimeMinMS = currentTiming.P3TesterResponseTimeMinMs;
                DefaultP3TesterResponseTimeMaxMS = currentTiming.P3TesterResponseTimeMaxMs;
                DefaultP4TesterInterByteTimeMinMS = currentTiming.P4TesterInterByteTimeMinMs;
            }
        }

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
                        TimingParameters currentParams = App.KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P1ECUInterByteTimeMaxMs != _DefaultP1ECUInterByteTimeMaxMS)
                        {
                            currentParams.P1ECUInterByteTimeMaxMs = _DefaultP1ECUInterByteTimeMaxMS;
                            App.KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP1ECUInterByteTimeMaxMS;

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
                        TimingParameters currentParams = App.KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P2ECUResponseTimeMinMs != _DefaultP2ECUResponseTimeMinMS)
                        {
                            currentParams.P2ECUResponseTimeMinMs = _DefaultP2ECUResponseTimeMinMS;
                            App.KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP2ECUResponseTimeMinMS;

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
                        TimingParameters currentParams = App.KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P2ECUResponseTimeMaxMs != _DefaultP2ECUResponseTimeMaxMS)
                        {
                            currentParams.P2ECUResponseTimeMaxMs = _DefaultP2ECUResponseTimeMaxMS;
                            App.KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP2ECUResponseTimeMaxMS;

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
                        TimingParameters currentParams = App.KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P3TesterResponseTimeMinMs != _DefaultP3TesterResponseTimeMinMS)
                        {
                            currentParams.P3TesterResponseTimeMinMs = _DefaultP3TesterResponseTimeMinMS;
                            App.KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP3TesterResponseTimeMinMS;

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
                        TimingParameters currentParams = App.KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P3TesterResponseTimeMaxMs != _DefaultP3TesterResponseTimeMaxMS)
                        {
                            currentParams.P3TesterResponseTimeMaxMs = _DefaultP3TesterResponseTimeMaxMS;
                            App.KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP3TesterResponseTimeMaxMS;

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
                        TimingParameters currentParams = App.KWP2000CommInterface.DefaultTimingParameters;

                        if (currentParams.P4TesterInterByteTimeMinMs != _DefaultP4TesterInterByteTimeMinMS)
                        {
                            currentParams.P4TesterInterByteTimeMinMs = _DefaultP4TesterInterByteTimeMinMS;
                            App.KWP2000CommInterface.DefaultTimingParameters = currentParams;
                        }
                    }
                }
            }
        }
        private long _DefaultP4TesterInterByteTimeMinMS;        
    }
}
