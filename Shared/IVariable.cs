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
using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.Schema;

namespace Shared
{
	[Serializable]
    [XmlInclude(typeof(ScaleOffsetMemoryVariableValueConverter))]
    public abstract class VariableValueConverter : INotifyPropertyChanged
    {
        public abstract byte[] ConvertFrom(double val, DataUtils.DataType dataType);

        public abstract double ConvertTo(byte[] rawData, DataUtils.DataType dataType);
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    [Serializable]
    public class ScaleOffsetMemoryVariableValueConverter : VariableValueConverter, IXmlSerializable
    {
        //serialization requires a parameterless constructor
        public ScaleOffsetMemoryVariableValueConverter()
        {
        }

        public ScaleOffsetMemoryVariableValueConverter(double scale, double offset, uint bitmask)
        {
            if (bitmask == 0x0)
            {
                bitmask = 0xFFFFFFFF;
            }

            Scale = scale;
            Offset = offset;
            BitMask = bitmask;
        }

        public ScaleOffsetMemoryVariableValueConverter(double scale, double offset)
        {
            Scale = scale;
            Offset = offset;
        }

        public override double ConvertTo(byte[] rawData, DataUtils.DataType dataType)
        {
            double rawValue = 0.0;

            if (rawData != null)
            {
                if (BitMask != 0xFFFFFFFF)
                {
                    byte[] localRawData = new byte[rawData.Length];

                    for (int x = 0; x < rawData.Length; x++)
                    {
                        localRawData[x] = (byte)(rawData[x] & (BitMask >> x));
                    }

                    rawData = localRawData;
                }

                DataUtils.ReadRawFloatValueByType(out rawValue, dataType, rawData, 0);
            }

            return DataUtils.GetCorrectedValueFromRaw(rawValue, Scale, Offset);
        }

        public override byte[] ConvertFrom(double val, DataUtils.DataType dataType)
        {
            throw new NotImplementedException();
        }

        public XmlSchema GetSchema()
        {
            return null;
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteElementString("Scale", Scale.ToString());
            writer.WriteElementString("Offset", Offset.ToString());
            writer.WriteElementString("BitMask", BitMask.ToString());
        }

        public void ReadXml(XmlReader reader)
        {
            var isEmptyElement = reader.IsEmptyElement;

            reader.ReadStartElement();

            if (!isEmptyElement)
            {
                try
                {
                    Scale = double.Parse(reader.ReadElementString("Scale"));
                }
                catch
                {
                    Scale = 1.0;
                }
                try
                {
                    Offset = double.Parse(reader.ReadElementString("Offset"));
                }
                catch
                {
                    Offset = 0.0;
                }
                try
                {
					var bitmaskString = reader.ReadElementString("BitMask");

					if (bitmaskString.StartsWith("0x"))
					{
						BitMask = DataUtils.ReadHexString(bitmaskString);
					}
					else
					{
						BitMask = uint.Parse(bitmaskString);
					}
                }
                catch
                {
                    BitMask = 0xFFFFFFFF;
                }

                reader.ReadEndElement();
            }
        }

        public double Scale
        {
            get { return _Scale; }
            set
            {
                if (_Scale != value)
                {
                    _Scale = value;
                    NotifyPropertyChanged("Scale");
                }
            }
        }
        private double _Scale = 1.0;

        public double Offset
        {
            get { return _Offset; }
            set
            {
                if (_Offset != value)
                {
                    _Offset = value;
                    NotifyPropertyChanged("Offset");
                }
            }
        }
        private double _Offset = 0.0;

        public uint BitMask
        {
            get { return _BitMask; }
            set
            {
                if (_BitMask != value)
                {
                    _BitMask = value;
                    NotifyPropertyChanged("BitMask");
                }
            }
        }
        private uint _BitMask = 0xFFFFFFFF;
    }
}
