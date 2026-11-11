// Friends (list, online players, requests, search / add / remove) and group
// chat (your groups, browse / join by ID or invite link / create, channels,
// live messages, typing indicator, subscription, leave) - the same server
// calls and pushes as the PC client.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FortBox
{
    /// Unread counters shown on the main menu.
    public static class Social
    {
        public static int FriendRequests;
        public static readonly Dictionary<int, int> Unread = new Dictionary<int, int>();
        public static int UnreadTotal { get { int n = 0; foreach (var v in Unread.Values) n += v; return n; } }
        public const string InviteScheme = "fortroyal2d://join-group/";

        public static Dictionary<string, object> Auth(Dictionary<string, object> extra = null)
        {
            var d = new Dictionary<string, object> { { "username", Session.Username }, { "token", Session.Token } };
            if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
            return d;
        }

        public static bool Ok(Dictionary<string, object> r) { return r != null && r.Str("status") == "ok"; }
        public static string Err(Dictionary<string, object> r, string def) { return r == null ? "Server unreachable" : r.Str("msg", def).Replace('_', ' '); }

        public static string Hex(Color c) { return ColorUtility.ToHtmlStringRGB(c); }

        /// Stops user text from being read as rich-text tags.
        public static string Safe(string s) { return (s ?? "").Replace("<", "<\u200B"); }

        public static async void RefreshRequests()
        {
            if (!Session.LoggedIn) return;
            var r = await Net.Rpc("get_friend_requests", Auth());
            if (Ok(r)) FriendRequests = r.L("incoming").Count;
        }

        /// Push handler for friends / group chat events.
        public static void OnPush(string type, Dictionary<string, object> data)
        {
            switch (type)
            {
                case "friend_request_received":
                    FriendRequests++;
                    Toast.Show(data.Str("from") + " sent you a friend request", Ui.Orange, 3.5f);
                    Sfx.Ping();
                    break;
                case "friendship_updated":
                    if (data.Str("relationship") == "friends") Toast.Show("You and " + data.Str("friend") + " are now friends", Ui.Green, 3f);
                    RefreshRequests();
                    break;
                case "friend_request_updated":
                    break;
                case "group_message":
                    {
                        var gs = App.I.Current as GroupsScreen;
                        int gid = data.Int("group_id");
                        bool showing = gs != null && gs.Showing(gid, data.Int("channel_id"));
                        if (gs != null) gs.Incoming(data);
                        if (!showing && data.Str("username") != Session.Username)
                        {
                            int c;
                            Unread.TryGetValue(gid, out c);
                            Unread[gid] = c + 1;
                            if (MatchController.Active == null)
                            {
                                Toast.Show(data.Str("username") + ": " + Clip(data.Str("message"), 60), Ui.Text2, 3f);
                                Sfx.Ping();
                            }
                        }
                        break;
                    }
                case "group_typing":
                    {
                        var gs = App.I.Current as GroupsScreen;
                        if (gs != null) gs.Typing(data);
                        break;
                    }
                case "group_mention":
                    Toast.Show(data.Str("from", data.Str("username")) + " mentioned you in " + data.Str("group_name", "a group"), Ui.Orange, 4f);
                    Sfx.Ping();
                    break;
            }
            var menu = App.I.Current as MenuScreen;
            if (menu != null) menu.RefreshBadges();
        }

        public static string Clip(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) + "..." : s; }
    }

    // ---------------------------------------------------------------------------
    public class FriendsScreen : SubScreen
    {
        enum Tab { Friends, Online, Requests, Find }
        Tab tab = Tab.Friends;
        RectTransform list;
        Text empty;
        InputField search;
        RectTransform searchRow;
        readonly Dictionary<Tab, Ui.Btn> tabs = new Dictionary<Tab, Ui.Btn>();
        int loadSeq;

        public override void Build()
        {
            Frame("Friends", "users");
            var bar = Ui.Node(body, "tabs").Place(0, 1, 1, 1, 0, -60, 0, 0);
            bar.HList(10, 0, true);
            AddTab(bar, Tab.Friends, "FRIENDS", "users");
            AddTab(bar, Tab.Online, "ONLINE NOW", "wifi");
            AddTab(bar, Tab.Requests, "REQUESTS", "bell");
            AddTab(bar, Tab.Find, "FIND PLAYERS", "plus");
            searchRow = Ui.Node(body, "search").Place(0, 1, 1, 1, 0, -136, 0, 72);
            searchRow.HList(10, 0, true);
            search = Ui.Input(searchRow, "Search by username");
            search.GetComponent<RectTransform>().Size(-1, -1, 1);
            search.onEndEdit.AddListener(v => Load());
            Ui.Button(searchRow, "SEARCH", Ui.Style.Primary, "chevron_right", Load, 17).GetComponent<RectTransform>().Size(180, -1);
            ScrollRect sr;
            list = Ui.Scroll(body, out sr, 8);
            sr.GetComponent<RectTransform>().Place(0, 0, 1, 1, 0, 0, 0, 76);
            sr.name = "listscroll";
            empty = Ui.Label(body, "", 22, Ui.Text3, TextAnchor.MiddleCenter, Ui.Semi);
            empty.rectTransform.Place(0, 0, 1, 1, 0, 0, 0, 76);
        }

        void AddTab(Transform bar, Tab t, string label, string icon)
        {
            var b = Ui.Button(bar, label, Ui.Style.Secondary, icon, () => { tab = t; Load(); }, 16);
            b.GetComponent<RectTransform>().Size(220, -1);
            tabs[t] = b;
        }

        public override void OnShow() { base.OnShow(); Load(); }

        public void Refresh() { if (gameObject.activeInHierarchy) Load(); }

        async void Load()
        {
            int seq = ++loadSeq;
            foreach (var kv in tabs)
            {
                kv.Value.style = kv.Key == tab ? Ui.Style.Primary : Ui.Style.Secondary;
                kv.Value.Refresh();
            }
            tabs[Tab.Requests].SetLabel(Social.FriendRequests > 0 ? "REQUESTS (" + Social.FriendRequests + ")" : "REQUESTS");
            bool find = tab == Tab.Find;
            searchRow.gameObject.SetActive(find);
            var srt = list.parent.parent.GetComponent<RectTransform>();
            srt.Place(0, 0, 1, 1, 0, 0, 0, find ? 150 : 76);
            Ui.Clear(list);
            empty.text = "LOADING...";
            Dictionary<string, object> r;
            switch (tab)
            {
                case Tab.Friends: r = await Net.Rpc("get_friends", Social.Auth()); break;
                case Tab.Online: r = await Net.Rpc("get_online_players", Social.Auth()); break;
                case Tab.Requests: r = await Net.Rpc("get_friend_requests", Social.Auth()); break;
                default:
                    if (search.text.Trim().Length == 0) { empty.text = "Type a username to search"; return; }
                    r = await Net.Rpc("search_players", Social.Auth(new Dictionary<string, object> { { "query", search.text.Trim() }, { "limit", 25 } }));
                    break;
            }
            if (seq != loadSeq || this == null) return;
            if (!Social.Ok(r)) { empty.text = Social.Err(r, "Could not load").ToUpperInvariant(); return; }
            empty.text = "";
            int count = 0;
            if (tab == Tab.Requests)
            {
                var inc = r.L("incoming");
                Social.FriendRequests = inc.Count;
                tabs[Tab.Requests].SetLabel(inc.Count > 0 ? "REQUESTS (" + inc.Count + ")" : "REQUESTS");
                if (inc.Count > 0) Ui.Section(list, "Incoming");
                foreach (var o in inc) { var q = o as Dictionary<string, object>; if (q != null) { Row(q.O("from"), "in", q.Int("id")); count++; } }
                var outg = r.L("outgoing");
                if (outg.Count > 0) Ui.Section(list, "Sent");
                foreach (var o in outg) { var q = o as Dictionary<string, object>; if (q != null) { Row(q.O("to"), "out", q.Int("id")); count++; } }
            }
            else
            {
                var arr = tab == Tab.Friends ? r.L("friends") : r.L("players");
                if (tab == Tab.Friends)
                    arr.Sort((a, b) => ((b as Dictionary<string, object>).Bool("online") ? 1 : 0).CompareTo((a as Dictionary<string, object>).Bool("online") ? 1 : 0));
                foreach (var o in arr) { var p = o as Dictionary<string, object>; if (p != null) { Row(p, p.Str("relationship", tab == Tab.Friends ? "friends" : "none"), 0); count++; } }
            }
            if (count == 0)
                empty.text = tab == Tab.Friends ? "No friends yet - find players and add them" :
                             tab == Tab.Online ? "Nobody else is online right now" :
                             tab == Tab.Requests ? "No friend requests" : "No players found";
        }

        void Row(Dictionary<string, object> p, string rel, int requestId)
        {
            if (p == null) return;
            string u = p.Str("username");
            var row = Ui.Panel(list, 0.92f, p.Bool("online") ? Ui.Green : (Color?)null);
            row.Size(-1, 76);
            var h = Ui.Node(row, "h").Fill(12);
            h.HList(14, 0, true);
            var dot = Ui.Img(h, p.Bool("online") ? Ui.Green : Ui.Text3, Ui.Circle(), "dot");
            dot.rectTransform.Size(14, 14);
            var sw = Ui.Img(h, Ui.PlayerColor(p.Str("color", "blue")), Ui.Round(), "colour");
            sw.rectTransform.Size(10, 44);
            var info = Ui.Node(h, "info");
            info.Size(-1, -1, 1);
            string dn = p.Str("display_name");
            Ui.Label(info, u + (string.IsNullOrEmpty(dn) || dn == u ? "" : "  <color=#" + Social.Hex(Ui.Text3) + ">" + Social.Safe(dn) + "</color>"), 22, Ui.Text, TextAnchor.UpperLeft, Ui.Bold).rectTransform.Place(0, 0.5f, 1, 1);
            string meta = "LV " + p.Int("level", 1) + "  ·  " + p.Str("rank", "player").ToUpperInvariant() + "  ·  " + (p.Bool("online") ? "ONLINE" : LastSeen(p.Str("last_online")));
            Ui.Label(info, meta, 14, Ui.Text3, TextAnchor.LowerLeft, Ui.Bold).rectTransform.Place(0, 0, 1, 0.5f);
            switch (rel)
            {
                case "friends":
                    Btn(h, "REMOVE", Ui.Style.Ghost, "close", () => Prompt.Confirm("Remove friend", "Remove " + u + " from your friends?", "REMOVE", () => Act("remove_friend", new Dictionary<string, object> { { "target", u } }, "Removed " + u)));
                    break;
                case "in":
                    Btn(h, "DECLINE", Ui.Style.Secondary, "close", () => Act("decline_friend_request", new Dictionary<string, object> { { "request_id", requestId } }, "Declined"));
                    Btn(h, "ACCEPT", Ui.Style.Success, "check", () => Act("accept_friend_request", new Dictionary<string, object> { { "request_id", requestId } }, "You and " + u + " are now friends"));
                    break;
                case "out":
                case "request_sent":
                    Btn(h, "CANCEL", Ui.Style.Secondary, "close", () => Act("cancel_friend_request", new Dictionary<string, object> { { "target", u }, { "request_id", requestId } }, "Request cancelled"));
                    break;
                case "request_received":
                    Ui.Label(h, "SENT YOU A REQUEST", 14, Ui.Orange, TextAnchor.MiddleRight, Ui.Bold).rectTransform.Size(200, -1);
                    break;
                default:
                    Btn(h, "ADD FRIEND", Ui.Style.Primary, "plus", () => Act("send_friend_request", new Dictionary<string, object> { { "target", u } }, "Friend request sent to " + u));
                    break;
            }
        }

        static string LastSeen(string iso)
        {
            DateTime t;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out t)) return "OFFLINE";
            var d = DateTime.UtcNow - t;
            if (d.TotalMinutes < 60) return "SEEN " + Math.Max(1, (int)d.TotalMinutes) + "M AGO";
            if (d.TotalHours < 48) return "SEEN " + (int)d.TotalHours + "H AGO";
            return "SEEN " + (int)d.TotalDays + "D AGO";
        }

        static void Btn(Transform h, string label, Ui.Style st, string icon, UnityEngine.Events.UnityAction a)
        {
            Ui.Button(h, label, st, icon, a, 15).GetComponent<RectTransform>().Size(label.Length > 7 ? 190 : 150, -1);
        }

        async void Act(string rpc, Dictionary<string, object> extra, string okMsg)
        {
            var r = await Net.Rpc(rpc, Social.Auth(extra));
            if (Social.Ok(r)) Toast.Show(okMsg, Ui.Green);
            else Toast.Show(Social.Err(r, "Failed"), Ui.Red, 3.5f);
            if (this != null && gameObject.activeInHierarchy) Load();
        }
    }

    // ---------------------------------------------------------------------------
    public class GroupsScreen : SubScreen
    {
        RectTransform groupList, msgList, channelBar, chatPanel, subBox, browseBox, browseList, headBtns, inputRow;
        ScrollRect msgScroll;
        Text title, subtitle, typingText, emptyGroups, emptyChat;
        InputField input;
        Ui.Btn sendBtn, leaveBtn;
        int groupId, channelId;
        string myRole = "member";
        bool canSend = true;
        readonly HashSet<int> shown = new HashSet<int>();
        int lastId;
        float nextPoll, lastTypingSent, typingUntil;
        readonly List<Dictionary<string, object>> myGroups = new List<Dictionary<string, object>>();

        public override void Build()
        {
            Frame("Group chat", "chat");
            // left: groups
            var left = Ui.Panel(body, 0.92f, Ui.Orange);
            left.Place(0, 0, 0.3f, 1, 0, 0, 8, 0);
            Ui.Section(left, "Your groups").Place(0, 1, 1, 1, 14, -40, 14, 14);
            ScrollRect gs;
            groupList = Ui.Scroll(left, out gs, 6);
            gs.GetComponent<RectTransform>().Place(0, 0, 1, 1, 10, 150, 10, 48);
            emptyGroups = Ui.Label(left, "", 17, Ui.Text3, TextAnchor.MiddleCenter, Ui.Semi);
            emptyGroups.rectTransform.Place(0, 0, 1, 1, 14, 150, 14, 48);
            var acts = Ui.Node(left, "acts").Place(0, 0, 1, 0, 10, 10, 10, -136);
            acts.VList(8, 0);
            var row1 = Ui.Node(acts, "r1"); row1.Size(-1, 60); row1.HList(8, 0, true);
            Ui.Button(row1, "BROWSE", Ui.Style.Secondary, "map", OpenBrowse, 15).GetComponent<RectTransform>().Size(-1, -1, 1);
            Ui.Button(row1, "JOIN", Ui.Style.Secondary, "plus", () => Prompt.Ask("Join a group", "Group ID or invite link", false, JoinByText), 15).GetComponent<RectTransform>().Size(-1, -1, 1);
            Ui.Button(acts, "CREATE GROUP", Ui.Style.Primary, "plus", () => Prompt.Ask("Create a group", "Group name", false, Create), 16).GetComponent<RectTransform>().Size(-1, 60);

            // right: chat
            chatPanel = Ui.Panel(body, 0.92f, null);
            chatPanel.Place(0.3f, 0, 1, 1, 8, 0, 0, 0);
            title = Ui.Label(chatPanel, "", 28, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            title.rectTransform.Place(0, 1, 1, 1, 20, -54, 320, 12);
            subtitle = Ui.Label(chatPanel, "", 14, Ui.Text3, TextAnchor.MiddleLeft, Ui.Bold);
            subtitle.rectTransform.Place(0, 1, 1, 1, 22, -78, 320, 54);
            var hb = Ui.Node(chatPanel, "hb").Box(1, 1, -16, -14, 300, 56);
            headBtns = hb;
            hb.HList(8, 0, true).childAlignment = TextAnchor.MiddleRight;
            Ui.Button(hb, "INVITE", Ui.Style.Secondary, "users", CopyInvite, 14).GetComponent<RectTransform>().Size(140, -1);
            leaveBtn = Ui.Button(hb, "LEAVE", Ui.Style.Danger, "logout", Leave, 14);
            leaveBtn.GetComponent<RectTransform>().Size(140, -1);
            channelBar = Ui.Node(chatPanel, "channels").Place(0, 1, 1, 1, 16, -140, 16, 88);
            channelBar.HList(8, 0, true);
            msgList = Ui.Scroll(chatPanel, out msgScroll, 6);
            msgScroll.GetComponent<RectTransform>().Place(0, 0, 1, 1, 16, 104, 16, 150);
            emptyChat = Ui.Label(chatPanel, "", 20, Ui.Text3, TextAnchor.MiddleCenter, Ui.Semi);
            emptyChat.rectTransform.Place(0, 0, 1, 1, 16, 104, 16, 150);
            typingText = Ui.Label(chatPanel, "", 14, Ui.Text3, TextAnchor.MiddleLeft, Ui.Semi);
            typingText.rectTransform.Place(0, 0, 1, 0, 20, 78, 20, -100);
            var inRow = Ui.Node(chatPanel, "input").Place(0, 0, 1, 0, 16, 14, 16, -74);
            inputRow = inRow;
            inRow.HList(10, 0, true);
            input = Ui.Input(inRow, "Message");
            input.characterLimit = 256;
            input.GetComponent<RectTransform>().Size(-1, -1, 1);
            input.onValueChanged.AddListener(v => SendTyping());
            input.onEndEdit.AddListener(v => { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Send(); });
            sendBtn = Ui.Button(inRow, "SEND", Ui.Style.Primary, "chevron_right", Send, 17);
            sendBtn.GetComponent<RectTransform>().Size(150, -1);

            // subscription box (owner, inactive group)
            subBox = Ui.Panel(chatPanel, 0.98f, Ui.Amber);
            subBox.Place(0, 0, 1, 0, 16, 104, 16, -250);
            var sc = Ui.Node(subBox, "c").Fill(16); sc.VList(10, 0);
            Ui.Label(sc, "This group is inactive. Group chats need an active subscription from the owner to send messages.", 17, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular).rectTransform.Size(-1, 50);
            var sr = Ui.Node(sc, "r"); sr.Size(-1, 56); sr.HList(10, 0, true);
            Ui.Button(sr, "40 COINS / MONTH", Ui.Style.Primary, "coin", () => BuySub("monthly"), 15).GetComponent<RectTransform>().Size(-1, -1, 1);
            Ui.Button(sr, "900 COINS / YEAR", Ui.Style.Secondary, "coin", () => BuySub("yearly"), 15).GetComponent<RectTransform>().Size(-1, -1, 1);
            subBox.gameObject.SetActive(false);

            // browse all groups
            browseBox = Ui.Node(root, "browse").Fill();
            var dim = Ui.Img(browseBox, new Color(0.03f, 0.035f, 0.045f, 0.85f), null, "dim");
            dim.rectTransform.Fill(); dim.raycastTarget = true;
            var bp = Ui.Panel(browseBox, 0.98f, Ui.Orange);
            bp.Box(0.5f, 0.5f, 0, 0, 900, 720);
            bp.pivot = new Vector2(0.5f, 0.5f);
            Ui.Section(bp, "All groups", Ui.Orange).Place(0, 1, 1, 1, 24, -48, 24, 24);
            ScrollRect bs;
            browseList = Ui.Scroll(bp, out bs, 8);
            bs.GetComponent<RectTransform>().Place(0, 0, 1, 1, 24, 96, 24, 64);
            Ui.Button(bp, "CLOSE", Ui.Style.Secondary, "close", () => browseBox.gameObject.SetActive(false), 16).GetComponent<RectTransform>().Place(0, 0, 1, 0, 24, 20, 24, -78);
            browseBox.gameObject.SetActive(false);
            ShowChat(false);
        }

        void ShowChat(bool on)
        {
            foreach (var t in new Component[] { subtitle, headBtns, channelBar, msgScroll, typingText, inputRow })
                t.gameObject.SetActive(on);
            subBox.gameObject.SetActive(false);
            if (!on)
            {
                title.text = "NO GROUP SELECTED";
                emptyChat.text = "Pick a group, or browse and join one";
                Ui.Clear(msgList);
                shown.Clear();
            }
        }

        public override void OnShow()
        {
            base.OnShow();
            LoadGroups();
        }

        public bool Showing(int gid, int cid) { return gameObject.activeInHierarchy && gid == groupId && (cid == 0 || cid == channelId); }

        async void LoadGroups()
        {
            emptyGroups.text = "LOADING...";
            var r = await Net.Rpc("group_list_mine", Social.Auth());
            if (this == null) return;
            Ui.Clear(groupList);
            myGroups.Clear();
            if (!Social.Ok(r)) { emptyGroups.text = Social.Err(r, "Could not load groups"); return; }
            var d = r.O("data") ?? new Dictionary<string, object>();
            foreach (var o in d.L("groups")) { var g = o as Dictionary<string, object>; if (g != null) myGroups.Add(g); }
            emptyGroups.text = myGroups.Count == 0 ? "You're not in any group yet" : "";
            foreach (var g in myGroups)
            {
                int gid = g.Int("id");
                var b = Ui.Button(groupList, "", gid == groupId ? Ui.Style.Primary : Ui.Style.Secondary, null, () => Open(gid, 0), 16);
                b.GetComponent<RectTransform>().Size(-1, 70);
                int unread;
                Social.Unread.TryGetValue(gid, out unread);
                var t = Ui.Label(b.transform, Social.Safe(g.Str("name")) + (unread > 0 ? "  <color=#" + Social.Hex(Ui.Orange) + ">(" + unread + ")</color>" : ""), 19, gid == groupId ? Ui.C(20, 10, 4) : Ui.Text, TextAnchor.UpperLeft, Ui.Bold);
                t.rectTransform.Place(0, 0, 1, 1, 14, 30, 10, 8);
                string meta = g.Int("member_count") + " MEMBERS  ·  " + g.Str("my_role", "member").ToUpperInvariant() + (g.Bool("subscription_active") ? "" : "  ·  INACTIVE") + (g.Bool("banned") ? "  ·  BLACKLISTED" : "");
                Ui.Label(b.transform, meta, 12, gid == groupId ? Ui.C(60, 24, 6) : Ui.Text3, TextAnchor.LowerLeft, Ui.Bold).rectTransform.Place(0, 0, 1, 1, 14, 8, 10, 36);
            }
            if (groupId == 0 && myGroups.Count > 0) Open(myGroups[0].Int("id"), 0);
            else if (groupId != 0 && !myGroups.Exists(g => g.Int("id") == groupId)) { groupId = 0; ShowChat(false); }
        }

        async void Open(int gid, int cid)
        {
            bool newGroup = gid != groupId;
            groupId = gid;
            Social.Unread.Remove(gid);
            if (newGroup) LoadGroups();
            var r = await Net.Rpc("group_get_info", Social.Auth(new Dictionary<string, object> { { "group_id", gid } }));
            if (this == null || groupId != gid) return;
            if (!Social.Ok(r)) { Toast.Show(Social.Err(r, "Could not open group"), Ui.Red); return; }
            ShowChat(true);
            var d = r.O("data");
            var g = d.O("group") ?? new Dictionary<string, object>();
            myRole = d.Str("my_role", "member");
            title.text = Social.Safe(g.Str("name")).ToUpperInvariant();
            subtitle.text = (g.Int("member_count") + " MEMBERS  ·  OWNER " + g.Str("owner_username") + "  ·  ID " + gid + (g.Bool("subscription_active") ? "" : "  ·  INACTIVE")).ToUpperInvariant();
            leaveBtn.gameObject.SetActive(myRole != "owner");
            bool inactive = !g.Bool("subscription_active");
            subBox.gameObject.SetActive(inactive && myRole == "owner");

            Ui.Clear(channelBar);
            int first = 0;
            foreach (var o in d.L("channels"))
            {
                var c = o as Dictionary<string, object>;
                if (c == null || c.Str("type", "text") != "text") continue;
                int id = c.Int("id");
                if (first == 0) first = id;
                if (cid == 0 && first == id) cid = id;
                if (id == cid) canSend = c.Bool("can_send", true);
                var b = Ui.Button(channelBar, "#" + c.Str("name"), id == cid ? Ui.Style.Primary : Ui.Style.Secondary, null, () => Open(gid, id), 15);
                b.GetComponent<RectTransform>().Size(Mathf.Clamp(c.Str("name").Length * 11 + 50, 110, 260), -1);
            }
            channelId = cid;
            input.interactable = canSend && !inactive;
            sendBtn.SetDisabled(!canSend || inactive);
            ((Text)input.placeholder).text = inactive ? "Group inactive" : (canSend ? "Message #" + ChannelName(d, cid) : "You can't send messages in this channel");
            await LoadMessages(true);
        }

        static string ChannelName(Dictionary<string, object> d, int cid)
        {
            foreach (var o in d.L("channels")) { var c = o as Dictionary<string, object>; if (c != null && c.Int("id") == cid) return c.Str("name"); }
            return "chat";
        }

        async System.Threading.Tasks.Task LoadMessages(bool reset)
        {
            int gid = groupId, cid = channelId;
            var extra = new Dictionary<string, object> { { "group_id", gid }, { "channel_id", cid }, { "limit", 100 } };
            if (!reset && lastId > 0) extra["since_id"] = lastId;
            var r = await Net.Rpc("group_get_messages", Social.Auth(extra));
            if (this == null || gid != groupId || cid != channelId) return;
            nextPoll = Time.time + 5f;
            if (!Social.Ok(r)) { if (reset) emptyChat.text = Social.Err(r, "Could not load messages"); return; }
            if (reset) { Ui.Clear(msgList); shown.Clear(); lastId = 0; }
            var d = r.O("data") ?? new Dictionary<string, object>();
            foreach (var o in d.L("messages")) { var m = o as Dictionary<string, object>; if (m != null) AddMessage(m); }
            emptyChat.text = shown.Count == 0 ? "No messages yet - say hi!" : "";
            ScrollToEnd();
        }

        void AddMessage(Dictionary<string, object> m)
        {
            int id = m.Int("id");
            if (id > 0 && !shown.Add(id)) return;
            if (id > lastId) lastId = id;
            string u = m.Str("username");
            Color nc = Ui.PlayerColor(m.Str("color", "blue"));
            nc = Color.Lerp(nc, Color.white, 0.35f);
            string rank = m.Str("rank", "player");
            string when = "";
            DateTime t;
            if (DateTime.TryParse(m.Str("created_at"), null, System.Globalization.DateTimeStyles.AssumeUniversal, out t)) when = t.ToLocalTime().ToString("HH:mm");
            string body = Social.Safe(m.Str("message"));
            string att = m.Str("attachment_name");
            if (!string.IsNullOrEmpty(att)) body += (body.Length > 0 ? "\n" : "") + "<color=#" + Social.Hex(Ui.Amber) + ">[attachment: " + Social.Safe(att) + "]</color>";
            bool mine = u == Session.Username;
            var text = Ui.Label(msgList, "<b><color=#" + Social.Hex(nc) + ">" + Social.Safe(u) + "</color></b>" +
                (rank != "player" ? "  <color=#" + Social.Hex(Ui.Orange) + "><size=12>" + rank.ToUpperInvariant() + "</size></color>" : "") +
                "  <color=#" + Social.Hex(Ui.Text3) + "><size=13>" + when + "</size></color>\n" + body,
                19, mine ? Ui.Text : Ui.Text2, TextAnchor.UpperLeft, Ui.Regular, "msg");
            text.verticalOverflow = VerticalWrapMode.Overflow;
            emptyChat.text = "";
        }

        void ScrollToEnd()
        {
            Canvas.ForceUpdateCanvases();
            msgScroll.verticalNormalizedPosition = 0f;
        }

        public void Incoming(Dictionary<string, object> data)
        {
            if (data.Int("group_id") != groupId || data.Int("channel_id") != channelId) return;
            bool atEnd = msgScroll.verticalNormalizedPosition < 0.05f;
            AddMessage(data);
            typingUntil = 0;
            if (atEnd || data.Str("username") == Session.Username) ScrollToEnd();
        }

        public void Typing(Dictionary<string, object> data)
        {
            if (data.Int("group_id") != groupId || data.Int("channel_id") != channelId) return;
            typingText.text = data.Str("username") + " is typing...";
            typingUntil = Time.time + 3.5f;
        }

        void SendTyping()
        {
            if (groupId == 0 || channelId == 0 || Time.time - lastTypingSent < 2.5f || input.text.Length == 0) return;
            lastTypingSent = Time.time;
            var _ = Net.Rpc("group_typing", Social.Auth(new Dictionary<string, object> { { "group_id", groupId }, { "channel_id", channelId } }), 3f);
        }

        async void Send()
        {
            string msg = input.text.Trim();
            if (msg.Length == 0 || groupId == 0) return;
            input.text = "";
            var r = await Net.Rpc("group_send_message", Social.Auth(new Dictionary<string, object> { { "group_id", groupId }, { "channel_id", channelId }, { "message", msg } }));
            if (this == null) return;
            if (!Social.Ok(r)) { Toast.Show(Social.Err(r, "Message not sent"), Ui.Red, 4f); if (input.text.Length == 0) input.text = msg; return; }
            var d = r.O("data");
            if (d == null || d.Int("id") <= 0)
            {
                // group commands (!kick / !ban) answer with a message instead of posting
                if (!string.IsNullOrEmpty(r.Str("msg"))) Toast.Show(r.Str("msg"), Ui.Text2, 3f);
                return;
            }
            if (!shown.Contains(d.Int("id")))
            {
                d["username"] = Session.Username;
                d["message"] = msg;
                d["color"] = Session.Color;
                d["rank"] = Session.Rank;
                AddMessage(d);
                ScrollToEnd();
            }
        }

        void Update()
        {
            if (typingUntil > 0 && Time.time > typingUntil) { typingText.text = ""; typingUntil = 0; }
            if (groupId != 0 && channelId != 0 && Time.time > nextPoll && MatchController.Active == null)
            {
                nextPoll = Time.time + 5f;
                var _ = LoadMessages(false);       // backup for missed pushes
            }
        }

        void CopyInvite()
        {
            if (groupId == 0) return;
            GUIUtility.systemCopyBuffer = Social.InviteScheme + groupId;
            Toast.Show("Invite link copied - group ID " + groupId, Ui.Green);
        }

        async void Leave()
        {
            int gid = groupId;
            Prompt.Confirm("Leave group", "Leave " + title.text + "?", "LEAVE", async () =>
            {
                var r = await Net.Rpc("group_leave", Social.Auth(new Dictionary<string, object> { { "group_id", gid } }));
                if (Social.Ok(r)) { Toast.Show("Left the group", Ui.Green); groupId = 0; channelId = 0; if (this != null) { ShowChat(false); LoadGroups(); } }
                else Toast.Show(Social.Err(r, "Could not leave"), Ui.Red);
            });
            await System.Threading.Tasks.Task.Yield();
        }

        async void BuySub(string plan)
        {
            var r = await Net.Rpc("group_buy_subscription", Social.Auth(new Dictionary<string, object> { { "group_id", groupId }, { "plan", plan } }));
            if (Social.Ok(r)) { Toast.Show("Group activated!", Ui.Green); await Session.Refresh(); if (this != null) { UpdateCoins(); Open(groupId, channelId); LoadGroups(); } }
            else Toast.Show(Social.Err(r, "Could not buy"), Ui.Red, 4f);
        }

        static int ParseGroupId(string text)
        {
            text = (text ?? "").Trim();
            int id;
            if (int.TryParse(text, out id)) return id;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s*/?\s*$");
            return m.Success && int.TryParse(m.Groups[1].Value, out id) ? id : 0;
        }

        void JoinByText(string text)
        {
            int id = ParseGroupId(text);
            if (id <= 0) { Toast.Show("That isn't a group ID or invite link", Ui.Red); return; }
            Join(id);
        }

        async void Join(int id)
        {
            var r = await Net.Rpc("group_join", Social.Auth(new Dictionary<string, object> { { "group_id", id } }));
            if (Social.Ok(r) || Social.Err(r, "") == "Already a member")
            {
                Toast.Show("Joined the group", Ui.Green);
                if (this == null) return;
                browseBox.gameObject.SetActive(false);
                groupId = 0;
                Open(id, 0);
            }
            else Toast.Show(Social.Err(r, "Could not join"), Ui.Red, 4f);
        }

        async void Create(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            var r = await Net.Rpc("group_create", Social.Auth(new Dictionary<string, object> { { "name", name } }));
            if (!Social.Ok(r)) { Toast.Show(Social.Err(r, "Could not create the group"), Ui.Red, 4f); return; }
            var g = (r.O("data") ?? new Dictionary<string, object>()).O("group");
            Toast.Show("Group created - activate it to start chatting", Ui.Green, 3.5f);
            if (this != null && g != null) { groupId = 0; Open(g.Int("id"), 0); }
        }

        async void OpenBrowse()
        {
            browseBox.gameObject.SetActive(true);
            browseBox.SetAsLastSibling();
            Ui.Clear(browseList);
            var r = await Net.Rpc("group_list_all", Social.Auth());
            if (this == null) return;
            if (!Social.Ok(r)) { Toast.Show(Social.Err(r, "Could not load groups"), Ui.Red); return; }
            var mine = new HashSet<int>();
            foreach (var g in myGroups) mine.Add(g.Int("id"));
            var list = (r.O("data") ?? new Dictionary<string, object>()).L("groups");
            list.Sort((a, b) => ((b as Dictionary<string, object>).Int("member_count")).CompareTo((a as Dictionary<string, object>).Int("member_count")));
            foreach (var o in list)
            {
                var g = o as Dictionary<string, object>;
                if (g == null) continue;
                int gid = g.Int("id");
                var row = Ui.Panel(browseList, 0.92f, g.Bool("subscription_active") ? Ui.Green : (Color?)null);
                row.Size(-1, 72);
                var h = Ui.Node(row, "h").Fill(12); h.HList(12, 0, true);
                var info = Ui.Node(h, "i"); info.Size(-1, -1, 1);
                Ui.Label(info, Social.Safe(g.Str("name")), 21, Ui.Text, TextAnchor.UpperLeft, Ui.Bold).rectTransform.Place(0, 0.5f, 1, 1);
                Ui.Label(info, (g.Int("member_count") + " members  ·  owner " + g.Str("owner_username") + (g.Bool("subscription_active") ? "" : "  ·  inactive")).ToUpperInvariant(), 13, Ui.Text3, TextAnchor.LowerLeft, Ui.Bold).rectTransform.Place(0, 0, 1, 0.5f);
                if (mine.Contains(gid)) Ui.Button(h, "OPEN", Ui.Style.Secondary, "chat", () => { browseBox.gameObject.SetActive(false); Open(gid, 0); }, 15).GetComponent<RectTransform>().Size(140, -1);
                else Ui.Button(h, "JOIN", Ui.Style.Primary, "plus", () => Join(gid), 15).GetComponent<RectTransform>().Size(140, -1);
            }
        }

        public override bool OnBack()
        {
            if (browseBox.gameObject.activeSelf) { browseBox.gameObject.SetActive(false); return true; }
            return false;
        }
    }
}
