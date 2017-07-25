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
using System.Globalization;
using System.IO;

using Shared;

namespace ECUFlasher
{    
    class ME7LoggerMeasurmentEntry
    {
        public string VariableID;
        public string Name;
        public string Units;
        public string Description;

        public uint Address;
        public uint NumBytes;
        public bool IsSigned;

        //Normal conversion:  value = A * raw - B
        //Inverse conversion: value = A / (raw - B)

        public bool IsInverseConversion;
        public double ScaleFactor;
        public double Offset;
        public uint BitMask;
    };

    class ME7LoggerECUFile
    {
		public static readonly string FILE_TYPE = "ME7Logger ECU Definition";
        public static readonly string FILE_EXT = ".ecu";
		public static readonly string FILE_FILTER = FILE_TYPE + " (*" + FILE_EXT + ")|*" + FILE_EXT;        

        public string Version { get; private set; }
        public Dictionary<string, string> Identification { get; private set; }
        public Dictionary<string, string> Communication { get; private set; }
        public List<ME7LoggerMeasurmentEntry> Measurements { get; private set; }

        public event DisplayStatusMessageDelegate DisplayStatusMessageEvent;

        public ME7LoggerECUFile()
        {
            Identification = new Dictionary<string, string>();
            Communication = new Dictionary<string, string>();
            Measurements = new List<ME7LoggerMeasurmentEntry>();
        }

        public bool LoadFromFile(string fileName)
        {
            bool result = true;

            try
            {
                var fileLines = File.ReadAllLines(fileName, Encoding.Default);

                var categories = GetLinesByCategory(fileLines);

                if (categories != null)
                {
                    if (categories.ContainsKey("version"))
                    {
                        foreach (var line in categories["version"])
                        {
                            KeyValuePair<string, string> declaration;
                            if (ParseDeclaractionLineToken(line, out declaration))
                            {
                                if (declaration.Key == "Version")
                                {
                                    Version = declaration.Value;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        result = false;
                        DisplayStatusMessage("Failed to find category: version.", StatusMessageType.USER);
                    }

                    if (categories.ContainsKey("communication"))
                    {
                        foreach (var line in categories["communication"])
                        {
                            KeyValuePair<string, string> declaration;
                            if (ParseDeclaractionLineToken(line, out declaration))
                            {
                                Communication.Add(declaration.Key, declaration.Value);
                            }
                        }
                    }
                    else
                    {
                        result = false;
                        DisplayStatusMessage("Failed to find category: communication.", StatusMessageType.USER);
                    }

                    if (categories.ContainsKey("identification"))
                    {
                        foreach (var line in categories["identification"])
                        {
                            KeyValuePair<string, string> declaration;
                            if (ParseDeclaractionLineToken(line, out declaration))
                            {
                                Identification.Add(declaration.Key, declaration.Value);
                            }
                        }
                    }
                    else
                    {
                        result = false;
                        DisplayStatusMessage("Failed to find category: identification.", StatusMessageType.USER);
                    }

                    if (categories.ContainsKey("measurements"))
                    {
                        string[] measurementEntryKeys = { "VariableID", "Name", "Address", "NumBytes", "BitMask", "Units", "IsSigned", "IsInverted", "Scale", "Offset", "Description" };
                        var measurementEntryKeysList = new List<string>(measurementEntryKeys);

                        foreach (var line in categories["measurements"])
                        {
                            Measurements.Add(GetMeasurementEntryFromLine(line, measurementEntryKeysList));
                        }
                    }
                    else
                    {
                        result = false;
                        DisplayStatusMessage("Failed to find category: measurements.", StatusMessageType.USER);
                    }
                }
            }
            catch(Exception e)
            {
                DisplayStatusMessage("Encountered exception while loading file: " + e.ToString(), StatusMessageType.USER);

                result = false;
            }
            
            return result;
        }

        //categories are kept lowercase
        private Dictionary<string, List<string>> GetLinesByCategory(string[] lines)
        {
            var linesByCategory = new Dictionary<string, List<string>>();

            if (lines != null)
            {
                List<string> currentCategoryLines = null;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (!String.IsNullOrEmpty(trimmedLine))
                    {
                        char startingChar = trimmedLine[0];

                        switch (startingChar)
                        {
                            case ';':
                                {
                                    break;
                                }
                            case '[':
                                {
                                    string categoryName = "";

                                    int braceEndIndex = trimmedLine.IndexOf(']');

                                    if (braceEndIndex > 0)
                                    {
                                        categoryName = trimmedLine.Substring(1, braceEndIndex - 1).ToLowerInvariant();
                                    }

                                    currentCategoryLines = new List<string>();
                                    linesByCategory.Add(categoryName, currentCategoryLines);

                                    break;
                                }
                            default:
                                {
                                    currentCategoryLines.Add(trimmedLine);
                                    break;
                                }

                        }
                    }
                }
            }

            return linesByCategory;
        }

        private bool ParseDeclaractionLineToken(string inputLine, out KeyValuePair<string, string> declaration)
        {
            var key = new StringBuilder();
            var value = new StringBuilder();

            if (inputLine != null)
            {
                char[] inputLineChars = inputLine.ToCharArray();

                bool keyFinished = false;
                int currentIndex = 0;
                bool finished = false;

                while (!finished && (currentIndex < inputLine.Length))
                {
                    var nextChar = inputLineChars[currentIndex];

                    switch (nextChar)
                    {
                        case '{':
                            {
                                currentIndex++;

                                int braceEndIndex = inputLine.IndexOf('}', currentIndex);

                                if (braceEndIndex > currentIndex)
                                {
                                    int length = braceEndIndex - currentIndex;

                                    if (!keyFinished)
                                    {
                                        key.Append(inputLine.Substring(currentIndex, length));
                                    }
                                    else
                                    {
                                        value.Append(inputLine.Substring(currentIndex, length));
                                    }

                                    currentIndex += length;
                                }

                                break;
                            }
                        case ';':
                            {
                                finished = true;

                                break;
                            }
                        case '=':
                            {
                                keyFinished = true;
                                break;
                            }
                        default:
                            {
                                if (!char.IsWhiteSpace(nextChar))
                                {
                                    if (!keyFinished)
                                    {
                                        key.Append(nextChar);
                                    }
                                    else
                                    {
                                        value.Append(nextChar);
                                    }
                                }

                                break;
                            }
                    }

                    currentIndex++;
                }
            }

            declaration = new KeyValuePair<string, string>(key.ToString(), value.ToString());

            return (key.Length > 0);
        }

        //parses a CSV line into tokens
        //I believe the ME7Logger treats this line format as a MAP entry
        private List<string> ParseMeasurementLineTokens(string inputLine)
        {
            var tokens = new List<string>();

            if (inputLine != null)
            {
                char[] inputLineChars = inputLine.ToCharArray();

                var nextToken = new StringBuilder();
                int currentIndex = 0;
                bool finished = false;

                while (!finished && (currentIndex < inputLine.Length))
                {
                    var nextChar = inputLineChars[currentIndex];

                    switch (nextChar)
                    {
                        case '{':
                            {
                                currentIndex++;

                                int braceEndIndex = inputLine.IndexOf('}', currentIndex);

                                if (braceEndIndex > currentIndex)
                                {
                                    int length = braceEndIndex - currentIndex;
                                    nextToken.Append(inputLine.Substring(currentIndex, length));
                                    currentIndex += length;
                                }

                                break;
                            }
                        case ';':
                            {
                                tokens.Add(nextToken.ToString());
                                nextToken = null;
                                finished = true;

                                break;
                            }
                        case ',':
                            {
                                tokens.Add(nextToken.ToString());
                                nextToken.Length = 0;

                                break;
                            }
                        default:
                            {
                                if (!char.IsWhiteSpace(nextChar))
                                {
                                    nextToken.Append(nextChar);
                                }

                                break;
                            }
                    }

                    currentIndex++;
                }

                if (nextToken != null)
                {
                    tokens.Add(nextToken.ToString());
                }
            }

            return tokens;
        }

        private Dictionary<string, string> MapTokensToKeys(List<string> keys, List<string> tokens)
        {
            var map = new Dictionary<string, string>();

            if ((keys != null) && (tokens != null) && (keys.Count == tokens.Count))
            {
                var tokenIter = tokens.GetEnumerator();

                foreach (var key in keys)
                {
                    tokenIter.MoveNext();
                    map.Add(key, tokenIter.Current);
                }
            }

            return map;
        }

        private ME7LoggerMeasurmentEntry GetMeasurementEntryFromLine(string inputLine, List<string> measurmentEntryTokenKeys)
        {
            ME7LoggerMeasurmentEntry entry = null;

            if((inputLine != null) && (measurmentEntryTokenKeys != null))
            {
                var unKeyedMeasurementTokens = ParseMeasurementLineTokens(inputLine);
                var measurementTokens = MapTokensToKeys(measurmentEntryTokenKeys, unKeyedMeasurementTokens);

                entry = new ME7LoggerMeasurmentEntry();

                if (measurementTokens.ContainsKey("VariableID"))
                {
                    entry.VariableID = measurementTokens["VariableID"];
                }                

                if (measurementTokens.ContainsKey("Name"))
                {
                    entry.Name = measurementTokens["Name"];
                }

                if (measurementTokens.ContainsKey("Address"))
                {
                    entry.Address = DataUtils.ReadHexString(measurementTokens["Address"]);
                }

                if (measurementTokens.ContainsKey("NumBytes"))
                {
                    entry.NumBytes = DataUtils.ReadHexString(measurementTokens["NumBytes"]);
                }

                if (measurementTokens.ContainsKey("BitMask"))
                {
                    entry.BitMask = DataUtils.ReadHexString(measurementTokens["BitMask"]);
                }

                if (measurementTokens.ContainsKey("Units"))
                {
                    entry.Units = measurementTokens["Units"];
                }

                if(measurementTokens.ContainsKey("IsSigned"))
                {
                    uint isSignedUint = 0;
                    uint.TryParse(measurementTokens["IsSigned"], out isSignedUint);
                    entry.IsSigned = (isSignedUint == 1);
                }

                if(measurementTokens.ContainsKey("IsInverted"))
                {
                    uint isInverseConversionUint = 0;
                    uint.TryParse(measurementTokens["IsInverted"], out isInverseConversionUint);
                    entry.IsInverseConversion = (isInverseConversionUint == 1);
                }

                if (measurementTokens.ContainsKey("Scale"))
                {
                    double.TryParse(measurementTokens["Scale"], out entry.ScaleFactor);
                }

                if (measurementTokens.ContainsKey("Offset"))
                {
                    double.TryParse(measurementTokens["Offset"], out entry.Offset);
                }

                if (measurementTokens.ContainsKey("Description"))
                {
                    entry.Description = measurementTokens["Description"];
                }
            }

            return entry;
        }

        private void DisplayStatusMessage(string message, StatusMessageType messageType)
        {
            if (DisplayStatusMessageEvent != null)
            {
                DisplayStatusMessageEvent(message, messageType);
            }
        }
    }
}
