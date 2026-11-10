// StormPT Mobile LAB: read-only structural probe of NFMsg.ReqRoleList.
// No network, no credentials, no player database access.
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace StormPT.MobileLab
{
    public sealed class MobileRoleListProbeResult
    {
        public int GameId { get; internal set; }
        public string ClaimedAccount { get; internal set; } = "";
        public bool HasAccountType { get; internal set; }
    }

    public static class MobileRoleListProbe
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static MobileRoleListProbeResult Inspect(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > 4096)
                throw new ArgumentException("Invalid ReqRoleList payload length.");
            int pos = 0;
            bool hasGame = false, hasAccount = false, hasAccountType = false;
            int game = 0;
            string account = "";
            int fieldCount = 0;
            while (pos < data.Length)
            {
                if (++fieldCount > 64) throw new FormatException("Too many fields.");
                ulong tag = MobileLoginProbe.ReadVarint(data, ref pos);
                if ((tag >> 3) == 0 || (tag >> 3) > Int32.MaxValue)
                    throw new FormatException("Invalid field number.");
                int number = (int)(tag >> 3), wire = (int)(tag & 7);
                if (number == 1 && wire == 0)
                {
                    if (hasGame) throw new FormatException("Duplicate game id.");
                    ulong val = MobileLoginProbe.ReadVarint(data, ref pos);
                    if (val == 0 || val > Int32.MaxValue)
                        throw new FormatException("Invalid game id.");
                    game = (int)val;
                    hasGame = true;
                }
                else if (number == 2 && wire == 2)
                {
                    if (hasAccount) throw new FormatException("Duplicate account.");
                    int start, length;
                    MobileLoginProbe.ReadLengthDelimited(data, ref pos, out start, out length);
                    if (length < 1 || length > 32) throw new FormatException("Invalid account length.");
                    account = Utf8.GetString(data, start, length);
                    if (!Regex.IsMatch(account, "^[A-Za-z0-9]{1,32}$"))
                        throw new FormatException("Invalid account characters.");
                    hasAccount = true;
                }
                else if (number == 3 && wire == 2)
                {
                    int start, length;
                    MobileLoginProbe.ReadLengthDelimited(data, ref pos, out start, out length);
                    hasAccountType = length > 0;
                }
                else MobileLoginProbe.SkipValue(data, ref pos, wire);
            }
            if (!hasGame || !hasAccount)
                throw new FormatException("Required game_id/account absent.");
            return new MobileRoleListProbeResult { GameId = game, ClaimedAccount = account, HasAccountType = hasAccountType };
        }
    }
}
