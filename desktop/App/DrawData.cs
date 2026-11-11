// EuroWatcher - reads, checks and rebuilds the draw history (data/draws.js).
//
// This is the same procedure as tools/update-data.mjs, so the installed program can update
// itself without Node: three public sources are read, the balls must agree wherever two of them
// have the same draw, and nothing is written when anything looks wrong.
// If the sources cannot be reached, the copy kept up to date in the project's GitHub repository
// is used instead, after the same checks.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace EuroWatcher
{
    internal sealed class Draw
    {
        public int No;
        public string Date;
        public int[] N;
        public int[] S;
        public double?[] Prizes;
        public long?[] Winners;
        public long? Gbp;
        public long? ListWins;   // only from the draw list: jackpot winners according to it

        public string Key { get { return string.Join(",", N) + "|" + string.Join(",", S); } }
    }

    internal sealed class DrawFile
    {
        public List<Draw> Draws = new List<Draw>();
        public int CrossChecked;
        public bool OfficialLatest;
        public string Generated;

        public string Latest { get { return Draws.Count > 0 ? Draws[Draws.Count - 1].Date : null; } }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly int[][] Tiers =
        {
            new[] { 5, 2 }, new[] { 5, 1 }, new[] { 5, 0 }, new[] { 4, 2 }, new[] { 4, 1 }, new[] { 3, 2 }, new[] { 4, 0 },
            new[] { 2, 2 }, new[] { 3, 1 }, new[] { 3, 0 }, new[] { 1, 2 }, new[] { 2, 1 }, new[] { 2, 0 },
        };

        public static readonly string[][] Sources =
        {
            new[] { "Merseyworld lottery archive", "https://lottery.merseyworld.com/cgi-bin/lottery?days=20&Machine=Z&Ballset=0&order=1&show=1&year=0&display=CSV", "Draw numbers, dates and balls for every draw" },
            new[] { "EuroMillions API (pedromealha.dev)", "https://euromillions.api.pedromealha.dev/v1/draws", "Second copy of the balls, plus winners and prizes in euros" },
            new[] { "UK National Lottery results feed", "https://www.national-lottery.co.uk/results/euromillions/draw-history/xml", "Official record of the most recent draw" },
        };

        // ---------- reading ----------

        static JavaScriptSerializer Json()
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            json.MaxJsonLength = int.MaxValue;
            json.RecursionLimit = 64;
            return json;
        }

        /// <summary>Reads a draws.js file ("window.EURO_DATA = {...};").</summary>
        public static DrawFile Parse(string text)
        {
            Match m = Regex.Match(text ?? "", @"window\.EURO_DATA\s*=\s*(\{[\s\S]*\});?\s*$");
            if (!m.Success) throw new FormatException("not a draw data file");
            Dictionary<string, object> root = (Dictionary<string, object>)Json().DeserializeObject(m.Groups[1].Value);
            DrawFile file = new DrawFile();
            object generated;
            if (root.TryGetValue("generated", out generated)) file.Generated = generated as string;
            object checks;
            if (root.TryGetValue("checks", out checks) && checks is Dictionary<string, object>)
            {
                Dictionary<string, object> c = (Dictionary<string, object>)checks;
                object v;
                if (c.TryGetValue("crossChecked", out v) && v != null) file.CrossChecked = Convert.ToInt32(v, Inv);
                if (c.TryGetValue("officialLatest", out v) && v is bool) file.OfficialLatest = (bool)v;
            }
            foreach (object rowObj in (object[])root["draws"])
            {
                object[] row = (object[])rowObj;
                Draw d = new Draw();
                d.No = Convert.ToInt32(row[0], Inv);
                d.Date = (string)row[1];
                d.N = ((object[])row[2]).Select(x => Convert.ToInt32(x, Inv)).ToArray();
                d.S = ((object[])row[3]).Select(x => Convert.ToInt32(x, Inv)).ToArray();
                if (row[4] != null) d.Prizes = ((object[])row[4]).Select(x => x == null ? (double?)null : Convert.ToDouble(x, Inv)).ToArray();
                if (row[5] != null) d.Winners = ((object[])row[5]).Select(x => x == null ? (long?)null : Convert.ToInt64(x, Inv)).ToArray();
                if (row.Length > 6 && row[6] != null) d.Gbp = Convert.ToInt64(row[6], Inv);
                file.Draws.Add(d);
            }
            return file;
        }

        static readonly Dictionary<string, int> Months = new Dictionary<string, int>
        {
            { "Jan", 1 }, { "Feb", 2 }, { "Mar", 3 }, { "Apr", 4 }, { "May", 5 }, { "Jun", 6 },
            { "Jul", 7 }, { "Aug", 8 }, { "Sep", 9 }, { "Oct", 10 }, { "Nov", 11 }, { "Dec", 12 },
        };

        /// <summary>"1997, Tue,10,Nov,2026, 15,18,31,37,41,01,05, 118812405,    0"</summary>
        public static Dictionary<string, Draw> ParseList(string text)
        {
            Dictionary<string, Draw> draws = new Dictionary<string, Draw>();
            Regex row = new Regex(@"^\s*(\d+),\s*\w{3},\s*(\d+),(\w{3}),(\d{4}),\s*(\d+),(\d+),(\d+),(\d+),(\d+),(\d+),(\d+),\s*(\d*),\s*(\d*)");
            foreach (string line in text.Split('\n'))
            {
                Match m = row.Match(line.TrimEnd('\r'));
                int month;
                if (!m.Success || !Months.TryGetValue(m.Groups[3].Value, out month)) continue;
                Draw d = new Draw();
                d.No = int.Parse(m.Groups[1].Value, Inv);
                d.Date = m.Groups[4].Value + "-" + month.ToString("00", Inv) + "-" + int.Parse(m.Groups[2].Value, Inv).ToString("00", Inv);
                d.N = Enumerable.Range(5, 5).Select(i => int.Parse(m.Groups[i].Value, Inv)).OrderBy(x => x).ToArray();
                d.S = Enumerable.Range(10, 2).Select(i => int.Parse(m.Groups[i].Value, Inv)).OrderBy(x => x).ToArray();
                d.Gbp = m.Groups[12].Value == "" ? (long?)null : long.Parse(m.Groups[12].Value, Inv);
                d.ListWins = m.Groups[13].Value == "" ? (long?)null : long.Parse(m.Groups[13].Value, Inv);
                draws[d.Date] = d;
            }
            return draws;
        }

        public static Dictionary<string, Draw> ParsePrizes(string text)
        {
            Dictionary<string, Draw> draws = new Dictionary<string, Draw>();
            foreach (object itemObj in (object[])Json().DeserializeObject(text))
            {
                Dictionary<string, object> item = (Dictionary<string, object>)itemObj;
                Draw d = new Draw();
                d.Date = Convert.ToString(item["date"], Inv).Substring(0, 10);
                d.N = ((object[])item["numbers"]).Select(x => Convert.ToInt32(x, Inv)).OrderBy(x => x).ToArray();
                d.S = ((object[])item["stars"]).Select(x => Convert.ToInt32(x, Inv)).OrderBy(x => x).ToArray();
                object prizesObj;
                object[] prizes = item.TryGetValue("prizes", out prizesObj) ? prizesObj as object[] : null;
                if (prizes != null && prizes.Length > 0)
                {
                    d.Winners = new long?[Tiers.Length];
                    d.Prizes = new double?[Tiers.Length];
                    for (int i = 0; i < Tiers.Length; i++)
                    {
                        Dictionary<string, object> found = null;
                        foreach (object pObj in prizes)
                        {
                            Dictionary<string, object> p = (Dictionary<string, object>)pObj;
                            if (Convert.ToInt32(p["matched_numbers"], Inv) == Tiers[i][0] && Convert.ToInt32(p["matched_stars"], Inv) == Tiers[i][1]) { found = p; break; }
                        }
                        if (found == null) continue;
                        object w, amount;
                        if (found.TryGetValue("winners", out w) && w != null) d.Winners[i] = Convert.ToInt64(w, Inv);
                        // A tier nobody won has no per-winner amount, except the jackpot, where the figure is the pot.
                        if (found.TryGetValue("prize", out amount) && amount != null)
                        {
                            double value = Convert.ToDouble(amount, Inv);
                            if (!(i > 0 && value == 0)) d.Prizes[i] = Math.Round(value * 100) / 100;
                        }
                    }
                }
                draws[d.Date] = d;
            }
            return draws;
        }

        public static Draw ParseLatest(string text)
        {
            Match date = Regex.Match(text, @"<draw-date>(\d{4}-\d{2}-\d{2})<");
            int[] n = Regex.Matches(text, @"<ball number=""\d+"">(\d+)<").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, Inv)).OrderBy(x => x).ToArray();
            int[] s = Regex.Matches(text, @"<bonus-ball type=""luckystar"" number=""\d+"">(\d+)<").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, Inv)).OrderBy(x => x).ToArray();
            if (!date.Success || n.Length != 5 || s.Length != 2) throw new FormatException("unrecognised format");
            Draw d = new Draw();
            d.Date = date.Groups[1].Value;
            d.N = n;
            d.S = s;
            Match no = Regex.Match(text, @"<draw-number>(\d+)<");
            if (no.Success) d.No = int.Parse(no.Groups[1].Value, Inv);
            Match block = Regex.Match(text, @"<prize-tiers country=""All"">([\s\S]*?)</prize-tiers>");
            if (block.Success && Regex.IsMatch(text, @"<confirmed>Y<"))
            {
                Dictionary<int, long> byLevel = new Dictionary<int, long>();
                foreach (Match m in Regex.Matches(block.Groups[1].Value, @"<prize-tier level=""(\d+)""><number-of-winners>(\d+)<"))
                    byLevel[int.Parse(m.Groups[1].Value, Inv)] = long.Parse(m.Groups[2].Value, Inv);
                if (byLevel.Count == Tiers.Length)
                    d.Winners = Enumerable.Range(1, Tiers.Length).Select(level => (long?)byLevel[level]).ToArray();
            }
            Match gbp = Regex.Match(text, @"<jackpot-amount>([\d,]+)<");
            if (gbp.Success) d.Gbp = long.Parse(gbp.Groups[1].Value.Replace(",", ""), Inv);
            return d;
        }

        // ---------- checks ----------

        const string TuesdaysSince = "2011-05-10";   // second weekly draw, 11 stars
        const string Star12Since = "2016-09-27";

        public static List<string> Validate(Draw d)
        {
            List<string> issues = new List<string>();
            int starMax = string.CompareOrdinal(d.Date, TuesdaysSince) < 0 ? 9 : string.CompareOrdinal(d.Date, Star12Since) < 0 ? 11 : 12;
            if (!OkSet(d.N, 5, 50)) issues.Add("numbers out of range");
            if (!OkSet(d.S, 2, starMax)) issues.Add("stars out of range for that date (max " + starMax + ")");
            DateTime day;
            if (!DateTime.TryParseExact(d.Date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out day)) issues.Add("bad date");
            else if (!(day.DayOfWeek == DayOfWeek.Friday || (day.DayOfWeek == DayOfWeek.Tuesday && string.CompareOrdinal(d.Date, TuesdaysSince) >= 0)))
                issues.Add("not a draw day");
            return issues;
        }

        static bool OkSet(int[] values, int count, int max)
        {
            return values != null && values.Length == count && values.Distinct().Count() == count && values.All(x => x >= 1 && x <= max);
        }

        /// <summary>Checks a whole file; returns the problems found (none means it is fine to use).</summary>
        public List<string> Problems()
        {
            List<string> problems = new List<string>();
            for (int i = 0; i < Draws.Count; i++)
            {
                Draw d = Draws[i];
                if (d.No != i + 1) problems.Add(d.Date + ": draw number " + d.No + " breaks the sequence");
                if (i > 0 && string.CompareOrdinal(Draws[i - 1].Date, d.Date) >= 0) problems.Add(d.Date + ": out of order");
                foreach (string issue in Validate(d)) problems.Add(d.Date + ": " + issue);
            }
            return problems;
        }

        // ---------- building from the sources ----------

        public static DrawFile Build(DrawFile previous, string listText, string prizesText, string latestText)
        {
            Dictionary<string, Draw> list = ParseList(listText);
            if (list.Count < 1900) throw new InvalidDataException("the draw list came back incomplete");
            Dictionary<string, Draw> prizes = new Dictionary<string, Draw>();
            if (prizesText != null)
            {
                try { prizes = ParsePrizes(prizesText); }
                catch (Exception ex) { App.Log("prizes source unreadable: " + ex.Message); }
            }
            Draw latest = null;
            if (latestText != null)
            {
                try { latest = ParseLatest(latestText); }
                catch (Exception ex) { App.Log("latest-draw feed unreadable: " + ex.Message); }
            }
            Dictionary<string, Draw> old = new Dictionary<string, Draw>();
            if (previous != null) foreach (Draw d in previous.Draws) old[d.Date] = d;

            SortedSet<string> all = new SortedSet<string>(list.Keys, StringComparer.Ordinal);
            all.UnionWith(prizes.Keys);
            if (latest != null) all.Add(latest.Date);
            List<string> dates = all.ToList();

            List<string> problems = new List<string>();
            DrawFile file = new DrawFile();
            for (int i = 0; i < dates.Count; i++)
            {
                string date = dates[i];
                Draw L, P, O = latest != null && latest.Date == date ? latest : null, before;
                list.TryGetValue(date, out L);
                prizes.TryGetValue(date, out P);
                old.TryGetValue(date, out before);
                List<Draw> copies = new List<Draw>();
                foreach (Draw c in new[] { L, P, O }) if (c != null) copies.Add(c);
                if (copies.Select(c => c.Key).Distinct().Count() > 1)
                    problems.Add(date + ": sources disagree on the balls (" + string.Join("  vs  ", copies.Select(c => c.Key)) + ")");
                else if (copies.Count > 1)
                {
                    file.CrossChecked++;
                    if (O != null && i == dates.Count - 1) file.OfficialLatest = true;
                }

                Draw d = new Draw();
                d.No = L != null ? L.No : O != null && O.No > 0 ? O.No : i + 1;
                d.Date = date;
                d.N = copies[0].N;
                d.S = copies[0].S;
                if (d.No != i + 1) problems.Add(date + ": draw number " + d.No + " breaks the sequence (expected " + (i + 1) + ")");
                foreach (string issue in Validate(d)) problems.Add(date + ": " + issue);

                d.Prizes = P != null && P.Prizes != null ? P.Prizes : before != null ? before.Prizes : null;
                d.Winners = P != null && P.Winners != null ? P.Winners : O != null && O.Winners != null ? O.Winners : before != null ? before.Winners : null;
                if (d.Winners == null && L != null)
                {
                    // Only the list knows this draw. Its "0 winners" is trusted once the next draw shows the pot kept growing.
                    Draw next = i + 1 < dates.Count && list.ContainsKey(dates[i + 1]) ? list[dates[i + 1]] : null;
                    long? jackpotWinners = L.ListWins;
                    if (jackpotWinners == 0 && !(next != null && next.Gbp != null && L.Gbp != null && next.Gbp >= L.Gbp * 0.5)) jackpotWinners = null;
                    if (jackpotWinners != null)
                    {
                        d.Winners = new long?[Tiers.Length];
                        d.Winners[0] = jackpotWinners;
                    }
                }
                d.Gbp = L != null && L.Gbp != null ? L.Gbp : O != null && O.Gbp != null ? O.Gbp : before != null ? before.Gbp : null;
                file.Draws.Add(d);
            }

            if (previous != null && file.Draws.Count < previous.Draws.Count)
                problems.Add("only " + file.Draws.Count + " draws found but " + previous.Draws.Count + " are already on file");
            if (problems.Count > 0)
                throw new InvalidDataException(problems.Count + " problem(s), first: " + problems[0]);
            return file;
        }

        // ---------- writing ----------

        static string Num(double? v) { return v == null ? "null" : v.Value.ToString("R", Inv); }
        static string Num(long? v) { return v == null ? "null" : v.Value.ToString(Inv); }

        static string Str(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        public static string Row(Draw d)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('[').Append(d.No.ToString(Inv)).Append(',').Append(Str(d.Date)).Append(',');
            sb.Append('[').Append(string.Join(",", d.N)).Append("],[").Append(string.Join(",", d.S)).Append("],");
            sb.Append(d.Prizes == null ? "null" : "[" + string.Join(",", d.Prizes.Select(Num)) + "]").Append(',');
            sb.Append(d.Winners == null ? "null" : "[" + string.Join(",", d.Winners.Select(Num)) + "]").Append(',');
            sb.Append(Num(d.Gbp)).Append(']');
            return sb.ToString();
        }

        public string Serialize()
        {
            string generated = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);
            StringBuilder sb = new StringBuilder();
            sb.Append("// Generated by tools/update-data.mjs or the EuroWatcher app. Do not edit by hand.\n");
            sb.Append("window.EURO_DATA = {\n");
            sb.Append(" \"generated\": ").Append(Str(generated)).Append(",\n");
            sb.Append(" \"tiers\": [").Append(string.Join(",", Tiers.Select(t => "[" + t[0] + "," + t[1] + "]"))).Append("],\n");
            sb.Append(" \"sources\": [").Append(string.Join(",", Sources.Select(s => "{\"name\":" + Str(s[0]) + ",\"url\":" + Str(s[1]) + ",\"role\":" + Str(s[2]) + "}"))).Append("],\n");
            sb.Append(" \"checks\": {\"crossChecked\":").Append(CrossChecked.ToString(Inv))
              .Append(",\"officialLatest\":").Append(OfficialLatest ? "true" : "false")
              .Append(",\"withPrizes\":").Append(Draws.Count(d => d.Prizes != null).ToString(Inv)).Append("},\n");
            sb.Append(" \"draws\": [\n");
            sb.Append(string.Join(",\n", Draws.Select(Row)));
            sb.Append("\n ]\n};\n");
            return sb.ToString();
        }
    }

    internal sealed class UpdateResult
    {
        public bool Ok;
        public int Added;
        public int Total;
        public string Latest;
        public string Message = "";
        public string Via = "";
    }

    internal static class Updater
    {
        public const string GitHubCopy = "https://raw.githubusercontent.com/Mohenni/EuroWatcher/main/data/draws.js";
        static readonly object Gate = new object();

        static string Fetch(string url, bool retry)
        {
            Exception last = null;
            int[] waits = retry ? new[] { 0, 4, 10 } : new[] { 0 };
            foreach (int wait in waits)
            {
                if (wait > 0) Thread.Sleep(wait * 1000);
                try
                {
                    return Get(url);
                }
                catch (WebException ex)
                {
                    last = ex;
                    HttpWebResponse res = ex.Response as HttpWebResponse;
                    // Only "busy" answers are worth another try; a missing page will stay missing.
                    if (res != null && (int)res.StatusCode != 429 && (int)res.StatusCode < 500) break;
                }
            }
            throw last ?? new WebException("no answer");
        }

        /// <summary>One GET request. Follows redirects itself, because .NET does not follow "308 Permanent Redirect".</summary>
        static string Get(string url)
        {
            Uri current = new Uri(url);
            for (int hop = 0; hop < 6; hop++)
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(current);
                req.UserAgent = "Mozilla/5.0 (compatible; EuroWatcher/" + App.Version + ")";
                req.Accept = "*/*";
                req.AllowAutoRedirect = false;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                req.Timeout = 45000;
                req.ReadWriteTimeout = 45000;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)res.StatusCode;
                    string location = res.Headers["Location"];
                    if (code >= 300 && code < 400 && !string.IsNullOrEmpty(location))
                    {
                        Uri next = new Uri(current, location);
                        if (next.Scheme != Uri.UriSchemeHttps) throw new WebException("refused to follow a redirect away from https");
                        current = next;
                        continue;
                    }
                    using (StreamReader reader = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                        return reader.ReadToEnd();
                }
            }
            throw new WebException("too many redirects");
        }

        static string TryFetch(string url)
        {
            try { return Fetch(url, true); }
            catch (Exception ex)
            {
                App.Log("could not read " + url + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Fetches the newest draws and saves them. Safe to call from several threads.</summary>
        public static UpdateResult Run()
        {
            lock (Gate)
            {
                UpdateResult result = new UpdateResult();
                DrawFile previous = null;
                try { previous = DrawFile.Parse(DataStore.ReadText()); }
                catch (Exception ex) { App.Log("data on file unreadable: " + ex.Message); }
                int before = previous != null ? previous.Draws.Count : 0;

                DrawFile built = null;
                string directError = null;
                try
                {
                    string list = Fetch(DrawFile.Sources[0][1], true);
                    built = DrawFile.Build(previous, list, TryFetch(DrawFile.Sources[1][1]), TryFetch(DrawFile.Sources[2][1]));
                    result.Via = "sources";
                }
                catch (Exception ex)
                {
                    directError = ex.Message;
                    App.Log("update from the sources failed: " + ex.Message);
                }

                if (built == null)
                {
                    try
                    {
                        DrawFile copy = DrawFile.Parse(Fetch(GitHubCopy, true));
                        List<string> problems = copy.Problems();
                        if (problems.Count > 0) throw new InvalidDataException(problems[0]);
                        if (copy.Draws.Count < before) throw new InvalidDataException("the online copy is older than the data on this PC");
                        built = copy;
                        result.Via = "github";
                    }
                    catch (Exception ex)
                    {
                        App.Log("update from the online copy failed: " + ex.Message);
                        result.Ok = false;
                        result.Message = directError ?? ex.Message;
                        return result;
                    }
                }

                DataStore.Write(built.Serialize());
                result.Ok = true;
                result.Total = built.Draws.Count;
                result.Added = Math.Max(0, built.Draws.Count - before);
                result.Latest = built.Latest;
                App.Log("updated via " + result.Via + ": " + result.Total + " draws, " + result.Added + " new, latest " + result.Latest);
                return result;
            }
        }
    }
}
