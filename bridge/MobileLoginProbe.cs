// StormPT Mobile LAB - Protobuf ReqAccountLogin frame inspector.
// Debugging only: does not authenticate, store or reveal password contents.
// This is deliberately NOT a replacement for generated Protobuf classes.
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace StormPT.MobileLab
{
    public sealed class MobileLoginProbeResult
    {
        public string Account { get; internal set; }
        public bool HasPasswordField { get; internal set; }
        public int ClientVersion { get; internal set; }
        public int ParsedFields { get; internal set; }
    }

    public static class MobileLoginProbe
    {
        private const int MaxPacketSize = 16 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static MobileLoginProbeResult Inspect(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > MaxPacketSize)
                throw new ArgumentException("Payload length invalid.");

            int pos = 0;
            int fields = 0;
            bool seenAccount = false;
            bool seenPassword = false;
            string account = null;
            int clientVersion = -1;

            while (pos < data.Length)
            {
                if (++fields > 256) throw new FormatException("Too many fields.");
                ulong tag = ReadVarint(data, ref pos);
                int field = checked((int)(tag >> 3));
                int wire = (int)(tag & 7);
                if (field == 0) throw new FormatException("Invalid field number.");

                if (field == 2 && wire == 2)
                {
                    if (seenAccount) throw new FormatException("Duplicate account field.");
                    int start, length;
                    ReadLengthDelimited(data, ref pos, out start, out length);
                    if (length < 1 || length > 32) throw new FormatException("Invalid account length.");
                    account = Utf8.GetString(data, start, length);
                    if (!Regex.IsMatch(account, "^[A-Za-z0-9]{1,32}$"))
                        throw new FormatException("Invalid account characters.");
                    seenAccount = true;
                }
                else if (field == 3 && wire == 2)
                {
                    if (seenPassword) throw new FormatException("Duplicate password field.");
                    int start, length;
                    ReadLengthDelimited(data, ref pos, out start, out length);
                    if (length < 1 || length > 128)
                        throw new FormatException("Invalid password-field length.");
                    // Password bytes are not decoded, copied, hashed or logged.
                    seenPassword = true;
                }
                else if (field == 6 && wire == 0)
                {
                    ulong v = ReadVarint(data, ref pos);
                    if (v > Int32.MaxValue) throw new FormatException("ClientVersion out of range.");
                    clientVersion = (int)v;
                }
                else
                {
                    SkipValue(data, ref pos, wire);
                }
            }

            if (!seenAccount || !seenPassword)
                throw new FormatException("Required account/password fields absent.");

            return new MobileLoginProbeResult
            {
                Account = account,
                HasPasswordField = seenPassword,
                ClientVersion = clientVersion,
                ParsedFields = fields
            };
        }

        private static ulong ReadVarint(byte[] data, ref int pos)
        {
            ulong result = 0;
            for (int i = 0; i < 10; i++)
            {
                if (pos >= data.Length) throw new FormatException("Truncated varint.");
                byte b = data[pos++];
                if (i == 9 && b > 1) throw new FormatException("Varint overflow.");
                result |= (ulong)(b & 0x7F) << (7 * i);
                if ((b & 0x80) == 0) return result;
            }
            throw new FormatException("Varint too long.");
        }

        private static void ReadLengthDelimited(byte[] data, ref int pos, out int start, out int length)
        {
            ulong value = ReadVarint(data, ref pos);
            if (value > (ulong)(data.Length - pos)) throw new FormatException("Truncated payload.");
            if (value > 8192) throw new FormatException("Field too large.");
            length = (int)value;
            start = pos;
            pos += length;
        }

        private static void SkipValue(byte[] data, ref int pos, int wire)
        {
            switch (wire)
            {
                case 0: ReadVarint(data, ref pos); break;
                case 1: Skip(data, ref pos, 8); break;
                case 2: int start, length; ReadLengthDelimited(data, ref pos, out start, out length); break;
                case 5: Skip(data, ref pos, 4); break;
                default: throw new FormatException("Unsupported wire type.");
            }
        }

        private static void Skip(byte[] data, ref int pos, int count)
        {
            if (pos > data.Length - count) throw new FormatException("Truncated fixed-width field.");
            pos += count;
        }
    }
}
