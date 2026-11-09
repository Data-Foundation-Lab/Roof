using System;
using System.Collections.Generic;
using System.Linq;

namespace Roof.Engine.Tools
{
    public static class WifiAPLister
    {
        public sealed record WifiAP
        (
            string Ssid,
            string Bssid,
            int Channel,
            int Signal,
            bool Active,
            string Security
        )
        {
            public bool Is5Ghz => Channel > 14;

            public string BandLabel => Is5Ghz ? "5GHz" : "2.4GHz";

            public bool IsOpenNetwork =>
                string.IsNullOrWhiteSpace(Security) ||
                Security == "--" ||
                Security.Equals("NONE", StringComparison.OrdinalIgnoreCase);

            public override string ToString()
            {
                return $"{(Active ? "[CONNECTED] " : "")}{Ssid}  {BandLabel}  ch{Channel}  {Signal}%  " +
                       $"{(IsOpenNetwork ? "[OPEN]" : Security)}  {Bssid}";
            }
        }

        /// <summary>
        /// Input: raw output of:
        /// nmcli -t -f IN-USE,BSSID,SSID,CHAN,SIGNAL,SECURITY device wifi list
        /// </summary>
        public static List<WifiAP> ListBestPerSsid(string nmcliOutput, int strong5GhzThreshold = 60)
        {
            var aps = ParseNmcliTerse(nmcliOutput)
                .Where(a => !string.IsNullOrWhiteSpace(a.Ssid))
                .ToList();

            var bestPerSsid = aps
                .GroupBy(a => a.Ssid, StringComparer.Ordinal)
                .Select(g => PickBestApForSsid(g, strong5GhzThreshold))
                .OrderByDescending(a => a.Is5Ghz)
                .ThenByDescending(a => a.Signal)
                .ThenBy(a => a.Ssid, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return bestPerSsid;
        }

        public static List<WifiAP> ParseNmcliTerse(string nmcliOutput)
        {
            var result = new List<WifiAP>();
            if (string.IsNullOrWhiteSpace(nmcliOutput))
                return result;

            var lines = nmcliOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                var parts = line.Split(':');

                if (parts.Length < 11)
                    continue;

                bool active = parts[0] == "*";

                string bssid = string.Join(":", parts.Skip(1).Take(6));
                bssid = UnescapeBssid(bssid);

                int channel = TryParseInt(parts[^3]);
                int signal = Clamp(TryParseInt(parts[^2]), 0, 100);
                string security = parts[^1].Trim();

                int ssidStart = 7;
                int ssidCount = parts.Length - 3 - ssidStart;
                if (ssidCount <= 0) continue;

                string ssid = string.Join(":", parts.Skip(ssidStart).Take(ssidCount)).Trim();
                if (ssid.Length == 0) continue;

                result.Add(new WifiAP(ssid, bssid, channel, signal, active, security));
            }

            return result;
        }

        private static WifiAP PickBestApForSsid(IEnumerable<WifiAP> apsForSsid, int strong5GhzThreshold)
        {
            var list = apsForSsid.ToList();

            // Prefer strong 5GHz AP for this SSID
            var strong5 = list.Where(a => a.Is5Ghz && a.Signal >= strong5GhzThreshold).ToList();
            if (strong5.Count > 0)
                return strong5.OrderByDescending(SelectionScore).First();

            // Otherwise best overall
            return list.OrderByDescending(SelectionScore).First();
        }

        private static int SelectionScore(WifiAP a)
        {
            int score = a.Signal * 10;

            if (a.Is5Ghz) score += 50;
            if (a.Active) score += 25;

            // Optional? slightly penalise open networks so secured ones rank higher
            if (a.IsOpenNetwork) 
                score -= 30;

            return score;
        }

        private static string UnescapeBssid(string bssid)
            => bssid.Replace("\\:", ":").Replace("\\", "");

        private static int TryParseInt(string s)
            => int.TryParse(s?.Trim(), out var v) ? v : 0;

        private static int Clamp(int v, int min, int max)
            => v < min ? min : (v > max ? max : v);
    }
}