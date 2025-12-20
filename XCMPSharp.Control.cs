using NAudio.SoundFont;
using Serilog;

namespace xcmp
{
    public enum ChanZoneSelFunction : byte
    {
        NONE = 0x00,
        ZONE_INCREMENT = 0x01,
        ZONE_DECREMENT = 0x02,
        CHAN_INCREMENT = 0x03,
        CHAN_DECREMENT = 0x04,
        GOTO_ZONE_CHAN = 0x05,
        GOTO_CHAN = 0x06,
        GOTO_ZONE = 0x07,
        QUERY_ZONE_CHAN = 0x80,
        QUERY_NUM_ZONES = 0x81,
        QUERY_NUM_CHANS = 0x82,
        QUERY_ZONE_CHAN_MAP = 0x83,
        QUERY_CHAN_STATUS = 0x84,
        QUERY_CUR_CHAN_STATUS = 0x85
    }

    public partial class XCMP
    {
        public class ChanZoneSelectMsg : XcmpMessage
        {
            /// <summary>
            /// Function (1st byte of data)
            /// </summary>
            public ChanZoneSelFunction Function
            {
                get
                {
                    return (ChanZoneSelFunction)Data[0];
                }
                set
                {
                    Data[0] = (byte)value;
                }
            }
            /// <summary>
            /// Zone number or step size
            /// </summary>
            public UInt16 ZoneNumber
            {
                get
                {
                    if (MsgType == MsgType.BROADCAST)
                        return BitConverter.ToUInt16(Data.Take(2).Reverse().ToArray());
                    else
                        return BitConverter.ToUInt16(Data.Skip(1).Take(2).Reverse().ToArray());
                }
                set
                {
                    if (MsgType == MsgType.BROADCAST)
                        Array.Copy(BitConverter.GetBytes(value).Reverse().ToArray(), 0, Data, 0, 2);
                    else
                        Array.Copy(BitConverter.GetBytes(value).Reverse().ToArray(), 0, Data, 1, 2);
                }
            }
            /// <summary>
            /// Channel number or step size
            /// </summary>
            public UInt16 ChanNumber
            {
                get
                {
                    if (MsgType == MsgType.BROADCAST)
                        return BitConverter.ToUInt16(Data.Skip(2).Take(2).Reverse().ToArray());
                    else
                        return BitConverter.ToUInt16(Data.Skip(3).Take(2).Reverse().ToArray());
                }
                set
                {
                    if (MsgType == MsgType.BROADCAST)
                        Array.Copy(BitConverter.GetBytes(value).Reverse().ToArray(), 0, Data, 2, 2);
                    else
                        Array.Copy(BitConverter.GetBytes(value).Reverse().ToArray(), 0, Data, 3, 2);
                }
            }
            /// <summary>
            /// Whether the specified channel is inhibited (only valid for broadcast messages)
            /// </summary>
            public bool? ChanInhibited
            {
                get
                {
                    // This field is only valid for broadcasts that included it
                    if (MsgType == MsgType.BROADCAST && Data.Length == 5)
                    {
                        return Data[4] == 0x01;   
                    }
                    else
                        return null;
                }
                set
                {
                    if (MsgType == MsgType.BROADCAST)
                    {
                        if (value == true)
                            Data[4] = 0x01;
                        else if (value == false)
                            Data[4] = 0x00;
                        else
                            Data[4] = 0xFF;
                    }
                }
            }
            /// <summary>
            /// A list of UInt16s representing the number of channels in each zone of the radio
            /// where list index 0 represents zone 1's channel count.
            /// Only valid for responses
            /// </summary>
            public List<UInt16> ZoneChanMap
            {
                get
                {
                    if (MsgType != MsgType.RESPONSE)
                        return null;
                    else
                    {
                        // Return null if we're missing the map
                        if (Data.Length <= 6)
                            return null;
                        // Number of zones is the first 2-byte number after function/zone/channel
                        UInt16 num_zones = BitConverter.ToUInt16(Data.Skip(5).Take(2).Reverse().ToArray());
                        List<UInt16> map = new List<UInt16>(num_zones);
                        // Iterate over the zone channel counts
                        for (int i = 0; i < num_zones; i++)
                        {
                            UInt16 num_chans = BitConverter.ToUInt16(Data.Skip(7 + (2*i)).Take(2).Reverse().ToArray());
                            map.Add(num_chans);
                        }
                        // Return our list
                        return map;
                    }
                }
            }
            /// <summary>
            /// Initialize a new Channel/Zone Select Message
            /// </summary>
            /// <param name="type"></param>
            /// <param name="func"></param>
            public ChanZoneSelectMsg(MsgType type, ChanZoneSelFunction func) : base(type, Opcode.CHZNSEL)
            {
                // The data array defaults to 5 bytes
                Data = new byte[5];
                // Set the function
                Function = func;
            }
            /// <summary>
            /// Decode a message into a Channel/Zone select message
            /// </summary>
            /// <param name="msgBytes"></param>
            /// <exception cref="ArgumentException"></exception>
            public ChanZoneSelectMsg(byte[] msgBytes) : base(msgBytes)
            {
                // Ensure the message opcode is correct
                if (Opcode != Opcode.CHZNSEL)
                    throw new ArgumentException($"XCMP Opcode {Enum.GetName(Opcode)}  does not match expected CHZNSEL opcode!");
                // Debug Print
                logDebugPrint();
            }

            /// <summary>
            /// Cast a base XCMP message to a CHZNSEL message
            /// </summary>
            /// <param name="msg"></param>
            /// <exception cref="ArgumentException"></exception>
            public ChanZoneSelectMsg(XcmpMessage msg) : base(msg.MsgType, msg.Opcode)
            {
                // Sanity check
                if (msg.Opcode != Opcode.CHZNSEL)
                    throw new ArgumentException($"Cannog cast XCMP message with opcode {Enum.GetName(msg.Opcode)} to CHZNSEL!");
                // Copy Data
                Result = msg.Result;
                Data = msg.Data;
                // Debug Print
                logDebugPrint();
            }

            /// <summary>
            /// Extra debug print detailing message
            /// </summary>
            private void logDebugPrint()
            {
                if (MsgType == MsgType.REQUEST)
                {
                    Log.Verbose("CHZNSEL Request: Function {func}, Zone {zone} Chan {chan}", Enum.GetName(Function), ZoneNumber, ChanNumber);
                }
                else if (MsgType == MsgType.RESPONSE)
                {
                    Log.Verbose("CHZNSEL Response: Result {res}, Function {func}, Zone {zone}, Chan {chan}", Enum.GetName(Result), Enum.GetName(Function), ZoneNumber, ChanNumber);
                    if (ZoneChanMap != null)
                    {
                        List<UInt16> map = ZoneChanMap;
                        Log.Verbose(" ┗  Got Zone/Channel Map:");
                        for (int i = 0; i < map.Count; i++)
                        {
                            Log.Verbose("    ┗ Zone {n}: {y} Channels", i+1, map[i]);
                        }
                    }  
                }
                else if (MsgType == MsgType.BROADCAST)
                {
                    Log.Verbose("CHZNSEL Broadcast: Currently selected Zone {zone} Chan {chan}, Inhibited {stat}", ZoneNumber, ChanNumber, ChanInhibited);
                }
            }

        }
    }

    public enum ScanControlFunction : byte
    {
        DISABLE = 0x00,
        ENABLE = 0x01,
        NUISANCE_DEL = 0x02,
        NUISCANCE_RESET = 0x03,
        DYNAMIC_PRIORITY = 0x04,
        SCAN_LANDED = 0x05,
        DESIGNATED_TX_MEMBER = 0x06,
        SCAN_RESUMED = 0x07,
        STATUS = 0x80
    }

    public enum ScanControlState : byte
    {
        NORMAL_OFF = 0x00,
        NORMAL_ON = 0x01,
        VOTE_OFF = 0x02,
        VOTE_ON = 0x03,
        NO_LIST = 0xFF
    }
}