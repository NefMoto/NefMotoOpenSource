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
using System.Xml.Serialization;

using Communication;

namespace ApplicationShared
{
    public class BaseFile
    {
        [XmlAttribute]
        public string Version
        {
            get { return _Version; }
            set { _Version = value; }
        }
        private string _Version = new Version(1, 0, 0, 0).ToString();
    }

    [XmlType("DTCsFile")]
    public class DTCsFile : BaseFile
    {
        public const string SHORT_EXT = ".xml";
        public const string EXT = ".DTCs" + SHORT_EXT;
        public static readonly string FILTER = "DTCs (*" + EXT + ")|*" + EXT;

        [XmlArray]
        [XmlArrayItem(ElementName = "DTC", Type = typeof(KWP2000DTCInfo))]
        public List<KWP2000DTCInfo> DTCs
        {
            get
            {
                if (_DTCs == null)
                {
                    _DTCs = new List<KWP2000DTCInfo>();
                }

                return _DTCs;
            }
            set { _DTCs = value; }
        }
        private List<KWP2000DTCInfo> _DTCs;
    }

    [XmlType("IdentificationFile")]
    public class IdentificationFile : BaseFile
    {
        public const string SHORT_EXT = ".xml";
        public const string EXT = ".Info" + SHORT_EXT;
        public static readonly string FILTER = "ECU Info (*" + EXT + ")|*" + EXT;

        [XmlArray]
        [XmlArrayItem(ElementName = "IdentificationEntry", Type = typeof(KWP2000IdentificationOptionValue))]
        public List<KWP2000IdentificationOptionValue> IdentificationValues
        {
            get
            {
                if (_IdentificationValues == null)
                {
                    _IdentificationValues = new List<KWP2000IdentificationOptionValue>();
                }

                return _IdentificationValues;
            }
            set { _IdentificationValues = value; }
        }
        private List<KWP2000IdentificationOptionValue> _IdentificationValues;
    }
}

// vi: set sw=4 ts=8 expandtab:
