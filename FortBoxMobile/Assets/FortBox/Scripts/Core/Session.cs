// Account session: same login / sign-up / remember-me flow as the PC game
// (player_verify, player_create, device tokens instead of saved passwords).
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FortBox
{
    public static class Session
    {
        public static Dictionary<string, object> Player = new Dictionary<string, object>();
        public static string Username = "";
        public static bool LoggedIn { get { return !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Token); } }
        public static string Token { get { return Player.Str("token"); } }
        public static int Coins { get { return Player.Int("coins"); } }
        public static int Score { get { return Player.Int("score"); } }
        public static int Level { get { int l = Player.Int("level", 1); return l < 1 ? 1 : l; } }
        public static string Rank { get { return Player.Str("rank", "player"); } }
        public static string Color { get { return Player.Str("color", "blue"); } }

        public const string DevicePrefix = "frdev1:";       // PC DEVICE_LOGIN_PREFIX

        public static string Hwid
        {
            get
            {
                var id = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier)
                {
                    id = PlayerPrefs.GetString("fb_hwid", "");
                    if (string.IsNullOrEmpty(id)) { id = System.Guid.NewGuid().ToString("N"); PlayerPrefs.SetString("fb_hwid", id); }
                }
                return "mobile-" + id;
            }
        }

        public static string DeviceLabel { get { return (Application.platform == RuntimePlatform.IPhonePlayer ? "iOS " : "Android ") + SystemInfo.deviceModel; } }

        public class Result
        {
            public bool ok;
            public string msg = "";
            public bool totp;
            public bool banned;
            public Dictionary<string, object> raw;
        }

        /// PC attempt_login(): player_verify with password, device token or TOTP.
        public static async Task<Result> Login(string username, string password, bool remember, string totp = null)
        {
            var r = new Result();
            username = (username ?? "").Trim();
            password = password ?? "";
            var p = new Dictionary<string, object> {
                { "username", username }, { "password", password }, { "hwid", Hwid },
                { "hwid_profile", new Dictionary<string, object> { { "platform", Application.platform.ToString() }, { "model", SystemInfo.deviceModel } } },
                { "device", DeviceLabel } };
            if (password.StartsWith(DevicePrefix))
            {
                p["device_token"] = password.Substring(DevicePrefix.Length);
                p["password"] = "";
            }
            else if (remember) p["remember_device"] = true;
            if (!string.IsNullOrEmpty(totp)) p["totp_code"] = totp;
            if (string.IsNullOrEmpty(username) || (string.IsNullOrEmpty(password)))
            {
                r.msg = "Username and password required";
                return r;
            }
            var resp = await Net.Rpc("player_verify", p, 8f);
            r.raw = resp;
            if (resp == null) { r.msg = "Cannot contact the FortBox server"; return r; }
            string st = resp.Str("status");
            if (st == "totp_required") { r.totp = true; r.msg = resp.Str("msg", "Enter your two-factor code"); return r; }
            if (st == "banned")
            {
                r.banned = true;
                r.msg = "Banned: " + resp.Str("ban_reason", "no reason given");
                return r;
            }
            if (st == "restricted") { r.msg = resp.Str("msg", "The game is limited to staff right now."); return r; }
            if (st != "ok") { r.msg = resp.Str("msg", "Invalid credentials"); return r; }
            Player = resp.O("player") ?? new Dictionary<string, object>();
            Username = Player.Str("username", username);
            if (string.IsNullOrEmpty(Player.Str("token")) && resp.ContainsKey("token")) Player["token"] = resp.Str("token");
            string dev = resp.Str("device_token");
            if (!string.IsNullOrEmpty(dev))
            {
                PlayerPrefs.SetString("fb_user", Username);
                PlayerPrefs.SetString("fb_device", dev);
                PlayerPrefs.Save();
            }
            else if (!remember && !password.StartsWith(DevicePrefix))
            {
                PlayerPrefs.DeleteKey("fb_device");
            }
            r.ok = true;
            Push.Reidentify();
            return r;
        }

        /// Auto login with the saved device token (PC attempt_auto_login).
        public static async Task<Result> AutoLogin()
        {
            string u = PlayerPrefs.GetString("fb_user", "");
            string d = PlayerPrefs.GetString("fb_device", "");
            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(d)) return new Result { msg = "" };
            var r = await Login(u, DevicePrefix + d, true);
            if (!r.ok && !r.totp) PlayerPrefs.DeleteKey("fb_device");
            return r;
        }

        /// PC create_player_db_entry(): player_create.
        public static async Task<Result> Register(string username, string password, string birthDate, string displayName, string email, string emailCode)
        {
            var r = new Result();
            var p = new Dictionary<string, object> {
                { "username", (username ?? "").Trim() }, { "password", password ?? "" }, { "hwid", Hwid },
                { "hwid_profile", new Dictionary<string, object> { { "platform", Application.platform.ToString() } } },
                { "birth_date", birthDate ?? "" }, { "display_name", displayName ?? "" },
                { "email", (email ?? "").Trim() }, { "email_code", (emailCode ?? "").Trim() } };
            var resp = await Net.Rpc("player_create", p, 10f);
            r.raw = resp;
            if (resp == null) { r.msg = "Cannot contact the FortBox server"; return r; }
            if (resp.Str("status") != "ok") { r.msg = resp.Str("msg", "Could not create the account"); return r; }
            r.ok = true;
            return r;
        }

        public static async Task<bool> SendEmailCode(string email, string username)
        {
            var resp = await Net.Rpc("email_send_code", new Dictionary<string, object> { { "email", email }, { "username", username } }, 15f);
            return resp != null && resp.Str("status") == "ok";
        }

        /// Fresh copy of our own player row (coins, grenades, owned weapons...).
        /// player_update with our unchanged colour answers with the private view
        /// (get_player only returns the public profile, without coins).
        public static async Task Refresh()
        {
            if (!LoggedIn) return;
            var resp = await Net.Rpc("player_update", new Dictionary<string, object> {
                { "username", Username }, { "token", Token }, { "color", Color } });
            var pl = resp != null && resp.Str("status") == "ok" ? resp.O("player") : null;
            if (pl == null)
            {
                resp = await Net.Rpc("get_player", new Dictionary<string, object> { { "username", Username } });
                pl = resp != null ? resp.O("player") : null;
            }
            if (pl == null) return;
            foreach (var kv in pl)
            {
                if (kv.Value == null) continue;                       // privacy-hidden in the public view
                if (kv.Key == "token" && string.IsNullOrEmpty(kv.Value as string)) continue;
                Player[kv.Key] = kv.Value;
            }
        }

        public static async Task<bool> AcceptTos()
        {
            var r = await Net.Rpc("accept_tos", new Dictionary<string, object> { { "username", Username }, { "token", Token } });
            bool ok = r != null && r.Str("status") == "ok";
            if (ok) Player["tos_required"] = false;
            return ok;
        }

        public static void Logout(bool forget)
        {
            if (forget)
            {
                string d = PlayerPrefs.GetString("fb_device", "");
                if (!string.IsNullOrEmpty(d))
                {
                    var _ = Net.Rpc("device_login_revoke", new Dictionary<string, object> { { "device_token", d } }, 4f);
                }
                PlayerPrefs.DeleteKey("fb_device");
            }
            Player = new Dictionary<string, object>();
            Username = "";
            Push.Reidentify();
        }

        public static List<string> OwnedWeapons()
        {
            var l = new List<string>();
            var ow = Player.L("owned_weapons");
            if (ow == null && Player.ContainsKey("owned_weapons") && Player["owned_weapons"] is string)
                ow = Json.Parse((string)Player["owned_weapons"]) as List<object>;
            if (ow != null) foreach (var o in ow) if (o != null && !l.Contains(o.ToString())) l.Add(o.ToString());
            if (!l.Contains("Pistol")) l.Insert(0, "Pistol");
            return l;
        }
    }
}
