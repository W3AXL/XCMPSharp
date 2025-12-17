using xcmp.connection;

using System.Text;
using Serilog;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Diagnostics;
using System.Net.Sockets;

namespace xcmp
{
    public partial class XCMP
    {
        protected XCMPBaseConnection _connection;

        public string SerialNumber { get; private set; }
        public string ModelNumber { get; private set; }
        public string FirmwareVersion { get; private set; }

        public int Timeout { get; private set; }

        public event EventHandler<XcmpMessage> OnReceive;

        public XCMPConnectionStatus Status { get { return _connection.Status; } }

        private XcmpMessage lastMessage;

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
            /// The length of the entire XCMP message in bytes
            /// </summary>
            public int ByteLength
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
            /// Get the XCMP message as bytes to send over a connection
            /// Note that some XCMP transport layers require length bytes, which are not prepended here
            /// </summary>
            public byte[] Bytes
            {
                get
                {
                    // Create the new 
                    byte[] msg = new byte[ByteLength];
                    // Generate Type/Opcode Header Bytes
                    byte[] header = GetTypeOpcodeHeader(MsgType, Opcode);
                    msg[0] = header[0];
                    msg[1] = header[1];
                    // Add optional result code and data
                    if (MsgType == MsgType.RESPONSE)
                    {
                        msg[2] = (byte)Result;
                        Array.Copy(Data, 0, msg, 3, Data.Length);
                    }
                    else
                    {
                        Array.Copy(Data, 0, msg, 2, Data.Length);
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
            /// Parse an XCMP message from a byte aray
            /// </summary>
            /// <param name="data"></param>
            public XcmpMessage(byte[] data)
            {
                // Get type & opcode
                UInt16 header = (UInt16)((data[0] << 8) + (data[1] & 0xFF));
                MsgType = GetMsgType(header);
                Opcode = GetOpcode(header);
                // Response messages have an extra byte for the result
                if (MsgType == MsgType.RESPONSE)
                {
                    Result = (Result)data[2];
                    Data = data.Skip(3).ToArray();
                    Log.Debug("XCMP: Got MsgType {type:X} ({typeName}), Opcode {opcode:X} ({opcodeName}), Result {result:X} ({resultName}), Data: [{dataHex}]", MsgType, Enum.GetName(MsgType), Opcode, Enum.GetName(Opcode), Result, Enum.GetName(Result), Convert.ToHexString(Data));
                }
                else
                {
                    Data = data.Skip(2).ToArray();
                    Log.Debug("XCMP: Got MsgType {type:X} ({typeName}), Opcode {opcode:X} ({opcodeName}), Data: [{dataHex}]", MsgType, Enum.GetName(MsgType), Opcode, Enum.GetName(Opcode), Convert.ToHexString(Data));
                }
            }
        }

        public XCMP(XCMPBaseConnection conn, int timeout = 1000)
        {
            Timeout = timeout;
            _connection = conn;
            // Bind data handler
            _connection.OnReceive += (sender, data) => { onReceiveBytes(data); };
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
        /// <param name="awaitInit">Wait for DEV_INIT_STS messages on connect</param>
        public async void Connect(bool underTest = false)
        {
            // Wait for connection to complete
            _connection.Connect();
            while (_connection.Status != XCMPConnectionStatus.CONNECTED) { await Task.Delay(2); }
            // Retrieve serial/model once we're all booted up
            if (!underTest)
            {
                SerialNumber = await GetSerial();
                ModelNumber = await GetModel();
                FirmwareVersion = $"HOST {await GetVersion(VersionOperation.HostSoftware)}, DSP {await GetVersion(VersionOperation.DSPSoftware)}";
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
        /// Send an XCMP message without expecting a response
        /// </summary>
        /// <param name="message"></param>
        public void Send(XcmpMessage message)
        {
            sendBytes(message.Bytes);
        }

        /// <summary>
        /// Send a raw byte sequence via the XCMP connection
        /// </summary>
        /// <param name="data"></param>
        private void sendBytes(byte[] data)
        {
            if (Status != XCMPConnectionStatus.CONNECTED) { throw new InvalidOperationException("XCMP not connected!"); }
            Log.Verbose("XCMP: >>SENT>> {0}", Convert.ToHexString(data));
            _connection.Send(data);
        }

        private void onReceiveBytes(byte[] data)
        {
            Log.Verbose("XCMP: <<RCV<< {0}", Convert.ToHexString(data));
            XcmpMessage msg = new XcmpMessage(data);
            lastMessage = msg;
            OnReceive?.Invoke(this, msg);
        }

        /// <summary>
        /// Wait for a message to come over the XCMP connection with the specified opcode
        /// </summary>
        /// <param name="opcode"></param>
        /// <returns></returns>
        private async Task<XcmpMessage> getMessage(Opcode opcode)
        {
            Stopwatch sw = new Stopwatch();
            while (sw.ElapsedMilliseconds < Timeout)
            {
                if (lastMessage?.Opcode == opcode)
                {
                    return lastMessage;
                }
                // Sleep
                await Task.Delay(2);
            }
            throw new TimeoutException($"Timed out waiting for XCMP message matching opcode {opcode}");
        }

        /// <summary>
        /// Send an XCMP message and await a response to it
        /// </summary>
        /// <param name="msg">the message to send</param>
        /// <param name="responseOpcode">The expected response's opcode</param>
        /// <returns></returns>
        public async Task<XcmpMessage> Get(XcmpMessage msg, Opcode responseOpcode)
        {
            Send(msg);
            return await getMessage(responseOpcode);
        }

        /// <summary>
        /// Get the connected radio's serial number
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetSerial()
        {
            Log.Debug("XCMP: getting radio serial number");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.SERIAL_NUMBER);
            msg.Data = [0x00];  // get radio serial number

            XcmpMessage resp = await Get(msg, Opcode.SERIAL_NUMBER);
            return Encoding.UTF8.GetString(resp.Data).TrimEnd('\0');
        }

        /// <summary>
        /// Get the connected radio's model number
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetModel()
        {
            Log.Debug("XCMP: getting radio model number");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.MODEL_NUMBER);
            msg.Data = [0x00];  // get radio model number

            XcmpMessage resp = await Get(msg, Opcode.MODEL_NUMBER);
            return Encoding.UTF8.GetString(resp.Data).TrimEnd('\0');
        }

        /// <summary>
        /// Get Radio SW Version
        /// </summary>
        /// <param name="oper"></param>
        /// <returns></returns>
        public async Task<string> GetVersion(VersionOperation oper)
        {
            Log.Debug("XCMP: getting radio version for {oper}", Enum.GetName(oper));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.VERSION_INFO);
            msg.Data = new byte[] { (byte)oper };

            XcmpMessage resp = await Get(msg, Opcode.VERSION_INFO);
            return Encoding.UTF8.GetString(resp.Data).TrimEnd('\0');
        }

        public async Task<byte[]> GetStatus(StatusOperation oper)
        {
            Log.Debug("XCMP: getting radio status {oper}", Enum.GetName(oper));

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RADIO_STATUS);
            msg.Data = new byte[] { (byte)oper };

            XcmpMessage resp = await Get(msg, Opcode.RADIO_STATUS);

            // Verify we got the same status back
            if ((StatusOperation)resp.Data[0] != oper)
                throw new Exception($"Did not receive expected status operation (got {resp.Data[0]:X} ({Enum.GetName((StatusOperation)resp.Data[0])}) but expected {(byte)oper:X} ({Enum.GetName(oper)}))");

            // Skip the first byte (the operation)
            return resp.Data.Skip(1).ToArray();
        }

        public async Task<RadioBand[]> GetBands()
        {
            Log.Debug("XCMP: getting radio bands");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.VERSION_INFO);
            msg.Data = new byte[] { (byte)VersionOperation.RFBand };

            XcmpMessage resp = await Get(msg, Opcode.VERSION_INFO);

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

        public async Task<bool> Keyup(TxMicrophone microphone = TxMicrophone.ExternalMuted)
        {
            Log.Debug("XCMP: keying radio");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.TRANSMIT);
            msg.Data = new byte[1] { (byte)microphone };

            XcmpMessage resp = await Get(msg, Opcode.TRANSMIT);

            if (resp.Result != Result.SUCCESS)
            {
                Log.Error("Failed to keyup radio with code {code} ({result})", resp.Result, Enum.GetName(resp.Result));
                return false;
            }
            else
                return true;
        }

        public async Task<bool> Dekey()
        {
            Log.Debug("XCMP: dekeying radio");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RECEIVE);
            msg.Data = new byte[1] { (byte)RxSpeaker.InternalMuted };

            XcmpMessage resp = await Get(msg, Opcode.RECEIVE);

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

        public async Task<bool> Ping()
        {
            Log.Debug("XCMP: pinging radio");

            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.PING);

            XcmpMessage resp = await Get(msg, Opcode.PING);

            return resp.Result == Result.SUCCESS;
        }
        /// <summary>
        /// Change the channel on the radio
        /// </summary>
        /// <param name="down">true to decrement the selected channel</param>
        /// <param name="step">number of channels to move per step</param>
        /// <returns></returns>
        public async Task<bool> ChangeChannel(bool down = false, UInt16 step = 1)
        {
            Log.Debug("XCMP: Moving {step} channels {dir}", step, down ? "down": "up");

            ChanZoneSelectMsg msg = new ChanZoneSelectMsg(MsgType.REQUEST, down ? ChanZoneSelFunction.CHAN_DECREMENT : ChanZoneSelFunction.CHAN_INCREMENT);
            msg.ChanNumber = 1;

            XcmpMessage resp = await Get(msg, Opcode.CHZNSEL);

            return resp.Result == Result.SUCCESS;
        }
    }
}