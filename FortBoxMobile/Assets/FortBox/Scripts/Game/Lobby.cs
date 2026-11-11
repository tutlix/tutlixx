// Server list / lobby / start flow, the same RPCs the PC client uses.
// Singleplayer = a private one-player server whose match runs on the game
// server (server-authoritative enemies, damage, coins), exactly like the PC
// backend mode.
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FortBox
{
    public static class Lobby
    {
        public static int ServerId;
        public static string Map = "citadel";
        public static bool Solo;
        public static string Leader = "";
        public static bool IsLeader { get { return Leader == Session.Username; } }
        public static string LastError = "";

        public static readonly string[] Difficulties = { "easy", "normal", "hard", "nightmare" };
        public static readonly string[] DifficultyLabels = { "Easy", "Normal", "Hard", "Challenging" };

        static string Err(Dictionary<string, object> r, string def)
        {
            if (r == null) return "Server unreachable - check your connection.";
            string m = r.Str("msg", r.Str("error", def));
            switch (m)
            {
                case "server_full": return "That server is full.";
                case "invalid_password": return "Wrong server password.";
                case "permission_denied": return "Only the lobby leader can start the match.";
                case "id_required": return "Server not found.";
            }
            return m;
        }

        static bool Ok(Dictionary<string, object> r) { return r != null && r.Str("status") == "ok"; }

        /// Map catalog entries: id, label, desc (data/maps.json + pvp).
        public static List<Dictionary<string, object>> Maps()
        {
            var ta = Resources.Load<TextAsset>("FortBox/data/maps");
            var o = ta != null ? Json.Obj(ta.text) : null;
            var list = new List<Dictionary<string, object>>();
            if (o != null) foreach (var m in o.L("maps")) { var d = m as Dictionary<string, object>; if (d != null) list.Add(d); }
            return list;
        }

        public static string MapLabel(string id)
        {
            if (id == "pvp") return "PvP Arena";
            foreach (var m in Maps()) if (m.Str("id") == id) return m.Str("label", id);
            return id;
        }

        /// Map name from the server -> one of the maps shipped in the app.
        public static string LocalMap(string serverMap)
        {
            if (string.IsNullOrEmpty(serverMap)) return "citadel";
            if (Resources.Load<TextAsset>("FortBox/maps/" + serverMap + "_meta") != null) return serverMap;
            switch (serverMap)
            {
                case "grass": case "plains": return "green_plains";
                case "beach": return "sun_temple";
                case "snow": case "winter": return "frost_keep";
                case "desert": return "red_rock";
                case "industrial": case "foundry": return "mire_hollow";
            }
            return "citadel";
        }

        public static async Task<bool> Create(string name, int maxPlayers, string password, string map)
        {
            LastError = "";
            var r = await Net.Rpc("create_server", new Dictionary<string, object> {
                { "name", name.Length > 32 ? name.Substring(0, 32) : name },
                { "max_players", Mathf.Clamp(maxPlayers, 2, 4) },
                { "private", !string.IsNullOrEmpty(password) },
                { "password", password ?? "" },
                { "map", map },
                { "created_by", Session.Username } });
            if (!Ok(r)) { LastError = Err(r, "Could not create the server"); return false; }
            var d = r.O("data");
            int id = d != null ? d.Int("id") : r.Int("id");
            if (id <= 0) { LastError = "Server did not return an id"; return false; }
            return await Join(id, password, map);
        }

        public static async Task<bool> Join(int id, string password, string map = null)
        {
            LastError = "";
            var r = await Net.Rpc("join_server", new Dictionary<string, object> {
                { "id", id }, { "username", Session.Username }, { "password", password ?? "" } });
            if (!Ok(r)) { LastError = Err(r, "Could not join"); return false; }
            ServerId = id;
            if (map != null) Map = map;
            Push.TryIdentify();
            await FetchLobby();
            return true;
        }

        public static async Task<Dictionary<string, object>> FetchLobby()
        {
            if (ServerId <= 0) return null;
            var r = await Net.Rpc("get_lobby", new Dictionary<string, object> { { "id", ServerId } });
            if (!Ok(r)) return null;
            var d = r.O("data");
            if (d == null) return null;
            var srv = d.O("server");
            if (srv != null)
            {
                Map = LocalMap(srv.Str("map", Map));
                Leader = d.Str("leader", srv.Str("created_by", Leader));
            }
            return d;
        }

        public static async Task<List<Dictionary<string, object>>> Servers()
        {
            var r = await Net.Rpc("get_servers", new Dictionary<string, object> { { "limit", 50 } });
            var list = new List<Dictionary<string, object>>();
            if (!Ok(r)) { LastError = Err(r, "Could not load servers"); return list; }
            var d = r.O("data");
            var arr = d != null ? d.L("servers") : r.L("servers");
            foreach (var o in arr) { var s = o as Dictionary<string, object>; if (s != null) list.Add(s); }
            return list;
        }

        public static async Task<bool> SetReady(bool ready)
        {
            var r = await Net.Rpc("set_ready", new Dictionary<string, object> {
                { "id", ServerId }, { "username", Session.Username }, { "ready", ready } });
            return Ok(r);
        }

        public static async Task Leave()
        {
            int id = ServerId;
            ServerId = 0;
            if (id <= 0) return;
            await Net.Rpc("leave_lobby", new Dictionary<string, object> { { "id", id }, { "username", Session.Username } }, 3f);
        }

        /// Leader: start the match. The obstacles are the map's plain scenery
        /// hitboxes exactly as the PC leader computes them.
        public static async Task<bool> StartMatch(string difficulty)
        {
            LastError = "";
            var pack = MapPack.Load(Map);
            var payload = new Dictionary<string, object> {
                { "id", ServerId }, { "requestor", Session.Username },
                { "difficulty", difficulty ?? "normal" } };
            if (pack != null) payload["obstacles"] = pack.ObstacleList();
            var r = await Net.Rpc("start_match", payload, 8f);
            if (!Ok(r)) { LastError = Err(r, "Failed to start the match"); return false; }
            var d = r.O("data");
            var info = d != null ? (d.O("match") ?? d) : null;
            if (info != null && MatchController.Active == null) OnMatchStart(info);
            return true;
        }

        /// Singleplayer: private one-seat server on the chosen map, start at once.
        public static async Task<bool> PlaySolo(string map, string difficulty)
        {
            Solo = true;
            if (ServerId > 0) await Leave();
            string pw = System.Guid.NewGuid().ToString("N").Substring(0, 10);
            if (!await Create(Session.Username + "'s game", 2, pw, map)) return false;
            Leader = Session.Username;
            return await StartMatch(difficulty);
        }

        public static void OnMatchStart(Dictionary<string, object> info)
        {
            if (MatchController.Active != null) return;
            int sid = info.Int("server_id", ServerId);
            if (sid > 0) ServerId = sid;
            MatchController.Begin(info);
        }
    }
}
