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

using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Shared
{
    public static class BindingSentinels
    {
        public static readonly object UnsetValue = new object();
    }

    public class ExtensionFixer
    {
        public static string SwitchToLongExtension(string fileName, string shortExt, string longExt)
        {
            var convertedFileName = fileName;

            if (!fileName.EndsWith(longExt, StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(shortExt, StringComparison.OrdinalIgnoreCase))
            {
                convertedFileName = fileName.Replace(shortExt, longExt);
            }

            return convertedFileName;
        }
    }

    public static class AsStringConverterLogic
    {
        public static object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((value is string) || (value == null))
            {
                return value;
            }

            if ((parameter == null) || !(parameter is string))
            {
                return value.ToString();
            }
            else
            {
                return string.Format(parameter as string, value);
            }
        }
    }

    public static class TimeSpanSecondsConverterLogic
    {
        public static object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan)
            {
                return ((TimeSpan)value).TotalSeconds;
            }

            return value;//can't convert, let databinding try to handle it
        }

        public static object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return TimeSpan.FromSeconds((double)value);
            }
            catch
            {
                return value;//can't convert, let databinding try to handle it
            }
        }
    }

    public static class TimeSpanStringFormatConverterLogic
    {
        public static object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan)
            {
                var builder = new StringBuilder();

                var timeSpan = (TimeSpan)value;

                builder.AppendFormat("{0:D2}", timeSpan.Hours);
                builder.AppendFormat(":{0:D2}", timeSpan.Minutes);
                builder.AppendFormat(":{0:D2}", timeSpan.Seconds);
                builder.AppendFormat(".{0:D3}", timeSpan.Milliseconds);

                return builder.ToString();
            }

            return value;//can't convert, let databinding try to handle it
        }

        public static object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var converter = new TimeSpanConverter();
                return converter.ConvertFrom(value);
            }
            catch
            {
                return value;//can't convert, let databinding try to handle it
            }
        }
    }

    public static class HexConverterLogic
    {
        public static object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DataUtils.WriteHexString(value);
        }

        public static object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                if (value is string)
                {
                    var valueString = value as string;
                    return DataUtils.ReadHexString(valueString);
                }
            }

            return value;//can't convert, let databinding try to handle it
        }
    }

    public static class StringValidConverterLogic
    {
        public static object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = null;

            if (value != null)
            {
                strValue = value.ToString();
            }

            if (string.IsNullOrEmpty(strValue))
            {
                return BindingSentinels.UnsetValue;
            }

            if (targetType == typeof(Boolean))
            {
                return true;
            }
            else
            {
                return strValue;
            }
        }
    }

    public static class DescriptionAttributeConverterLogic
    {
        public static object GetDescriptionAttribute(object value)
        {
            if (value != null)
            {
                var fieldInfo = value.GetType().GetField(value.ToString());

                if (fieldInfo != null)
                {
                    var attributes = (DescriptionAttribute[])fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);

                    if (attributes.Length > 0)
                    {
                        return attributes[0].Description;
                    }
                }
            }

            return null;
        }

        public static object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var description = GetDescriptionAttribute(value);

            if (description == null)
            {
                description = BindingSentinels.UnsetValue;
            }

            return description;
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class ValueMappedDescriptionAttribute : Attribute
    {
        public ValueMappedDescriptionAttribute(object value, string description)
        {
            Value = value;
            Description = description;
        }

        public object Value
        {
            get;
            set;
        }

        public string Description
        {
            get;
            set;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
