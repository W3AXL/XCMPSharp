using System.Net;
using Serilog;
using Org.BouncyCastle.Crypto.Operators;
using System.Net.Sockets;
using System.Data;
using FFmpeg.AutoGen;

namespace xcmp.connection
{
    public enum XNLOpcode : UInt16
    {
        MASTER_PRESENT_BRDCST = 0x0001,
        MASTER_STATUS_BRDCST = 0x0002,
        DEVICE_MASTER_QUERY = 0x0003,
        DEVICE_AUTH_KEY_REQUEST = 0x0004,
        DEVICE_AUTH_KEY_REPLY = 0x0005,
        DEVICE_CONN_REQUEST = 0x0006,
        DEVICE_CONN_REPLY = 0x0007,
        DEVICE_SYSMAP_REQUEST = 0x0008,
        DEVICE_SYSMAP_BRDCST = 0x0009,
        DEVICE_RESET_MSG = 0x000A,
        DATA_MSG = 0x000B,
        DATA_MSG_ACK = 0x000C
    }

    public enum XNLProtocol : byte
    {
        XNL_CTRL = 0x00,
        XCMP = 0x01
    }

    public enum XNLAuthLevel : byte
    {
        INTERNAL = 0x00,
        EXTERNAL = 0x01,
        EXT_PRIV = 0x02
    }

    public enum XNLResultCode : byte
    {
        FAILURE = 0x00,
        SUCCESS = 0x01,
    }

    /// <summary>
    /// Container for storing XNL authentication keys
    /// </summary>
    public class XNLKeys
    {
        /// <summary>
        /// List of 4 keys
        /// </summary>
        public List<UInt32> keys;
        /// <summary>
        /// Delta value
        /// </summary>
        public UInt32 delta;

        /// <summary>
        /// Obtain the encrypted value of an 8-byte plaintext array
        /// </summary>
        /// <param name="plaintext"></param>
        /// <returns></returns>
        public byte[] GetEncrypted(byte[] plaintext)
        {
            // Log
            //Log.Verbose("Generating XNL auth string using plaintext keys {k0:X8}, {k1:X8}, {k2:X8}, {k3:X8} and delta {delta:X8}", keys[0], keys[1], keys[2], keys[3], delta);

            // Split 8 byte value into upper 4 and lower 4 bytes as uint32s
            UInt32 lower = BitConverter.ToUInt32(plaintext.Take(4).Reverse().ToArray());
            UInt32 upper = BitConverter.ToUInt32(plaintext.Skip(4).Reverse().Take(4).ToArray());

            //Log.Verbose("Got lower {lower:X8} and upper {upper:X8} from plaintext {plaintext}", lower, upper, Convert.ToHexString(plaintext));

            // Temp var
            UInt32 sum = 0;

            // It's TEA🍵
            for (int i = 0; i < 32; i++)
            {
                sum += delta;
                lower += (upper << 4) + keys[0] ^ upper + sum ^ (upper >> 5) + keys[1];
                upper += (lower << 4) + keys[2] ^ lower + sum ^ (lower >> 5) + keys[3];
            }

            // Return the result
            byte[] result = new byte[8];
            Array.Copy(BitConverter.GetBytes(lower).Reverse().ToArray(), 0, result, 0, 4);
            Array.Copy(BitConverter.GetBytes(upper).Reverse().ToArray(), 0, result, 4, 4);
            //Log.Verbose("Combined lower {lower:X8} and upper {upper:X8} results into array {encrypted}", lower, upper, Convert.ToHexString(result));
            return result;
        }

        public XNLKeys(List<UInt32> keys, UInt32 delta)
        {
            this.keys = keys;
            this.delta = delta;
        }
    }

    public class XNLMessage
    {
        /// <summary>
        /// Opcode for the message
        /// </summary>
        public XNLOpcode Opcode { get; set; }
        /// <summary>
        /// Protocol used in the message
        /// </summary>
        public XNLProtocol Protocol { get; set; }
        /// <summary>
        /// 3-bit rollover counter in flags byte
        /// </summary>
        public byte Rollover { get; set; }
        /// <summary>
        /// Whether an ACK is needed, stored in flags byte
        /// </summary>
        public bool AckNeeded { get; set; }
        /// <summary>
        /// Destination Address
        /// </summary>
        public UInt16 DstAddress { get; set; }
        /// <summary>
        /// Source Address
        /// </summary>
        public UInt16 SrcAddress { get; set; }
        /// <summary>
        /// Transaction ID counter
        /// </summary>
        public UInt16 TransactionID { get; set; }
        /// <summary>
        /// Payload Bytes
        /// </summary>
        public byte[] Payload { get; set; }

        /// <summary>
        /// Retrieve the bytes of an XNL message
        /// </summary>
        /// <returns></returns>
        public byte[] GetMsgBytes()
        {
            // Prepare the data array
            int length = 12;
            if (Payload != null)
                length += Payload.Length;
            byte[] data = new byte[length];
            // Opcode
            Array.Copy(BitConverter.GetBytes((UInt16)Opcode).Reverse().ToArray(), 0, data, 0, 2);
            // Procotol ID
            data[2] = (byte)Protocol;
            // Flags
            data[3] = (byte)(Rollover & 0b00000111);
            data[3] += (byte)(Convert.ToByte(AckNeeded) << 3);
            // Destination Address
            Array.Copy(BitConverter.GetBytes(DstAddress).Reverse().ToArray(), 0, data, 4, 2);
            // Source Address
            Array.Copy(BitConverter.GetBytes(SrcAddress).Reverse().ToArray(), 0, data, 6, 2);
            // Transaction ID
            Array.Copy(BitConverter.GetBytes(TransactionID).Reverse().ToArray(), 0, data, 8, 2);
            // Payload
            if (Payload != null)
            {
                // Copy length
                Array.Copy(BitConverter.GetBytes((UInt16)Payload.Length).Reverse().ToArray(), 0, data, 10, 2);
                // Copy data
                Array.Copy(Payload, 0, data, 12, Payload.Length);
            }
            // Return
            return data;
        }

        /// <summary>
        /// Decode a byte array into an XNL message
        /// </summary>
        /// <param name="data">the byte array containing the XNL message</param>
        /// <returns></returns>
        public XNLMessage(byte[] data)
        {
            // Opcode
            Opcode = (XNLOpcode)BitConverter.ToUInt16(data.Take(2).Reverse().ToArray());
            // Protocol ID
            Protocol = (XNLProtocol)data[2];
            // Flags
            Rollover = (byte)(data[3] & 0b00000111);
            AckNeeded = Convert.ToBoolean(data[3] >> 3 & 0x1);
            // Destination Address
            DstAddress = BitConverter.ToUInt16(data.Skip(4).Take(2).Reverse().ToArray());
            // Source Address
            SrcAddress = BitConverter.ToUInt16(data.Skip(6).Take(2).Reverse().ToArray());
            // Transaction ID
            TransactionID = BitConverter.ToUInt16(data.Skip(8).Take(2).Reverse().ToArray());
            // Get payload length
            int payloadLen = BitConverter.ToUInt16(data.Skip(10).Take(2).Reverse().ToArray());
            if (payloadLen > 0)
            {
                // Create array
                Payload = new byte[payloadLen];
                // Extract payload
                Array.Copy(data, 12, Payload, 0, Payload.Length);
            }
            // Check if we have any extra data
            if (data.Length > 12 + Payload.Length)
                Log.Warning("Extra data after end of XNL payload found! {0}", Convert.ToHexString(data.Skip(12 + Payload.Length).ToArray()));
            // Debug print
            if (Payload != null)
                Log.Verbose(
                    "Decoded XNL message, Opcode {opcode}, Proto {proto}, Rollover {rollover}, AckReqd {ack}, Dst Addr {dst}, Src Addr {src}, Trans ID {trans}, Payload Length {length}, Payload {payload}",
                    Enum.GetName(Opcode),
                    Enum.GetName(Protocol),
                    Rollover,
                    AckNeeded,
                    DstAddress,
                    SrcAddress,
                    TransactionID,
                    Payload.Length,
                    Convert.ToHexString(Payload)
                );
            else
                Log.Verbose(
                    "Decoded XNL message, Opcode {opcode}, Proto {proto}, Rollover {rollover}, AckReqd {ack}, Dst Addr {dst}, Src Addr {src}, Trans ID {trans}, No Payload",
                    Enum.GetName(Opcode),
                    Enum.GetName(Protocol),
                    Rollover,
                    AckNeeded,
                    DstAddress,
                    SrcAddress,
                    TransactionID
                );
        }

        public XNLMessage()
        {
            // Stub
        }
    }

    /// <summary>
    /// A wrapper class for sending/receiving XNL-encapsulated XCMP
    /// </summary>
    public class XCMPXNLWrapper : XCMPBaseConnection
    {
        /// <summary>
        /// The base connection which the wrapped XCMP will be sent over
        /// </summary>
        private XCMPBaseConnection conn;

        /// <summary>
        /// XNL authentication information
        /// </summary>
        private XNLKeys keys;
        
        /// <summary>
        /// The XNL source address for messages originating from this connection
        /// </summary>
        public UInt16? SrcAddress { get; private set; }
        /// <summary>
        /// The XNL address of the master in the network, obtained from xnlConnect()
        /// </summary>
        public UInt16? MasterAddr { get; private set; }
        /// <summary>
        /// Base (upper byte) for transaction IDs sent from this connection
        /// Obtained from the device connection reply
        /// </summary>
        public byte TIDBase { get; private set; }
        /// <summary>
        /// Logical address obtained from device connection reply
        /// </summary>
        public UInt16 LogicalAddr { get; private set; }

        public bool Connected { get; private set; }

        /// <summary>
        /// The encrypted plaintext value obtained after authentication
        /// </summary>
        private byte[] encryptedKey;
        /// <summary>
        /// A 3-bit counter that increments with every message sent
        /// </summary>
        private byte counterFlag;

        public XCMPXNLWrapper(XCMPBaseConnection baseConnection, XNLKeys keys)
        {
            conn = baseConnection;
            this.keys = keys;
            SrcAddress = null;
            MasterAddr = null;
        }

        public void Connect()
        {
            // Start the base connection
            conn.Connect();
            // Query for master
            getMaster();
            // Authenticate
            authenticate();
            // Connect
            connect();
            // We're connected, yay!
            Connected = true;
        }

        public void Disconnect()
        {
            // TODO: XNL disconnect
            // Disconnect from the base connection
            conn.Disconnect();
            // We're disconnected
            Connected = false;
        }

        public void Dispose()
        {
            Disconnect();
            conn.Dispose();
        }

        public void Send(byte[] data)
        {
            // Prepare the XNL_DATA_MSG
            XNLMessage msg = new XNLMessage();
            msg.Opcode = XNLOpcode.DATA_MSG;
            msg.Protocol = XNLProtocol.XCMP;
            msg.Rollover = counterFlag;
            msg.TransactionID = getTID();
            msg.DstAddress = (UInt16)MasterAddr;
            msg.SrcAddress = (UInt16)SrcAddress;
            msg.Payload = data;
            // Send and expect an ACK
            XNLMessage resp = sendMessage(msg, XNLOpcode.DATA_MSG_ACK);
            // Ensure our response has the same counter flag
            if (resp.Rollover != msg.Rollover)
                throw new InvalidDataException($"ACK for XNL data contains wrong rollover flag! Got {resp.Rollover} but sent {msg.Rollover}");
            // Ensure our response has the same TID
            if (resp.TransactionID != msg.TransactionID)
                throw new InvalidDataException($"ACK for XNL data contains wrong transaction ID! Got {resp.TransactionID} but sent {msg.TransactionID}");
            // After successful send, incremenet the counter
            incrementFlag();
        }

        public byte[] Receive()
        {
            // Receive from connection
            byte[] respBytes = conn.Receive();
            // Parse into message
            XNLMessage msg = new XNLMessage(respBytes);
            // 
        }

        /// <summary>
        /// Send an XNL message and expect a reply
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="expectedResponse"></param>
        /// <returns></returns>
        private XNLMessage sendMessage(XNLMessage msg, XNLOpcode expectedResponse)
        {
            // Send
            conn.Send(msg.GetMsgBytes());
            // Receive
            byte[] resp = conn.Receive();
            // Parse
            XNLMessage respMsg = new XNLMessage(resp);
            // Valiate response
            if (respMsg.Opcode != expectedResponse)
                throw new InvalidDataException($"Did not receive expected XNL response to message! {Enum.GetName(respMsg.Opcode)} != {Enum.GetName(expectedResponse)}");
            // Return
            return respMsg;
        }

        /// <summary>
        /// Routine for verifying connection to a master via XNL
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private void getMaster()
        {
            Log.Debug("Querying for XNL master...");
            XNLMessage msg = new XNLMessage();
            msg.Opcode = XNLOpcode.DEVICE_MASTER_QUERY;
            msg.Protocol = XNLProtocol.XNL_CTRL;
            msg.SrcAddress =
            msg.DstAddress = 0;
            // Get a response back
            XNLMessage masterStatusMsg = sendMessage(msg, XNLOpcode.MASTER_STATUS_BRDCST);
            // Parse the master status info
            UInt32 xnlVersion = BitConverter.ToUInt32(masterStatusMsg.Payload.Take(4).Reverse().ToArray());
            byte deviceType = masterStatusMsg.Payload[4];
            byte deviceNumber = masterStatusMsg.Payload[5];
            bool dataMsgSent = Convert.ToBoolean(masterStatusMsg.Payload[6]);
            MasterAddr = masterStatusMsg.SrcAddress;
            Log.Debug("XNL found master at address {addr:X4} using XNL version {ver:X4} (Device Type {type:X2}, Device Number {num:X2})", MasterAddr, xnlVersion, deviceType, deviceNumber);
        }

        /// <summary>
        /// Routine for authenticating with an XNL master
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private void authenticate()
        {
            // Throw exception if we haven't connected
            if (MasterAddr == null)
                throw new ArgumentException("XNL not connected, cannot authenticate!");
            // Prepare the auth key request
            XNLMessage authReq = new XNLMessage();
            authReq.Opcode = XNLOpcode.DEVICE_AUTH_KEY_REQUEST;
            authReq.Protocol = XNLProtocol.XNL_CTRL;
            authReq.DstAddress = (UInt16)MasterAddr;
            // Send and expect an auth reply back
            XNLMessage authReply = sendMessage(authReq, XNLOpcode.DEVICE_AUTH_KEY_REPLY);
            // Get temporary source address from payload
            SrcAddress = BitConverter.ToUInt16(authReply.Payload.Take(2).Reverse().ToArray());
            // Get unencrypted auth value
            byte[] plaintext = authReply.Payload.Skip(2).Take(8).ToArray();
            // Generate encrypted key to use for all future messages
            encryptedKey = keys.GetEncrypted(plaintext);
            Log.Debug(
                "Generated encrypted key {key} from plaintext {pt} for XNL connection",
                Convert.ToHexString(encryptedKey),
                Convert.ToHexString(plaintext)
            );
        }

        /// <summary>
        /// Routine for connecting to an XNL master after authentication
        /// </summary>
        private void connect()
        {
            // Throw exception if we haven't authenticated
            if (encryptedKey == null)
                throw new ArgumentException("XNL not authenticated, cannot connect!");
            // Prepare our connection request
            XNLMessage req = new XNLMessage();
            req.Opcode = XNLOpcode.DEVICE_CONN_REQUEST;
            req.Protocol = XNLProtocol.XNL_CTRL;
            req.DstAddress = (UInt16)MasterAddr;
            req.SrcAddress = (UInt16)SrcAddress;
            req.TransactionID = 0;
            req.Payload = new byte[12]; // DEV_CONN_REQ is 12 byte payload
            // Set device type
            req.Payload[2] = (byte)DeviceType.PC_APPLICATION;
            // Authentication Level
            req.Payload[3] = (byte)XNLAuthLevel.INTERNAL;
            // Our encrypted key string
            Array.Copy(encryptedKey, 0, req.Payload, 4, 8);
            // Send the message
            XNLMessage reply = sendMessage(req, XNLOpcode.DEVICE_CONN_REPLY);
            // Ensure we got success
            if ((XNLResultCode)reply.Payload[0] != XNLResultCode.SUCCESS)
                throw new Exception($"XNL connection request reply did not indicate success! Got {Enum.GetName((XNLResultCode)reply.Payload[0])}");
            // Parse the rest of the reply
            TIDBase = reply.Payload[1];
            SrcAddress = BitConverter.ToUInt16(reply.Payload.Skip(2).Take(2).Reverse().ToArray());
            LogicalAddr = BitConverter.ToUInt16(reply.Payload.Skip(4).Take(2).Reverse().ToArray());
            byte[] enc = reply.Payload.Skip(6).Take(8).ToArray();
            // We're connected, yay!
            Log.Debug("Successfully authenticated and connected to XNL device {dstAddr:X4} (SrcAddr {src:X4}, LogicalAddr {logical:X4}, TIDBase {tid:X2}, Enc: {enc:X8})", MasterAddr, SrcAddress, LogicalAddr, TIDBase, Convert.ToHexString(enc));
        }

        /// <summary>
        /// Increment the counter flag for data messages
        /// </summary>
        private void incrementFlag()
        {
            if (counterFlag == 7)
                counterFlag = 0;
            else
                counterFlag++;
        }

        /// <summary>
        /// Get a transaction ID using the currently negotiated TID base
        /// </summary>
        /// <returns></returns>
        private UInt16 getTID()
        {
            Random rnd = new Random();
            byte[] buf = new byte[1];
            rnd.NextBytes(buf);
            // Add the shifted TID base to the random byte
            return (UInt16)((TIDBase << 8) + buf[0]);
        }
    }
}