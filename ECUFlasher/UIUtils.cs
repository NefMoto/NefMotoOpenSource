using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml.Serialization;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Reflection;

using Communication;

namespace ECUFlasher
{
    [ValueConversion(typeof(UInt32), typeof(Boolean))]
    public class IsBaudRateUndefinedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((value is string) && String.IsNullOrEmpty(value as string))
            {
                return false;
            }

            uint baud = (uint)System.Convert.ChangeType(value, typeof(uint));

            return baud == (uint)KWP2000BaudRates.BAUD_UNSPECIFIED;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(CommunicationInterface.ConnectionStatusType), typeof(Boolean))]
    public class IsConnectionOpenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {            
            CommunicationInterface.ConnectionStatusType connectionStatus = (CommunicationInterface.ConnectionStatusType)value;
            bool unsetIfTrue = ((parameter != null) && (parameter is string) && ((string)parameter == "UnsetIfTrue"));

            bool connected = (connectionStatus != CommunicationInterface.ConnectionStatusType.Disconnected) && (connectionStatus != CommunicationInterface.ConnectionStatusType.CommunicationTerminated);

            if (connected)
            {
                if (unsetIfTrue)
                {
                    return DependencyProperty.UnsetValue;
                }
            }

            return connected;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(CommunicationInterface.ConnectionStatusType), typeof(Boolean))]
    public class IsConnectionNotOpenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {            
            CommunicationInterface.ConnectionStatusType connectionStatus = (CommunicationInterface.ConnectionStatusType)value;
            bool unsetIfTrue = ((parameter != null) && (parameter is string) && ((string)parameter == "UnsetIfTrue"));

            bool disconnected = (connectionStatus == CommunicationInterface.ConnectionStatusType.Disconnected) || (connectionStatus == CommunicationInterface.ConnectionStatusType.CommunicationTerminated);

            if (disconnected)
            {
                if (unsetIfTrue)
                {
                    return DependencyProperty.UnsetValue;
                }
            }

            return disconnected;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}