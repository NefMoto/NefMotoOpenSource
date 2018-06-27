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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml.Serialization;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Markup;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Reflection;

namespace Shared
{
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

    public class AsStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TimeSpanSecondsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan)
            {
                return ((TimeSpan)value).TotalSeconds;
            }

            return value;//can't convert, let databinding try to handle it
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
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

    public class TimeSpanStringFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
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

    public class HexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DataUtils.WriteHexString(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
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

    public class StringValidConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = null;

            if (value != null)
            {
                strValue = value.ToString();
            }

            if (string.IsNullOrEmpty(strValue))
            {
                return DependencyProperty.UnsetValue;
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsStringNullOrEmpty : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = null;

            if (value != null)
            {
                strValue = value.ToString();
            }

            return string.IsNullOrEmpty(strValue);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DescriptionAttributeConverter : IValueConverter
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

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
			var description = GetDescriptionAttribute(value);

			if (description == null)
			{
				description = DependencyProperty.UnsetValue;
			}

			return description;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

	public class ValueConverterGroup : IValueConverter
	{
		public class ConverterTuple
		{
			public IValueConverter Converter;
			public object ConverterParam;
			public Type TargetType;
		}

		private List<ConverterTuple> Converters
		{
			get
			{
				if (_Converters == null)
				{
					_Converters = new List<ConverterTuple>();
				}

				return _Converters;
			}

			set
			{
				_Converters = value;
			}
		}
		private List<ConverterTuple> _Converters;

		public void AddConverter(IValueConverter converter, object converterParam, Type targetType)
		{
			var tuple = new ConverterTuple();
			tuple.Converter = converter;
			tuple.ConverterParam = converterParam;
			tuple.TargetType = targetType;

			Converters.Add(tuple);
		}

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			object result = value;

			foreach (var tuple in Converters)
			{
				result = tuple.Converter.Convert(result, tuple.TargetType, tuple.ConverterParam, culture);
				
				if (result == Binding.DoNothing)
				{
					// If the converter returns 'DoNothing' then the binding operation should terminate.
					break;
				}
			}

			return result;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class DefaultValueExtension : MarkupExtension
	{
		public DefaultValueExtension()
		{
		}

		public DefaultValueExtension(string path)
		{
			Path = path;
		}
		
		public string Path
		{
			get;
			set;
		}

		public IValueConverter Converter
		{
			get;
			set;
		}

		public object ConverterParam
		{
			get;
			set;
		}

		public override object ProvideValue(IServiceProvider serviceProvider)
		{
			if (serviceProvider != null)
			{
				var valueTargetProvider = (IProvideValueTarget)serviceProvider.GetService(typeof(IProvideValueTarget));

				if (valueTargetProvider != null)
				{
					var ValueConverterGroup = new ValueConverterGroup();

					var defaultConverter = new DefaultValueFromBindingConverter();
					ValueConverterGroup.AddConverter(defaultConverter, Path, typeof(object));

					if(Converter != null)
					{
						ValueConverterGroup.AddConverter(Converter, ConverterParam, typeof(object));
					}

					var binding = new Binding(Path);
					binding.Mode = BindingMode.OneTime;
					binding.Converter = ValueConverterGroup;
					
					var bindingExp = binding.ProvideValue(serviceProvider) as BindingExpression;
					defaultConverter.SourceBinding = bindingExp;

					return bindingExp;
				}
			}

			return DependencyProperty.UnsetValue;
		}
	}
	
	public class DefaultValueFromBindingConverter : IValueConverter
	{
		public BindingExpression SourceBinding
		{
			get;
			set;
		}

		public static object GetDefaultValueAttribute(object source, string propertyPath)
		{
			if ((source != null) && (propertyPath != null))
			{
				PropertyInfo prop = null;
				{
					var type = source.GetType();
					object value = source;

					//TODO: handle other special characters in the property path
					foreach (var part in propertyPath.Split('.'))
					{
						prop = type.GetProperty(part);
						value = prop.GetValue(value, null);

						if (value is IList)
						{
							value = (value as IList)[0];//TODO: handle property path indexers
						}

						type = value.GetType();
					}
				}

				var propertyAttributes = prop.GetCustomAttributes(typeof(DefaultValueAttribute), false);

				if (propertyAttributes.Length > 0)
				{
					return (propertyAttributes[0] as DefaultValueAttribute).Value;
				}
			}

			return DependencyProperty.UnsetValue;
		}

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (parameter is string)
			{
				return GetDefaultValueAttribute(SourceBinding.DataItem, parameter as string);
			}

			return DependencyProperty.UnsetValue;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	[ValueConversion(typeof(byte[]), typeof(string))]
	public class ByteArrayConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (value is byte[])
			{
				var bytes = value as byte[];

				var sb = new StringBuilder(bytes.Length * 2);

				for (int x = 0; x < bytes.Length; x++)
				{
					sb.Append(bytes[x].ToString("X2"));
				}

				return sb.ToString();
			}

			return value;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (value is string)
			{
				var bytes = new List<byte>();

				var strValue = value as string;
				var array = strValue.ToCharArray();

				Debug.Assert(array.Length % 2 == 0);

				for (int x = 0; x < array.Length; x += 2)
				{
					var curStr = array[x].ToString() + array[x + 1].ToString();
					var curByte = Byte.Parse(curStr, NumberStyles.HexNumber);
					bytes.Add(curByte);
				}

				return bytes.ToArray();
			}

			return value;
		}
	}

	[ValueConversion(typeof(byte[]), typeof(BitmapSource))]
	public class ByteArrayToBitmapConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (value is byte[])
			{
				try
				{
					byte[] valueBytes = value as byte[];

					int width = 1024;
					int height = valueBytes.Length / width;

					Debug.Assert((value as byte[]).Length == width * height);

					var format = PixelFormats.Gray8;
					int stride = (width * format.BitsPerPixel + 7) / 8;//as per MSDN

					return BitmapSource.Create(width, height, 96, 96, format, BitmapPalettes.Halftone8, valueBytes, stride);
				}
				catch
				{
				}
			}

			return value;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotSupportedException();
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
    
    /*    
    [ValueConversion(typeof(Boolean), typeof(Boolean))]
    public class BooleanNotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(Boolean)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(Boolean)value;
        }
    }

    [ValueConversion(typeof(object), typeof(Boolean))]
    public class NotEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (parameter != value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(object), typeof(Boolean))]
    public class EqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (parameter == value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(Boolean), typeof(Boolean))]
    public class BooleanAndMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            Boolean result = true;

            foreach (object curValue in values)
            {
                result &= (Boolean)curValue;
            }

            return result;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(Boolean), typeof(Boolean))]
    public class BooleanOrMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            Boolean result = false;

            foreach (object curValue in values)
            {
                result |= (Boolean)curValue;
            }

            return result;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    */

    public class WatchedEntry
    {
        public WatchedEntry(INotifyPropertyChanged owner, string propertyName, INotifyCollectionChanged collection)
        {
            Owner = owner;
            PropertyName = propertyName;
            Collection = collection;
        }

        public INotifyPropertyChanged Owner;
        public string PropertyName;
        public INotifyCollectionChanged Collection;
    }

    //watches for PropertyChanged and CollectionChanged
    public class PropertyChangedWatcher
    {
        public PropertyChangedWatcher()
        {
            WatchedProperties = null;
        }

        public void AddWatchedProperty(INotifyPropertyChanged owner, string propertyName)
        {
            if (WatchedProperties == null)
            {
                WatchedProperties = new List<WatchedEntry>();
            }

            if (owner != null)
            {
                Debug.Assert(owner.GetType().GetMember(propertyName).Any(), "Property does not exist in object");

                owner.PropertyChanged += this.PropertyChangedEventHandler;

                WatchedProperties.Add(new WatchedEntry(owner, propertyName, null));
            }
        }

        public void AddWatchedCollection(INotifyPropertyChanged owner, string propertyName, INotifyCollectionChanged collection)
        {
            if (WatchedCollections == null)
            {
                WatchedCollections = new List<WatchedEntry>();
            }

            if (collection != null)
            {
                collection.CollectionChanged += this.CollectionChangedEventHandler;                
            }

            WatchedCollections.Add(new WatchedEntry(owner, propertyName, collection));

            AddWatchedProperty(owner, propertyName);
        }
               
        private void PropertyChangedEventHandler(object sender, PropertyChangedEventArgs e)
        {
            if ((PropertyChangedCallback != null) && !mIsInCallback)
            {
                mIsInCallback = true;

                foreach (WatchedEntry entry in WatchedProperties)
                {
                    if ( (sender == entry.Owner) && (entry.PropertyName == e.PropertyName) )
                    {                        
                        PropertyChangedCallback();

                        UpdateWatchedCollectionsFromPropertyChange((INotifyPropertyChanged)entry.Owner, entry.PropertyName);

                        break;
                    }
                }

                mIsInCallback = false;
            }
        }

        private void CollectionChangedEventHandler(object sender, NotifyCollectionChangedEventArgs e)
        {
            if ((PropertyChangedCallback != null) && !mIsInCallback)
            {
                mIsInCallback = true;

                foreach (WatchedEntry entry in WatchedCollections)
                {
                    if (sender == entry.Collection)
                    {
                        PropertyChangedCallback();

                        break;
                    }
                }

                mIsInCallback = false;
            }
        }

        private void UpdateWatchedCollectionsFromPropertyChange(INotifyPropertyChanged owner, string propertyName)
        {
            if (WatchedCollections != null)
            {
                foreach (WatchedEntry entry in WatchedCollections)
                {
                    if ((owner == entry.Owner) && (propertyName == entry.PropertyName))
                    {
                        entry.Collection.CollectionChanged -= this.CollectionChangedEventHandler;

                        INotifyCollectionChanged newCollection = (INotifyCollectionChanged)owner.GetType().GetProperty(propertyName).GetValue(owner, null);

                        if (newCollection != null)
                        {
                            newCollection.CollectionChanged += this.CollectionChangedEventHandler;
                        }

                        entry.Collection = newCollection;

                        PropertyChangedCallback();

                        return;
                    }
                }
            }
        }

        private bool mIsInCallback = false;//some property getters cause setters to run that change the value

        public event Action PropertyChangedCallback;

        private List<WatchedEntry> WatchedProperties;
        private List<WatchedEntry> WatchedCollections;
    }

    public delegate void CommandExecuteDelegate(object param);

    public class ReactiveCommand : ICommand, INotifyPropertyChanged
    {
        public delegate bool CanExecuteDelegate(List<string> reasonsDisabled);

        public ReactiveCommand()
            : this((Action)null, null, null)
        {

        }

        public ReactiveCommand(CommandExecuteDelegate execute = null, CanExecuteDelegate canExecute = null, PropertyChangedWatcher propertyWatcher = null)
        {
            ReasonsDisabled = new ObservableCollection<string>();

            CanExecuteMethod = canExecute;
            ExecuteMethod = execute;
            PropertyWatcher = propertyWatcher;

            if (PropertyWatcher != null)
            {
                PropertyWatcher.PropertyChangedCallback += this.WatchedPropertyChangedEventHandler;
            }
        }

        public ReactiveCommand(Action execute = null, CanExecuteDelegate canExecute = null, PropertyChangedWatcher propertyWatcher = null)
            : this(delegate(object param) { if (execute != null) { execute(); } }, canExecute, propertyWatcher)
        {
        }
        
        public void AddWatchedProperty(INotifyPropertyChanged owner, string propertyName)
        {
            if (PropertyWatcher == null)
            {
                PropertyWatcher = new PropertyChangedWatcher();
                PropertyWatcher.PropertyChangedCallback += this.WatchedPropertyChangedEventHandler;
            }

            PropertyWatcher.AddWatchedProperty(owner, propertyName);
        }

        public void AddWatchedCollection(INotifyPropertyChanged owner, string propertyName, INotifyCollectionChanged collection)
        {
            if (PropertyWatcher == null)
            {
                PropertyWatcher = new PropertyChangedWatcher();
                PropertyWatcher.PropertyChangedCallback += this.WatchedPropertyChangedEventHandler;
            }

            PropertyWatcher.AddWatchedCollection(owner, propertyName, collection);
        }

        public bool CanExecute(object parameter)
        {
            if (!mIsInCanExecuteMethod)
            {
                mIsInCanExecuteMethod = true;

                if (CanExecuteMethod != null)
                {
                    List<string> newReasons = new List<string>();
                    IsEnabled = CanExecuteMethod(newReasons);

                    ReasonsDisabled.Clear();
                    foreach (string reason in newReasons)
                    {
                        ReasonsDisabled.Add(reason);
                    }
                }
                else
                {
                    ReasonsDisabled.Clear();
                    IsEnabled = true;
                }

                mIsInCanExecuteMethod = false;
            }
            
            return IsEnabled;
        }
        private bool mIsInCanExecuteMethod = false;

        public void Execute(object parameter)
        {
            if (ExecuteMethod != null)
            {
                ExecuteMethod(parameter);
            }
        }

        public ObservableCollection<string> ReasonsDisabled
        {
            get { return mReasonsDisabled; }
            private set
            {
                if (mReasonsDisabled != value)
                {
                    mReasonsDisabled = value;
                    OnPropertyChanged("ReasonsDisabled");
                }
            }
        }
        private ObservableCollection<string> mReasonsDisabled;
       
        public event EventHandler CanExecuteChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public CommandExecuteDelegate ExecuteMethod;
        public CanExecuteDelegate CanExecuteMethod;

        public bool IsEnabled 
        {
            get { return mIsEnabled; }
            private set
            {
                if (mIsEnabled != value)
                {
                    mIsEnabled = value;
                    OnPropertyChanged("IsEnabled");
                }
            }
        }
        private bool mIsEnabled;

        public string Name 
        {
            get { return mName; }
            set
            {
                if (mName != value)
                {
                    mName = value;
                    OnPropertyChanged("Name");
                }
            }
        }
        private string mName;

        public string Description 
        {
            get { return mDescription; }
            set
            {
                if (mDescription != value)
                {
                    mDescription = value;
                    OnPropertyChanged("Description");
                }
            }
        }
        private string mDescription;

        private void WatchedPropertyChangedEventHandler()
        {
            if (CanExecuteChanged != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    CanExecuteChanged(null, null);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke((Action)this.WatchedPropertyChangedEventHandler, null);
                }
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private PropertyChangedWatcher PropertyWatcher;
    }

    public class CollectionViewSourcePropertyWatcher
    {
        //TODO: we never remove property watchers from the map

        public void AddUniqueChangedPropertyWatcher(object entry, PropertyChangedEventHandler handler)
        {
            mEventHandlerMap.AddUnique(entry, handler);
        }

        private void AttachToViewSourceEntry(object entry)
        {
            var notifyProperty = entry as INotifyPropertyChanged;

            if (notifyProperty != null)
            {
                notifyProperty.PropertyChanged += this.ViewSourceEntryPropertyChanged;
            }
        }

        private void DetachFromViewSourceEntry(object entry)
        {
            var notifyProperty = entry as INotifyPropertyChanged;

            if (notifyProperty != null)
            {
                notifyProperty.PropertyChanged -= this.ViewSourceEntryPropertyChanged;
            }
        }

        private void AttachToViewSource(object source)
        {
            var viewSource = source as INotifyCollectionChanged;

            if (viewSource != null)
            {
                viewSource.CollectionChanged += this.ViewSourceCollectionChanged;
            }

            var collectionSource = source as IEnumerable;

            if (collectionSource != null)
            {
                foreach (INotifyPropertyChanged propertyChanged in collectionSource)
                {
                    AttachToViewSourceEntry(propertyChanged);
                }
            }
        }

        private void DetachFromViewSource(object source)
        {
            var viewSource = source as INotifyCollectionChanged;

            if (viewSource != null)
            {
                viewSource.CollectionChanged -= this.ViewSourceCollectionChanged;
            }

            var collectionSource = source as IEnumerable;

            if (collectionSource != null)
            {
                foreach (INotifyPropertyChanged propertyChanged in collectionSource)
                {
                    DetachFromViewSourceEntry(propertyChanged);
                }
            }
        }

        public void AttachToView(DependencyObject view)
        {
            var collectionViewSource = view as CollectionViewSource;

            if (collectionViewSource != null)
            {
                AttachToViewSource(collectionViewSource.Source);

                //TODO: track if the collection view source changes
            }
        }

        public void DetachFromView(DependencyObject view)
        {
            var collectionViewSource = view as CollectionViewSource;

            if (collectionViewSource != null)
            {
                DetachFromViewSource(collectionViewSource.Source);

                //TODO: untrack if the collection view source changes
            }
        }

        private void ViewSourceEntryPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedArgs)
        {
            PropertyChangedEventHandler handler = mEventHandlerMap.GetHandler(sender);

            if (handler != null)
            {
                //call the delegate via the dispatcher to avoid re-entrancy
                object[] args = { sender, propertyChangedArgs };
                Application.Current.Dispatcher.BeginInvoke(handler, args);
            }
        }

        private void ViewSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs collectionChangedArgs)
        {
            if (collectionChangedArgs.Action == NotifyCollectionChangedAction.Reset)
            {
                //TODO: detach from all entries in the old collection
            }

            if (collectionChangedArgs.OldItems != null)
            {
                foreach (object entry in collectionChangedArgs.OldItems)
                {
                    DetachFromViewSourceEntry(entry);
                }
            }

            if (collectionChangedArgs.NewItems != null)
            {
                foreach (object entry in collectionChangedArgs.NewItems)
                {
                    AttachToViewSourceEntry(entry);
                }
            }
        }

        private class PropertyChangedEventHandlerMap
        {
            public void AddUnique(object entry, PropertyChangedEventHandler handler)
            {
                mMap[entry] = handler;
            }

            public void Remove(object entry)
            {
                mMap[entry] = null;
            }

            public PropertyChangedEventHandler GetHandler(object entry)
            {
                if (mMap.ContainsKey(entry))
                {
                    return mMap[entry];
                }

                return null;
            }

            private Dictionary<object, PropertyChangedEventHandler> mMap = new Dictionary<object, PropertyChangedEventHandler>();
        }

        private PropertyChangedEventHandlerMap mEventHandlerMap = new PropertyChangedEventHandlerMap();

        public static readonly DependencyProperty Property = DependencyProperty.RegisterAttached("Property", typeof(CollectionViewSourcePropertyWatcher), typeof(CollectionViewSourcePropertyWatcher),
            new FrameworkPropertyMetadata(null, CollectionViewSourcePropertyWatcherChanged));

        private static void CollectionViewSourcePropertyWatcherChanged(DependencyObject depObj, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue != e.OldValue)
            {
                var oldWatcher = e.OldValue as CollectionViewSourcePropertyWatcher;

                if (oldWatcher != null)
                {
                    oldWatcher.DetachFromView(depObj);
                }

                var newWatcher = e.NewValue as CollectionViewSourcePropertyWatcher;

                if (newWatcher != null)
                {
                    newWatcher.AttachToView(depObj);
                }
            }
        }

        public static void SetProperty(DependencyObject depObj, CollectionViewSourcePropertyWatcher value)
        {
            depObj.SetValue(Property, value);
        }

        public static CollectionViewSourcePropertyWatcher GetProperty(DependencyObject depObj)
        {
            var objList = depObj.GetValue(Property) as CollectionViewSourcePropertyWatcher;

            if (objList == null)
            {
                objList = new CollectionViewSourcePropertyWatcher();

                SetProperty(depObj, objList);
            }

            return objList;
        }
    }

    public static class InputBindingsHelper
    {
        public static readonly DependencyProperty UpdatePropertySourceWhenEnterPressedProperty = DependencyProperty.RegisterAttached(
            "UpdatePropertySourceWhenEnterPressed", typeof(DependencyProperty), typeof(InputBindingsHelper), new PropertyMetadata(null, OnUpdatePropertySourceWhenEnterPressedPropertyChanged));

        public static void SetUpdatePropertySourceWhenEnterPressed(DependencyObject dp, DependencyProperty value)
        {
            dp.SetValue(UpdatePropertySourceWhenEnterPressedProperty, value);
        }

        public static DependencyProperty GetUpdatePropertySourceWhenEnterPressed(DependencyObject dp)
        {
            return (DependencyProperty)dp.GetValue(UpdatePropertySourceWhenEnterPressedProperty);
        }

        private static void OnUpdatePropertySourceWhenEnterPressedPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            var element = dp as UIElement;

            if (element == null)
            {
                return;
            }

            if (e.OldValue != null)
            {
                element.PreviewKeyDown -= HandlePreviewKeyDown;
            }

            if (e.NewValue != null)
            {
                element.PreviewKeyDown += HandlePreviewKeyDown;
            }
        }

        static void HandlePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DoUpdateSource(e.Source);
            }
        }

        static void DoUpdateSource(object source)
        {
            var property = GetUpdatePropertySourceWhenEnterPressed(source as DependencyObject);

            if (property == null)
            {
                return;
            }

            var elt = source as UIElement;

            if (elt == null)
            {
                return;
            }

            var binding = BindingOperations.GetBindingExpression(elt, property);

            if (binding != null)
            {
                binding.UpdateSource();
            }
        }
    }

    public static class ViewSourceHelper
    {
        public static readonly DependencyProperty AutoSelectNewItemsProperty = DependencyProperty.RegisterAttached(
            "AutoSelectNewItems", typeof(bool), typeof(ViewSourceHelper), new PropertyMetadata(false, OnUpdateAutoSelectNewItemsPropertyChanged));

        public static void SetAutoSelectNewItems(DependencyObject dp, bool value)
        {
            dp.SetValue(AutoSelectNewItemsProperty, value);
        }

        public static bool GetAutoSelectNewItems(DependencyObject dp)
        {
            return (bool)dp.GetValue(AutoSelectNewItemsProperty);
        }

        private static void OnUpdateAutoSelectNewItemsPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            var viewSource = dp as CollectionViewSource;

            if (viewSource == null)
            {
                return;
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(CollectionViewSource.SourceProperty, typeof(CollectionViewSource));

            if (descriptor != null)
            {
                EventHandler handler = delegate
                {
                    AttachToSource(viewSource);
                };

                descriptor.AddValueChanged(viewSource, handler);
            }

            AttachToSource(viewSource);
        }

        private static void AttachToSource(CollectionViewSource source)
        {
            if (source.View == null)
            {
                return;
            }

            NotifyCollectionChangedEventHandler CollectionChangedHandler = delegate(object sender, NotifyCollectionChangedEventArgs collectionChangedArgs)
            {
                if (collectionChangedArgs.NewItems != null)
                {
                    source.View.MoveCurrentTo(collectionChangedArgs.NewItems[collectionChangedArgs.NewItems.Count - 1]);
                }
            };

            if (GetAutoSelectNewItems(source))
            {
                source.View.CollectionChanged += CollectionChangedHandler;
            }
            else
            {
                source.View.CollectionChanged -= CollectionChangedHandler;
            }
        }
    }

    public static class ListBoxHelper
    {
        public static readonly DependencyProperty AutoScrollToCurrentItemProperty = DependencyProperty.RegisterAttached(
            "AutoScrollToCurrentItem", typeof(bool), typeof(ListBoxHelper), new PropertyMetadata(false, OnUpdateAutoScrollToCurrentItemPropertyChanged));

        public static void SetAutoScrollToCurrentItem(DependencyObject dp, bool value)
        {
            dp.SetValue(AutoScrollToCurrentItemProperty, value);
        }

        public static bool GetAutoScrollToCurrentItem(DependencyObject dp)
        {
            return (bool)dp.GetValue(AutoScrollToCurrentItemProperty);
        }

        private static void OnUpdateAutoScrollToCurrentItemPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            var listBox = dp as ListBox;

            if (listBox != null)
            {
                var listBoxItems = listBox.Items;

                if (listBoxItems != null)
                {
                    EventHandler handler = delegate
                    {
                        OnAutoScrollToCurrentItem(listBox, listBox.Items.CurrentPosition);
                    };

                    if ((bool)e.NewValue == true)
                    {
                        listBoxItems.CurrentChanged += handler;
                    }
                    else
                    {
                        listBoxItems.CurrentChanged -= handler;
                    }
                }
            }
        }

        private static void OnAutoScrollToCurrentItem(ListBox listBox, int index)
        {
            if ((listBox != null) && (listBox.Items != null) && (listBox.Items.Count > index) && (index > 0))
            {
                listBox.ScrollIntoView(listBox.Items[index]);
            }
        }
    }

    public static class AutoScrollTextBox
    {
        public static readonly DependencyProperty AutoScrollTextBoxProperty = DependencyProperty.RegisterAttached(
            "AutoScrollTextBox", typeof(bool), typeof(AutoScrollTextBox), new PropertyMetadata(false, OnUpdateAutoScrollPropertyChanged));

        public static void SetAutoScrollTextBox(DependencyObject dp, bool value)
        {
            dp.SetValue(AutoScrollTextBoxProperty, value);
        }

        public static bool GetAutoScrollTextBox(DependencyObject dp)
        {
            return (bool)dp.GetValue(AutoScrollTextBoxProperty);
        }

        private static void OnUpdateAutoScrollPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            var textBox = dp as TextBoxBase;

            if (textBox != null)
            {
                TextChangedEventHandler handler = delegate
                {
                    OnAutoScroll(textBox);
                };

                if ((bool)e.NewValue == true)
                {
                    textBox.TextChanged += handler;
                }
                else
                {
                    textBox.TextChanged -= handler;
                }
            }
        }

        private static void OnAutoScroll(TextBoxBase textBox)
        {
            if (textBox != null)
            {
                textBox.ScrollToEnd();                
            }
        }
    }
}