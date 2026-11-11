// Networking: the same WebSocket protocol as the PC game.
//   RPC   : open a socket, send {id, type, payload}, wait for the reply with that id.
//   Push  : one long-lived socket, 'identify'-ed with username + token, that
//           receives lobby_update / match_start / match_state / match_over /
//           kicked / banned / force_logout ... and carries match_input.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FortBox
{
    public static class Config
    {
        // Same backend as the PC client (BACKEND_HOST / BACKEND_SCHEME). If the
        // server offers HTTPS, set Scheme = "https" and the app uses wss://.
        public static string Host = "77.90.42.18:1040";
        public static string Scheme = "http";
        // Must equal the server's GAME_VERSION exactly or 'identify' is refused
        // (no presence socket = no match_state pushes). If the server moves on,
        // the app adopts the version from its 'update_required' reply.
        public static string GameVersion = "v1.0.0.0";

        public static string HttpUrl { get { return Scheme + "://" + Host; } }
        public static string WsUrl { get { return (Scheme == "https" ? "wss" : "ws") + "://" + Host + "/ws"; } }

        public static void Load()
        {
            // a server override can be saved in Settings (testing a staging server)
            var h = PlayerPrefs.GetString("fb_host", "");
            var s = PlayerPrefs.GetString("fb_scheme", "");
            if (!string.IsNullOrEmpty(h)) Host = h;
            if (s == "http" || s == "https") Scheme = s;
            var v = PlayerPrefs.GetString("fb_server_version", "");
            if (!string.IsNullOrEmpty(v)) GameVersion = v;
        }
    }

    /// Runs callbacks from network threads on Unity's main thread.
    public static class MainThread
    {
        static readonly ConcurrentQueue<Action> q = new ConcurrentQueue<Action>();
        public static void Post(Action a) { if (a != null) q.Enqueue(a); }

        public static void Pump()
        {
            Action a;
            int n = 0;
            while (n++ < 400 && q.TryDequeue(out a))
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    public static class Net
    {
        static async Task<string> ReceiveText(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new ArraySegment<byte>(new byte[64 * 1024]);
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    var r = await ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == WebSocketMessageType.Close) return null;
                    ms.Write(buf.Array, 0, r.Count);
                    if (r.EndOfMessage) break;
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        static Task SendText(ClientWebSocket ws, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        /// One request, like the PC ws_request(): returns the reply object (or
        /// null on timeout / connection failure). Adds the session's credentials.
        public static async Task<Dictionary<string, object>> Rpc(string type, Dictionary<string, object> payload = null, float timeout = 6f)
        {
            payload = payload ?? new Dictionary<string, object>();
            if (Session.LoggedIn && type != "player_verify" && type != "player_create")
            {
                if (!payload.ContainsKey("_auth_user")) payload["_auth_user"] = Session.Username;
                if (!payload.ContainsKey("_auth_token")) payload["_auth_token"] = Session.Token;
            }
            string id = Guid.NewGuid().ToString();
            string msg = Json.Write(new Dictionary<string, object> { { "id", id }, { "type", type }, { "payload", payload } });
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout)))
            using (var ws = new ClientWebSocket())
            {
                try
                {
                    await ws.ConnectAsync(new Uri(Config.WsUrl), cts.Token);
                    await SendText(ws, msg, cts.Token);
                    while (!cts.IsCancellationRequested)
                    {
                        string raw = await ReceiveText(ws, cts.Token);
                        if (raw == null) break;
                        var data = Json.Obj(raw);
                        if (data == null) continue;
                        var resp = data.O("response");
                        string rid = data.Str("id", resp != null ? resp.Str("id") : "");
                        if (rid == id) return data;
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("[net] " + type + " failed: " + e.Message);
                }
                finally
                {
                    try
                    {
                        if (ws.State == WebSocketState.Open)
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    catch (Exception) { }
                }
            }
            return null;
        }

        /// POST a JSON body over HTTP; returns the JSON reply (or null).
        public static async Task<Dictionary<string, object>> PostJson(string path, Dictionary<string, object> body, float timeout = 6f)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(timeout);
                    var content = new System.Net.Http.StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                    var resp = await client.PostAsync(Config.HttpUrl.TrimEnd('/') + path, content);
                    return Json.Obj(await resp.Content.ReadAsStringAsync());
                }
            }
            catch (Exception e)
            {
                Debug.Log("[net] POST " + path + " failed: " + e.Message);
                return null;
            }
        }

        /// GET over HTTP (shop catalogue etc.).
        public static async Task<string> Get(string path, float timeout = 6f)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(timeout);
                    return await client.GetStringAsync(Config.HttpUrl.TrimEnd('/') + path);
                }
            }
            catch (Exception e)
            {
                Debug.Log("[net] GET " + path + " failed: " + e.Message);
                return null;
            }
        }
    }

    /// The long-lived, identified socket (PC: persistent_ws).
    public static class Push
    {
        public static event Action<string, Dictionary<string, object>> OnMessage;   // type, whole message (main thread)
        public static bool Connected { get; private set; }
        public static bool Identified { get; private set; }

        static ClientWebSocket ws;
        static CancellationTokenSource life;
        static readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        static string identifiedAs;

        public static void Start()
        {
            if (life != null) return;
            life = new CancellationTokenSource();
            var ct = life.Token;
            Task.Run(() => Loop(ct));
        }

        public static void Stop()
        {
            if (life != null) life.Cancel();
            life = null;
            Connected = Identified = false;
            identifiedAs = null;
        }

        /// Call after login / logout so the socket re-identifies.
        public static void Reidentify() { identifiedAs = null; Identified = false; TryIdentify(); }

        static async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    using (var c = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                        await ws.ConnectAsync(new Uri(Config.WsUrl), c.Token);
                    Connected = true;
                    identifiedAs = null;
                    TryIdentify();
                    var buf = new ArraySegment<byte>(new byte[256 * 1024]);
                    var ms = new MemoryStream();
                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        var r = await ws.ReceiveAsync(buf, ct);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buf.Array, 0, r.Count);
                        if (!r.EndOfMessage) continue;
                        string raw = Encoding.UTF8.GetString(ms.ToArray());
                        ms.SetLength(0);
                        var msg = Json.Obj(raw);
                        if (msg == null) continue;
                        if (identifyId != null && (msg.Str("id") == identifyId || (msg.O("response") != null && msg.O("response").Str("id") == identifyId)))
                        {
                            HandleIdentifyReply(msg);
                            continue;
                        }
                        string type = msg.Str("type");
                        if (string.IsNullOrEmpty(type)) continue;
                        MainThread.Post(() => { if (OnMessage != null) OnMessage(type, msg); });
                    }
                }
                catch (Exception e)
                {
                    if (!ct.IsCancellationRequested) Debug.Log("[push] " + e.Message);
                }
                Connected = false;
                Identified = false;
                try { if (ws != null) ws.Dispose(); } catch (Exception) { }
                ws = null;
                if (!ct.IsCancellationRequested) await Task.Delay(1500);
            }
        }

        static string identifyId;
        static int versionRetries;
        public static string IdentifyError = "";

        public static void TryIdentify()
        {
            if (!Connected || !Session.LoggedIn) return;
            if (identifiedAs == Session.Username) return;
            identifiedAs = Session.Username;
            identifyId = Guid.NewGuid().ToString();
            SendWithId(identifyId, "identify", new Dictionary<string, object> {
                { "username", Session.Username }, { "token", Session.Token }, { "client_version", Config.GameVersion } });
            Identified = true;
        }

        static void HandleIdentifyReply(Dictionary<string, object> msg)
        {
            identifyId = null;
            if (msg.Str("status") == "ok") { IdentifyError = ""; versionRetries = 0; return; }
            Identified = false;
            string code = msg.Str("code"), sv = msg.Str("server_version");
            if (code == "update_required" && !string.IsNullOrEmpty(sv) && sv != Config.GameVersion && versionRetries < 2)
            {
                // the protocol is the same; follow the server's version string
                versionRetries++;
                Config.GameVersion = sv;
                MainThread.Post(() => { PlayerPrefs.SetString("fb_server_version", sv); PlayerPrefs.Save(); });
                identifiedAs = null;
                TryIdentify();
                return;
            }
            IdentifyError = msg.Str("msg", "identify_failed");
            Debug.Log("[push] identify refused: " + IdentifyError);
        }

        public static void Send(string type, Dictionary<string, object> payload)
        {
            SendWithId(Guid.NewGuid().ToString(), type, payload);
        }

        static void SendWithId(string id, string type, Dictionary<string, object> payload)
        {
            var sock = ws;
            if (sock == null || sock.State != WebSocketState.Open) return;
            string text = Json.Write(new Dictionary<string, object> {
                { "id", id }, { "type", type }, { "payload", payload } });
            Task.Run(async () =>
            {
                await sendLock.WaitAsync();
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    await sock.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception) { }
                finally { sendLock.Release(); }
            });
        }
    }
}
