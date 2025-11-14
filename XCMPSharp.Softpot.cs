using Serilog;

namespace xcmp
{
    /// <summary>
    /// Softpot-specific implementations for XCMP class
    /// </summary>
    public partial class XCMP
    {
        public class SoftpotMessage : XcmpMessage
        {
            /// <summary>
            /// The softpot operation
            /// </summary>
            public SoftpotOperation Operation
            {
                get
                {
                    return (SoftpotOperation)Data[0];
                }
                set
                {
                    Data[0] = (byte)value;
                }
            }
            /// <summary>
            /// The softpot type
            /// </summary>
            public SoftpotType Type
            {
                get
                {
                    return (SoftpotType)Data[1];
                }
                set
                {
                    Data[1] = (byte)value;
                }
            }
            /// <summary>
            /// The softpot value or values as a variable-length byte array
            /// </summary>
            public byte[] Value
            {
                get
                {
                    // Return everything after the softpot oepration/type
                    return Data.Skip(2).ToArray();
                }
                set
                {
                    // Save the old values for recreating the array
                    SoftpotOperation oper = Operation;
                    SoftpotType type = Type;
                    // Create a new data array
                    Data = new byte[value.Length + 2];
                    Operation = oper;
                    Type = type;
                    // Copy the value into the data array
                    Array.Copy(value, 0, Data, 2, value.Length);
                }
            }
            /// <summary>
            /// Create a new Softpot-specific XCMP message
            /// </summary>
            /// <param name="msgType"></param>
            /// <param name="operation"></param>
            /// <param name="type"></param>
            public SoftpotMessage(MsgType msgType, SoftpotOperation operation, SoftpotType type) : base(msgType, Opcode.SOFTPOT)
            {
                // Start the data array with size 2
                Data = new byte[2];
                // Parse our values
                Operation = operation;
                Type = type;
            }
            /// <summary>
            /// Parse a softpot-specific XCMP message from a byte array
            /// </summary>
            /// <param name="data"></param>
            public SoftpotMessage(byte[] data) : base(data)
            {
                // Stub
            }
        }

        /// <summary>
        /// Softpot Parameters Struct
        /// </summary>
        public struct SoftpotParams
        {
            public int Min { get; set; }
            public int Max { get; set; }
            public int[] Frequencies { get; set; }
            public int ByteLength { get; set; }
            public int[] Values { get; set; }
        }

        /// <summary>
        /// Convert an array of bytes to an integer value
        /// </summary>
        /// <param name="bytes">bytes to convert</param>
        /// <returns>converted integer value</returns>
        /// <exception cref="NotImplementedException">if byte size is not 8, 4, 2, or 1</exception>
        public static int SoftpotBytesToValue(byte[] bytes)
        {
            // flip byte array since softpot bytes are little-endian
            bytes = bytes.Reverse().ToArray();
            // Convert
            switch (bytes.Length)
            {
                case 4:
                    return BitConverter.ToInt32(bytes);
                case 2:
                    return BitConverter.ToInt16(bytes);
                case 1:
                    return bytes[0];
                default:
                    throw new NotImplementedException($"Value byte length of {bytes.Length} not supported!");
            }
        }

        /// <summary>
        /// Convert an integer value to an array of bytes
        /// </summary>
        /// <param name="val">value to convert</param>
        /// <returns>byte array</returns>
        /// <exception cref="NotImplementedException"></exception>
        public static byte[] SoftpotValueToBytes(int val, int byteLen)
        {
            // Bytes holder
            byte[] bytes;
            // Convert
            switch (byteLen)
            {
                case 4:
                    bytes = BitConverter.GetBytes(val);
                    break;
                case 2:
                    bytes = BitConverter.GetBytes((short)val);
                    break;
                case 1:
                    bytes = new byte[] { (byte)val };
                    break;
                default:
                    throw new NotImplementedException($"Value byte length of {byteLen} not supported!");
            }
            // Swap for little-endian
            bytes = bytes.Reverse().ToArray();
            // Return
            return bytes;
        }

        /// <summary>
        /// Send a softpot message and retrieve a softpot response
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public SoftpotMessage SendSoftpot(SoftpotMessage message)
        {
            // Send the softpot message and receive and standard XCMP message
            XcmpMessage resp = Send(message);
            // Convert the response to a softpot message by parsing the bytes
            SoftpotMessage sp_resp = new SoftpotMessage(resp.Bytes);
            // Verify that we got the correct type back
            if (sp_resp.Type != message.Type)
                throw new Exception($"Received different softpot type from what was sent! (Sent {message.Type} but got {sp_resp.Type})");
            // Return
            return sp_resp;
        }

        /// <summary>
        /// Get the current value of a softpot
        /// </summary>
        /// <param name="type">softpot type</param>
        /// <returns>The bytes representing the softpot value (variable length)</returns>
        public byte[] SoftpotGetValue(SoftpotType type)
        {
            Log.Debug("XCMP: Getting softpot value for {type}", Enum.GetName(type));

            SoftpotMessage msg = new SoftpotMessage(MsgType.REQUEST, SoftpotOperation.READ, type);

            return SendSoftpot(msg).Value;
        }

        /// <summary>
        /// Get the minimum value for a softpot
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        public byte[] SoftpotGetMinimum(SoftpotType type)
        {
            Log.Debug("XCMP: getting softpot minimum for {type}", Enum.GetName(type));

            SoftpotMessage msg = new SoftpotMessage(MsgType.REQUEST, SoftpotOperation.READ_MIN, type);

            return SendSoftpot(msg).Value;
        }

        /// <summary>
        /// Get the minimum value for a softpot
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        public byte[] SoftpotGetMaximum(SoftpotType type)
        {
            Log.Debug("XCMP: getting softpot maximum for {type}", Enum.GetName(type));

            SoftpotMessage msg = new SoftpotMessage(MsgType.REQUEST, SoftpotOperation.READ_MAX, type);

            return SendSoftpot(msg).Value;
        }

        /// <summary>
        /// Write a softpot value to radio
        /// </summary>
        /// <param name="type"></param>
        /// <param name="val"></param>
        /// <exception cref="InvalidDataException"></exception>
        public void SoftpotWrite(SoftpotType type, byte[] val)
        {
            Log.Debug("XCMP: writing softpot {type} -> {val}", Enum.GetName(type), Convert.ToHexString(val));

            SoftpotMessage msg = new SoftpotMessage(MsgType.REQUEST, SoftpotOperation.WRITE, type);
            msg.Value = val;

            SendSoftpot(msg);
        }

        /// <summary>
        /// Temporarily update a softpot value (will not persist, make sure to write)
        /// </summary>
        /// <param name="id"></param>
        /// <param name="val"></param>
        public void SoftpotUpdate(SoftpotType type, byte[] val)
        {
            Log.Debug("XCMP: updating softpot {type} -> {val}", Enum.GetName(type), Convert.ToHexString(val));

            SoftpotMessage msg = new SoftpotMessage(MsgType.REQUEST, SoftpotOperation.UPDATE, type);
            msg.Value = val;

            SendSoftpot(msg);
        }

        /// <summary>
        /// Return all values for a softpot type as a list of byte arrays
        /// </summary>
        /// <param name="type"></param>
        /// <param name="byteLen"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public List<byte[]> SoftpotReadAll(SoftpotType type, int byteLen)
        {
            Console.Write("XCMP: reading all softpot values for softpot {type} ({len} bytes each", Enum.GetName(type), byteLen);

            SoftpotMessage msg = new SoftpotMessage(MsgType.REQUEST, SoftpotOperation.READ_ALL, type);

            SoftpotMessage resp = SendSoftpot(msg);

            // Validate
            if (resp.Value.Length % byteLen != 0)
                throw new Exception($"Softpot value array not an even multiple of byte length!");

            // Determine number of values in response
            int n_vals = (int)(resp.Value.Length / byteLen);

            // List
            List<byte[]> values = new List<byte[]>();

            // Iterate
            for (int i = 0; i < n_vals; i++)
            {
                byte[] value = resp.Value.Skip(i * byteLen).Take(byteLen).ToArray();
                values.Add(value);
            }

            return values;
        }

        /// <summary>
        /// Read all frequencies associated with a softpot type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public int[] SoftpotReadAllFrequencies(SoftpotType type)
        {
            Log.Debug("XCMP: reading all softpot frequencies for softpot {type}", Enum.GetName(type));

            SoftpotMessage msg = new SoftpotMessage(MsgType.REQUEST, SoftpotOperation.READ_ALL_FREQ, type);

            SoftpotMessage resp = SendSoftpot(msg);

            // Parse the frequencies in the response (freqs are 4 byes each)
            int n_freqs = (resp.Length - 4) / 4;
            int[] freqs = new int[n_freqs];
            for (int i = 0; i < n_freqs; i++)
            {
                byte[] freq_bytes = resp.Value.Skip(i * 4).Take(4).ToArray();
                freqs[i] = (int)BytesToFrequency(freq_bytes);
                Log.Verbose("Parsing frequency {0}/{1}: {2} -> {3} Hz", i + 1, n_freqs, Convert.ToHexString(freq_bytes), freqs[i]);
            }

            return freqs;
        }

        public virtual int[] GetTXPowerPoints()
        {
            // Implemented by derived classes
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get the P25 RX BER
        /// </summary>
        /// <param name="nFrames">number of frames to average over for measurement</param>
        /// <returns></returns>
        public double GetP25BER(int nIntFrames)
        {
            Log.Debug("XCMP: measuring P25 BER using {nframes} frames of integration", nIntFrames);

            // Configure the RX chain
            XcmpMessage msg = new XcmpMessage(MsgType.REQUEST, Opcode.RECEIVE_CONFIG);
            msg.Data = new byte[2]
            {
                (byte)RxBerTestPattern.P25_1011,
                (byte)RxModulation.C4FM
            };
            Send(msg);

            Thread.Sleep(500);

            // Setup for the test
            msg = new XcmpMessage(MsgType.REQUEST, Opcode.RX_BER_CONTROL);
            msg.Data = new byte[2]
            {
                (byte)RxBerTestMode.CONTINUOUS,
                (byte)nIntFrames
            };
            Send(msg);

            // Wait for the requested number of frames
            Thread.Sleep(800 * nIntFrames);

            // Request an RX BER report
            msg = new XcmpMessage(MsgType.REQUEST, Opcode.RX_BER_SYNC_REPORT);
            XcmpMessage resp = Send(msg);

            //System.Threading.Thread.Sleep(500);

            // Parse the response
            return CalculateP25BER(resp.Data, nIntFrames);
        }

        /// <summary>
        /// Parse an RX BER response byte array
        /// </summary>
        /// <param name="berBytes">the array of BER responses, must be a multiple of 5</param>
        /// <param name="nFrames">the number of total frames integrated per measurement</param>
        /// <returns></returns>
        private static double CalculateP25BER(byte[] berBytes, int nFrames)
        {
            // Ensure length is correct
            if (berBytes.Length % 5 != 0)
                throw new ArgumentException($"BER byte array must be a multiple of 5 (got length {berBytes.Length})");

            // Calculate number of BER frames
            int frames = berBytes.Length / 5;

            // Number of bits in a single P25 frame
            const int P25_FRAME_BITS = 3456;

            // Running total bit errors count
            int totalBitErrors = 0;
            // Total number of bits to count against
            int totalBits = 0;

            // Iterate over each report
            for (int i = 0; i < frames; i++)
            {
                // Get the frame bytes
                byte[] frame = berBytes.Skip(i * 5).Take(5).ToArray();
                // Extract frame number
                byte frame_n = frame[0];
                // Extract sync/nosync
                RxBerSyncStatus status = (RxBerSyncStatus)frame[1];
                // If no sync or lost sync, ignore
                if (status == RxBerSyncStatus.NO_SYNC || status == RxBerSyncStatus.LOST)
                    continue;
                // Add bit errors to running total
                totalBitErrors += (int)BitConverter.ToUInt32(frame.Skip(2).Take(3).ToArray());
                // The total number of bits for this report is the number of frames integrated plus the frame bit count
                totalBits += (P25_FRAME_BITS * nFrames);
            }
            // Return the percentage of bit errors
            return (totalBitErrors / totalBits);
        }

        /// <summary>
        /// Retrieve the softpot parameters for a softpot from the radio
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public SoftpotParams SoftpotGetParams(SoftpotType type)
        {
            // New struct
            SoftpotParams p = new SoftpotParams();

            // Read frequencies
            p.Frequencies = SoftpotReadAllFrequencies(type);

            // Get min/max/byte length
            byte[] min = SoftpotGetMinimum(type);
            p.Min = SoftpotBytesToValue(min);
            p.Max = SoftpotBytesToValue(SoftpotGetMaximum(type));
            p.ByteLength = min.Length;

            // Get initial values
            List<byte[]> vals = SoftpotReadAll(type, p.ByteLength);

            // Validate
            if (vals.Count != p.Frequencies.Length)
                throw new Exception($"Did not get expected number of softpot values for frequencies (Got {vals.Count}, Expected {p.Frequencies.Length}");

            // Add to list
            p.Values = new int[vals.Count];
            for (int i = 0; i < vals.Count; i++)
            {
                p.Values[i] = SoftpotBytesToValue(vals[i]);
            }

            // Return
            return p;
        }
    }
}