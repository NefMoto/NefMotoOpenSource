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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using System.Globalization;
using Microsoft.Win32;

using Communication;
using Shared;
using FTD2XX_NET;
using ApplicationShared;

namespace ECUFlasher
{
    [XmlInclude(typeof(MemoryVariableDefinition_Serialized))]
    [XmlType("VariableDefinition")]
    public abstract class VariableDefinitionBase_Serialized
    {
        public string VariableID { get; set; }//unique identifier of the variable
        public string Name { get; set; }
        public string Units { get; set; }
        public string Description { get; set; }

        [XmlElement(ElementName = "ScaleOffsetMemoryVariableValueConverter", Type = typeof(ScaleOffsetMemoryVariableValueConverter))]
        public VariableValueConverter ValueConverter { get; set; }
    }

    [XmlType("MemoryVariableDefinition")]
    public class MemoryVariableDefinition_Serialized : VariableDefinitionBase_Serialized
    {        
        public string Address { get; set; }
        public DataUtils.DataType DataType { get; set; }        
    }

    [XmlInclude(typeof(ReadMemoryVariable_Serialized))]
    [XmlType("ReadVariable")]
    public class ReadVariableBase_Serialized
    {
        public string VariableID { get; set; }//unique identifier of the variable
    }

    [XmlType("ReadMemoryVariable")]
    public class ReadMemoryVariable_Serialized : ReadVariableBase_Serialized
    {

    }

	[XmlInclude(typeof(MemoryVariableLogEntry_Serialized))]
	[XmlInclude(typeof(MultiVariableLogEntry_Serialized))]
	[XmlType("BaseLogEntry")]
	public class BaseLogEntry_Serialized
	{
		public DateTime TimeStamp { get; set; }
	}

	[XmlType("MemoryVariableLogEntry")]
	public class MemoryVariableLogEntry_Serialized : BaseLogEntry_Serialized
	{
		public string VariableID { get; set; }
		public byte[] RawData { get; set; }
	}

	[XmlType("MultiVariableLogEntry")]
	public class MultiVariableLogEntry_Serialized : BaseLogEntry_Serialized
	{
		public MultiVariableLogEntry_Serialized()
		{
			EntryItems = new List<BaseVariable_LogEntryItem>();
		}

		[XmlArray]
		[XmlArrayItem(ElementName = "MemoryVariableLogEntryItem", Type = typeof(MemoryVariable_LogEntryItem))]
		public List<BaseVariable_LogEntryItem> EntryItems { get; set; }
	}

    [XmlType("VariableLogEntry")]
	public class VariableLogEntry
    {
		public virtual bool LoadSerialized(BaseLogEntry_Serialized serializedEntry)
        {
            TimeStamp = serializedEntry.TimeStamp;

			if (serializedEntry is MultiVariableLogEntry_Serialized)
			{
				var multiEntry = serializedEntry as MultiVariableLogEntry_Serialized;

				foreach (var item in multiEntry.EntryItems)
				{
					//TODO: use a factory or something
					var newItem = new MemoryVariable_LogEntryItem();
					newItem.LoadSerialized(item);

					EntryItems.Add(newItem);
				}
			}
			else if (serializedEntry is MemoryVariableLogEntry_Serialized)
			{
				var memEntry = serializedEntry as MemoryVariableLogEntry_Serialized;

				//TODO: use a factory or something
				var newItem = new MemoryVariable_LogEntryItem();
				newItem.VariableID = memEntry.VariableID;
				newItem.RawData = memEntry.RawData;

				EntryItems.Add(newItem);
			}

            return true;
        }

		public BaseLogEntry_Serialized SaveSerialized()
		{
			var serializedData = new MultiVariableLogEntry_Serialized();
		    SaveSerializedInteral(serializedData);
		    return serializedData;
		}

		protected virtual void SaveSerializedInteral(BaseLogEntry_Serialized serializedDataOut)
		{
			serializedDataOut.TimeStamp = TimeStamp;

			if (serializedDataOut is MultiVariableLogEntry_Serialized)
			{
				var serializedMemDataOut = serializedDataOut as MultiVariableLogEntry_Serialized;

				foreach (var item in EntryItems)
				{
					serializedMemDataOut.EntryItems.Add(item.SaveSerialized());
				}
			}
		}

        public DateTime TimeStamp { get; set; }//time of the entry

		[XmlArray]
		[XmlArrayItem(ElementName = "MemoryVariableLogEntryItem", Type = typeof(MemoryVariable_LogEntryItem))]
		public List<BaseVariable_LogEntryItem> EntryItems 
		{
			get
			{
				if (_EntryItems == null)
				{
					_EntryItems = new List<BaseVariable_LogEntryItem>();
				}

				return _EntryItems;
			}
			set
			{
				_EntryItems = value;
			}
		}
		private List<BaseVariable_LogEntryItem> _EntryItems;
    }
    
	[XmlInclude(typeof(MemoryVariable_LogEntryItem))]
	[XmlType("VariableLogEntryItem")]
	public abstract class BaseVariable_LogEntryItem
	{
		public string VariableID { get; set; }//unique identifier of the variable

		public virtual bool LoadSerialized(BaseVariable_LogEntryItem serializedEntry)
		{
			VariableID = serializedEntry.VariableID;

			return true;
		}

		public abstract BaseVariable_LogEntryItem SaveSerialized();
		//{
		//    var serializedData = new BaseVariable_LogEntryItem();
		//    SaveSerializedInteral(serializedData);
		//    return serializedData;
		//}

		protected virtual void SaveSerializedInteral(BaseVariable_LogEntryItem serializedDataOut)
		{
			serializedDataOut.VariableID = VariableID;
		}
	}

	[XmlType("MemoryVariableLogEntryItem")]
	public class MemoryVariable_LogEntryItem : BaseVariable_LogEntryItem
	{
		public byte[] RawData//raw value of the memory variable
		{
			get
			{
				return _RawData;
			}
			set
			{
				if (value != null)
				{
					_RawData = new byte[value.Length];
					value.CopyTo(_RawData, 0);
				}
				else
				{
					_RawData = null;
				}
			}
		}
		private byte[] _RawData;

		public override bool LoadSerialized(BaseVariable_LogEntryItem serializedEntry)
		{
			RawData = null;

			bool result = base.LoadSerialized(serializedEntry);

			var serializedMemEntry = serializedEntry as MemoryVariable_LogEntryItem;

			if (result && (serializedMemEntry != null))
			{
				if (serializedMemEntry.RawData != null)
				{
					RawData = serializedMemEntry.RawData;
				}
			}

			return result;
		}

		public override BaseVariable_LogEntryItem SaveSerialized()
		{
		    var serializedData = new MemoryVariable_LogEntryItem();
		    SaveSerializedInteral(serializedData);
		    return serializedData;
		}

		protected override void SaveSerializedInteral(BaseVariable_LogEntryItem serializedDataOut)
		{
			base.SaveSerializedInteral(serializedDataOut);

			if (serializedDataOut is MemoryVariable_LogEntryItem)
			{
				var serializedMemDataOut = serializedDataOut as MemoryVariable_LogEntryItem;
				serializedMemDataOut.RawData = RawData;
			}
		}		
	}

	[XmlType("VariableDefinitionsFile")]
    public class VariableDefinitionsFile : BaseFile
    {
        [XmlArray]
        [XmlArrayItem(ElementName = "MemoryVariableDefinition", Type = typeof(MemoryVariableDefinition_Serialized))]
        public List<VariableDefinitionBase_Serialized> VariableDefinitions { get; set; }
    }

	[XmlType("ReadVariablesFile")]
    public class ReadVariablesFile : BaseFile
    {
        [XmlArray]
        [XmlArrayItem(ElementName = "ReadMemoryVariable", Type = typeof(ReadMemoryVariable_Serialized))]
        public List<ReadVariableBase_Serialized> ReadVariables { get; set; }
    }

	[XmlType("LogFile")]
    public class LogFile : BaseFile
    {
		public LogFile()
		{
			//added multi variable log entries in version 1.1.0.0
			Version = new Version(1, 1, 0, 0).ToString();
		}

        [XmlArray]
        [XmlArrayItem(ElementName = "MemoryVariableDefinition", Type = typeof(MemoryVariableDefinition_Serialized))]
        public List<VariableDefinitionBase_Serialized> VariableDefinitions { get; set; }
        
        [XmlArray]
        [XmlArrayItem(ElementName = "ReadMemoryVariable", Type = typeof(ReadMemoryVariable_Serialized))]
        public List<ReadVariableBase_Serialized> ReadVariables { get; set; }

        [XmlArray]
		[XmlArrayItem(ElementName = "MemoryVariableLogEntry", Type = typeof(MemoryVariableLogEntry_Serialized))]		
		[XmlArrayItem(ElementName = "MultiVariableLogEntry", Type = typeof(MultiVariableLogEntry_Serialized))]
        public List<BaseLogEntry_Serialized> LogEntries { get; set; }
    }

    //TODO: This class is combining and confusing the separation between variables being read, and the current value of a read variable
    public abstract class ReadVariableBase_ViewModel : INotifyPropertyChanged
    {
        public ReadVariableBase_ViewModel()
        {

        }

        public ReadVariableBase_ViewModel(VariableDefinitionBase_ViewModel variableDefinition)
        {
            VariableID = variableDefinition.VariableID;
            VariableDefinition = variableDefinition;
        }

        //VariableID is authoritative over VariableDefinition
        public string VariableID 
        {
            get { return _VariableID; }
            set
            {
                if (_VariableID != value)
                {
                    string oldVariableID = _VariableID;

                    _VariableID = value;

                    if (VariableIDChangedEvent != null)
                    {
                        VariableIDChangedEvent(this, oldVariableID, _VariableID);
                    }

                    NotifyPropertyChanged("VariableID");

                    if ((VariableDefinition != null) && (VariableDefinition.VariableID != _VariableID))
                    {
                        VariableDefinition = null;
                    }
                }
            }
        }
        private string _VariableID = null;

        public delegate void VariableIDChanged(ReadVariableBase_ViewModel variable, string oldValue, string newValue);
        public event VariableIDChanged VariableIDChangedEvent;

        //VariableID is authoritative over VariableDefinition
        public virtual VariableDefinitionBase_ViewModel VariableDefinition
        {
            get
            {
                return _VariableDefinition;
            }

            set
            {
                if (_VariableDefinition != value)
                {
                    if (_VariableDefinition != null)
                    {   
                        _VariableDefinition.IsDisposingEvent -= this.VaribleDefinitionIsDisposing;
                        _VariableDefinition.VariableIDChangedEvent -= this.VariableDefinitionIDChanged;
                        _VariableDefinition.PropertyChanged -= this.VariableDefinitionPropertyChanged;
                        _VariableDefinition.ValueConversionChangedEvent -= this.VariableDefinitionValueConversionChanged;
                    }                    
                    
                    _VariableDefinition = value;

                    if (VariableDefinitionChangedEvent != null)
                    {
                        VariableDefinitionChangedEvent(this);
                    }

                    NotifyPropertyChanged("VariableDefinition");

                    if(_VariableDefinition != null)
                    {
                        _VariableDefinition.IsDisposingEvent += this.VaribleDefinitionIsDisposing;
                        _VariableDefinition.VariableIDChangedEvent += this.VariableDefinitionIDChanged;
                        _VariableDefinition.PropertyChanged += this.VariableDefinitionPropertyChanged;
                        _VariableDefinition.ValueConversionChangedEvent += this.VariableDefinitionValueConversionChanged;

                        VariableID = _VariableDefinition.VariableID;
                    }

                    UpdateValueConversion();
                }
            }
        }
        protected VariableDefinitionBase_ViewModel _VariableDefinition;

        public delegate void VariableDefinitionChanged(ReadVariableBase_ViewModel variable);
        public event VariableDefinitionChanged VariableDefinitionChangedEvent;

        protected virtual void VariableDefinitionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Debug.Assert(VariableDefinition == sender);
        }

        private void VariableDefinitionValueConversionChanged()
        {
            UpdateValueConversion();
        }

        private void VaribleDefinitionIsDisposing(VariableDefinitionBase_ViewModel variableDefinition)
        {
            Debug.Assert(variableDefinition == VariableDefinition);

            VariableDefinition = null;
        }

        private void VariableDefinitionIDChanged(VariableDefinitionBase_ViewModel variableDefinition, string oldValue, string newValue)
        {
            Debug.Assert(VariableID == oldValue);

            VariableDefinition = null;
        }        
                
        public double Value
        {
            get { return _Value; }
            set
            {
                if (_Value != value)
                {
                    _Value = value;

                    NotifyPropertyChanged("Value");
                }
            }
        }
        private double _Value = 0.0;

        public bool IsSelected
        {
            get { return _IsSelected; }
            set
            {
                if (_IsSelected != value)
                {
                    _IsSelected = value;

                    NotifyPropertyChanged("IsSelected");
                }
            }
        }
        private bool _IsSelected = false;

        protected virtual bool UpdateValueConversion()
        {
            return false;
        }

        public abstract bool UpdateValueFromLogEntry(BaseVariable_LogEntryItem logEntry);

        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        
        public virtual bool LoadSerialized(ReadVariableBase_Serialized serializedData)
        {
            VariableID = serializedData.VariableID;            

            return true;
        }

        public abstract ReadVariableBase_Serialized SaveSerialized();
        //{
        //    var serializedData = new ReadVariableBase_Serialized();
        //    SaveSerializedInteral(serializedData);
        //    return serializedData;
        //}

        protected virtual void SaveSerializedInteral(ReadVariableBase_Serialized serializedDataOut)
        {
            serializedDataOut.VariableID = VariableID;            
        }

        public bool BindToVariableDefinition(Dictionary<string, VariableDefinitionBase_ViewModel> variableDefinitionMap)
        {
            bool result = false;

            if (VariableID != null)
            {
                VariableDefinitionBase_ViewModel newVariable;
                if (variableDefinitionMap.TryGetValue(VariableID, out newVariable))
                {
                    VariableDefinition = newVariable;
                    result = true;
                }
            }

            return result;
        }
    }

    public class ReadMemoryVariable_ViewModel : ReadVariableBase_ViewModel
    {
        public ReadMemoryVariable_ViewModel()
        {
            UpdateValueConversion();
        }

        public ReadMemoryVariable_ViewModel(VariableDefinitionBase_ViewModel variableDefinition)
            : base(variableDefinition)
        {
            UpdateValueConversion();
        }

        public override VariableDefinitionBase_ViewModel VariableDefinition
        {
            get
            {
                return base.VariableDefinition;
            }
            set
            {
                if(_VariableDefinition != value)
                {                    
                    base.VariableDefinition = value;

                    if (MemoryRegionChangedEvent != null)
                    {
                        MemoryRegionChangedEvent(this, VariableID);
                    }

                    RawData = null;
                }
            }
        }

        public override bool UpdateValueFromLogEntry(BaseVariable_LogEntryItem logEntry)
        {
            bool result = false;

            if (logEntry is MemoryVariable_LogEntryItem)
            {
				var memLogEntry = logEntry as MemoryVariable_LogEntryItem;

                RawData = memLogEntry.RawData;

                result = true;
            }

            return result;
        }

        public byte[] RawData
        {
            get
            {
                return _RawData;
            }
            set
            {
                //we update the data even if it hasn't changed because it is easier than having to compare all the data

                if (value != null)
                {
                    _RawData = new byte[value.Length];
					value.CopyTo(_RawData, 0);
                }
                else
                {
                    _RawData = null;
                }

                UpdateValueConversion();
                NotifyPropertyChanged("RawData");
            }
        }
        private byte[] _RawData = null;

        protected override bool UpdateValueConversion()
        {
            bool result = false;

            lock (this)
            {
                double newValue = 0.0;

                if ((VariableDefinition != null) && (VariableDefinition.ValueConverter != null))
                {
                    if (VariableDefinition is MemoryVariableDefinition_ViewModel)
                    {
                        var memVarDefintion = VariableDefinition as MemoryVariableDefinition_ViewModel;
                        Debug.Assert(memVarDefintion != null);

                        if(memVarDefintion != null)
                        {
                            newValue = VariableDefinition.ValueConverter.ConvertTo(_RawData, memVarDefintion.DataType);
                            result = true;
                        }
                    }
                }

                Value = newValue;
            }

            return result;
        }

        protected override void VariableDefinitionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.VariableDefinitionPropertyChanged(sender, e);

            bool memoryRegionChanged = false;

            switch (e.PropertyName)
            {
                case "DataType":
                {
                    memoryRegionChanged = true;
                    break;
                }
                case "StartAddress":
                {
                    memoryRegionChanged = true;
                    break;
                }
            }

            if(memoryRegionChanged)
            {
                if (MemoryRegionChangedEvent != null)
                {                    
                    MemoryRegionChangedEvent(this, VariableID);
                }
            }
        }

        public delegate void MemoryRegionChanged(ReadVariableBase_ViewModel readVariable, string oldVariableID);
        public event MemoryRegionChanged MemoryRegionChangedEvent;

        public override bool LoadSerialized(ReadVariableBase_Serialized serializedData)
        {
            bool result = false;

            if (serializedData is ReadMemoryVariable_Serialized)
            {
                result = base.LoadSerialized(serializedData);

                if (result)
                {
                    var serializedMemoryData = serializedData as ReadMemoryVariable_Serialized;
                }
            }

            return result;
        }

        public override ReadVariableBase_Serialized SaveSerialized()
        {
            var serializedData = new ReadMemoryVariable_Serialized();
            SaveSerializedInteral(serializedData);
            return serializedData;
        }

        protected override void SaveSerializedInteral(ReadVariableBase_Serialized serializedDataOut)
        {
            base.SaveSerializedInteral(serializedDataOut);

            if (serializedDataOut is ReadMemoryVariable_Serialized)
            {
                var serializedMemDataOut = serializedDataOut as ReadMemoryVariable_Serialized;

				//TODO: add anything we need to save
            }
        }
    }

    public abstract class VariableDefinitionBase_ViewModel : DependencyObject, INotifyPropertyChanged, IDisposable
    {
        public string VariableID
        {
            get { return _VariableID; }
            set
            {
                if (_VariableID != value)
                {
                    string oldVariableID = _VariableID;

                    _VariableID = value;

                    if (VariableIDChangedEvent != null)
                    {
                        VariableIDChangedEvent(this, oldVariableID, _VariableID);
                    }

                    NotifyPropertyChanged("VariableID");
                }
            }
        }
        private string _VariableID = null;

        public delegate void VariableIDChanged(VariableDefinitionBase_ViewModel variable, string oldValue, string newValue);
        public event VariableIDChanged VariableIDChangedEvent;

        public delegate void IsDisposing(VariableDefinitionBase_ViewModel variable);
        public event IsDisposing IsDisposingEvent;

        public string Name
        {
            get { return _Name; }
            set
            {
                if (_Name != value)
                {
                    _Name = value;
                    NotifyPropertyChanged("Name");
                }
            }
        }
        private string _Name = "New Variable";

        public string Units
        {
            get { return _Units; }
            set
            {
                if (_Units != value)
                {
                    _Units = value;
                    NotifyPropertyChanged("Units");
                }
            }
        }
        private string _Units;

        public string Description
        {
            get { return _Description; }
            set
            {
                if (_Description != value)
                {
                    _Description = value;
                    NotifyPropertyChanged("Description");
                }
            }
        }
        private string _Description;

        public VariableValueConverter ValueConverter
        {
            get
            {
                return _ValueConverter;
            }
            set
            {
                if (_ValueConverter != value)
                {
                    if (_ValueConverter != null)
                    {
                        _ValueConverter.PropertyChanged -= this.ValueConverterPropertyChanged;
                    }

                    _ValueConverter = value;

                    if (_ValueConverter != null)
                    {
                        _ValueConverter.PropertyChanged += this.ValueConverterPropertyChanged;
                    }

                    if (ValueConversionChangedEvent != null)
                    {
                        ValueConversionChangedEvent();
                    }

                    NotifyPropertyChanged("ValueConverter");
                }
            }
        }
        private VariableValueConverter _ValueConverter;

        private void ValueConverterPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (ValueConversionChangedEvent != null)
            {
                ValueConversionChangedEvent();
            }
        }

        public delegate void ValueConversionChanged();
        public event ValueConversionChanged ValueConversionChangedEvent;

        public bool IsSelected
        {
            get { return _IsSelected; }
            set
            {
                if (_IsSelected != value)
                {
                    _IsSelected = value;

                    NotifyPropertyChanged("IsSelected");
                }
            }
        }
        private bool _IsSelected = false;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public virtual bool LoadSerialized(VariableDefinitionBase_Serialized serializedData)
        {
            VariableID = serializedData.VariableID;
            Name = serializedData.Name;
            Units = serializedData.Units;
            Description = serializedData.Description;            
            ValueConverter = serializedData.ValueConverter;

            return true;
        }

        public abstract VariableDefinitionBase_Serialized SaveSerialized();
        //{
        //    var serializedData = new VariableDefinitionBase_Serialized();
        //    SaveSerializedInteral(serializedData);
        //    return serializedData;
        //}

        protected virtual void SaveSerializedInteral(VariableDefinitionBase_Serialized serializedDataOut)
        {
            serializedDataOut.VariableID = VariableID;
            serializedDataOut.Name = Name;
            serializedDataOut.Units = Units;
            serializedDataOut.Description = Description;
            serializedDataOut.ValueConverter = ValueConverter;
        }

        public void Dispose()
        {
            if (IsDisposingEvent != null)
            {
                IsDisposingEvent(this);
            }
        }
    }

    public class MemoryVariableDefinition_ViewModel : VariableDefinitionBase_ViewModel
    {
        public MemoryVariableDefinition_ViewModel()
        {
            ValueConverter = new ScaleOffsetMemoryVariableValueConverter(1.0, 0.0);
        }

        public DataUtils.DataType DataType
        {
            get { return _DataType; }
            set
            {
                if (_DataType != value)
                {
                    _DataType = value;
                    NotifyPropertyChanged("DataType");
                    NotifyPropertyChanged("NumBytes");
                    NotifyPropertyChanged("EndAddress");
                }
            }
        }
        private DataUtils.DataType _DataType = DataUtils.DataType.UInt16;

        public UInt32 StartAddress
        {
            get { return _StartAddress; }
            set
            {
                if (_StartAddress != value)
                {
                    _StartAddress = value;
                    NotifyPropertyChanged("StartAddress");
                    NotifyPropertyChanged("EndAddress");
                }
            }
        }        
        protected UInt32 _StartAddress;

        public UInt32 NumBytes
        {
            get { return DataUtils.GetDataTypeSize(_DataType); }
        }

        public override bool LoadSerialized(VariableDefinitionBase_Serialized serializedData)
        {
            bool result = false;

            if (serializedData is MemoryVariableDefinition_Serialized)
            {
                result = base.LoadSerialized(serializedData);

                if (result)
                {
                    var serializedMemoryData = serializedData as MemoryVariableDefinition_Serialized;

                    DataType = serializedMemoryData.DataType;

					if(serializedMemoryData.Address.StartsWith("0x"))
					{
                    	StartAddress = DataUtils.ReadHexString(serializedMemoryData.Address);
					}
					else
					{
						UInt32.TryParse(serializedMemoryData.Address, out _StartAddress);
					}

                    result = true;
                }
            }

            return result;
        }

        public override VariableDefinitionBase_Serialized SaveSerialized()
        {
            var serializedData = new MemoryVariableDefinition_Serialized();
            SaveSerializedInteral(serializedData);
            return serializedData;
        }

        protected override void SaveSerializedInteral(VariableDefinitionBase_Serialized serializedDataOut)
        {
            base.SaveSerializedInteral(serializedDataOut);

            if (serializedDataOut is MemoryVariableDefinition_Serialized)
            {
                var serializedMemDataOut = serializedDataOut as MemoryVariableDefinition_Serialized;

                serializedMemDataOut.Address = StartAddress.ToString();
                serializedMemDataOut.DataType = DataType;
            }
        }
    }

    /// <summary>
    /// Interaction logic for DataLoggerControl.xaml
    /// </summary>
    public partial class DataLoggerControl : BaseUserControl
    {
        public DataLoggerControl()
        {
            InitializeComponent();
        }

        public ObservableCollection<ReadVariableBase_ViewModel> ReadVariables
        {
            get
            {
                if (_ReadVariables == null)
                {
                    mReadVariablesMap = new Dictionary<string, HashSet<ReadVariableBase_ViewModel>>();

                    //using the property setter to trigger a change notification
                    ReadVariables = new ObservableCollection<ReadVariableBase_ViewModel>();
                }

                return _ReadVariables;
            }

            private set
            {
                if (_ReadVariables != value)
                {
                    if (_ReadVariables != null)
                    {
                        _ReadVariables.CollectionChanged -= this.ReadVariables_CollectionChanged_MapUpdate;

                        RemoveAllReadVariableFromMap();
                    }

                    _ReadVariables = value;

                    if (_ReadVariables != null)
                    {
                        _ReadVariables.CollectionChanged += this.ReadVariables_CollectionChanged_MapUpdate;

                        foreach (var variable in _ReadVariables)
                        {
                            AddReadVariableToMap(variable);
                        }
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("ReadVariables"));
                }
            }
        }
        private ObservableCollection<ReadVariableBase_ViewModel> _ReadVariables;
        private Dictionary<string, HashSet<ReadVariableBase_ViewModel>> mReadVariablesMap;

        void ReadVariable_VariableIDChanged(ReadVariableBase_ViewModel variable, string oldValue, string newValue)
        {
            Debug.Assert(variable.VariableID == newValue);

            RemoveReadVariableFromMap(variable, oldValue);
            
            AddReadVariableToMap(variable);

            variable.BindToVariableDefinition(mVariableDefinitionsMap);
        }

        private void ReadVariables_CollectionChanged_MapUpdate(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                RemoveAllReadVariableFromMap();
            }

            if (e.OldItems != null)
            {
                foreach (var variable in e.OldItems.OfType<ReadVariableBase_ViewModel>())
                {
                    if ((e.NewItems == null) || !e.NewItems.Contains(variable))
                    {
                        RemoveReadVariableFromMap(variable, variable.VariableID);
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (var variable in e.NewItems.OfType<ReadVariableBase_ViewModel>())
                {
                    if ((e.OldItems == null) || !e.OldItems.Contains(variable))
                    {
                        AddReadVariableToMap(variable);
                    }
                }
            }
        }

        private bool AddReadVariableToMap(ReadVariableBase_ViewModel variable)
        {
            bool result = false;

            if (variable.VariableID != null)
            {
                HashSet<ReadVariableBase_ViewModel> idSet;

                if (!mReadVariablesMap.TryGetValue(variable.VariableID, out idSet))
                {
                    idSet = new HashSet<ReadVariableBase_ViewModel>();
                    mReadVariablesMap[variable.VariableID] = idSet;
                }

                Debug.Assert(idSet != null);

                if (!idSet.Contains(variable))
                {
                    variable.VariableIDChangedEvent += this.ReadVariable_VariableIDChanged;

                    idSet.Add(variable);

                    result = true;
                }
            }

            return result;
        }

        private bool RemoveReadVariableFromMap(ReadVariableBase_ViewModel variable, string variableID)
        {            
            bool result = false;

            if (variableID != null)
            {
                variable.VariableIDChangedEvent -= this.ReadVariable_VariableIDChanged;

                HashSet<ReadVariableBase_ViewModel> idSet;
                if (mReadVariablesMap.TryGetValue(variableID, out idSet))
                {
                    result = idSet.Remove(variable);

                    if (idSet.Count == 0)
                    {
                        mReadVariablesMap.Remove(variableID);
                    }
                }
            }

            return result;
        }

        private void RemoveAllReadVariableFromMap()
        {
            foreach (var idSet in mReadVariablesMap.Values)
            {
                foreach (var variable in idSet)
                {
                    variable.VariableIDChangedEvent -= this.ReadVariable_VariableIDChanged;
                }
            }

            mReadVariablesMap.Clear();
        }

        public ObservableCollection<VariableDefinitionBase_ViewModel> VariableDefinitions
        {
            get
            {
                if (_VariableDefinitions == null)
                {
                    mVariableDefinitionsMap = new Dictionary<string, VariableDefinitionBase_ViewModel>();

                    //using the property setter to trigger a change notification
                    VariableDefinitions = new ObservableCollection<VariableDefinitionBase_ViewModel>();                    
                }

                return _VariableDefinitions;
            }

            private set
            {
                if (_VariableDefinitions != value)
                {
                    var variableIDsChanged = new List<string>();

                    if (_VariableDefinitions != null)
                    {
                        _VariableDefinitions.CollectionChanged -= this.VariableDefinitions_CollectionChanged_MapUpdate;

                        foreach (var variable in _VariableDefinitions)
                        {
                            variable.VariableIDChangedEvent -= this.VariableDefinition_VariableIDChanged;

                            variableIDsChanged.Add(variable.VariableID);

                            variable.Dispose();
                        }

                        mVariableDefinitionsMap.Clear();                       
                    }

                    _VariableDefinitions = value;

                    if (_VariableDefinitions != null)
                    {
                        _VariableDefinitions.CollectionChanged += this.VariableDefinitions_CollectionChanged_MapUpdate;

                        foreach (var variable in _VariableDefinitions)
                        {
                            AddVariableDefinitionToMap(variable);

                            variable.VariableIDChangedEvent += this.VariableDefinition_VariableIDChanged;

                            variableIDsChanged.Add(variable.VariableID);
                        }
                    }

                    //TODO: remove duplicate variable IDs from changed list
                    if (variableIDsChanged.Any())
                    {
                        UpdateReadVariableDefinitions(variableIDsChanged);
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("VariableDefinitions"));
                }
            }
        }
        private ObservableCollection<VariableDefinitionBase_ViewModel> _VariableDefinitions;
        private Dictionary<string, VariableDefinitionBase_ViewModel> mVariableDefinitionsMap;

        void VariableDefinition_VariableIDChanged(VariableDefinitionBase_ViewModel variable, string oldValue, string newValue)
        {
            var changedDefinitions = new List<string>();

            if(RemoveVariableDefinitionFromMap(oldValue, variable))
            {
                changedDefinitions.Add(oldValue);
            }

            if(AddVariableDefinitionToMap(variable))
            {
                changedDefinitions.Add(variable.VariableID);
            }

            //disconnect read variables from old ID, and connect read variables to new ID
            UpdateReadVariableDefinitions(changedDefinitions);
        }

        private void UpdateReadVariableDefinitions(IEnumerable<string> changedDefinitions)
        {
            foreach (var variableID in changedDefinitions)
            {
                if (variableID != null)
                {
                    HashSet<ReadVariableBase_ViewModel> readVariablesUsingDefinition;
                    if (mReadVariablesMap.TryGetValue(variableID, out readVariablesUsingDefinition))
                    {
                        VariableDefinitionBase_ViewModel variableDefinition;
                        if(mVariableDefinitionsMap.TryGetValue(variableID, out variableDefinition))
                        {
                            foreach (var readVar in readVariablesUsingDefinition)
                            {
                                readVar.VariableDefinition = variableDefinition;
                            }
                        }
                    }
                }
            }
        }

        private void VariableDefinitions_CollectionChanged_MapUpdate(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var variable in mVariableDefinitionsMap.Values)
                {
                    variable.VariableIDChangedEvent -= this.VariableDefinition_VariableIDChanged;

                    variable.Dispose();
                }

                mVariableDefinitionsMap.Clear();
            }

            var variableIDsChanged = new List<string>();

            if (e.OldItems != null)
            {
                foreach (var variable in e.OldItems.OfType<VariableDefinitionBase_ViewModel>())
                {
                    if ((e.NewItems == null) || !e.NewItems.Contains(variable))
                    {
                        RemoveVariableDefinitionFromMap(variable.VariableID, variable);

                        variable.VariableIDChangedEvent -= this.VariableDefinition_VariableIDChanged;

                        variableIDsChanged.Add(variable.VariableID);

                        variable.Dispose();
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (var variable in e.NewItems.OfType<VariableDefinitionBase_ViewModel>())
                {
                    if ((e.OldItems == null) || !e.OldItems.Contains(variable))
                    {
                        AddVariableDefinitionToMap(variable);

                        variable.VariableIDChangedEvent += this.VariableDefinition_VariableIDChanged;

                        variableIDsChanged.Add(variable.VariableID);
                    }
                }
            }

            if (variableIDsChanged.Any())
            {
                UpdateReadVariableDefinitions(variableIDsChanged);
            }
        }

        public static readonly DependencyProperty IsDefinitionAddedToMapProperty = DependencyProperty.RegisterAttached("IsDefinitionAddedToMap", typeof(bool), typeof(VariableDefinitionBase_ViewModel));

        public static void SetIsDefinitionAddedToMap(DependencyObject depObj, bool value)
        {
            depObj.SetValue(IsDefinitionAddedToMapProperty, value);
        }

        public static bool GetIsDefinitionAddedToMap(DependencyObject depObj)
        {
            return (bool)depObj.GetValue(IsDefinitionAddedToMapProperty);
        }

        private bool AddVariableDefinitionToMap(VariableDefinitionBase_ViewModel variable)
        {
            bool variableIsInMap = false;
            
            if (variable.VariableID != null)
            {
                if (!mVariableDefinitionsMap.ContainsKey(variable.VariableID))
                {
                    mVariableDefinitionsMap.Add(variable.VariableID, variable);
                    variableIsInMap = true;
                }
                else if(mVariableDefinitionsMap[variable.VariableID] == variable)
                {
                    variableIsInMap = true;
                }
            }

            SetIsDefinitionAddedToMap(variable, variableIsInMap);

            return variableIsInMap;
        }

        private bool RemoveVariableDefinitionFromMap(string variableID, VariableDefinitionBase_ViewModel variable)
        {
            bool removed = false;

            if ((variableID != null) && mVariableDefinitionsMap.ContainsKey(variableID))
            {
                SetIsDefinitionAddedToMap(variable, false);

                if (mVariableDefinitionsMap[variableID] == variable)
                {
                    removed = mVariableDefinitionsMap.Remove(variableID);
                }
            }

            return removed;
        }

        public ICommand AddVariableDefinitionCommand
        {
            get
            {
                if (_AddVariableDefinitionCommand == null)
                {
                    _AddVariableDefinitionCommand = new ReactiveCommand(this.OnAddVariableDefinition);
                    _AddVariableDefinitionCommand.Name = "Add New Variable Definition";
                    _AddVariableDefinitionCommand.Description = "Add new variable definition";
                }

                return _AddVariableDefinitionCommand;
            }
        }
        private ReactiveCommand _AddVariableDefinitionCommand;

        private void OnAddVariableDefinition()
        {
            var variable = new MemoryVariableDefinition_ViewModel();
            variable.Name = "New Variable";
            variable.VariableID = null;
            
            VariableDefinitions.Add(variable);
        }
                
        public ICommand RemoveVariableDefinitionCommand
        {
            get
            {
                if (_RemoveVariableDefinitionCommand == null)
                {
                    _RemoveVariableDefinitionCommand = new ReactiveCommand();
                    _RemoveVariableDefinitionCommand.Name = "Remove Variable Definition";
                    _RemoveVariableDefinitionCommand.Description = "Remove selected variable definitions";
                    _RemoveVariableDefinitionCommand.ExecuteMethod = delegate(object param)
                    {
                        if (param is IEnumerable)
                        {
                            var collection = param as IEnumerable;
                            var collectionCopy = new List<VariableDefinitionBase_ViewModel>();

                            //need to copy before we change the collection
                            foreach(var variable in collection)
                            {
                                collectionCopy.Add(variable as VariableDefinitionBase_ViewModel);
                            }

                            foreach (var variable in collectionCopy)
                            {
                                OnRemoveVariableDefinition(variable as VariableDefinitionBase_ViewModel);
                            }

                        }
                        else if (param is VariableDefinitionBase_ViewModel)
                        {
                            OnRemoveVariableDefinition(param as VariableDefinitionBase_ViewModel);
                        }
                    };
                }

                return _RemoveVariableDefinitionCommand;
            }
        }
        private ReactiveCommand _RemoveVariableDefinitionCommand;

        private void OnRemoveVariableDefinition(VariableDefinitionBase_ViewModel variable)
        {
            if (variable != null)
            {
                VariableDefinitions.Remove(variable);               
            }
        }

        public ICommand AddReadVariableCommand
        {
            get
            {
                if (_AddReadVariableCommand == null)
                {
                    _AddReadVariableCommand = new ReactiveCommand();
                    _AddReadVariableCommand.Name = "Add Read Variable";
                    _AddReadVariableCommand.Description = "Add selected variable definitions to read variables";
                    _AddReadVariableCommand.ExecuteMethod = delegate(object param)
                    {
                        if (param is IEnumerable)
                        {
                            var collection = param as IEnumerable;
                            var collectionCopy = new List<VariableDefinitionBase_ViewModel>();

                            //need to copy before we change the collection
                            foreach (var variable in collection)
                            {
                                collectionCopy.Add(variable as VariableDefinitionBase_ViewModel);
                            }

                            foreach (var variable in collectionCopy)
                            {
                                OnAddReadVariable(variable as VariableDefinitionBase_ViewModel);
                            }

                        }
                        else if (param is VariableDefinitionBase_ViewModel)
                        {
                            OnAddReadVariable(param as VariableDefinitionBase_ViewModel);
                        }
                    };
                }

                return _AddReadVariableCommand;
            }
        }
        private ReactiveCommand _AddReadVariableCommand;

        private void OnAddReadVariable(VariableDefinitionBase_ViewModel variableDefinition)
        {
            if ((variableDefinition != null) && (variableDefinition.VariableID != null))
            {
                ReadVariables.Add(new ReadMemoryVariable_ViewModel(variableDefinition));
            }
        }

        public ICommand RemoveReadVariableCommand
        {
            get
            {
                if (_RemoveReadVariableCommand == null)
                {
                    _RemoveReadVariableCommand = new ReactiveCommand();
                    _RemoveReadVariableCommand.Name = "Remove Read Variable";
                    _RemoveReadVariableCommand.Description = "Remove selected read variables";
                    _RemoveReadVariableCommand.ExecuteMethod = delegate(object param)
                    {                        
                        if (param is IEnumerable)
                        {
                            var collection = param as IEnumerable;
                            var collectionCopy = new List<ReadVariableBase_ViewModel>();

                            //need to copy before we change the collection
                            foreach (var variable in collection)
                            {
                                collectionCopy.Add(variable as ReadVariableBase_ViewModel);
                            }

                            foreach (var variable in collectionCopy)
                            {
                                OnRemoveReadVariable(variable as ReadVariableBase_ViewModel);
                            }
                        }
                        else if (param is ReadVariableBase_ViewModel)
                        {
                            OnRemoveReadVariable(param as ReadVariableBase_ViewModel);
                        }
                    };
                }

                return _RemoveReadVariableCommand;
            }
        }
        private ReactiveCommand _RemoveReadVariableCommand;

        private void OnRemoveReadVariable(ReadVariableBase_ViewModel variable)
        {
            if (variable != null)
            {
                ReadVariables.Remove(variable);
            }
        }

		public DataLoggingOperationType DesiredDataLoggingType
		{
			get { return _DesiredDataLoggingType; }
			set
			{
				if (value != _DesiredDataLoggingType)
				{
					_DesiredDataLoggingType = value;

					OnPropertyChanged(new PropertyChangedEventArgs("DesiredDataLoggingType"));
				}
			}
		}
		private DataLoggingOperationType _DesiredDataLoggingType = DataLoggingOperationType.MultipleValueExtendedRAMDataLogging;

        public ICommand StartReadingCommand
        {
            get
            {
                if (_StartReadingCommand == null)
                {
                    _StartReadingCommand = new ReactiveCommand();
                    _StartReadingCommand.Name = "Start Reading";
                    _StartReadingCommand.Description = "Start reading variables";

                    if (App != null)
                    {
                        _StartReadingCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");
                        _StartReadingCommand.AddWatchedProperty(App, "CommInterface");//to watch for protocol changes
                        _StartReadingCommand.AddWatchedProperty(App, "OperationInProgress");
                        _StartReadingCommand.AddWatchedProperty(this, "IsReadingVariables");
                        _StartReadingCommand.AddWatchedProperty(this, "IsLogPlaying");
                    }

					_StartReadingCommand.ExecuteMethod = delegate
					{
						OnStartReading(DesiredDataLoggingType);
					};

                    _StartReadingCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        if (App == null)
                        {
                            reasonsDisabled.Add("Internal program error");
                            return false;
                        }

                        bool result = true;

						if (!App.CommInterface.IsConnected())
						{
							reasonsDisabled.Add("Not connected to ECU");
							result = false;
						}

						if (App.CommInterface.CurrentProtocol != CommunicationInterface.Protocol.KWP2000)
						{
							reasonsDisabled.Add("Not connected with KWP2000 protocol");
							result = false;
						}

                        if (App.OperationInProgress)
                        {
                            reasonsDisabled.Add("Another operation is in progress");
                            result = false;
                        }

                        if (IsLogPlaying)
                        {
                            reasonsDisabled.Add("Log playback currently running");
                            result = false;
                        }

                        if (IsReadingVariables)
                        {
                            reasonsDisabled.Add("Already reading variables");
                            result = false;
                        }

                        return result;
                    };
                }

                return _StartReadingCommand;
            }
        }
        private ReactiveCommand _StartReadingCommand;

        public ICommand StopReadingCommand
        {
            get
            {
                if (_StopReadingCommand == null)
                {
                    _StopReadingCommand = new ReactiveCommand(this.OnStopReading);
                    _StopReadingCommand.Name = "Stop Reading";
                    _StopReadingCommand.Description = "Stop reading variables";

                     if (App != null)
                    {
                        _StopReadingCommand.AddWatchedProperty(App.CommInterface, "ConnectionStatus");                        
                    }

                    _StopReadingCommand.AddWatchedProperty(this, "IsReadingVariables");

                     _StopReadingCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                     {
                         if (App == null)
                         {
                             reasonsDisabled.Add("Internal program error");
                             return false;
                         }

                         bool result = true;

                         if (!IsReadingVariables)
                         {
                             reasonsDisabled.Add("Not currently reading variables");
                             result = false;
                         }

                         return result;
                     };
                }

                return _StopReadingCommand;
            }
        }
        private ReactiveCommand _StopReadingCommand;

		public enum DataLoggingOperationType
		{
			[Description("Single Variable Per Sample")]
			SingleValueRAMDataLogging,
			[Description("Contiguous Variables Per Sample")]
			ContiguousBlockRAMDataLogging,
			[Description("All Variables Per Sample")]
			MultipleValueExtendedRAMDataLogging
		}

		public float MaxSamplesPerSecond
		{
			get { return _MaxSamplesPerSecond; }
			set
			{
				if (value != _MaxSamplesPerSecond)
				{
					value = Math.Min(Math.Max(value, 1.0f), 50.0f);

					_MaxSamplesPerSecond = value;

					if (App.CurrentOperation is ITrackedMemoryRegionsOperation)
					{
						(App.CurrentOperation as ITrackedMemoryRegionsOperation).MaxReadsPerSecond = _MaxSamplesPerSecond;
					}

					OnPropertyChanged(new PropertyChangedEventArgs("MaxSamplesPerSecond"));
				}
			}
		}
		private float _MaxSamplesPerSecond = 10;

		public byte MaxVariableReadsPerTick
		{
			get { return _MaxVariableReadsPerTick; }
			set
			{
				if (value != _MaxVariableReadsPerTick)
				{
					value = Math.Min(Math.Max(value, (byte)1), (byte)255);

					_MaxVariableReadsPerTick = value;

					if (App.CurrentOperation is ITrackedMemoryRegionsOperation)
					{
						(App.CurrentOperation as ITrackedMemoryRegionsOperation).MaxVariableReadsPerTick = _MaxVariableReadsPerTick;
					}

					OnPropertyChanged(new PropertyChangedEventArgs("MaxVariableReadsPerTick"));
				}
			}
		}
		private byte _MaxVariableReadsPerTick = 255;

		public byte MaxBytesPerRead
		{
			get { return _MaxBytesPerRead; }
			set
			{
				if (value != _MaxBytesPerRead)
				{
					value = Math.Min(Math.Max(value, (byte)1), (byte)255);

					_MaxBytesPerRead = value;

					if (App.CurrentOperation is ITrackedMemoryRegionsOperation)
					{
						(App.CurrentOperation as ITrackedMemoryRegionsOperation).MaxNumBytesPerRead = _MaxBytesPerRead;
					}

					OnPropertyChanged(new PropertyChangedEventArgs("MaxBytesPerRead"));
				}
			}
		}
		private byte _MaxBytesPerRead = 64;

		public float AverageSamplesPerSecond
		{
			get { return _AverageSamplesPerSecond; }
			set
			{
				if (value != _AverageSamplesPerSecond)
				{
					_AverageSamplesPerSecond = value;

					OnPropertyChanged(new PropertyChangedEventArgs("AverageSamplesPerSecond"));
				}
			}
		}
		private float _AverageSamplesPerSecond = 0;

        private void OnStartReading(DataLoggingOperationType loggingType)
        {
            var KWP2000CommViewModel = App.CommInterfaceViewModel as KWP2000Interface_ViewModel;

			if (loggingType == DataLoggingOperationType.MultipleValueExtendedRAMDataLogging)
			{
				App.CurrentOperation = new ExtendedDataLoggingOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates);            
			}
			else
			{
				bool readVariablesInBlocks = false;

				if (loggingType == DataLoggingOperationType.ContiguousBlockRAMDataLogging)
				{
					readVariablesInBlocks = true;
				}

				App.CurrentOperation = new SynchronizeRAMRegionsOperation(KWP2000CommViewModel.KWP2000CommInterface, KWP2000CommViewModel.DesiredBaudRates, readVariablesInBlocks);
			}			
			
			var syncOperation = App.CurrentOperation as ITrackedMemoryRegionsOperation;
			syncOperation.MaxReadsPerSecond = MaxSamplesPerSecond;
			syncOperation.MaxVariableReadsPerTick = MaxVariableReadsPerTick;
			syncOperation.MaxNumBytesPerRead = MaxBytesPerRead;

            IsReadingVariables = true;//this will add all the regions to the operation

            App.CurrentOperation.CompletedOperationEvent += this.OnSynchronizeRAMVariablesOperationCompleted;

			var lastReadTime = DateTime.Now;
			double runningAverageTotalSeconds = 0.0;
			double numVariableSamplesRead = 0.0;

			var pendingVariableUpdates = new List<VariableLogEntry>();

			syncOperation.RegionsRead += delegate(IEnumerable<TaggedMemoryImage> regionsRead)
			{
				var curTime = DateTime.Now;

				var newLogEntry = new VariableLogEntry();
				newLogEntry.TimeStamp = curTime;

				foreach (var region in regionsRead)
				{
					var variableID = region.UserTag as string;
#if DEBUG
					bool containsKey = mVariableDefinitionsMap.ContainsKey(variableID);
					Debug.Assert(containsKey);

					if (containsKey)
					{
						var definition = mVariableDefinitionsMap[variableID] as MemoryVariableDefinition_ViewModel;

						Debug.Assert(definition != null);
						Debug.Assert(DataUtils.GetDataTypeSize(definition.DataType) == region.Size);
					}
#endif
					//TODO: read variables should have a factory method for creating log entries of the correct type
					var logEntryItem = new MemoryVariable_LogEntryItem();
					logEntryItem.VariableID = variableID;
					logEntryItem.RawData = region.RawData;

					newLogEntry.EntryItems.Add(logEntryItem);
				}

				lock (pendingVariableUpdates)
				{
					pendingVariableUpdates.Add(newLogEntry);
				}

				//update running average for samples per second				
				{
					numVariableSamplesRead++;
					runningAverageTotalSeconds += (curTime - lastReadTime).TotalSeconds;

					if (runningAverageTotalSeconds > 0)
					{
						AverageSamplesPerSecond = (float)(numVariableSamplesRead / runningAverageTotalSeconds);

						const double ratio = 2.0;

						if (runningAverageTotalSeconds > ratio)
						{
							runningAverageTotalSeconds /= ratio;
							numVariableSamplesRead /= ratio;
						}
					}

					lastReadTime = curTime;
				}
			};

			var applyPendingTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
			applyPendingTimer.Interval = TimeSpan.FromMilliseconds(25);
			applyPendingTimer.Tick += delegate
			{
				if (!IsReadingVariables)
				{
					applyPendingTimer.Stop();
				}

				var oldPendingVariableUpdate = pendingVariableUpdates;

				lock (pendingVariableUpdates)
				{
					if (pendingVariableUpdates.Any())
					{
						pendingVariableUpdates = new List<VariableLogEntry>();
					}
				}

				if (oldPendingVariableUpdate.Any())
				{
					if (IsRecordingVariables)
					{
						foreach (var logEntry in oldPendingVariableUpdate)
						{
							LogEntries.Add(logEntry);
						}

						//TOOD: support log play back while reading. don't always advance time and apply the newest log entries to the variables.
						//advance the time and apply any new log entries to the variables
						LogCurrentTime = LogTimeSpan;
					}
					else
					{
						var mostRecentVariableUpdates = new Dictionary<string, BaseVariable_LogEntryItem>();

						foreach (var logEntry in oldPendingVariableUpdate)
						{
							foreach (var logItem in logEntry.EntryItems)
							{
								mostRecentVariableUpdates[logItem.VariableID] = logItem;
							}
						}

						foreach (var logEntryItem in mostRecentVariableUpdates.Values.OfType<MemoryVariable_LogEntryItem>())
						{
							HashSet<ReadVariableBase_ViewModel> idSet;
							if (mReadVariablesMap.TryGetValue(logEntryItem.VariableID, out idSet))
							{
								Debug.Assert(idSet != null);

								foreach (var readMemVar in idSet.OfType<ReadMemoryVariable_ViewModel>())
								{
									readMemVar.RawData = logEntryItem.RawData;
								}
							}
						}
					}
				}
			};
			applyPendingTimer.Start();

            App.OperationInProgress = true;
            App.PercentOperationComplete = -1.0f;

            App.DisplayStatusMessage("Starting variable reading.", StatusMessageType.USER);

            App.CurrentOperation.Start();
        }

        private void OnSynchronizeRAMVariablesOperationCompleted(Operation operation, bool success)
        {
			//UI should occur on the UI thread...
			Dispatcher.Invoke((Action)(() => 
			{
				operation.CompletedOperationEvent -= this.OnSynchronizeRAMVariablesOperationCompleted;

				IsReadingVariables = false;

				App.PercentOperationComplete = 100.0f;

				App.DisplayStatusMessage("Stopped reading variables.", StatusMessageType.USER);

				App.OperationInProgress = false;
			}), null);
        }

        private void OnStopReading()
        {
            if (IsReadingVariables)
            {
                App.DisplayStatusMessage("Stopping variable reading.", StatusMessageType.USER);

                App.CurrentOperation.Abort();
            }
        }

		public ObservableCollection<VariableLogEntry> LogEntries
        {
            get
            {
                if (_LogEntries == null)
                {
                    //using property setter to trigger change notification
					LogEntries = new ObservableCollection<VariableLogEntry>();                    
                }

                return _LogEntries;
            }

            private set
            {
                if (_LogEntries != value)
                {
                    if (_LogEntries != null)
                    {
                        _LogEntries.CollectionChanged -= this.LogEntries_CollectionChanged;
                    }

                    _LogEntries = value;

                    if (_LogEntries != null)
                    {
                        _LogEntries.CollectionChanged += this.LogEntries_CollectionChanged;
                    }

					int entryItemCount = 0;
					if (_LogEntries != null)
					{
						foreach (var entry in _LogEntries)
						{
							entryItemCount += entry.EntryItems.Count;
						}
					}
					NumLogEntryItems = entryItemCount;

                    RecalculateLogTimes();
                    
                    OnPropertyChanged(new PropertyChangedEventArgs("LogEntries"));
                }
            }
        }
		private ObservableCollection<VariableLogEntry> _LogEntries;

		public int NumLogEntryItems
		{
			get
			{
				return _NumLogEntryItems;
			}
			private set
			{
				if (value != _NumLogEntryItems)
				{
					_NumLogEntryItems = value;

					OnPropertyChanged(new PropertyChangedEventArgs("NumLogEntryItems"));
				}
			}
		}
		private int _NumLogEntryItems;

        private void LogEntries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
			int itemCountChange = 0;

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				{
					foreach (var entry in e.NewItems.OfType<VariableLogEntry>())
					{
						itemCountChange += entry.EntryItems.Count;
					}

					break;
				}
				case NotifyCollectionChangedAction.Remove:
				{
					foreach (var entry in e.OldItems.OfType<VariableLogEntry>())
					{
						itemCountChange -= entry.EntryItems.Count;
					}

					break;
				}
				case NotifyCollectionChangedAction.Replace:
				{
					foreach (var entry in e.NewItems.OfType<VariableLogEntry>())
					{
						itemCountChange += entry.EntryItems.Count;
					}

					foreach (var entry in e.OldItems.OfType<VariableLogEntry>())
					{
						itemCountChange -= entry.EntryItems.Count;
					}

					break;
				}
				case NotifyCollectionChangedAction.Reset:
				{
					itemCountChange = -NumLogEntryItems;

					break;
				}
			}

			NumLogEntryItems += itemCountChange;

            RecalculateLogTimes();
        }

        private void RecalculateLogTimes()
        {
            mLogForwardIterator = null;
            mLogReverseIterator = null;

            if ((_LogEntries != null) && _LogEntries.Any())
            {
                LogStartTime = _LogEntries.First().TimeStamp;
                LogEndTime = _LogEntries.Last().TimeStamp;
            }
            else
            {
                LogStartTime = DateTime.Now;
                LogEndTime = LogStartTime;
            }
        }
        
        public TimeSpan LogTimeSpan
        {
            get { return _LogTimeSpan; }
            set
            {
                if (_LogTimeSpan != value)
                {
                    _LogTimeSpan = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("LogTimeSpan"));

                    if (LogCurrentTime > LogTimeSpan)
                    {
                        LogCurrentTime = LogTimeSpan;
                    }
                }
            }
        }
        private TimeSpan _LogTimeSpan;

        public DateTime LogStartTime
        {
            get { return _LogStartTime; }
            set
            {
                if (_LogStartTime != value)
                {
                    _LogStartTime = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("LogStartTime"));

                    LogTimeSpan = LogEndTime - LogStartTime;
                }
            }
        }
        private DateTime _LogStartTime;

        public DateTime LogEndTime
        {
            get { return _LogEndTime; }
            set
            {
                if (_LogEndTime != value)
                {
                    _LogEndTime = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("LogEndTime"));

                    LogTimeSpan = LogEndTime - LogStartTime;                    
                }
            }
        }
        private DateTime _LogEndTime;

        public TimeSpan LogCurrentTime
        {
            get { return _LogCurrentTime; }
            set
            {
                if (_LogCurrentTime != value)
                {
                    TimeSpan prevCurrentTime = _LogCurrentTime;

                    _LogCurrentTime = value;

                    if (_LogCurrentTime > LogTimeSpan)
                    {
                        _LogCurrentTime = LogTimeSpan;
                    }

                    if (_LogCurrentTime < TimeSpan.Zero)
                    {
                        _LogCurrentTime = TimeSpan.Zero;
                    }

                    if (_LogCurrentTime != prevCurrentTime)
                    {
                        CurrentLogTimeChanged(prevCurrentTime, _LogCurrentTime);

                        OnPropertyChanged(new PropertyChangedEventArgs("LogCurrentTime"));
                    }
                }
            }
        }
        private TimeSpan _LogCurrentTime;
        
        public ICommand StartRecordingCommand
        {   
            get
            {
                if (_StartRecordingCommand == null)
                {
                    _StartRecordingCommand = new ReactiveCommand(this.OnStartRecording);
                    _StartRecordingCommand.Name = "Start Recording";
                    _StartRecordingCommand.Description = "Start recording variables to a new log";
                    
                    _StartRecordingCommand.AddWatchedProperty(this, "IsReadingVariables");
                    _StartRecordingCommand.AddWatchedProperty(this, "IsRecordingVariables");

                    _StartRecordingCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;
                        
                        if (!IsReadingVariables)
                        {
                            reasonsDisabled.Add("Not currently reading variables");
                            result = false;
                        }

                        if (IsRecordingVariables)
                        {
                            reasonsDisabled.Add("Variables are already being recorded to log");
                            result = false;
                        }

                        return result;
                    };
                }

                return _StartRecordingCommand;
            }
        }
        private ReactiveCommand _StartRecordingCommand;        

        public ICommand StopRecordingCommand
        {
            get
            {
                if (_StopRecordingCommand == null)
                {
                    _StopRecordingCommand = new ReactiveCommand(this.OnStopRecording);
                    _StopRecordingCommand.Name = "Stop Recording";
                    _StopRecordingCommand.Description = "Stop recording variables to the log";
                    
                    _StopRecordingCommand.AddWatchedProperty(this, "IsReadingVariables");
                    _StopRecordingCommand.AddWatchedProperty(this, "IsRecordingVariables");

                    _StopRecordingCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (!IsReadingVariables)
                        {
                            reasonsDisabled.Add("Not currently reading variables");
                            result = false;
                        }

                        if (!IsRecordingVariables)
                        {
                            reasonsDisabled.Add("Variables are not being recorded to log");
                            result = false;
                        }

                        return result;
                    };
                }

                return _StopRecordingCommand;
            }
        }
        private ReactiveCommand _StopRecordingCommand;

        public bool IsReadingVariables
        {
            get
            {
                if(_IsReadingVariables == null)
                {
                    //using property instead of member variable to go through change handling in property setter
                    IsReadingVariables = false;
                }

                return (_IsReadingVariables == true);
            }
            private set
            {
                if(_IsReadingVariables != value)
                {
                    _IsReadingVariables = value;

                    if(_IsReadingVariables == true)
                    {
                        ReadVariables.CollectionChanged += this.ReadVariables_CollectionChanged_SyncOperationUpdate;

                        foreach (var variable in ReadVariables.OfType<ReadMemoryVariable_ViewModel>())
                        {
                            variable.MemoryRegionChangedEvent += this.ReadVariable_MemoryRegionChanged_SyncOperationUpdate;
                        }

                        if(App.CurrentOperation != null)
                        {                            
							var syncOperation = App.CurrentOperation as ITrackedMemoryRegionsOperation;

                            foreach (var variable in ReadVariables.OfType<ReadMemoryVariable_ViewModel>())
                            {
                                if (variable.VariableDefinition is MemoryVariableDefinition_ViewModel)
                                {
                                    var memVarDefinition = variable.VariableDefinition as MemoryVariableDefinition_ViewModel;

                                    syncOperation.AddMemoryRegion(memVarDefinition.StartAddress, memVarDefinition.NumBytes, memVarDefinition.VariableID);
                                }
                            }
                        }

                        PropertyChanged += this.ReadVariables_PropertyChanged_SyncOperationUpdate;
                    }
                    else
                    {
                        ReadVariables.CollectionChanged -= this.ReadVariables_CollectionChanged_SyncOperationUpdate;

                        foreach (var variable in ReadVariables.OfType<ReadMemoryVariable_ViewModel>())
                        {
                            variable.MemoryRegionChangedEvent -= this.ReadVariable_MemoryRegionChanged_SyncOperationUpdate;
                        }

                        if(App.CurrentOperation != null)
                        {                            
							var syncOperation = App.CurrentOperation as ITrackedMemoryRegionsOperation;
                            syncOperation.RemoveAllMemoryRegions();
                        }

                        PropertyChanged -= this.ReadVariables_PropertyChanged_SyncOperationUpdate;
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("IsReadingVariables"));
                }
            }  
        }
        private bool? _IsReadingVariables = null;

        private void ReadVariable_MemoryRegionChanged_SyncOperationUpdate(ReadVariableBase_ViewModel readVariable, string oldVariableID)
        {            
			var syncOperation = App.CurrentOperation as ITrackedMemoryRegionsOperation;

            if (oldVariableID != null)
            {
                syncOperation.RemoveMemoryRegion(oldVariableID);
            }

            if(readVariable.VariableDefinition is MemoryVariableDefinition_ViewModel)
            {
                var updatedMemVarDefinition = readVariable.VariableDefinition as MemoryVariableDefinition_ViewModel;                                

                syncOperation.AddMemoryRegion(updatedMemVarDefinition.StartAddress, updatedMemVarDefinition.NumBytes, updatedMemVarDefinition.VariableID);
            }
        }

        private void ReadVariables_PropertyChanged_SyncOperationUpdate(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ReadVariables")
            {
                //TODO: is there a memory leak from not unsubscribing from the collection changed event on the old VariableDefinitions?
                ReadVariables.CollectionChanged += this.ReadVariables_CollectionChanged_SyncOperationUpdate;
                
				var syncOperation = App.CurrentOperation as ITrackedMemoryRegionsOperation;
                syncOperation.RemoveAllMemoryRegions();                

                //TODO: is there a memory leak from not unsubscribing from the property changed event on the old variable definitions?

                foreach (var variable in ReadVariables.OfType<ReadMemoryVariable_ViewModel>())
                {
                    if(variable.VariableDefinition is MemoryVariableDefinition_ViewModel)
                    {
                        var memVarDefinition = variable.VariableDefinition as MemoryVariableDefinition_ViewModel;

                        syncOperation.AddMemoryRegion(memVarDefinition.StartAddress, memVarDefinition.NumBytes, memVarDefinition.VariableID);

                        variable.MemoryRegionChangedEvent += this.ReadVariable_MemoryRegionChanged_SyncOperationUpdate;
                    }
                }
            }
        }

        private void ReadVariables_CollectionChanged_SyncOperationUpdate(object sender, NotifyCollectionChangedEventArgs e)
        {            
			var syncOperation = App.CurrentOperation as ITrackedMemoryRegionsOperation;

            if(e.Action == NotifyCollectionChangedAction.Reset)
            {
                syncOperation.RemoveAllMemoryRegions();

                //TODO: is there a memory leak from not unsubscribing from the property changed event on the old variable definitions?
            }

            if(e.OldItems != null)
            {
                foreach (var variable in e.OldItems.OfType<ReadMemoryVariable_ViewModel>())
                {
                    syncOperation.RemoveMemoryRegion(variable.VariableID);

                    variable.MemoryRegionChangedEvent -= this.ReadVariable_MemoryRegionChanged_SyncOperationUpdate;
                }
            }

            if (e.NewItems != null)
            {
                foreach (var variable in e.NewItems.OfType<ReadMemoryVariable_ViewModel>())
                {
                    if(variable.VariableDefinition is MemoryVariableDefinition_ViewModel)
                    {
                        var memVarDefinition = variable.VariableDefinition as MemoryVariableDefinition_ViewModel;

                        syncOperation.AddMemoryRegion(memVarDefinition.StartAddress, memVarDefinition.NumBytes, memVarDefinition.VariableID);
                    
                        variable.MemoryRegionChangedEvent += this.ReadVariable_MemoryRegionChanged_SyncOperationUpdate;
                    }
                }
            }
        }

        public bool IsRecordingVariables
        {
            get
            {
                if (_IsRecordingVariables == null)
                {
                    //using property instead of member variable to go through change handling in property setter
                    IsRecordingVariables = false;
                }

                return (_IsRecordingVariables == true);
            }
            private set
            {
                if (_IsRecordingVariables != value)
                {
                    _IsRecordingVariables = value;                    

                    OnPropertyChanged(new PropertyChangedEventArgs("IsRecordingVariables"));
                }
            }
        }
        private bool? _IsRecordingVariables = null;

        private void CurrentLogTimeChanged(TimeSpan prevCurrentTime, TimeSpan currentTime)
        {
			IEnumerable<VariableLogEntry> logEntriesToApply = null;

			if (LogEntries.Any())
			{
				if (_LogCurrentTime > prevCurrentTime)
				{
					if (mLogForwardIterator == null)
					{
						mLogReverseIterator = null;

						mLogForwardIterator = LogEntries.GetEnumerator();
						mLogForwardIterator.MoveNext();
					}

					logEntriesToApply = GetLogEntriesInTimeRange(mLogForwardIterator, LogStartTime + prevCurrentTime, LogStartTime + currentTime, false);
				}
				else
				{
					if (mLogReverseIterator == null)
					{
						mLogForwardIterator = null;

						mLogReverseIterator = LogEntries.Reverse().GetEnumerator();
						mLogReverseIterator.MoveNext();
					}

					logEntriesToApply = GetLogEntriesInTimeRange(mLogReverseIterator, LogStartTime + currentTime, LogStartTime + prevCurrentTime, true);
				}
			}

			ApplyLogEntriesToVariables(logEntriesToApply, mReadVariablesMap);
        }
        private IEnumerator<VariableLogEntry> mLogForwardIterator;
		private IEnumerator<VariableLogEntry> mLogReverseIterator;

        private void OnStartRecording()
        {
            if (!IsRecordingVariables)
            {                
                App.DisplayStatusMessage("Starting variable recording.", StatusMessageType.USER);

                LogEntries.Clear();

                IsRecordingVariables = true;

                App.DisplayStatusMessage("Started variable recording.", StatusMessageType.USER);
            }
        }
        
        private void OnStopRecording()
        {
            if (IsRecordingVariables)
            {
                App.DisplayStatusMessage("Stopping variable recording.", StatusMessageType.USER);

                IsRecordingVariables = false;   

                App.DisplayStatusMessage("Stopped variable recording.", StatusMessageType.USER);
            }
        }

		private const string LOG_FILE_SHORT_EXT = ".xml";
		private const string LOG_FILE_EXT = ".Log" + LOG_FILE_SHORT_EXT;
		private readonly string LOG_FILE_FILTER = "Log File (*" + LOG_FILE_EXT + ")|*" + LOG_FILE_EXT;

        public ICommand SaveLogFileCommand
        {
            get
            {
                if (_SaveLogFileCommand == null)
                {
                    _SaveLogFileCommand = new ReactiveCommand(this.OnSaveLogFile);
                    _SaveLogFileCommand.Name = "Save Log";
                    _SaveLogFileCommand.Description = "Save recorded variables to log file";

                    _SaveLogFileCommand.AddWatchedCollection(this, "LogEntries", LogEntries);
                    
                    _SaveLogFileCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if ((LogEntries == null) || !LogEntries.Any())
                        {
                            result = false;
                            reasonsDisabled.Add("There are no log entries to save");
                        }

                        return result;
                    };
                }

                return _SaveLogFileCommand; 
            }
        }
        private ReactiveCommand _SaveLogFileCommand;

        private void OnSaveLogFile()
        {
            var dialog = new SaveFileDialog();
			dialog.DefaultExt = LOG_FILE_EXT;//gets long extensions to work properly when they are added to a filename when saved
			dialog.Filter = LOG_FILE_FILTER;
            dialog.InitialDirectory = Directory.GetCurrentDirectory();
            dialog.OverwritePrompt = true;
            dialog.Title = "Select Where to Save Log File";

            if (dialog.ShowDialog() == true)
            {
                //replace .xml with .Log.xml
				var actualFileName = ExtensionFixer.SwitchToLongExtension(dialog.FileName, LOG_FILE_SHORT_EXT, LOG_FILE_EXT);                

                SaveLogFile(actualFileName);
            }
        }        

        private void SaveLogFile(string logFileName)
        {
            App.DisplayStatusMessage("Saving log file.", StatusMessageType.USER);

            if (!String.IsNullOrEmpty(logFileName))
            {
                DisableUserInput();

                bool successfullySavedFile = false;

                var worker = new BackgroundWorker();

                worker.DoWork += delegate
                {
                    var logFile = new LogFile();
                    logFile.LogEntries = new List<BaseLogEntry_Serialized>();
                    logFile.VariableDefinitions = new List<VariableDefinitionBase_Serialized>();
                    logFile.ReadVariables = new List<ReadVariableBase_Serialized>();

                    //only add read variables with definitions
                    foreach (var readVariable in ReadVariables)
                    {
                        if (mVariableDefinitionsMap.ContainsKey(readVariable.VariableID))
                        {
                            logFile.ReadVariables.Add(readVariable.SaveSerialized());
                        }
                    }

                    foreach (var logEntry in LogEntries)
                    {
						var savedEntry = logEntry.SaveSerialized();

						if(savedEntry is MultiVariableLogEntry_Serialized)
						{
							var multiVarSavedEntry = savedEntry as MultiVariableLogEntry_Serialized;

							var removedItems = new List<BaseVariable_LogEntryItem>();

							foreach (var entryItem in multiVarSavedEntry.EntryItems)
							{
								if (!mReadVariablesMap.ContainsKey(entryItem.VariableID))
								{
									removedItems.Add(entryItem);
								}
							}

							foreach(var removedItem in removedItems)
							{
								multiVarSavedEntry.EntryItems.Remove(removedItem);
							}

							if (multiVarSavedEntry.EntryItems.Any())
							{
								logFile.LogEntries.Add(multiVarSavedEntry);
							}
						}
						else if(savedEntry is MemoryVariableLogEntry_Serialized)
						{
							var memVarSavedEntry = savedEntry as MemoryVariableLogEntry_Serialized;

							if (mReadVariablesMap.ContainsKey(memVarSavedEntry.VariableID))
							{
								logFile.LogEntries.Add(memVarSavedEntry);
							}
						}
						else
						{
							logFile.LogEntries.Add(savedEntry);
						}
                    }

                    //only add definitions that were read
                    foreach (var variable in VariableDefinitions)
                    {
                        if (mReadVariablesMap.ContainsKey(variable.VariableID))
                        {
                            logFile.VariableDefinitions.Add(variable.SaveSerialized());
                        }
                    }

                    try
                    {
                        using (var fileStream = File.Open(logFileName, FileMode.Create, FileAccess.Write))
                        {
                            App.SerializeToXML(fileStream, logFile);

                            successfullySavedFile = true;
                        }
                    }
                    catch (Exception e)
                    {
                        App.DisplayStatusMessage("Log file not saved, exception encountered: " + e.Message, StatusMessageType.USER);
                    }
                };

                worker.RunWorkerCompleted += delegate
                {
                    if (successfullySavedFile)
                    {
                        App.DisplayStatusMessage("Log file successfully saved to: " + logFileName, StatusMessageType.USER);
                    }

                    EnableUserInput();
                };

                worker.RunWorkerAsync();
            }
            else
            {
                App.DisplayStatusMessage("Log file not saved, no file name.", StatusMessageType.USER);
            }
        }

        public ICommand LoadLogFileCommand
        {
            get
            {
                if (_LoadLogFileCommand == null)
                {
                    _LoadLogFileCommand = new ReactiveCommand(this.OnLoadLogFile);
                    _LoadLogFileCommand.Name = "Load Log";
                    _LoadLogFileCommand.Description = "Load recorded variables from log file";

                    _LoadLogFileCommand.AddWatchedProperty(this, "IsReadingVariables");

                    _LoadLogFileCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (IsReadingVariables)
                        {
                            result = false;
                            reasonsDisabled.Add("Currently reading variables");
                        }

                        return result;
                    };
                }

                return _LoadLogFileCommand;
            }
        }
        private ReactiveCommand _LoadLogFileCommand;

        private void OnLoadLogFile()
        {
            OpenFileDialog dialog = new OpenFileDialog();
			dialog.Filter = LOG_FILE_FILTER;
            dialog.CheckFileExists = true;
            dialog.CheckPathExists = true;
            dialog.InitialDirectory = Directory.GetCurrentDirectory();
            dialog.Title = "Select Log File to Load";

            if (dialog.ShowDialog() == true)
            {
               LoadLogFile(dialog.FileName);
            }
        }

        private void LoadLogFile(string logFileName)
        {
            App.DisplayStatusMessage("Loading log file.", StatusMessageType.USER);

            if (!String.IsNullOrEmpty(logFileName))
            {
                DisableUserInput();

                VariableDefinitions.Clear();
                ReadVariables.Clear();
                LogEntries = null;

                LogFile logFile = null;                

                var worker = new BackgroundWorker();

                worker.DoWork += (object sender, DoWorkEventArgs e) =>
                {
                    Thread.Sleep(1);//yield

                    try
                    {
                        using (var fStream = new FileStream(logFileName, FileMode.Open, FileAccess.Read))
                        {
                            var xmlFormat = new XmlSerializer(typeof(LogFile));
                            logFile = (LogFile)xmlFormat.Deserialize(fStream);                            
                        }
                    }
                    catch (Exception ex)
                    {
                        App.DisplayStatusMessage("Log file not loaded, exception encountered: " + ex.Message, StatusMessageType.USER);
                    }

                    if (logFile != null)
                    {                        
                        //TODO: need to use a factory, or serialized objects with static create methods to support multiple types
                        foreach (var variableData in logFile.VariableDefinitions)
                        {
                            Dispatcher.Invoke((Action)(() => 
                            {
                                var newDefinition = new MemoryVariableDefinition_ViewModel();
                                newDefinition.LoadSerialized(variableData);
                                VariableDefinitions.Add(newDefinition);
                            
                            }), null);

                            Thread.Sleep(1);//yield
                        }

                        //TODO: need to use a factory, or serialized objects with static create methods to support multiple types
                        foreach (var variableData in logFile.ReadVariables)
                        {   
                            Dispatcher.Invoke((Action)(() => 
                            { 
                                var readVariable = new ReadMemoryVariable_ViewModel();
                                readVariable.LoadSerialized(variableData);
                                readVariable.BindToVariableDefinition(mVariableDefinitionsMap);
                                ReadVariables.Add(readVariable);

                            }), null);

                            Thread.Sleep(1);//yield
                        }

						var newLogEntries = new ObservableCollection<VariableLogEntry>();

                        //TODO: need to use a factory, or serialized objects with static create methods to support multiple types
                        foreach (var logEntry in logFile.LogEntries)
                        {
                            //we don't create the log entries in the UI dispatcher thread because it is way slower and only required for dependency objects
                            {
                                var newEntry = new VariableLogEntry();
                                newEntry.LoadSerialized(logEntry);
                                newLogEntries.Add(newEntry);
                            }
                        }

                        //set this way to only trigger one change notification for the whole list
                        Dispatcher.Invoke((Action)(() => 
                        {
#if DEBUG
                            foreach (var logEntry in newLogEntries)
                            {
								foreach (var logEntryItem in logEntry.EntryItems.OfType<MemoryVariable_LogEntryItem>())
								{
									Debug.Assert(mVariableDefinitionsMap.ContainsKey(logEntryItem.VariableID));

									if (mVariableDefinitionsMap.ContainsKey(logEntryItem.VariableID))
									{
										var definition = mVariableDefinitionsMap[logEntryItem.VariableID] as MemoryVariableDefinition_ViewModel;

										Debug.Assert(definition != null);

										if (definition != null)
										{
											Debug.Assert(DataUtils.GetDataTypeSize(definition.DataType) == logEntryItem.RawData.Length);
										}
									}
								}
                            }
#endif                      
                            LogEntries = newLogEntries;
                        }), null);
                    }
                };

                worker.RunWorkerCompleted += (object sender, RunWorkerCompletedEventArgs e) =>
                {
                    EnableUserInput();

                    if (logFile != null)
                    {
                        App.DisplayStatusMessage("Log file successfully loaded from: " + logFileName, StatusMessageType.USER);
                    }
                };

                worker.RunWorkerAsync();
            }
            else
            {
                App.DisplayStatusMessage("Log file not loaded, no file name.", StatusMessageType.USER);
            }
        }

        public ICommand LoadVariableDefinitionsFromFileCommand
        {
            get
            {
                if (_LoadVariableDefinitionsFromFileCommand == null)
                {
                    _LoadVariableDefinitionsFromFileCommand = new ReactiveCommand(this.OnLoadVariableDefinitionsFromFile);
                    _LoadVariableDefinitionsFromFileCommand.Name = "Load Variable Definitions";
                    _LoadVariableDefinitionsFromFileCommand.Description = "Load variable definitions from file and replace the current variable definitions";
                }

                return _LoadVariableDefinitionsFromFileCommand;
            }
        }
        private ReactiveCommand _LoadVariableDefinitionsFromFileCommand;

        private void OnLoadVariableDefinitionsFromFile()
        {
            OpenFileDialog dialog = new OpenFileDialog();
			dialog.Filter = VARIABLE_DEFINITIONS_FILE_FILTER;
            dialog.Filter += "|" + ME7LoggerECUFile.FILE_FILTER;
            dialog.CheckFileExists = true;
            dialog.CheckPathExists = true;
            dialog.InitialDirectory = Directory.GetCurrentDirectory();
            dialog.Title = "Select Variable Definitions File to Load";

            if (dialog.ShowDialog() == true)
            {
                if (dialog.FileName.EndsWith(ME7LoggerECUFile.FILE_EXT))
                {
                    LoadVariableDefinitionsFromME7LoggerECUFile(dialog.FileName);
                }
                else
                {
                    LoadVariableDefinitionsFromFile(dialog.FileName);
                }
            }
        }

        private void LoadVariableDefinitionsFromME7LoggerECUFile(string me7LoggerECUFileName)
        {
            App.DisplayStatusMessage("Loading variable definitions.", StatusMessageType.USER);

            if (!String.IsNullOrEmpty(me7LoggerECUFileName))
            {
                DisableUserInput();

                VariableDefinitions = null;
                LogEntries.Clear();//variables and log entries no longer match so we need to clear them

                bool loadedME7LoggerFile = false;
                
                var loadWorker = new BackgroundWorker();

                loadWorker.DoWork += (object sender, DoWorkEventArgs e) =>
                {
                    Thread.Sleep(1);//yield

                    var ME7LoggerFile = new ME7LoggerECUFile();
                    ME7LoggerFile.DisplayStatusMessageEvent += App.DisplayStatusMessage;
                    loadedME7LoggerFile = ME7LoggerFile.LoadFromFile(me7LoggerECUFileName);

                    if(!loadedME7LoggerFile)
                    {
                        App.DisplayStatusMessage("Loading variable definitions failed.", StatusMessageType.USER);
                    }
                    else
                    {
                        //TODO: use a factory method
                        foreach (var measurementEntry in ME7LoggerFile.Measurements)
                        {
                            var variableData = new MemoryVariableDefinition_Serialized();
                            variableData.VariableID = measurementEntry.VariableID;                            

                            if(string.IsNullOrEmpty(measurementEntry.Name))
                            {
                                variableData.Name = measurementEntry.VariableID;
                            }
                            else
                            {
                                variableData.Name = measurementEntry.Name;
                            }

                            variableData.Units = measurementEntry.Units;
                            variableData.Description = measurementEntry.Description;
                            variableData.Address = measurementEntry.Address.ToString();

                            if (measurementEntry.IsSigned)
                            {
                                if (measurementEntry.NumBytes == 1)
                                {
                                    variableData.DataType = DataUtils.DataType.Int8;
                                }
                                else if(measurementEntry.NumBytes == 2)
                                {
                                    variableData.DataType = DataUtils.DataType.Int16;
                                }
                                else if (measurementEntry.NumBytes == 4)
                                {
                                    variableData.DataType = DataUtils.DataType.Int32;
                                }
                            }
                            else
                            {
                                if (measurementEntry.NumBytes == 1)
                                {
                                    variableData.DataType = DataUtils.DataType.UInt8;
                                }
                                else if (measurementEntry.NumBytes == 2)
                                {
                                    variableData.DataType = DataUtils.DataType.UInt16;
                                }
                                else if (measurementEntry.NumBytes == 4)
                                {
                                    variableData.DataType = DataUtils.DataType.UInt32;
                                }
                            }

                            if (!measurementEntry.IsInverseConversion)
                            {
                                if (measurementEntry.BitMask != 0)
                                {
                                    measurementEntry.Offset = 0;
                                    measurementEntry.ScaleFactor = 1.0 / measurementEntry.BitMask;
                                }
                                
                                variableData.ValueConverter = new ScaleOffsetMemoryVariableValueConverter(measurementEntry.ScaleFactor, measurementEntry.Offset * -1.0f, measurementEntry.BitMask);

                                Dispatcher.Invoke((Action)(() =>
                                {
                                    var newDefinition = new MemoryVariableDefinition_ViewModel();
                                    newDefinition.LoadSerialized(variableData);
                                    VariableDefinitions.Add(newDefinition);

                                }), null);
                            }
                            else
                            {
                                App.DisplayStatusMessage("Could not load variable definition \"" + measurementEntry.VariableID + "\" because inverse value conversions are not supported.", StatusMessageType.USER);
                            }

                            Thread.Sleep(1);//yield
                        }
                    }
                };

                loadWorker.RunWorkerCompleted += (object sender, RunWorkerCompletedEventArgs e) =>
                {
                    EnableUserInput();

                    if (loadedME7LoggerFile)
                    {
                        App.DisplayStatusMessage("Successfully loaded variable definitions from: " + me7LoggerECUFileName, StatusMessageType.USER);
                    }
                };

                loadWorker.RunWorkerAsync();
            }
            else
            {
                App.DisplayStatusMessage("Loading variable definitions failed, no file name.", StatusMessageType.USER);
            }
        }

        private void LoadVariableDefinitionsFromFile(string variableDefinitionsFileName)
        {
            App.DisplayStatusMessage("Loading variable definitions.", StatusMessageType.USER);

            if (!String.IsNullOrEmpty(variableDefinitionsFileName))
            {
                DisableUserInput();

                VariableDefinitions = null;
                LogEntries.Clear();//variables and log entries no longer match so we need to clear them

                VariableDefinitionsFile variableDefinitionsFile = null;
                
                var loadWorker = new BackgroundWorker();

                loadWorker.DoWork += (object sender, DoWorkEventArgs e) =>
                {
                    Thread.Sleep(1);//yield

                    try
                    {
                        using (var fStream = new FileStream(variableDefinitionsFileName, FileMode.Open, FileAccess.Read))
                        {
                            var xmlFormat = new XmlSerializer(typeof(VariableDefinitionsFile));
                            variableDefinitionsFile = (VariableDefinitionsFile)xmlFormat.Deserialize(fStream);                            
                        }
                    }
                    catch (Exception ex)
                    {
                        App.DisplayStatusMessage("Loading variable definitions failed, exception encountered: " + ex.Message, StatusMessageType.USER);
                    }

                    if (variableDefinitionsFile != null)
                    {
                        //TODO: use a factory method
                        foreach (var variableData in variableDefinitionsFile.VariableDefinitions)
                        {
                            Dispatcher.Invoke((Action)(() =>
                            {
                                var newDefinition = new MemoryVariableDefinition_ViewModel();
                                newDefinition.LoadSerialized(variableData);
                                VariableDefinitions.Add(newDefinition);

                            }), null);

                            Thread.Sleep(1);//yield
                        }
                    }                    
                };

                loadWorker.RunWorkerCompleted += (object sender, RunWorkerCompletedEventArgs e) =>
                {   
                    EnableUserInput();

                    if (variableDefinitionsFile != null)
                    {
                        App.DisplayStatusMessage("Successfully loaded variable definitions from: " + variableDefinitionsFileName, StatusMessageType.USER);
                    }
                };

                loadWorker.RunWorkerAsync();                
            }
            else
            {
                App.DisplayStatusMessage("Loading variable definitions failed, no file name.", StatusMessageType.USER);
            }
        }

        public ICommand SaveVariableDefinitionsToFileCommand
        {
            get
            {
                if (_SaveVariableDefinitionsToFileCommand == null)
                {
                    _SaveVariableDefinitionsToFileCommand = new ReactiveCommand(this.OnSaveVariableDefinitionsToFile);
                    _SaveVariableDefinitionsToFileCommand.Name = "Save Variable Definitions";
                    _SaveVariableDefinitionsToFileCommand.Description = "Save current variable definitions to file";

                    _SaveVariableDefinitionsToFileCommand.AddWatchedCollection(this, "VariableDefinitions", VariableDefinitions);

                    _SaveVariableDefinitionsToFileCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if ((VariableDefinitions == null) || !VariableDefinitions.Any())
                        {
                            result = false;
                            reasonsDisabled.Add("There are no variable definitions to save");
                        }

                        return result;
                    };
                }

                return _SaveVariableDefinitionsToFileCommand;
            }
        }
        private ReactiveCommand _SaveVariableDefinitionsToFileCommand;

        private void OnSaveVariableDefinitionsToFile()
        {
            var dialog = new SaveFileDialog();
			dialog.DefaultExt = VARIABLE_DEFINITIONS_FILE_EXT;//gets long extensions to work properly when they are added to a filename when saved
			dialog.Filter = VARIABLE_DEFINITIONS_FILE_FILTER;
            dialog.InitialDirectory = Directory.GetCurrentDirectory();
            dialog.OverwritePrompt = true;
            dialog.Title = "Select Where to Save Variable Definitions File";

            if (dialog.ShowDialog() == true)
            {
                //replace .xml with .variables.xml
				var actualFileName = ExtensionFixer.SwitchToLongExtension(dialog.FileName, VARIABLE_DEFINITIONS_FILE_SHORT_EXT, VARIABLE_DEFINITIONS_FILE_EXT);

                SaveVariableDefinitionsToFile(actualFileName);
            }
        }

        private void SaveVariableDefinitionsToFile(string variableDefinitionsFileName)
        {
            App.DisplayStatusMessage("Saving variable definitions.", StatusMessageType.USER);

            if (!String.IsNullOrEmpty(variableDefinitionsFileName))
            {
                DisableUserInput();

                bool successfullyWroteFile = false;

                var worker = new BackgroundWorker();

                worker.DoWork += delegate
                {
                    var variableDefinitionsFile = new VariableDefinitionsFile();
                    variableDefinitionsFile.VariableDefinitions = new List<VariableDefinitionBase_Serialized>();

                    foreach (var variable in VariableDefinitions)
                    {
                        variableDefinitionsFile.VariableDefinitions.Add(variable.SaveSerialized());
                    }

                    try
                    {
                        using (var fileStream = File.Open(variableDefinitionsFileName, FileMode.Create, FileAccess.Write))
                        {
                            App.SerializeToXML(fileStream, variableDefinitionsFile);

                            successfullyWroteFile = true;
                        }
                    }
                    catch (Exception e)
                    {
                        App.DisplayStatusMessage("Saving variable definitions failed, exception encountered: " + e.Message, StatusMessageType.USER);
                    }
                };

                worker.RunWorkerCompleted += delegate
                {
                    if (successfullyWroteFile)
                    {
                        App.DisplayStatusMessage("Successfully saved variable definitions to: " + variableDefinitionsFileName, StatusMessageType.USER);
                    }

                    EnableUserInput();
                };

                worker.RunWorkerAsync();
            }
            else
            {
                App.DisplayStatusMessage("Saving variable definitions failed, no file name.", StatusMessageType.USER);
            }
        }

		private const string VARIABLE_DEFINITIONS_FILE_SHORT_EXT = ".xml";
        private const string VARIABLE_DEFINITIONS_FILE_EXT = ".VariableDefinitions" + VARIABLE_DEFINITIONS_FILE_SHORT_EXT;
		private readonly string VARIABLE_DEFINITIONS_FILE_FILTER = "Variable Definitions (*" + VARIABLE_DEFINITIONS_FILE_EXT + ")|*" + VARIABLE_DEFINITIONS_FILE_EXT;

        private const string READ_VARIABLES_FILE_SHORT_EXT = ".xml";
		private const string READ_VARIABLES_FILE_EXT = ".ReadVariables" + READ_VARIABLES_FILE_SHORT_EXT;
		private readonly string READ_VARIABLES_FILE_FILTER = "Read Variables (*" + READ_VARIABLES_FILE_EXT + ")|*" + READ_VARIABLES_FILE_EXT;

        public ICommand LoadReadVariablesFromFileCommand
        {
            get
            {
                if (_LoadReadVariablesFromFileCommand == null)
                {
                    _LoadReadVariablesFromFileCommand = new ReactiveCommand(this.OnLoadReadVariablesFromFile);
                    _LoadReadVariablesFromFileCommand.Name = "Load Read Variables";
                    _LoadReadVariablesFromFileCommand.Description = "Load read variables from file and replace the current read variables";
                }

                return _LoadReadVariablesFromFileCommand;
            }
        }
        private ReactiveCommand _LoadReadVariablesFromFileCommand;

        private void OnLoadReadVariablesFromFile()
        {
            OpenFileDialog dialog = new OpenFileDialog();
			dialog.Filter = READ_VARIABLES_FILE_FILTER;
            dialog.CheckFileExists = true;
            dialog.CheckPathExists = true;
            dialog.InitialDirectory = Directory.GetCurrentDirectory();
            dialog.Title = "Select Read Variables File to Load";

            if (dialog.ShowDialog() == true)
            {
                LoadReadVariablesFromFile(dialog.FileName);
            }
        }

        private void LoadReadVariablesFromFile(string readVariablesFileName)
        {
            App.DisplayStatusMessage("Loading read variables.", StatusMessageType.USER);

            if (!String.IsNullOrEmpty(readVariablesFileName))
            {
                DisableUserInput();

                ReadVariables = null;
                LogEntries.Clear();//read variables and log entries no longer match so we need to clear them

                ReadVariablesFile readVariablesFile = null;

                var loadWorker = new BackgroundWorker();

                loadWorker.DoWork += (object sender, DoWorkEventArgs e) =>
                {
                    Thread.Sleep(1);//yield

                    try
                    {
                        using (var fStream = new FileStream(readVariablesFileName, FileMode.Open, FileAccess.Read))
                        {
                            var xmlFormat = new XmlSerializer(typeof(ReadVariablesFile));
                            readVariablesFile = (ReadVariablesFile)xmlFormat.Deserialize(fStream);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.DisplayStatusMessage("Loading read variables failed, exception encountered: " + ex.Message, StatusMessageType.USER);
                    }

                    if (readVariablesFile != null)
                    {
                        //TODO: use a factory method
                        foreach (var variableData in readVariablesFile.ReadVariables)
                        {
                            Dispatcher.Invoke((Action)(() =>
                            {
                                var newReadVar = new ReadMemoryVariable_ViewModel();
                                newReadVar.LoadSerialized(variableData);
                                newReadVar.BindToVariableDefinition(mVariableDefinitionsMap);
                                ReadVariables.Add(newReadVar);

                            }), null);

                            Thread.Sleep(1);//yield
                        }
                    }
                };

                loadWorker.RunWorkerCompleted += (object sender, RunWorkerCompletedEventArgs e) =>
                {
                    EnableUserInput();

                    if (readVariablesFile != null)
                    {
                        App.DisplayStatusMessage("Successfully loaded read variables from: " + readVariablesFile, StatusMessageType.USER);
                    }
                };

                loadWorker.RunWorkerAsync();
            }
            else
            {
                App.DisplayStatusMessage("Loading read variables failed, no file name.", StatusMessageType.USER);
            }
        }

        public ICommand SaveReadVariablesToFileCommand
        {
            get
            {
                if (_SaveReadVariablesToFileCommand == null)
                {
                    _SaveReadVariablesToFileCommand = new ReactiveCommand(this.OnSaveReadVariablesToFile);
                    _SaveReadVariablesToFileCommand.Name = "Save Read Variables";
                    _SaveReadVariablesToFileCommand.Description = "Save current read variables to file";

                    _SaveReadVariablesToFileCommand.AddWatchedCollection(this, "ReadVariables", ReadVariables);

                    _SaveReadVariablesToFileCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if ((ReadVariables == null) || !ReadVariables.Any())
                        {
                            result = false;
                            reasonsDisabled.Add("There are no read variables to save");
                        }

                        return result;
                    };
                }

                return _SaveReadVariablesToFileCommand;
            }
        }
        private ReactiveCommand _SaveReadVariablesToFileCommand;

        private void OnSaveReadVariablesToFile()
        {
            var dialog = new SaveFileDialog();
			dialog.DefaultExt = READ_VARIABLES_FILE_EXT;//gets long extensions to work properly when they are added to a filename when saved
			dialog.Filter = READ_VARIABLES_FILE_FILTER;
            dialog.InitialDirectory = Directory.GetCurrentDirectory();
            dialog.OverwritePrompt = true;
            dialog.Title = "Select Where to Save Read Variables File";

            if (dialog.ShowDialog() == true)
            {
                //replace .xml with .variables.xml
				var actualFileName = ExtensionFixer.SwitchToLongExtension(dialog.FileName, READ_VARIABLES_FILE_SHORT_EXT, READ_VARIABLES_FILE_EXT);

                SaveReadVariablesToFile(actualFileName);
            }
        }

        private void SaveReadVariablesToFile(string readVariablesFileName)
        {
            App.DisplayStatusMessage("Saving read variables.", StatusMessageType.USER);

            if (!String.IsNullOrEmpty(readVariablesFileName))
            {
                DisableUserInput();

                bool successfullyWroteFile = false;

                var worker = new BackgroundWorker();

                worker.DoWork += delegate
                {
                    var readVariablesFile = new ReadVariablesFile();
                    readVariablesFile.ReadVariables = new List<ReadVariableBase_Serialized>();

                    foreach (var variable in ReadVariables)
                    {
                        readVariablesFile.ReadVariables.Add(variable.SaveSerialized());
                    }
                    
                    try
                    {
                        using (var fileStream = File.Open(readVariablesFileName, FileMode.Create, FileAccess.Write))
                        {
                            App.SerializeToXML(fileStream, readVariablesFile);

                            successfullyWroteFile = true;
                        }
                    }
                    catch (Exception e)
                    {
                        App.DisplayStatusMessage("Saving read variables failed, exception encountered: " + e.Message, StatusMessageType.USER);
                    }
                };

                worker.RunWorkerCompleted += delegate
                {
                    if (successfullyWroteFile)
                    {
                        App.DisplayStatusMessage("Successfully saved read variables to: " + readVariablesFileName, StatusMessageType.USER);
                    }

                    EnableUserInput();
                };

                worker.RunWorkerAsync();
            }
            else
            {
                App.DisplayStatusMessage("Saving read variables failed, no file name.", StatusMessageType.USER);
            }
        }

        public TimeSpan SkipTime
        {
            get { return _SkipTime; }
            set
            {
                if (_SkipTime != value)
                {
                    _SkipTime = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("SkipTime"));
                }
            }
        }
        private TimeSpan _SkipTime = TimeSpan.FromSeconds(30);

        public ICommand SkipAheadCommand
        {
            get
            {
                if (_SkipAheadCommand == null)
                {
                    _SkipAheadCommand = new ReactiveCommand(delegate(object param) { LogCurrentTime += SkipTime; });
                    _SkipAheadCommand.Name = "Advance Log";
                    _SkipAheadCommand.Description = "Advance the current log time";

                    _SkipAheadCommand.AddWatchedProperty(this, "IsReadingVariables");
                    _SkipAheadCommand.AddWatchedProperty(this, "LogCurrentTime");
                    _SkipAheadCommand.AddWatchedProperty(this, "LogTimeSpan");

                    _SkipAheadCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (IsReadingVariables)
                        {
                            result = false;
                            reasonsDisabled.Add("Currently reading variables");
                        }

                        if (LogCurrentTime >= LogTimeSpan)
                        {
                            result = false;
                            reasonsDisabled.Add("Already at end of log");
                        }

                        return result;
                    };
                }

                return _SkipAheadCommand;
            }
        }
        private ReactiveCommand _SkipAheadCommand;

        public ICommand SkipBackCommand
        {
            get
            {
                if (_SkipBackCommand == null)
                {
                    _SkipBackCommand = new ReactiveCommand(delegate(object param) { LogCurrentTime -= SkipTime; });
                    _SkipBackCommand.Name = "Rewind Log";
                    _SkipBackCommand.Description = "Rewind the current log time";

                    _SkipBackCommand.AddWatchedProperty(this, "IsReadingVariables");
                    _SkipBackCommand.AddWatchedProperty(this, "LogCurrentTime");
                    
                    _SkipBackCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {
                        bool result = true;

                        if (IsReadingVariables)
                        {
                            result = false;
                            reasonsDisabled.Add("Currently reading variables");
                        }

                        if (LogCurrentTime <= TimeSpan.Zero)
                        {
                            result = false;
                            reasonsDisabled.Add("Already at start of log");
                        }

                        return result;
                    };
                }

                return _SkipBackCommand;
            }
        }
        private ReactiveCommand _SkipBackCommand;
        
        public ICommand PauseLogCommand
        {
            get
            {
                if (_PauseLogCommand == null)
                {
                    _PauseLogCommand = new ReactiveCommand(delegate() { StopPlaying(); });
                    _PauseLogCommand.Name = "Pause Log Playback";
                    _PauseLogCommand.Description = "Pause log playback";

                    _PauseLogCommand.AddWatchedProperty(this, "IsLogPlaying");
                    
                    _PauseLogCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {                        
                        bool result = true;

                        if (!IsLogPlaying)
                        {
                            reasonsDisabled.Add("Log playback is not running");
                            result = false;
                        }
                        
                        return result;
                    };
                }

                return _PauseLogCommand;
            }

        }
        private ReactiveCommand _PauseLogCommand;

        public ICommand PlayLogCommand
        {
            get
            {
                if (_PlayLogCommand == null)
                {
                    _PlayLogCommand = new ReactiveCommand(delegate() { StartPlaying(); });
                    _PlayLogCommand.Name = "Start Log Playback";
                    _PlayLogCommand.Description = "Start log playback";

                    _PlayLogCommand.AddWatchedProperty(this, "IsLogPlaying");
                    _PlayLogCommand.AddWatchedProperty(this, "IsReadingVariables");
                    _PlayLogCommand.AddWatchedCollection(this, "LogEntries", LogEntries);

                    _PlayLogCommand.CanExecuteMethod = delegate(List<string> reasonsDisabled)
                    {                        
                        bool result = true;

						if (IsReadingVariables)
						{
							result = false;
							reasonsDisabled.Add("Currently reading variables");
						}

                        if (IsLogPlaying)
                        {
                            result = false;
                            reasonsDisabled.Add("Log playback is already running");
                        }

                        if ((LogEntries == null) || !LogEntries.Any())
                        {
                            result = false;
                            reasonsDisabled.Add("There are no log entries to playback");
                        }

                        return result;
                    };
                }

                return _PlayLogCommand;
            }

        }
        private ReactiveCommand _PlayLogCommand;

        public double PlayBackSpeed
        {
            get { return _PlayBackSpeed; }
            set
            {
                if (_PlayBackSpeed != value)
                {
                    _PlayBackSpeed = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("PlayBackSpeed"));
                }
            }
        }
        private double _PlayBackSpeed = 1.0;

        private DispatcherTimer mLogPlaybackTimer;
        public void StartPlaying()
        {
            if (!IsLogPlaying && (LogCurrentTime < LogTimeSpan))
            {                
                IsLogPlaying = true;

                App.DisplayStatusMessage("Starting variable log playback.", StatusMessageType.USER);

                mLogPlaybackTimer = new DispatcherTimer(DispatcherPriority.Render);
				mLogPlaybackTimer.Interval = TimeSpan.FromMilliseconds(25);
                mLogPlaybackTimer.Tick += delegate
                {
                    LogCurrentTime += TimeSpan.FromSeconds(mLogPlaybackTimer.Interval.TotalSeconds * PlayBackSpeed);

                    if(LogCurrentTime >= LogTimeSpan)
                    {
                        StopPlaying();
                    }
                };

                App.DisplayStatusMessage("Started variable log playback.", StatusMessageType.USER);

                mLogPlaybackTimer.Start();
            }
        }

        public void StopPlaying()
        {
            if (IsLogPlaying)
            {
                App.DisplayStatusMessage("Stopping variable log playback.", StatusMessageType.USER);

                mLogPlaybackTimer.IsEnabled = false;
                mLogPlaybackTimer.Stop();
                mLogPlaybackTimer = null;

                IsLogPlaying = false;

                App.DisplayStatusMessage("Stopped variable log playback.", StatusMessageType.USER);
            }
        }

        public bool IsLogPlaying
        {
            get{ return _IsLogPlaying; }
            private set
            {
                if (_IsLogPlaying != value)
                {
                    _IsLogPlaying = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("IsLogPlaying"));
                }
            }
        }
        private bool _IsLogPlaying = false;

        private IEnumerable<VariableLogEntry> GetLogEntriesInTimeRange(IEnumerator<VariableLogEntry> iterator, DateTime startTime, DateTime endTime, bool reverse)
        {
            var entriesInRange = new List<VariableLogEntry>();

            var logEntry = iterator.Current;

            while (logEntry != null)
            {
                if (reverse)
                {
                    if (logEntry.TimeStamp <= endTime)
                    {
                        if (logEntry.TimeStamp >= startTime)
                        {
                            entriesInRange.Add(logEntry);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    if (logEntry.TimeStamp >= startTime)
                    {
                        if (logEntry.TimeStamp <= endTime)
                        {
                            entriesInRange.Add(logEntry);
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                if (!iterator.MoveNext())
                {
                    break;
                }

                logEntry = iterator.Current;
            }

            return entriesInRange;
        }

        private void ApplyLogEntriesToVariables(IEnumerable<VariableLogEntry> logEntries, Dictionary<string, HashSet<ReadVariableBase_ViewModel>> readVariableMap)
        {
            if (logEntries != null)
            {
				var mostRecentVariableUpdates = new Dictionary<string, BaseVariable_LogEntryItem>();

				foreach (var logEntry in logEntries)
				{
					foreach (var logItem in logEntry.EntryItems)
					{
						mostRecentVariableUpdates[logItem.VariableID] = logItem;
					}
				}

				foreach (var logEntryItem in mostRecentVariableUpdates.Values)
				{
					HashSet<ReadVariableBase_ViewModel> idSet;
					if (readVariableMap.TryGetValue(logEntryItem.VariableID, out idSet))
					{
						Debug.Assert(idSet != null);

						foreach (var variable in idSet)
						{
							variable.UpdateValueFromLogEntry(logEntryItem);
						}
					}
				}
            }
        }

        private void EnableUserInput()
        {
            _UserInputDisableCount--;

            if (_UserInputDisableCount == 0)
            {
                Mouse.OverrideCursor = null;
                IsEnabled = true;
            }
        }

        private void DisableUserInput()
        {
            if (_UserInputDisableCount == 0)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                IsEnabled = false;
            }

            _UserInputDisableCount++;
        }
        private int _UserInputDisableCount = 0;

        private void ResortRenamedVariablesFilter(object filterSender, FilterEventArgs filterEventArgs)
        {
            var variable = filterEventArgs.Item as VariableDefinitionBase_ViewModel;
            filterEventArgs.Accepted = true;

            CollectionViewSourcePropertyWatcher.GetProperty(filterSender as DependencyObject).AddUniqueChangedPropertyWatcher(variable, 
				delegate(object propertyChangedSender, PropertyChangedEventArgs propertyChangedEventArgs)
				{
					if (propertyChangedEventArgs.PropertyName == "Name")
					{
						var filterSource = filterSender as CollectionViewSource;					

						var oldCurrentItem = filterSource.View.CurrentItem;

						if (filterSource.View is IEditableCollectionView)
						{
							var editableView = filterSource.View as IEditableCollectionView;

							editableView.EditItem(propertyChangedSender);
							editableView.CommitEdit();
						}
						else
						{
							filterSource.View.Refresh();

							//This causes problems with the caret being reset when editing the current item
							//trigger the object to be re-filtered by re-adding it							
							//var collection = filterSource.Source as IList<VariableDefinitionBase_ViewModel>;
							//collection.Remove(variable);
							//collection.Add(variable);
						}

						filterSource.View.MoveCurrentTo(oldCurrentItem);
					}
				});
        }

        private void AddReadVariableFromDefinitionInContentControl(object sender, RoutedEventArgs e)
        {
            var control = sender as ContentControl;

            if (control != null)
            {
                var definition = control.Content as VariableDefinitionBase_ViewModel;
                OnAddReadVariable(definition);
            }
        }

        private void RemoveReadVariableInContentControl(object sender, RoutedEventArgs e)
        {
            var control = sender as ContentControl;

            if (control != null)
            {
                var readVar = control.Content as ReadVariableBase_ViewModel;
                OnRemoveReadVariable(readVar);
            }
        }
    }
}
