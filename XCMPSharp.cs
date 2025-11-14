using xcmp.connection;

using System.Text;
using Serilog;
using System.Linq.Expressions;
using System.Runtime.Serialization;

namespace xcmp
{
    public partial class XCMP
    {
        protected XCMPBaseConnection _connection;

        public string SerialNumber { get; private set; }
        public string ModelNumber { get; private set; }
        public string FirmwareVersion { get; private set; }

        public class XcmpMessage
        {
            /// <summary>
            /// XCMP message type
            /// </summary>
            public MsgType MsgType { get; private set; }
            /// <summary>
            /// Opcode for the request
            /// </summary>
            public Opcode Opcode { get; private set; }
            /// <summary>
            /// Result used for response messages
            /// </summary>
            public Result Result { get; set; }
            /// <summary>
            /// The byte data of the message
            /// </summary>
            public byte[] Data { get; set; }
            /// <summary>
            /// The length of all data excluding the starting two length bytes
            /// </summary>
            public int Length
            {
                get
                {
                    // Responses have an extra byte for status
                    if (MsgType == MsgType.RESPONSE)
                        return Data.Length + 3;
                    else
                        return Data.Length + 2;
                }
            }
            /// <summary>
            /// The length of all bytes in the message, including length bytes
            /// </summary>
            public int ByteLength
            {
                get
                {
                    return Length + 2;
                }
            }
            /// <summary>
            /// Get the XCMP message as bytes to send over a connection, including the starting length bytes
            /// </summary>
            public byte[] Bytes
            {
                get
                {
                    // Create the new 
                    byte[] msg = new byte[ByteLength];
                    // Add length bytes
                    msg[0] = (byte)((Length >> 8) & 0xFF);
                    msg[1] = (byte)(Length & 0xFF);
                    // Generate Type/Opcode Header Bytes
                    byte[] header = GetTypeOpcodeHeader(MsgType, Opcode);
                    msg[2] = header[0];
                    msg[3] = header[1];
                    // Add optional result code and data
                    if (MsgType == MsgType.RESPONSE)
                    {
                        msg[4] = (byte)Result;
                        Array.Copy(Data, 0, msg, 5, Data.Length);
                    }
                    else
                    {
                        Array.Copy(Data, 0, msg, 4, Data.Length);
                    }
                    // return the array
                    return msg;
                }
            }

            /// <summary>
            /// Create a new XCMP message of the specified type
            /// </summary>
            /// <param name="type"></param>
            public XcmpMessage(MsgType type, Opcode opcode)
            {
                MsgType = type;
                Opcode = opcode;
                Data = new byte[] { };
            }

            /// <summary>
            /// Parse an XCMP message from a byte aray including the starting length bytes
            /// </summary>
            /// <param name="data"></param>
            public XcmpMessage(byte[] data)
            {
                // Get length first
                int len = (data[0] << 8) + (data[1] & 0xFF);
                Log.Debug($"XCMP: Decoding message of length {len}");
                // Get type & opcode next
                UInt16 header = (UInt16)((data[2] << 8) + (data[3] & 0xFF));
                MsgType = GetMsgType(header);
                Opcode = GetOpcode(header);

                if (MsgType == MsgType.RESPONSE)
                {
                    Result = (Result)data[4];
                    Data = data.Skip(5).Take(len - 3).ToArray();
                    Log.Debug("XCMP: Got MsgType {type:X} ({typeName}), Opcode {opcode:X} ({opcodeName}), Result {result:X} ({resultName}), Data: [{dataHex}]", MsgType, Enum.GetName(MsgType), Opcode, Enum.GetName(Opcode), Result, Enum.GetName(Result), Convert.ToHexString(Data));
                }
                else
                {
                    Data = data.Skip(4).Take(len - 2).ToArray();
                    Log.Debug("XCMP: Got MsgType {type:X} ({typeName}), Opcode {opcode:X} ({opcodeName}), Data: [{dataHex}]", MsgType, Enum.GetName(MsgType), Opcode, Enum.GetName(Opcode), Convert.ToHexString(Data));
                }
                // Validate
                if (Length != len)
                {
                    throw new Exception($"Decoded message lengths don't match (got {Length} but expected {len})");
                }
            }
        }

        public XCMP(XCMPBaseConnection conn)
        {
            _connection = conn;
        }

        /// <summary>
        /// Convert a 4-byte motorola frequency to a frequency in Hz
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static UInt32 BytesToFrequency(byte[] data)
        {
            // Validate length
            if (data.Length != 4) { throw new ArgumentException("Frequency data must be 4 bytes!"); }
            // Convert bytes to int32 and multiply by 5 Hz (after reversing the endianess)
            return (UInt32)(BitConverter.ToInt32(data.Reverse().ToArray()) * 5);
        }

        /// <summary>
        /// Convert a frequency in Hz to a 4-byte motorola-specific array
        /// </summary>
        /// <param name="frequency"></param>
        /// <returns></returns>
        public static byte[] FrequencyToBytes(int frequency)
        {
            // We reverse the byte order to get the endianness correct
            return BitConverter.GetBytes((UInt32)frequency / 5).Reverse().ToArray();
        }

        /// <summary>
        /// Get the XCMP opcode from the 2-byte type/opcode
        /// </summary>
        /// <param name="typeOpcode"></param>
        /// <returns></returns>
        public static Opcode GetOpcode(UInt16 header)
        {
            // Opcode is the lower 12 bits of the header
            return (Opcode)(header & 0xFFF);
        }
        /// <summary>
        /// Get the XCMP message type from the 2-byte type/opcode
        /// </summary>
        /// <param name="typeOpcode"></param>
        /// <returns></returns>
        public static MsgType GetMsgType(UInt16 header)
        {
            // MsgType is the top 4 bits of the header
            return (MsgType)((header & 0xF000) >> 12);
        }
        /// <summary>
        /// Get the XCMP message header (type + opcode) as a UInt16
        /// </summary>
        /// <param name="type"></param>
        /// <param name="opcode"></param>
        /// <returns></returns>
        public static byte[] GetTypeOpcodeHeader(MsgType type, Opcode opcode)
        {
            return new byte[2]
            {
                (byte)( ((byte)type << 4) + ((UInt16)opcode >> 8 & 0xF) ),
                (byte)( (UInt16)opcode & 0xFF)
            };
        }

        /// <summary>
        /// Connect to the attached radio
        /// </summary>
        /// <param name="underTest"></param>
        public void Connect(bool underTest = false)
        {
            _connection.Connect();
            if (!underTest)
            {
                SerialNumber = GetSerial();
                ModelNumber = GetModel();
                FirmwareVersion = $"HOST {GetVersion(VersionOperation.HostSoftware)}, DSP {GetVersion(VersionOperation.DSPSoftware)}";
                Log.Debug("XCMP: connected to radio model {ModelNumber} (S/N {SerialNumber}, {FirmwareVersion})", ModelNumber, SerialNumber, FirmwareVersion);
            }
        }

        /// <summary>
        /// Disconnect from the radio
        /// </summary>
        public void Disconnect()
        {
            _connection.Disconnect();

            Log.Debug("XCMP: Disconnected from radio, seeya!");
        }

        /// <summary>
        /// Send an XCMP message and retrieve the response
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="timeout">timeout in seconds to wait for a resposne</param>
        /// <returns></returns>
        public XcmpMessage Send(XcmpMessage message, MsgType expectedReply = MsgType.RESPONSE)
        {
            // Throw if not connected
            if (!_connection.Connected) { throw new InvalidOperationException("XCMP not connected!"); }

            // Send the message
            Log.Verbose("XCMP: >>SNT>> {0}", Convert.ToHexString(message.Bytes));
            _connection.Send(message.Bytes);

            // Get the response
            byte[] rx = _connection.Receive();
            XcmpMessage response = new XcmpMessage(rx);
            Log.Verbose("XCMP: <<RCV<< {0}", Convert.ToHexString(response.Bytes));

            // Validate it's a response
            if (response.MsgType != expectedReply)
                throw new Exception($"Got unexpected reply to message! (Expected {Enum.GetName(expectedReply)} but got {Enum.GetName(response.MsgType)})");
            // Validate it was successful
            if (response.Result != Result.SUCCESS)
                throw new Exception($"Response indicates {Enum.GetName(response.Result)}!");
            // Validate it matches
            if (response.Opcode != message.Opcode)
                throw new Exception($"Received different opcode from what was sent! (Sent {Enum.GetName(message.Opcode)} but got {Enum.GetName(response.Opcode)})");
            // Return if everything is good

            return response;
        }

        /// <summary>
        /// Send an XCMP message without expecting a response
        /// </summary>
        /// <param name="message"></param>
        public void Write(XcmpMessage message)
        {
            // Throw if not connected
            if (!_connection.Connected) { throw new InvalidOperationException("XCMP not connected!"); }

            Log.Verbose("XCMP: >>SENT>> {0}", Convert.ToHexString(message.Bytes));
            _connection.Send(message.Bytes);
        }

        /// <summary>
        /// Byte-Level XCMP send/receive
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        public byte[] SendBytes(byte[] data)
        {
            // Throw if not connected
            if (!_connection.Connected) { throw new InvalidOperationException("XCMP not connected!"); }
            
            int opcodeOut = 0;
            opcodeOut |= (data[0] << 8);
            opcodeOut |= (data[1] & 0xFF);

            // expects to get an XCMP opcode and some data in, length is auto calculated
            byte[] toSend = new byte[data.Length + 2];

            int dataLen = data.Length;

            // length high and low bytes
            toSend[0] = (byte)((dataLen >> 8) & 0xFF);
            toSend[1] = (byte)(dataLen & 0xFF);

            Array.Copy(data, 0, toSend, 2, dataLen);

            Log.Verbose("XCMP: >>SNT>> {0}", Convert.ToHexString(toSend));

            _connection.Send(toSend);

            // start a timer so we don't hold infinitely
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < TimeSpan.FromSeconds(5))
            {
                byte[] fromRadio = _connection.Receive();

                int len = 0;

                len |= (fromRadio[0] << 8) & 0xFF;
                len |= fromRadio[1];

                Log.Verbose("XCMP: <<RCV<< {0}", Convert.ToHexString(fromRadio.Take(len + 2).ToArray()));

                byte[] retval = new byte[len];

                Array.Copy(fromRadio, 2, retval, 0, len);

                int opcodeIn = 0;
                opcodeIn |= (retval[0] << 8);
                opcodeIn |= (retval[1] & 0xFF);

                if (opcodeIn - 0x8000 == opcodeOut)
                {
                    return retval;
                }
            }
            throw new TimeoutException("Radio did not reply in a timely manner.");
        }

        /// <summary>
        /// Get the connected radio's serial number
        /// </summary>
        /// <returns></returns>
        public string GetSerial()
        {
            Log.Debug("XCMP: getting radio serial number");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.SERIAL_NUMBER);

            return Encoding.UTF8.GetString(Send(msg).Data).TrimEnd('\0');
        }

        /// <summary>
        /// Get the connected radio's model number
        /// </summary>
        /// <returns></returns>
        public string GetModel()
        {
            Log.Debug("XCMP: getting radio model number");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.MODEL_NUMBER);

            return Encoding.UTF8.GetString(Send(msg).Data).TrimEnd('\0');
        }

        /// <summary>
        /// Get Radio SW Version
        /// </summary>
        /// <param name="oper"></param>
        /// <returns></returns>
        public string GetVersion(VersionOperation oper)
        {
            Log.Debug("XCMP: getting radio version for {oper}", Enum.GetName(oper));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.VERSION_INFO);
            msg.Data = new byte[] { (byte)oper };

            XcmpMessage resp = Send(msg);

            return Encoding.UTF8.GetString(resp.Data).TrimEnd('\0');
        }

        public byte[] GetStatus(StatusOperation oper)
        {
            Log.Debug("XCMP: getting radio status {oper}", Enum.GetName(oper));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RADIO_STATUS);
            msg.Data = new byte[] { (byte)oper };

            XcmpMessage resp = Send(msg);

            // Verify we got the same status back
            if ((StatusOperation)resp.Data[0] != oper)
                throw new Exception($"Did not receive expected status operation (got {resp.Data[0]:X} ({Enum.GetName((StatusOperation)resp.Data[0])}) but expected {(byte)oper:X} ({Enum.GetName(oper)}))");

            // Skip the first byte (the operation)
            return resp.Data.Skip(1).ToArray();
        }

        public RadioBand[] GetBands()
        {
            Log.Debug("XCMP: getting radio bands");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.VERSION_INFO);
            msg.Data = new byte[] { (byte)VersionOperation.RFBand };

            XcmpMessage resp = Send(msg);

            List<RadioBand> bands = new List<RadioBand>();
            foreach (byte b in resp.Data) { bands.Add((RadioBand)b); }
            return bands.ToArray();
        }

        public void EnterServiceMode()
        {
            Log.Debug("XCMP: entering service mode");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.ENTER_TEST_MODE);

            Send(msg);
        }

        public void ResetRadio()
        {
            Log.Debug("XCMP: resetting radio");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RADIO_RESET);

            Send(msg);
        }

        public void SetTXFrequency(int frequency, Bandwidth bandwidth, TxDeviation deviation)
        {
            Log.Debug("XCMP: setting TX frequency to {frequency} (BW: {bw}, DEV: {dev})", frequency, Enum.GetName(bandwidth), Enum.GetName(deviation));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.TX_FREQUENCY);
            msg.Data = new byte[6];

            // First 4 bytes are frequency
            Array.Copy(FrequencyToBytes(frequency), 0, msg.Data, 0, 4);

            // Fifth byte is bandwidth
            msg.Data[4] = (byte)bandwidth;

            // Sixth byte is modulation
            msg.Data[5] = (byte)deviation;

            Send(msg);
        }

        public void SetRXFrequency(int frequency, Bandwidth bandwidth, RxModulation modulation)
        {
            Log.Debug("XCMP: setting RX frequency to {frequency} (BW: {bw}, MOD: {mod})", frequency, Enum.GetName(bandwidth), Enum.GetName(modulation));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RX_FREQUENCY);
            msg.Data = new byte[6];

            // First 4 bytes are frequency
            Array.Copy(FrequencyToBytes(frequency), 0, msg.Data, 0, 4);

            // Fifth byte is bandwidth
            msg.Data[4] = (byte)bandwidth;

            // Sixth byte is modulation
            msg.Data[5] = (byte)modulation;

            Send(msg);
        }

        public bool Keyup(TxMicrophone microphone = TxMicrophone.ExternalMuted)
        {
            Log.Debug("XCMP: keying radio");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.TRANSMIT);
            msg.Data = new byte[1] { (byte)microphone };

            XcmpMessage resp = Send(msg);

            if (resp.Result != Result.SUCCESS)
            {
                Log.Error("Failed to keyup radio with code {code} ({result})", resp.Result, Enum.GetName(resp.Result));
                return false;
            }
            else
                return true;
        }

        public bool Dekey()
        {
            Log.Debug("XCMP: dekeying radio");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RECEIVE);
            msg.Data = new byte[1] { (byte)RxSpeaker.InternalMuted };

            XcmpMessage resp = Send(msg);

            if (resp.Result != Result.SUCCESS)
            {
                Log.Error("Failed to dekey radio with code {code} ({result})", resp.Result, Enum.GetName(resp.Result));
                return false;
            }
            else
                return true;
        }

        public virtual void SetTransmitPower(TxPowerLevel power)
        {
            Log.Debug("XCMP: setting TX power to {pwr}", Enum.GetName(power));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.TX_POWER_LEVEL_INDEX);
            msg.Data = new byte[1] { (byte)power };

            Send(msg);
        }

        public void SetTransmitConfig(TxConfig config)
        {
            Log.Debug("XCMP: setting TX config to {config}", Enum.GetName(config));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.TRANSMIT_CONFIG);
            msg.Data = new byte[1] { (byte)config };

            Send(msg);
        }

        public void SetReceiveConfig(RxConfig config)
        {
            Log.Debug("XCMP: setting RX config to {config}", Enum.GetName(config));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RECEIVE_CONFIG);
            msg.Data = new byte[1] { (byte)config };

            Send(msg);
        }

        public bool Ping()
        {
            Log.Debug("XCMP: pinging radio");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.PING);

            XcmpMessage resp = Send(msg);

            return resp.Result == Result.SUCCESS;
        }

    }
}