// Hallow Keys v2.8 - password health for the Hallow family (Hallow Guard's one safety score), AGGREGATE COUNTS ONLY.
//  - While unlocked, status.json / the pipe's "status" carry "health": how many logins, and how many are weak, reused, leaked,
//    on a breached site, missing 2FA that the site offers, old, plus the score and when they were counted. Never an item's name,
//    username, address, folder, vault, id or password - only numbers and a time.
//  - While locked it reports the last counts with their age ("fresh": false, "ageMinutes"), kept only in this status.json (read
//    back at start, so a restart still knows them); never counted before = no "health" at all.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public static class HealthCounts
    {
        // the only fields "health" can ever have
        public static readonly string[] Numbers = { "logins", "weak", "reused", "leaked", "breached", "missing2fa", "old", "score" };
        public static readonly string[] Fields = Numbers.Concat(new[] { "when", "fresh", "ageMinutes" }).ToArray();

        // the counts from a Password Health report (breached / missing2fa are null until those public lists were downloaded)
        public static Dictionary<string, object> From(Watchtower.Report r, DateTime now)
        {
            return new Dictionary<string, object> {
                { "logins", r.Logins }, { "weak", r.Basic.Weak.Count }, { "reused", r.Basic.Reused.Distinct().Count() }, { "leaked", r.Basic.Leaked.Count },
                { "breached", r.HaveBreaches ? (object)r.Breached.Count : null }, { "missing2fa", r.HaveTwoFa ? (object)r.TwoFa.Count : null },
                { "old", r.Basic.Old.Count }, { "score", r.Score }, { "when", Family.Time(now) } };
        }

        // the "health" block of a status.json, cleaned: only the number fields (whole numbers 0..1,000,000 or null) and a valid time
        public static Dictionary<string, object> FromStatusJson(string json)
        {
            try
            {
                var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; object h;
                if (d == null || !d.TryGetValue("health", out h)) return null;
                var hd = h as Dictionary<string, object>; if (hd == null) return null;
                object w; DateTime t;
                if (!hd.TryGetValue("when", out w) || !(w is string) || !DateTime.TryParse((string)w, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return null;
                var o = new Dictionary<string, object>();
                foreach (var k in Numbers)
                {
                    object v; hd.TryGetValue(k, out v);
                    if (v is int && (int)v >= 0 && (int)v <= 1000000) o[k] = (int)v; else o[k] = null;
                }
                if (o["logins"] == null) return null;
                o["when"] = Family.Time(t);
                return o;
            }
            catch { return null; }
        }

        // what status.json shows: the counts, whether they come from the open vault right now, and how old they are
        public static Dictionary<string, object> WithAge(Dictionary<string, object> h, bool fresh, DateTime now)
        {
            if (h == null) return null;
            var o = new Dictionary<string, object>();
            foreach (var k in Numbers) { object v; o[k] = h.TryGetValue(k, out v) ? v : null; }
            o["when"] = h["when"]; o["fresh"] = fresh;
            DateTime t; o["ageMinutes"] = DateTime.TryParse((string)h["when"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? (object)Math.Max(0, (int)(now - t).TotalMinutes) : null;
            return o;
        }
    }

    public partial class MainWindow
    {
        Dictionary<string, object> lastHealth; bool healthLoaded; Vault healthVault; int healthSeq = -1; DateTime healthAt;

        // counted again when the vault was opened, saved, or every 10 minutes while it stays open (only when status is written)
        void CountHealth(bool force)
        {
            if (vault == null) return;
            if (!force && healthVault == vault && healthSeq == saveSeq && (DateTime.Now - healthAt).TotalMinutes < 10) return;
            try { lastHealth = HealthCounts.From(WatchReport(), DateTime.Now); } catch { }
            healthVault = vault; healthSeq = saveSeq; healthAt = DateTime.Now; healthLoaded = true;
        }
        Dictionary<string, object> HealthStatus()
        {
            if (Snap.On) return null;
            if (vault != null) { CountHealth(false); return HealthCounts.WithAge(lastHealth, true, DateTime.Now); }
            if (!healthLoaded)
            {
                healthLoaded = true;
                try { var f = Path.Combine(KeysSettings.Dir, "status.json"); if (File.Exists(f)) lastHealth = HealthCounts.FromStatusJson(File.ReadAllText(f)); } catch { }
            }
            return HealthCounts.WithAge(lastHealth, false, DateTime.Now);
        }
    }
}
