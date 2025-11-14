using System.Drawing;
using System.Text;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Asn1.X509;
using Serilog;

namespace xcmp
{
    /// <summary>
    /// Display function for a request or response
    /// </summary>
    public enum DisplayFunction : byte
    {
        UPDATE = 0,
        QUERY = 1,
        CLOSE = 2,
        ALL_PX_ON = 3,
        ALL_PX_OFF = 4,
        REFRESH = 5
    }

    /// <summary>
    /// Display regions (lower 5 bits)
    /// </summary>
    public enum DisplayRegion : byte
    {
        OVERLAY = 0,
        PRIMARY = 1,
        SECONDARY = 2,
        TERTIARY = 3,
        QUATERNARY = 4,
        QUINARY = 5,
        SENARY = 6,
        SEPTENARY = 7,
        OCTONARY = 8,
        NONARY = 9,
        DENARY = 10,
        MODE = 16,
        STATUS = 17
    }

    /// <summary>
    /// Display IDs (upper 3 bits)
    /// </summary>
    public enum DisplayID : byte
    {
        ALL = 0b00000000,
        PRIMARY = 0b00100000,
        SECONDARY = 0b01000000,
        THIRD = 0b01100000,
        FOURTH = 0b10000000,
        FIFTH = 0b10100000,
        SIXTH = 0b11000000,
        SEVENTH = 0b11100000
    }

    /// <summary>
    /// Text encoding used for display updates
    /// </summary>
    public enum CharacterEncoding : byte
    {
        ISO_LATIN = 0,
        UCS_2 = 1
    }

    public partial class XCMP
    {
        /// <summary>
        /// Retrieve the Display Region and Display ID from a display region byte in the XCMP message
        /// </summary>
        /// <param name="displayByte">the byte containing the Display Region & ID nibbles</param>
        /// <returns></returns>
        public static (DisplayRegion Region, DisplayID ID) GetDisplayParams(byte displayByte)
        {
            // Region is 5 lowest bits
            DisplayRegion region = (DisplayRegion)(displayByte & 0b00011111);
            // ID is 3 highest bits
            DisplayID id = (DisplayID)(displayByte & 0b11100000);
            // Return
            return (region, id);
        }

        /// <summary>
        /// Retrieve the display byte (concatenated region + ID nibbles) from the region & ID
        /// </summary>
        /// <param name="region"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static byte GetDisplayByte(DisplayRegion region, DisplayID id)
        {
            return (byte)((byte)region + (byte)id);
        }

        /// <summary>
        /// A class representing the data contained in an XCMP DSPTXT message
        /// </summary>
        public class DisplayTextMsg : XcmpMessage
        {
            /// <summary>
            /// the function that needs to be performed on the display device
            /// </summary>
            public DisplayFunction Function
            {
                get { return (DisplayFunction)Data[0]; }
                set { Data[0] = (byte)value; }
            }

            /// <summary>
            /// Unique identifier for a display update request. 
            /// Should be set to 0xFF for all other requests.
            /// </summary>
            public byte Token
            {
                get { return Data[1]; }
                set { Data[1] = value; }
            }

            /// <summary>
            /// Specifies the display region for the request
            /// Only used for Update & Query requests/replies or broadcasts
            /// </summary>
            public DisplayRegion? Region
            {
                get
                {
                    // For update/query, region is found in the 3rd byte
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        return GetDisplayParams(Data[2]).Region;
                    // For broadcasts, it's the first byte
                    else if (MsgType == MsgType.BROADCAST)
                        return GetDisplayParams(Data[0]).Region;
                    else
                        return null;
                }
                set
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        Data[2] = GetDisplayByte((DisplayRegion)value, (DisplayID)ID);
                    else if (MsgType == MsgType.BROADCAST)
                        Data[0] = GetDisplayByte((DisplayRegion)value, (DisplayID)ID);
                    else
                        return;
                }
            }

            /// <summary>
            /// Specifies the display ID for the request
            /// Only used for Update & Query requests/replies or broadcasts
            /// </summary>
            public DisplayID? ID
            {
                get
                {
                    // Same byte offset in the data array as for region above
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        return GetDisplayParams(Data[2]).ID;
                    else if (MsgType == MsgType.BROADCAST)
                        return GetDisplayParams(Data[0]).ID;
                    else
                        return null;
                }
                set
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        Data[2] = GetDisplayByte((DisplayRegion)Region, (DisplayID)value);
                    else if (MsgType == MsgType.BROADCAST)
                        Data[0] = GetDisplayByte((DisplayRegion)Region, (DisplayID)value);
                    else
                        return;
                }
            }

            /// <summary>
            /// Specifies the amount of time the display should draw the data.
            /// 0 = permanent, otherwise 500ms * value, 0xFF = default timer
            /// Only used for update requests and query replies
            /// </summary>
            public UInt16? TimedDisplay
            {
                get
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        return BitConverter.ToUInt16(Data.Skip(3).Take(2).Reverse().ToArray());
                    else
                        return null;
                }
                set
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        Array.Copy(BitConverter.GetBytes((UInt16)value).Reverse().ToArray(), 0, Data, 3, 2);
                    else
                        return;
                }
            }

            /// <summary>
            /// The priority of the display message (1 lowest, 5 highest)
            /// Only used for update requests and query replies
            /// </summary>
            public byte? MsgClass
            {
                get
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        return Data[4];
                    else
                        return null;
                }
                set
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        Data[4] = (byte)value;
                    else
                        return;
                }
            }

            /// <summary>
            /// Type of character encoding used in the DisplayText string
            /// Only used for update requests and query replies
            /// </summary>
            public CharacterEncoding? TextEncoding
            {
                get
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        return (CharacterEncoding)Data[5];
                    else
                        return null;
                }
                set
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        Data[5] = (byte)value;
                    else
                        return;
                }
            }

            /// <summary>
            /// Length of the text in bytes, including end bytes
            /// Only used for update requests and query replies
            /// </summary>
            public UInt16? TextByteLength
            {
                get
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                        return BitConverter.ToUInt16(Data.Skip(6).Take(2).Reverse().ToArray());
                    else
                        return null;
                }
            }

            /// <summary>
            /// The text to update
            /// Only used for update requests and query replies
            /// </summary>
            public string Text
            {
                get
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                    {
                        if (TextEncoding == CharacterEncoding.ISO_LATIN)
                            return Encoding.GetEncoding("ISO-8859-1").GetString(Data.Skip(8).ToArray());
                        else if (TextEncoding == CharacterEncoding.UCS_2)
                            return Encoding.GetEncoding(1200).GetString(Data.Skip(8).ToArray());
                        else
                            throw new ArgumentException("Invalid encoding specified for text!");
                    }
                    else
                        return null;
                }
                set
                {
                    if (Function == DisplayFunction.UPDATE || Function == DisplayFunction.QUERY)
                    {
                        // Encode the string
                        byte[] textBytes;
                        if (TextEncoding == CharacterEncoding.ISO_LATIN)
                            textBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(value);
                        else if (TextEncoding == CharacterEncoding.UCS_2)
                            textBytes = Encoding.GetEncoding(1200).GetBytes(value);
                        else
                            throw new ArgumentException("Invalid encoding specified for text!");
                        // Update the length bytes
                        Array.Copy(BitConverter.GetBytes(textBytes.Length).Reverse().ToArray(), 0, Data, 8, textBytes.Length);
                        // Update the data array length
                        if (Data.Length != textBytes.Length + 8)
                        {
                            byte[] oldData = Data;
                            Data = new byte[textBytes.Length + 8];
                            Array.Copy(oldData, 0, Data, 0, oldData.Length);
                        }
                        // Copy the text
                        Array.Copy(textBytes, 0, Data, 8, textBytes.Length);
                    }
                    else
                        return;
                }
            }

            /// <summary>
            /// Create a new Display Text Request XCMP message
            /// </summary>
            /// <param name="function"></param>
            public DisplayTextMsg(MsgType type, DisplayFunction function) : base(type, Opcode.DISPTXT)
            {
                // Initialize the base data array based on message type
                if (type == MsgType.BROADCAST)
                    // Broadcasts have at least 4 bytes
                    Data = new byte[4];
                else if (type == MsgType.REQUEST)
                {
                    if (function == DisplayFunction.UPDATE)
                        // Update requests include the full data field length
                        Data = new byte[9];
                    else if (function == DisplayFunction.QUERY)
                        // Query requests include 1 byte in the data field, so 3
                        Data = new byte[3];
                }
                else
                    // All other requests and replies only have the function & token fields
                    Data = new byte[2];

                // Set function
                Function = function;

                // Set token (will be ignored for broadcasts)
                Token = 0xFF;
            }

            /// <summary>
            /// Decode a message into a display text request msg
            /// </summary>
            /// <param name="msgBytes"></param>
            public DisplayTextMsg(byte[] msgBytes) : base(msgBytes)
            {
                // Stub
            }
        }
        
        public DisplayTextMsg GetDisplayText(DisplayRegion region, DisplayID id)
        {
            // Prepare query message
            DisplayTextMsg msg = new DisplayTextMsg(MsgType.REQUEST, DisplayFunction.QUERY);
            msg.Region = region;
            msg.ID = id;

            // Send & get response
            DisplayTextMsg resp = (DisplayTextMsg)Send(msg);

            Log.Debug("Got display {region} (ID {id}) text {text}", Enum.GetName((DisplayRegion)resp.Region), Enum.GetName((DisplayID)resp.ID), resp.Text);

            return resp;
        }
    }
}