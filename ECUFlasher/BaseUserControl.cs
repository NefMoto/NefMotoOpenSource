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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;

namespace ECUFlasher
{
    public class BaseViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        public BaseViewModel()
        {
            mPropertyErrors = new Dictionary<string, string>();
        }

        //get the error for idataerrorinfo
        public string Error
        {
            get
            {
                string error = null;

                foreach (string propertyName in mPropertyErrors.Keys)
                {
                    error = mPropertyErrors[propertyName];

                    if (error != null)
                    {
                        break;
                    }
                }

                return error;
            }
        }

        public string this[string columnName]
        {
            get
            {
                string error = null;

                if (mPropertyErrors.ContainsKey(columnName))
                {
                    error = mPropertyErrors[columnName];
                }

                return error;
            }
            protected set
            {
                if (!mPropertyErrors.ContainsKey(columnName) || (value != mPropertyErrors[columnName]))
                {
                    mPropertyErrors[columnName] = value;
                    OnPropertyChanged(new PropertyChangedEventArgs(Binding.IndexerName));
                    OnPropertyChanged(new PropertyChangedEventArgs("Error"));
                }
            }
        }
        private Dictionary<string, string> mPropertyErrors;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
            }
        }
    }

    public class BaseUserControl : UserControl, INotifyPropertyChanged, IDataErrorInfo
    {
        public BaseUserControl()
        {
            mPropertyErrors = new Dictionary<string, string>();

			if ((Application.Current != null) && (Application.Current is App))
			{
				App = (App)Application.Current;
			}
        }

        //get the error for idataerrorinfo
        public string Error
        {
            get
            {
                string error = null;

                foreach (string propertyName in mPropertyErrors.Keys)
                {
                    error = mPropertyErrors[propertyName];

                    if (error != null)
                    {
                        break;
                    }
                }

                return error;
            }
        }

        public string this[string columnName]
        {
            get
            {
                string error = null;

                if (mPropertyErrors.ContainsKey(columnName))
                {
                    error = mPropertyErrors[columnName];
                }

                return error;
            }
            protected set
            {
                if (!mPropertyErrors.ContainsKey(columnName) || (value != mPropertyErrors[columnName]))
                {
                    mPropertyErrors[columnName] = value;
                    OnPropertyChanged(new PropertyChangedEventArgs(Binding.IndexerName));
                    OnPropertyChanged(new PropertyChangedEventArgs("Error"));
                }
            }
        }
        private Dictionary<string, string> mPropertyErrors;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
            }
        }

        public static readonly DependencyProperty ControlModeProperty = DependencyProperty.RegisterAttached("ControlMode", typeof(String), typeof(BaseUserControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

        public static void SetControlMode(UIElement element, String value)
        {
            element.SetValue(ControlModeProperty, value);
        }
        public static String GetControlMode(UIElement element)
        {
            return (String)element.GetValue(ControlModeProperty);
        }

		public App App { get; private set; }
    }
}
