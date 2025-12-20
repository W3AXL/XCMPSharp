using Org.BouncyCastle.Math.EC.Multiplier;
using Serilog;

namespace xcmp
{
    public enum DeviceInitType : byte
    {
        INIT_STATUS = 0x00,
        INIT_COMPLETE = 0x01,
        STATUS_UPDATE = 0x02
    }

    public enum DeviceType : byte
    {
        UNKNOWN = 0x00,
        RF_XCVR = 0x01,
        CONTROL_HEAD = 0x02,
        SIREN_PA = 0x03,
        VRS = 0x04,
        CONSOLETTE = 0x05,
        VEHICULAR_ADAPTER = 0x06,
        OPTION_BOARD = 0x07,
        AUTO_TEST_SYS = 0x08,
        EXTERNAL_MIC = 0x09,
        PC_APPLICATION = 0x0A,
        EXT_ACCESSORY = 0x0B,
        URC = 0x0C,
        COLLABORATIVE = 0x0D,
        NON_IP_PERIPH = 0x0E,
        OTAP_BRIDGE = 0x0F
    }

    /// <summary>
    /// Attribute types used in the device descriptor structure
    /// </summary>
    public enum DeviceAttribute : byte
    {
        FAMILY = 0,
        PWR_LVL = 1,
        DISPLAY = 2,
        SPEAKER = 3,
        RF_BAND = 4,
        GPIO_CTRL = 5,
        SELECTED = 6,
        RADIO_TYPE = 7,
        SECURE_MODULE = 8,
        KEYPAD = 9,
        POWERUP_TYPE = 10,
        IDENTIFIER = 11,
        POWEROFF_TYPE = 12,
        CHANNEL_KNOB = 13,
        VIRTUAL_PERS_SUPPORTED = 14,
        PRODUCT_ID = 15,
        MIC_TYPE = 16,
        BT_TYPE = 17,
        PHYSICAL_CTRL_ID = 18,
        ACCELEROMETER = 19,
        GPS_TYPE = 20,
        INFO_UI = 21,
        NUM_PGM_BUTTONS = 22,
        INDIV_BCAST = 23
    }

    public enum DeviceFamily : byte
    {
        OTHER = 0x00,
        ODESSEY = 0x01,
        MANTARAY = 0x02,
        MANTARAY_EX = 0x03,
        CHIEF = 0x04,
        MOTOTRBO = 0x05,
        HOSTXTL = 0x06,
        HOSTAPX = 0x07,
        BANDIT = 0x08,
        HOSTPHOENIX = 0x09
    }

    public enum DevicePower : byte
    {
        LOW = 0x00,
        MID = 0x01,
        HIGH = 0x02
    }

    public enum DeviceDisplayType : byte
    {
        NONE = 0x00,
        BMP_1 = 0x01,
        BMP_2 = 0x02,
        BMP_3 = 0x03,
        BMP_4 = 0x04,
        BMP_5 = 0x05,
        BMP_6 = 0x06,
        BMP_7 = 0x07,
        BMP_8 = 0x08,
        BMP_9 = 0x09,
        BMP_10 = 0x0A,
        BMP_11 = 0x0B,
        INFO_ALL = 0x80,
        INFO_1 = 0x81,
        INFO_2 = 0x82,
        INFO_ALL_INDICATORS = 0x88,
        GENERIC = 0xFF
    }

    /// <summary>
    /// Device-specific implementations of XCMP class
    /// </summary>
    public partial class XCMP
    {
        public class DeviceInitStatusMsg : XcmpMessage
        {
            /// <summary>
            /// The XCMP version supported by the device which sent the message
            /// </summary>
            public UInt32 XcmpVersion
            {
                get
                {
                    return BitConverter.ToUInt32(Data.Take(4).Reverse().ToArray());
                }
                set
                {
                    Array.Copy(BitConverter.GetBytes((UInt32)value).Reverse().ToArray(), Data, 4);
                }
            }

            /// <summary>
            /// The type of init message sent
            /// </summary>
            public DeviceInitType InitType
            {
                get
                {
                    return (DeviceInitType)Data[4];
                }
                set
                {
                    Data[4] = (byte)value;
                }
            }

            /// <summary>
            /// The type of device which sent the status update
            /// </summary>
            public DeviceType DeviceType
            {
                get
                {
                    if (InitType == DeviceInitType.INIT_STATUS || InitType == DeviceInitType.STATUS_UPDATE)
                        return (DeviceType)Data[5];
                    else
                        return DeviceType.UNKNOWN;
                }
                set
                {
                    if (InitType == DeviceInitType.INIT_STATUS || InitType == DeviceInitType.STATUS_UPDATE)
                        Data[5] = (byte)value;
                }
            }

            /// <summary>
            /// The status of the device as a bitfield. All 0s indicates no failures
            /// MSB as 1 indicates a fatal error
            /// </summary>
            public UInt16 DeviceStatus
            {
                get
                {
                    if (InitType == DeviceInitType.INIT_STATUS || InitType == DeviceInitType.STATUS_UPDATE)
                        return BitConverter.ToUInt16(Data.Skip(6).Take(2).Reverse().ToArray());
                    else
                        return 0;
                }
                set
                {
                    if (InitType == DeviceInitType.INIT_STATUS || InitType == DeviceInitType.STATUS_UPDATE)
                        Array.Copy(BitConverter.GetBytes((UInt16)value).Reverse().ToArray(), 0, Data, 6, 2);
                }
            }

            /// <summary>
            /// Whether the status indicates a fatal error (bit 15 set)
            /// </summary>
            public bool FatalError
            {
                get
                {
                    return (DeviceStatus >> 15 & 0x1) == 1;
                }
            }

            /// <summary>
            /// Whether the status indicates a non-fatal error (bit 14 set)
            /// </summary>
            public bool NonFatalError
            {
                get
                {
                    return (DeviceStatus >> 14 & 0x1) == 1;
                }
            }

            /// <summary>
            /// The length of the device descriptor array in bytes
            /// </summary>
            public byte DescriptorLength
            {
                get
                {
                    return Data[8];
                }
                private set
                {
                    Data[8] = (byte)value;
                }
            }

            public DeviceInitStatusMsg(DeviceInitType type, UInt32 xcmpVersion) : base(MsgType.BROADCAST, Opcode.DEV_INIT_STS)
            {
                // Start the data array with size 9
                Data = new byte[9];
                // Parse our values
                InitType = type;
                XcmpVersion = xcmpVersion;
            }

            public DeviceInitStatusMsg(byte[] data) : base(data)
            {
                // Stub
            }

            public DeviceInitStatusMsg(XcmpMessage msg) : base(msg.MsgType, msg.Opcode)
            {
                // Sanity Check
                if (msg.Opcode != Opcode.DEV_INIT_STS)
                    throw new ArgumentException($"Cannot cast XCMP message with opcode {Enum.GetName(msg.Opcode)} to a DEVINITSTS message!");
                if (msg.MsgType != MsgType.BROADCAST)
                    throw new ArgumentException($"DEVINITSTS messages can only be broadcasts! (Got {msg.MsgType})");
                // Copy
                Result = msg.Result;
                Data = msg.Data;
                // Debug Print
                logDebugPrint();
            }

            private void logDebugPrint()
            {
                if (MsgType == MsgType.BROADCAST)
                {
                    if (InitType == DeviceInitType.INIT_COMPLETE)
                        Log.Debug("DEVINITSTS: Master indicates init complete (XCMP version {ver})", XcmpVersion);
                    else
                        Log.Debug("DEVINITSTS Broadcast: XCMP Verstion {ver}, Init Type {init}, Device Type {dev}, Status {stat:X}, Descriptor Length {size}", XcmpVersion, Enum.GetName(InitType), Enum.GetName(DeviceType), DeviceStatus, DescriptorLength);
                    // Iterate over any errors
                    if (FatalError)
                        Log.Error(" ┗  Fatal error detected! Error code {code:X}", DeviceStatus & 0x7FFF);
                    else if (NonFatalError)
                        Log.Warning(" ┗  Non-fatal error detected! Error code {code:X}", DeviceStatus & 0x3FFF);
                    // Iterate over device descriptors
                    if (DescriptorLength > 0)
                    {
                        List<DeviceAttribute> attribs = GetAttributeList();
                        attribs.ForEach((attr) =>
                        {
                            List<byte> vals = GetAttributeValues(attr);
                            Log.Debug(" ┗  Attribute {attr}, Values {vals}", Enum.GetName(attr), vals);
                        });
                    }
                    else
                        Log.Debug(" ┗  No descriptors included in message");
                } 
            }

            /// <summary>
            /// A list of (DeviceAttribute, Value) tuples contained in this status message
            /// </summary>
            public List<(DeviceAttribute Attribute, byte Value)> GetAttributes()
            {
                if (InitType == DeviceInitType.INIT_STATUS || InitType == DeviceInitType.STATUS_UPDATE)
                {
                    // Create the output list
                    List<(DeviceAttribute, byte)> attributeList = new List<(DeviceAttribute, byte)>();
                    // Get the attribute byte array
                    byte[] attrData = Data.Skip(9).ToArray();
                    // Iterate two bytes at a time
                    for (int i = 0; i < DescriptorLength; i += 2)
                    {
                        attributeList.Add(((DeviceAttribute)attrData[i], attrData[i + 1]));
                    }
                    return attributeList;
                }
                else
                    Log.Warning("DEVINITSTS message was not an INIT_STATUS or STATUS_UPDATE, no device attribute list");
                    return null;
            }

            /// <summary>
            /// Get a list of unique attributes for this message
            /// </summary>
            /// <returns></returns>
            public List<DeviceAttribute> GetAttributeList()
            {
                List<DeviceAttribute> attribList = new List<DeviceAttribute>();
                List<(DeviceAttribute Attribute, byte Value)> attribs = GetAttributes();
                attribs.ForEach((attr) =>
                {
                    if (!attribList.Contains(attr.Attribute))
                        attribList.Add(attr.Attribute);
                });
                return attribList;
            }

            /// <summary>
            /// Get a list of values for the specified attribute
            /// </summary>
            public List<byte> GetAttributeValues(DeviceAttribute attribute)
            {
                // Output list
                List<byte> valuesList = new List<byte>();
                // Iterate over atributes list
                foreach ((DeviceAttribute Attribute, byte Value) entry in GetAttributes())
                {
                    if (entry.Attribute == attribute)
                        valuesList.Add(entry.Value);
                }
                // Return
                return valuesList;
            }

            /// <summary>
            /// Add a new attribute:value pair to the device attribute array
            /// </summary>
            /// <param name="attribute"></param>
            /// <param name="value"></param>
            public void AddAttribute(DeviceAttribute attribute, byte value)
            {
                // Check if this attribute/value pair already exists
                if (GetAttributes().Any(attrib => attrib.Attribute == attribute && attrib.Value == value))
                {
                    return;
                }
                // Add to the end of the data array
                List<byte> newData = Data.ToList();
                newData.Add((byte)attribute);
                newData.Add(value);
                // Update
                Data = newData.ToArray();
                // Update length
                DescriptorLength += 2;
            }
        }
    }
}