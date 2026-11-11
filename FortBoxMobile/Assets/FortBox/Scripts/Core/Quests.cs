// Daily quests: the same pool and the same daily pick as the PC game
// (Python random.Random(md5(utc date)).sample(pool, 3) reproduced exactly),
// with progress kept on the device like the PC keeps it in its settings file.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FortBox
{
    /// CPython's random.Random (MT19937), enough for seed(int) + sample().
    public class PyRandom
    {
        readonly uint[] mt = new uint[624];
        int mti;

        public PyRandom(uint seed) { InitByArray(new[] { seed }); }

        void InitGenrand(uint s)
        {
            mt[0] = s;
            for (mti = 1; mti < 624; mti++)
                mt[mti] = (uint)(1812433253u * (mt[mti - 1] ^ (mt[mti - 1] >> 30)) + (uint)mti);
        }

        void InitByArray(uint[] key)
        {
            InitGenrand(19650218u);
            int i = 1, j = 0;
            int k = 624 > key.Length ? 624 : key.Length;
            for (; k > 0; k--)
            {
                mt[i] = (uint)((mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1664525u)) + key[j] + (uint)j);
                i++; j++;
                if (i >= 624) { mt[0] = mt[623]; i = 1; }
                if (j >= key.Length) j = 0;
            }
            for (k = 623; k > 0; k--)
            {
                mt[i] = (uint)((mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1566083941u)) - (uint)i);
                i++;
                if (i >= 624) { mt[0] = mt[623]; i = 1; }
            }
            mt[0] = 0x80000000u;
        }

        uint Next()
        {
            uint y;
            if (mti >= 624)
            {
                int kk;
                for (kk = 0; kk < 624 - 397; kk++)
                {
                    y = (mt[kk] & 0x80000000u) | (mt[kk + 1] & 0x7fffffffu);
                    mt[kk] = mt[kk + 397] ^ (y >> 1) ^ ((y & 1u) != 0 ? 0x9908b0dfu : 0u);
                }
                for (; kk < 623; kk++)
                {
                    y = (mt[kk] & 0x80000000u) | (mt[kk + 1] & 0x7fffffffu);
                    mt[kk] = mt[kk + (397 - 624)] ^ (y >> 1) ^ ((y & 1u) != 0 ? 0x9908b0dfu : 0u);
                }
                y = (mt[623] & 0x80000000u) | (mt[0] & 0x7fffffffu);
                mt[623] = mt[396] ^ (y >> 1) ^ ((y & 1u) != 0 ? 0x9908b0dfu : 0u);
                mti = 0;
            }
            y = mt[mti++];
            y ^= y >> 11;
            y ^= (y << 7) & 0x9d2c5680u;
            y ^= (y << 15) & 0xefc60000u;
            y ^= y >> 18;
            return y;
        }

        int GetRandBits(int k) { return (int)(Next() >> (32 - k)); }

        int RandBelow(int n)
        {
            if (n <= 0) return 0;
            int k = 0;
            for (int v = n; v > 0; v >>= 1) k++;
            int r = GetRandBits(k);
            while (r >= n) r = GetRandBits(k);
            return r;
        }

        public List<T> Sample<T>(IList<T> population, int k)
        {
            int n = population.Count;
            var result = new List<T>(k);
            int setsize = 21;
            if (k > 5) setsize += (int)Math.Pow(4, Math.Ceiling(Math.Log(k * 3, 4)));
            if (n <= setsize)
            {
                var pool = new List<T>(population);
                for (int i = 0; i < k; i++)
                {
                    int j = RandBelow(n - i);
                    result.Add(pool[j]);
                    pool[j] = pool[n - i - 1];
                }
            }
            else
            {
                var selected = new HashSet<int>();
                for (int i = 0; i < k; i++)
                {
                    int j = RandBelow(n);
                    while (selected.Contains(j)) j = RandBelow(n);
                    selected.Add(j);
                    result.Add(population[j]);
                }
            }
            return result;
        }
    }

    public class Quest
    {
        public string id, name, description, ev, reward;
        public int target, rewardScore, rewardCoins, progress;
        public bool claimed;
        public bool Done { get { return progress >= target; } }
    }

    public static class Quests
    {
        static List<Dictionary<string, object>> pool;
        static int dailyCount = 3;

        static void LoadPool()
        {
            if (pool != null) return;
            pool = new List<Dictionary<string, object>>();
            var ta = Resources.Load<TextAsset>("FortBox/data/quests");
            var d = ta != null ? Json.Obj(ta.text) : null;
            if (d == null) return;
            dailyCount = d.Int("daily", 3);
            var l = d.L("pool");
            if (l != null) foreach (var q in l) if (q is Dictionary<string, object>) pool.Add((Dictionary<string, object>)q);
        }

        public static string Today { get { return DateTime.UtcNow.ToString("yyyy-MM-dd"); } }

        public static uint SeedFor(string day)
        {
            using (var md5 = MD5.Create())
            {
                byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(day));
                // int(hexdigest, 16) & 0xFFFFFFFF = the last 4 bytes, big-endian
                return ((uint)h[12] << 24) | ((uint)h[13] << 16) | ((uint)h[14] << 8) | h[15];
            }
        }

        public static List<string> DailyIds(string day)
        {
            LoadPool();
            var ids = new List<string>();
            foreach (var q in pool) ids.Add(q.Str("id"));
            return new PyRandom(SeedFor(day)).Sample(ids, Math.Min(dailyCount, ids.Count));
        }

        static Dictionary<string, object> State()
        {
            var s = Json.Obj(PlayerPrefs.GetString("fb_quests_" + Session.Username, "")) ?? new Dictionary<string, object>();
            if (s.Str("day") != Today) s = new Dictionary<string, object> { { "day", Today } };
            return s;
        }

        static void Save(Dictionary<string, object> s)
        {
            PlayerPrefs.SetString("fb_quests_" + Session.Username, Json.Write(s));
            PlayerPrefs.Save();
        }

        public static List<Quest> Today3()
        {
            LoadPool();
            var s = State();
            var ids = DailyIds(Today);
            var list = new List<Quest>();
            foreach (var q in pool)
            {
                string id = q.Str("id");
                if (!ids.Contains(id)) continue;
                var e = s.O(id);
                list.Add(new Quest
                {
                    id = id, name = q.Str("name"), description = q.Str("description"), ev = q.Str("event"),
                    reward = q.Str("reward_description"), target = Math.Max(1, q.Int("target", 1)),
                    rewardScore = q.Int("reward_score"), rewardCoins = q.Int("reward_coins"),
                    progress = e != null ? e.Int("progress") : 0, claimed = e != null && e.Bool("claimed")
                });
            }
            return list;
        }

        /// Report a game event: enemy_killed, wave_reached (value = wave),
        /// session_ended, play_time (seconds)... Counting events add `amount`,
        /// "reached" events keep the best value - same as the PC quests.
        public static void Report(string ev, int amount = 1)
        {
            if (!Session.LoggedIn) return;
            var s = State();
            bool changed = false;
            foreach (var q in Today3())
            {
                if (q.ev != ev || q.claimed) continue;
                var e = s.O(q.id) ?? new Dictionary<string, object>();
                int prog = e.Int("progress");
                bool best = ev == "wave_reached" || ev == "level_reached" || ev == "score_milestone" || ev == "streak_reached";   // PC: max, others add
                int np = best ? Math.Max(prog, amount) : prog + amount;
                np = Math.Min(np, q.target);
                if (np != prog)
                {
                    e["progress"] = np;
                    s[q.id] = e;
                    changed = true;
                    if (np >= q.target) Toast.Show("Quest complete: " + q.name, Ui.Green);
                }
            }
            if (changed) Save(s);
        }

        /// Claim: score through player_update (like the PC sync), coins through
        /// the server's coin endpoint when the quest gives coins.
        public static async System.Threading.Tasks.Task<bool> Claim(Quest q)
        {
            if (!q.Done || q.claimed) return false;
            var s = State();
            var e = s.O(q.id) ?? new Dictionary<string, object>();
            e["claimed"] = true;
            e["progress"] = q.target;
            s[q.id] = e;
            Save(s);
            if (q.rewardScore > 0)
            {
                Session.Player["score"] = Session.Score + q.rewardScore;
                await Net.Rpc("player_update", new Dictionary<string, object> {
                    { "username", Session.Username }, { "token", Session.Token }, { "score", Session.Score }, { "level", Session.Level } });
            }
            if (q.rewardCoins > 0)
            {
                // same endpoint and reason the PC earn_coins() uses for quests
                var r = await Net.PostJson("/api/coins/earn", new Dictionary<string, object> {
                    { "username", Session.Username }, { "token", Session.Token }, { "amount", q.rewardCoins }, { "reason", "quest:" + q.id } });
                if (r != null && r.Str("status") == "ok") await Session.Refresh();
            }
            return true;
        }
    }
}
