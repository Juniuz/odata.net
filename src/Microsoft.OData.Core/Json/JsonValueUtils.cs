﻿//---------------------------------------------------------------------
// <copyright file="JsonValueUtils.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData.Json
{
    #region Namespaces
    using System;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Xml;
    using Microsoft.OData.Buffers;
    using Microsoft.OData.Edm;
    #endregion Namespaces

    /// <summary>
    /// Provides helper method for converting data values to and from the OData JSON format.
    /// </summary>
    internal static class JsonValueUtils
    {
        /// <summary>
        /// PositiveInfinitySymbol used in OData Json format
        /// </summary>
        internal static readonly string ODataJsonPositiveInfinitySymbol = "INF";

        /// <summary>
        /// NegativeInfinitySymbol used in OData Json format
        /// </summary>
        internal static readonly string ODataJsonNegativeInfinitySymbol = "-INF";

        /// <summary>
        /// The NumberFormatInfo used in OData Json format.
        /// </summary>
        internal static readonly NumberFormatInfo ODataNumberFormatInfo = JsonValueUtils.InitializeODataNumberFormatInfo();


        /// <summary>
        /// Const tick value for calculating tick values.
        /// </summary>
        private static readonly long JsonDateTimeMinTimeTicks = (new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks;

        /// <summary>
        /// Characters which, if found inside a number, indicate that the number is a double when no other type information is available.
        /// </summary>
        private static readonly char[] DoubleIndicatingCharacters = new char[] { '.', 'e', 'E' };

        /// <summary>
        /// Map of special characters to strings.
        /// </summary>
        private static readonly string[] SpecialCharToEscapedStringMap = JsonValueUtils.CreateSpecialCharToEscapedStringMap();

        /// <summary>
        /// Write a boolean value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">The boolean value to write.</param>
        internal static void WriteValue(TextWriter writer, bool value)
        {
            Debug.Assert(writer != null, "writer != null");

            writer.Write(value ? JsonConstants.JsonTrueLiteral : JsonConstants.JsonFalseLiteral);
        }

        /// <summary>
        /// Write an integer value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Integer value to be written.</param>
        internal static void WriteValue(TextWriter writer, int value)
        {
            Debug.Assert(writer != null, "writer != null");

            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write a float value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Float value to be written.</param>
        internal static void WriteValue(TextWriter writer, float value)
        {
            Debug.Assert(writer != null, "writer != null");

            if (float.IsInfinity(value) || float.IsNaN(value))
            {
                JsonValueUtils.WriteQuoted(writer, value.ToString(JsonValueUtils.ODataNumberFormatInfo));
            }
            else
            {
                // float.ToString() supports a max scale of six,
                // whereas float.MinValue and float.MaxValue have 8 digits scale. Hence we need
                // to use XmlConvert in all other cases, except infinity
                writer.Write(XmlConvert.ToString(value));
            }
        }

        /// <summary>
        /// Write a short value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Short value to be written.</param>
        internal static void WriteValue(TextWriter writer, short value)
        {
            Debug.Assert(writer != null, "writer != null");

            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write a long value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Long value to be written.</param>
        internal static void WriteValue(TextWriter writer, long value)
        {
            Debug.Assert(writer != null, "writer != null");

            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write a double value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Double value to be written.</param>
        internal static void WriteValue(TextWriter writer, double value)
        {
            Debug.Assert(writer != null, "writer != null");

            if (JsonSharedUtils.IsDoubleValueSerializedAsString(value))
            {
                JsonValueUtils.WriteQuoted(writer, value.ToString(JsonValueUtils.ODataNumberFormatInfo));
            }
            else
            {
                // double.ToString() supports a max scale of 14,
                // whereas double.MinValue and double.MaxValue have 16 digits scale. Hence we need
                // to use XmlConvert in all other cases, except infinity
                string valueToWrite = XmlConvert.ToString(value);

                writer.Write(valueToWrite);
                if (valueToWrite.IndexOfAny(JsonValueUtils.DoubleIndicatingCharacters) < 0)
                {
                    writer.Write(".0");
                }
            }
        }

        /// <summary>
        /// Write a Guid value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Guid value to be written.</param>
        internal static void WriteValue(TextWriter writer, Guid value)
        {
            Debug.Assert(writer != null, "writer != null");

            JsonValueUtils.WriteQuoted(writer, value.ToString());
        }

        /// <summary>
        /// Write a decimal value
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Decimal value to be written.</param>
        internal static void WriteValue(TextWriter writer, decimal value)
        {
            Debug.Assert(writer != null, "writer != null");

            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write a DateTimeOffset value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">DateTimeOffset value to be written.</param>
        /// <param name="dateTimeFormat">The format to write out the DateTime value in.</param>
        [SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Microsoft.OData.Json.JsonValueUtils.WriteQuoted(System.IO.TextWriter,System.String)", Justification = "Constant defined by the JSON spec.")]
        internal static void WriteValue(TextWriter writer, DateTimeOffset value, ODataJsonDateTimeFormat dateTimeFormat)
        {
            Debug.Assert(writer != null, "writer != null");

            Int32 offsetMinutes = (Int32)value.Offset.TotalMinutes;

            switch (dateTimeFormat)
            {
                case ODataJsonDateTimeFormat.ISO8601DateTime:
                    {
                        // Uses the same format as DateTime but with offset:
                        // jsonDateTime= quotation-mark
                        //  YYYY-MM-DDThh:mm:ss.sTZD
                        //  [("+" / "-") offset]
                        //  quotation-mark
                        //
                        // offset = 4DIGIT
                        string textValue = XmlConvert.ToString(value);
                        JsonValueUtils.WriteQuoted(writer, textValue);
                    }

                    break;

                case ODataJsonDateTimeFormat.ODataDateTime:
                    {
                        // Uses the same format as DateTime but with offset:
                        // jsonDateTime= quotation-mark
                        //  "\/Date("
                        //  ticks
                        //  [("+" / "-") offset]
                        //  ")\/"
                        //  quotation-mark
                        //
                        // ticks = *DIGIT
                        // offset = 4DIGIT
                        string textValue = String.Format(
                            CultureInfo.InvariantCulture,
                            JsonConstants.ODataDateTimeOffsetFormat,
                            JsonValueUtils.DateTimeTicksToJsonTicks(value.Ticks),
                            offsetMinutes >= 0 ? JsonConstants.ODataDateTimeOffsetPlusSign : string.Empty,
                            offsetMinutes);
                        JsonValueUtils.WriteQuoted(writer, textValue);
                    }

                    break;
            }
        }

        /// <summary>
        /// Write a TimeSpan value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">TimeSpan value to be written.</param>
        internal static void WriteValue(TextWriter writer, TimeSpan value)
        {
            Debug.Assert(writer != null, "writer != null");

            JsonValueUtils.WriteQuoted(writer, EdmValueWriter.DurationAsXml(value));
        }

        /// <summary>
        /// Write a TimeOfDay value
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">TimeOfDay value to be written.</param>
        internal static void WriteValue(TextWriter writer, TimeOfDay value)
        {
            Debug.Assert(writer != null, "writer != null");

            JsonValueUtils.WriteQuoted(writer, value.ToString());
        }

        /// <summary>
        /// Write a Date value
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Date value to be written.</param>
        internal static void WriteValue(TextWriter writer, Date value)
        {
            Debug.Assert(writer != null, "writer != null");

            JsonValueUtils.WriteQuoted(writer, value.ToString());
        }

        /// <summary>
        /// Write a byte value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Byte value to be written.</param>
        internal static void WriteValue(TextWriter writer, byte value)
        {
            Debug.Assert(writer != null, "writer != null");

            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write an sbyte value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">SByte value to be written.</param>
        internal static void WriteValue(TextWriter writer, sbyte value)
        {
            Debug.Assert(writer != null, "writer != null");

            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write a string value.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">String value to be written.</param>
        /// <param name="buffer">Char buffer to use for streaming data</param>
        internal static void WriteValue(TextWriter writer, string value, ref char[] buffer)
        {
            Debug.Assert(writer != null, "writer != null");

            if (value == null)
            {
                writer.Write(JsonConstants.JsonNullLiteral);
            }
            else
            {
                JsonValueUtils.WriteEscapedJsonString(writer, value, ref buffer);
            }
        }

        /// <summary>
        /// Write a byte array.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="value">Byte array to be written.</param>
        internal static void WriteValue(TextWriter writer, byte[] value)
        {
            Debug.Assert(writer != null, "writer != null");

            if (value == null)
            {
                writer.Write(JsonConstants.JsonNullLiteral);
            }
            else
            {
                writer.Write(JsonConstants.QuoteCharacter);
                writer.Write(Convert.ToBase64String(value));
                writer.Write(JsonConstants.QuoteCharacter);
            }
        }

        /// <summary>
        /// Returns the string value with special characters escaped.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="inputString">Input string value.</param>
        /// <param name="buffer">Char buffer to use for streaming data</param>
        internal static void WriteEscapedJsonString(TextWriter writer, string inputString, ref char[] buffer)
        {
            Debug.Assert(writer != null, "writer != null");
            Debug.Assert(inputString != null, "The string value must not be null.");

            writer.Write(JsonConstants.QuoteCharacter);

            int firstIndex;
            if (!JsonValueUtils.CheckIfStringHasSpecialChars(inputString, out firstIndex))
            {
                writer.Write(inputString);
            }
            else
            {
                int inputStringLength = inputString.Length;

                Debug.Assert(firstIndex < inputStringLength, "First index of the special character should be within the string");
                buffer = BufferUtils.InitializeBufferIfRequired(buffer);
                int bufferLength = buffer.Length;
                int bufferIndex = 0;
                int currentIndex = 0;

                // Let's copy and flush strings up to the first index of the special char
                while (currentIndex < firstIndex)
                {
                    int subStrLength = firstIndex - currentIndex;

                    Debug.Assert(subStrLength > 0, "SubStrLength should be greater than 0 always");

                    // If the first index of the special character is larger than the buffer length,
                    // flush everything to the buffer first and reset the buffer to the next chunk.
                    // Otherwise copy to the buffer and go on from there.
                    if (subStrLength >= bufferLength)
                    {
                        inputString.CopyTo(currentIndex, buffer, 0, bufferLength);
                        writer.Write(buffer, 0, bufferLength);
                        currentIndex += bufferLength;
                    }
                    else
                    {
                        inputString.CopyTo(currentIndex, buffer, 0, subStrLength);
                        bufferIndex = subStrLength;
                        currentIndex += subStrLength;
                    }
                }

                for (; currentIndex < inputStringLength; currentIndex++)
                {
                    char c = inputString[currentIndex];
                    string escapedString = JsonValueUtils.SpecialCharToEscapedStringMap[c];

                    // Append the unhandled characters (that do not require special treament)
                    // to the buffer.
                    if (escapedString == null)
                    {
                        buffer[bufferIndex] = c;
                        bufferIndex++;
                    }
                    else
                    {
                        // Okay, an unhandled character was deteced.
                        // First lets check if we can fit it in the existing buffer, if not,
                        // flush the current buffer and reset. Add the escaped string to the buffer
                        // and continue.
                        int escapedStringLength = escapedString.Length;
                        Debug.Assert(escapedStringLength <= bufferLength, "Buffer should be larger than the escaped string");

                        if ((bufferIndex + escapedStringLength) > bufferLength)
                        {
                            writer.Write(buffer, 0, bufferIndex);
                            bufferIndex = 0;
                        }

                        escapedString.CopyTo(0, buffer, bufferIndex, escapedStringLength);
                        bufferIndex += escapedStringLength;
                    }

                    if (bufferIndex >= bufferLength)
                    {
                        Debug.Assert(bufferIndex == bufferLength,
                            "We should never encounter a situation where the buffer index is greater than the buffer length");
                        writer.Write(buffer, 0, bufferIndex);
                        bufferIndex = 0;
                    }
                }

                if (bufferIndex > 0)
                {
                    writer.Write(buffer, 0, bufferIndex);
                }
            }

            writer.Write(JsonConstants.QuoteCharacter);
        }

        /// <summary>
        /// Converts the number of ticks from the JSON date time format to the one used in .NET DateTime or DateTimeOffset structure.
        /// </summary>
        /// <param name="ticks">The ticks to from the JSON date time format.</param>
        /// <returns>The ticks to use in the .NET DateTime of DateTimeOffset structure.</returns>
        internal static long JsonTicksToDateTimeTicks(long ticks)
        {
            // Ticks in .NET are in 100-nanoseconds and start at 1.1.0001.
            // Ticks in the JSON date time format are in milliseconds and start at 1.1.1970.
            return (ticks * 10000) + JsonValueUtils.JsonDateTimeMinTimeTicks;
        }

        /// <summary>
        /// Convert string to Json-formated string with proper escaped special characters.
        /// Note that the return value is not enclosed by the top level double-quotes.
        /// </summary>
        /// <param name="inputString">string that might contain special characters.</param>
        /// <returns>A string with special characters escaped properly.</returns>
        internal static string GetEscapedJsonString(string inputString)
        {
            Debug.Assert(inputString != null, "The string value must not be null.");

            StringBuilder builder = new StringBuilder();
            int startIndex = 0;
            int inputStringLength = inputString.Length;
            int subStrLength;
            for (int currentIndex = 0; currentIndex < inputStringLength; currentIndex++)
            {
                char c = inputString[currentIndex];

                // Append the un-handled characters (that do not require special treatment)
                // to the string builder when special characters are detected.
                if (JsonValueUtils.SpecialCharToEscapedStringMap[c] == null)
                {
                    continue;
                }

                // Flush out the un-escaped characters we've built so far.
                subStrLength = currentIndex - startIndex;
                if (subStrLength > 0)
                {
                    builder.Append(inputString.Substring(startIndex, subStrLength));
                }

                builder.Append(JsonValueUtils.SpecialCharToEscapedStringMap[c]);
                startIndex = currentIndex + 1;
            }

            subStrLength = inputStringLength - startIndex;
            if (subStrLength > 0)
            {
                builder.Append(inputString.Substring(startIndex, subStrLength));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Checks if the string contains special char and returns the first index 
        /// of special char if present.
        /// </summary>
        /// <param name="inputString">string that might contain special characters.</param>
        /// <param name="firstIndex">first index of the special char</param>
        /// <returns>A value indicating whether the string contains special character</returns>
        private static bool CheckIfStringHasSpecialChars(string inputString, out int firstIndex)
        {
            Debug.Assert(inputString != null, "The string value must not be null.");

            firstIndex = -1;
            int inputStringLength = inputString.Length;
            for (int currentIndex = 0; currentIndex < inputStringLength; currentIndex++)
            {
                char c = inputString[currentIndex];

                // Append the un-handled characters (that do not require special treatment)
                // to the string builder when special characters are detected.
                if (JsonValueUtils.SpecialCharToEscapedStringMap[c] != null)
                {
                    firstIndex = currentIndex;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Initialize static property ODataNumberFormatInfo.
        /// </summary>
        /// <returns>The <see cref=" NumberFormatInfo"/> object.</returns>
        private static NumberFormatInfo InitializeODataNumberFormatInfo()
        {
            NumberFormatInfo odataNumberFormatInfo = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            odataNumberFormatInfo.PositiveInfinitySymbol = JsonValueUtils.ODataJsonPositiveInfinitySymbol;
            odataNumberFormatInfo.NegativeInfinitySymbol = JsonValueUtils.ODataJsonNegativeInfinitySymbol;
            return odataNumberFormatInfo;
        }

        /// <summary>
        /// Write the string value with quotes.
        /// </summary>
        /// <param name="writer">The text writer to write the output to.</param>
        /// <param name="text">String value to be written.</param>
        private static void WriteQuoted(TextWriter writer, string text)
        {
            writer.Write(JsonConstants.QuoteCharacter);
            writer.Write(text);
            writer.Write(JsonConstants.QuoteCharacter);
        }

        /// <summary>
        /// Converts the number of ticks from the .NET DateTime or DateTimeOffset structure to the ticks use in the JSON date time format.
        /// </summary>
        /// <param name="ticks">The ticks from the .NET DateTime of DateTimeOffset structure.</param>
        /// <returns>The ticks to use in the JSON date time format.</returns>
        private static long DateTimeTicksToJsonTicks(long ticks)
        {
            // Ticks in .NET are in 100-nanoseconds and start at 1.1.0001.
            // Ticks in the JSON date time format are in milliseconds and start at 1.1.1970.
            return (ticks - JsonValueUtils.JsonDateTimeMinTimeTicks) / 10000;
        }

        /// <summary>
        /// Creates the special character to escaped string map.
        /// </summary>
        /// <returns>The map of special characters to the corresponding escaped strings.</returns>
        private static string[] CreateSpecialCharToEscapedStringMap()
        {
            string[] specialCharToEscapedStringMap = new string[char.MaxValue + 1];
            for (int c = char.MinValue; c <= char.MaxValue; ++c)
            {
                if ((c < ' ') || (c > 0x7F))
                {
                    // We only need to populate for characters < ' ' and > 0x7F.
                    specialCharToEscapedStringMap[c] = string.Format(CultureInfo.InvariantCulture, "\\u{0:x4}", c);
                }
                else
                {
                    specialCharToEscapedStringMap[c] = null;
                }
            }

            specialCharToEscapedStringMap['\r'] = "\\r";
            specialCharToEscapedStringMap['\t'] = "\\t";
            specialCharToEscapedStringMap['\"'] = "\\\"";
            specialCharToEscapedStringMap['\\'] = "\\\\";
            specialCharToEscapedStringMap['\n'] = "\\n";
            specialCharToEscapedStringMap['\b'] = "\\b";
            specialCharToEscapedStringMap['\f'] = "\\f";

            return specialCharToEscapedStringMap;
        }
    }
}