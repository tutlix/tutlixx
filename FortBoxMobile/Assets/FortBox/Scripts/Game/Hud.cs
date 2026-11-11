// In-match HUD in the Urban Ops style: health / power-ups, wave or PvP
// status, score, kill feed, announcements, crosshair with hit markers,
// damage flash, ammo, enemy health bars, the down / respawn overlay, the
// pause menu and the end-of-match results.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FortBox
{
    public class Hud : MonoBehaviour
    {
        public RectTransform root;
        public TouchControls touch;
        Text hpText, statusText, statusSub, scoreText, killsText, announce, announceSub, ammoText, weaponText, downTitle, downSub, netText;
        Image hpFill, dmgFlash, hitMark, reloadFill;
        RectTransform chips, feed, crosshair, bars, downPanel, pausePanel, resultsPanel, ammoPanel, scope;
        readonly List<RectTransform> cross = new List<RectTransform>();
        float announceUntil, hitUntil, dmgK, killMarkUntil;
        readonly List<(Text t, float until)> feedLines = new List<(Text, float)>();
        readonly List<(RectTransform rt, Image fill, Text name)> barPool = new List<(RectTransform, Image, Text)>();
        int barsUsed;
        public bool Paused { get; private set; }

        public static Hud Create(Transform parent)
        {
            var rt = Ui.Node(parent, "hud").Fill();
            var h = rt.gameObject.AddComponent<Hud>();
            h.root = rt;
            h.Build();
            return h;
        }

        void Build()
        {
            bars = Ui.Node(root, "bars").Fill();
            dmgFlash = Ui.Img(root, new Color(0.9f, 0.1f, 0.1f, 0f), null, "damage");
            dmgFlash.sprite = Sprite.Create(Vignette(), new Rect(0, 0, 64, 36), new Vector2(0.5f, 0.5f));
            dmgFlash.rectTransform.Fill();
            scope = Ui.Node(root, "scope").Fill();
            var sc = Ui.Img(scope, Color.black, Sprite.Create(ScopeTex(), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f)), "scope");
            sc.preserveAspect = false;
            sc.rectTransform.Box(0.5f, 0.5f, 0, 0, 900, 900);
            sc.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Ui.Img(scope, Color.black, null, "l").rectTransform.Place(0, 0, 0.5f, 1, 0, 0, 450, 0);
            Ui.Img(scope, Color.black, null, "r").rectTransform.Place(0.5f, 0, 1, 1, 450, 0, 0, 0);
            Ui.Img(scope, Ui.A(Color.black, 0.85f), null, "h").rectTransform.Place(0, 0.5f, 1, 0.5f, 0, -1, 0, -1);
            Ui.Img(scope, Ui.A(Color.black, 0.85f), null, "v").rectTransform.Place(0.5f, 0, 0.5f, 1, -1, 0, -1, 0);
            scope.gameObject.SetActive(false);

            // crosshair: four ticks + dot
            crosshair = Ui.Node(root, "crosshair");
            crosshair.Box(0.5f, 0.5f, 0, 0, 10, 10);
            crosshair.pivot = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                var t = Ui.Img(crosshair, Color.white, null, "tick").rectTransform;
                t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f);
                t.sizeDelta = i < 2 ? new Vector2(12, 3) : new Vector2(3, 12);
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.6f);
                cross.Add(t);
            }
            var dot = Ui.Img(crosshair, Ui.Orange, Ui.Circle(), "dot").rectTransform;
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(5, 5);
            hitMark = Ui.Img(crosshair, Color.white, Sprite.Create(HitTex(), new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f)), "hit");
            hitMark.rectTransform.anchorMin = hitMark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hitMark.rectTransform.sizeDelta = new Vector2(46, 46);
            hitMark.color = Color.clear;

            // top-left: health
            var hp = Ui.Panel(root, 0.86f, Ui.Green);
            hp.Box(0, 1, 24, -20, 360, 74);
            Ui.IconImg(hp, "heart", Ui.Red, 26).rectTransform.Box(0, 1, 16, -14, 26, 26);
            hpText = Ui.Label(hp, "100 / 100", 22, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            hpText.rectTransform.Place(0, 1, 1, 1, 52, -40, 10, 8);
            RectTransform barRoot;
            hpFill = Ui.StatBar(hp, 1f, Ui.Green, out barRoot);
            barRoot.Place(0, 0, 1, 0, 16, 14, 16, -24);
            chips = Ui.Node(root, "chips");
            chips.Box(0, 1, 24, -102, 500, 34);
            chips.HList(8, 0, true);

            // top centre: wave / PvP status
            var st = Ui.Panel(root, 0.86f, Ui.Orange);
            st.Box(0.5f, 1, 0, -20, 420, 74);
            statusText = Ui.Label(st, "", 24, Ui.Text, TextAnchor.MiddleCenter, Ui.Bold);
            statusText.rectTransform.Place(0, 0.45f, 1, 1, 10, 0, 10, 4);
            statusSub = Ui.Label(st, "", 16, Ui.Text2, TextAnchor.MiddleCenter, Ui.Semi);
            statusSub.rectTransform.Place(0, 0, 1, 0.48f, 10, 6, 10, 0);

            // top right: score / kills
            var sp = Ui.Panel(root, 0.86f, null);
            sp.Box(1, 1, -100, -20, 260, 74);
            var row = Ui.Node(sp, "row").Fill(10);
            row.HList(12, 0, true).childAlignment = TextAnchor.MiddleCenter;
            Ui.IconImg(row, "star", Ui.Amber, 22).rectTransform.Size(22, 22);
            scoreText = Ui.Label(row, "0", 22, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            scoreText.rectTransform.Size(80);
            Ui.IconImg(row, "crosshair", Ui.Orange, 22).rectTransform.Size(22, 22);
            killsText = Ui.Label(row, "0", 22, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            killsText.rectTransform.Size(60);
            netText = Ui.Label(root, "", 16, Ui.Amber, TextAnchor.MiddleRight, Ui.Bold);
            netText.rectTransform.Box(1, 1, -100, -100, 400, 24);

            feed = Ui.Node(root, "feed");
            feed.Box(1, 1, -24, -130, 460, 300);
            var v = feed.VList(4, 0);
            v.childAlignment = TextAnchor.UpperRight;

            announce = Ui.Label(root, "", 46, Ui.Text, TextAnchor.MiddleCenter, Ui.Bold);
            announce.rectTransform.Box(0.5f, 0.5f, 0, 190, 1200, 70);
            announce.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.7f);
            announceSub = Ui.Label(root, "", 22, Ui.Orange, TextAnchor.MiddleCenter, Ui.Bold);
            announceSub.rectTransform.Box(0.5f, 0.5f, 0, 140, 1200, 34);

            // ammo, above the fire button
            ammoPanel = Ui.Panel(root, 0.8f, null);
            ammoPanel.Box(1, 0, -392, 190, 210, 58);
            ammoText = Ui.Label(ammoPanel, "12 / 12", 26, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            ammoText.rectTransform.Place(0, 0, 1, 1, 14, 18, 8, 0);
            weaponText = Ui.Label(ammoPanel, "PISTOL", 13, Ui.Text3, TextAnchor.MiddleLeft, Ui.Bold);
            weaponText.rectTransform.Place(0, 0, 1, 0, 14, 4, 8, -20);
            reloadFill = Ui.Img(ammoPanel, Ui.Orange, null, "reload");
            reloadFill.rectTransform.Place(0, 0, 0, 0, 0, 0, 0, -3);
            ammoPanel.gameObject.SetActive(false);

            // down / respawn
            downPanel = Ui.Node(root, "down").Fill();
            Ui.Img(downPanel, new Color(0.25f, 0f, 0f, 0.35f), null, "tint").rectTransform.Fill();
            downTitle = Ui.Label(downPanel, "YOU ARE DOWN", 56, Ui.Red, TextAnchor.MiddleCenter, Ui.Bold);
            downTitle.rectTransform.Box(0.5f, 0.5f, 0, 40, 1200, 80);
            downSub = Ui.Label(downPanel, "", 24, Ui.Text2, TextAnchor.MiddleCenter, Ui.Semi);
            downSub.rectTransform.Box(0.5f, 0.5f, 0, -20, 1200, 40);
            downPanel.gameObject.SetActive(false);

            touch = TouchControls.Create(root);
        }

        static Texture2D Vignette()
        {
            int w = 64, h = 36;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = (x / (float)(w - 1)) * 2 - 1, fy = (y / (float)(h - 1)) * 2 - 1;
                    float d = Mathf.Sqrt(fx * fx * 0.8f + fy * fy);
                    float a = Mathf.Clamp01((d - 0.55f) / 0.6f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Texture2D ScopeTex()
        {
            int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f)) / 63.5f;
                    float a = Mathf.Clamp01((d - 0.93f) / 0.05f);
                    a = Mathf.Max(a, Mathf.Clamp01((d - 0.7f) / 0.3f) * 0.45f);
                    px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Texture2D HitTex()
        {
            int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - 31.5f, dy = y - 31.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float diag = Mathf.Min(Mathf.Abs(dx - dy), Mathf.Abs(dx + dy)) / 1.414f;
                    float a = (r > 11f && r < 30f) ? Mathf.Clamp01(2.6f - diag) : 0f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ---- updates ----------------------------------------------------------------
        public void SetHealth(int hp, int max, bool shield, bool rapid, bool dmg)
        {
            hpText.text = hp + " / " + max;
            float f = max > 0 ? Mathf.Clamp01(hp / (float)max) : 0f;
            hpFill.rectTransform.anchorMax = new Vector2(f, 1);
            hpFill.color = f > 0.5f ? Ui.Green : (f > 0.25f ? Ui.Amber : Ui.Red);
            string key = (shield ? "s" : "") + (rapid ? "r" : "") + (dmg ? "d" : "");
            if (chips.name != key)
            {
                chips.name = key;
                Ui.Clear(chips);
                if (shield) Chip("SHIELD", "shield", Ui.C(0, 100, 255));
                if (rapid) Chip("RAPID FIRE", "bolt", Ui.C(255, 220, 0));
                if (dmg) Chip("DAMAGE x2", "star", Ui.C(255, 0, 255));
            }
        }

        void Chip(string text, string icon, Color c)
        {
            var p = Ui.Panel(chips, 0.9f, c, true, "chip");
            p.Size(150, 32);
            Ui.IconImg(p, icon, c, 18).rectTransform.Box(0, 0.5f, 10, 0, 18, 18);
            var t = Ui.Label(p, text, 14, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            t.rectTransform.Place(0, 0, 1, 1, 34, 0, 4, 0);
        }

        public void SetStatus(string main, string sub) { statusText.text = main; statusSub.text = sub; }
        public void SetScore(int score, int kills) { scoreText.text = score.ToString(); killsText.text = kills.ToString(); }
        public void SetNet(string s) { netText.text = s; }

        public void Announce(string text, string sub = "", float secs = 2.5f)
        {
            announce.text = text;
            announceSub.text = sub;
            announceUntil = Time.time + secs;
        }

        public void Feed(string text, Color col)
        {
            var t = Ui.Label(feed, text, 18, col, TextAnchor.MiddleRight, Ui.Bold);
            t.rectTransform.Size(-1, 26);
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            feedLines.Add((t, Time.time + 5f));
            while (feedLines.Count > 6) { Destroy(feedLines[0].t.gameObject); feedLines.RemoveAt(0); }
        }

        public void HitMarker(bool kill)
        {
            hitUntil = Time.time + (kill ? 0.45f : 0.2f);
            if (kill) killMarkUntil = hitUntil;
        }

        public void Damage(float amount) { dmgK = Mathf.Min(1f, dmgK + 0.35f + amount / 60f); }

        public void SetCrosshair(float spread, bool aiming, bool scoped, bool visible)
        {
            crosshair.gameObject.SetActive(visible && !scoped);
            scope.gameObject.SetActive(visible && scoped);
            float g = 9f + spread;
            cross[0].anchoredPosition = new Vector2(-g - 6, 0);
            cross[1].anchoredPosition = new Vector2(g + 6, 0);
            cross[2].anchoredPosition = new Vector2(0, g + 6);
            cross[3].anchoredPosition = new Vector2(0, -g - 6);
            foreach (var c in cross) c.gameObject.SetActive(!aiming || spread > 2f);
        }

        public void SetAmmo(string weapon, int mag, int magSize, float reload01, bool melee)
        {
            weaponText.text = (weapon ?? "").ToUpperInvariant();
            ammoPanel.gameObject.SetActive(!melee);
            ammoText.text = melee ? "MELEE" : (reload01 >= 0f ? "RELOADING" : (mag + " / " + magSize));
            ammoText.color = !melee && mag <= Mathf.Max(2, magSize / 4) ? Ui.Amber : Ui.Text;
            reloadFill.rectTransform.anchorMax = new Vector2(reload01 >= 0f ? reload01 : 0f, 0);
            touch.SetWeaponLabel(weapon);
            touch.SetMelee(melee);
        }

        public void SetDown(bool down, string title, string sub)
        {
            downPanel.gameObject.SetActive(down);
            if (down) { downTitle.text = title; downSub.text = sub; }
        }

        public void BeginBars() { barsUsed = 0; }

        /// Health bar over an enemy / player at a screen point (pixels).
        public void Bar(Vector3 screen, float frac, Color col, string name)
        {
            if (screen.z <= 0) return;
            (RectTransform rt, Image fill, Text name) b;
            if (barsUsed < barPool.Count) b = barPool[barsUsed];
            else
            {
                var rt = Ui.Node(bars, "bar");
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0);
                rt.sizeDelta = new Vector2(64, 7);
                Ui.Img(rt, new Color(0, 0, 0, 0.65f), null, "bg").rectTransform.Fill(-1);
                var f = Ui.Img(rt, col, null, "fill");
                var n = Ui.Label(rt, "", 14, Ui.Text, TextAnchor.LowerCenter, Ui.Bold);
                n.rectTransform.Place(0.5f, 1, 0.5f, 1, -120, 2, -120, -22);
                n.horizontalOverflow = HorizontalWrapMode.Overflow;
                n.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
                b = (rt, f, n);
                barPool.Add(b);
            }
            barsUsed++;
            b.rt.gameObject.SetActive(true);
            var size = root.rect.size;
            b.rt.anchoredPosition = new Vector2(screen.x / Screen.width * size.x, screen.y / Screen.height * size.y);
            b.fill.rectTransform.Place(0, 0, Mathf.Clamp01(frac), 1);
            b.fill.color = col;
            b.name.text = name ?? "";
        }

        public void EndBars() { for (int i = barsUsed; i < barPool.Count; i++) barPool[i].rt.gameObject.SetActive(false); }

        void Update()
        {
            float a = Mathf.Clamp01((announceUntil - Time.time) / 0.4f);
            announce.color = Ui.A(Ui.Text, a);
            announceSub.color = Ui.A(Ui.Orange, a);
            float h = Mathf.Clamp01((hitUntil - Time.time) / 0.15f);
            hitMark.color = Time.time < killMarkUntil ? Ui.A(Ui.Red, h) : Ui.A(Color.white, h);
            dmgK = Mathf.Max(0f, dmgK - Time.deltaTime * 1.6f);
            dmgFlash.color = new Color(0.85f, 0.08f, 0.08f, dmgK * 0.75f);
            for (int i = feedLines.Count - 1; i >= 0; i--)
            {
                float left = feedLines[i].until - Time.time;
                if (left <= 0) { Destroy(feedLines[i].t.gameObject); feedLines.RemoveAt(i); continue; }
                var c = feedLines[i].t.color; c.a = Mathf.Clamp01(left / 0.5f); feedLines[i].t.color = c;
            }
        }

        // ---- pause menu -------------------------------------------------------------
        public void ShowPause(bool on, Action leave)
        {
            Paused = on;
            touch.Show(!on);
            if (pausePanel == null)
            {
                pausePanel = Ui.Node(root, "pause").Fill();
                var dim = Ui.Img(pausePanel, new Color(0.03f, 0.035f, 0.045f, 0.82f), null, "dim");
                dim.rectTransform.Fill();
                dim.raycastTarget = true;
                var p = Ui.Panel(pausePanel, 0.98f, Ui.Orange);
                p.Box(0.5f, 0.5f, 0, 0, 560, 600);
                p.pivot = new Vector2(0.5f, 0.5f);
                var col = Ui.Node(p, "col").Fill(28);
                col.VList(12, 0);
                Ui.Label(col, "PAUSED", 34, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Size(-1, 46);
                Ui.Label(col, "The match keeps running on the server.", 16, Ui.Text3, TextAnchor.MiddleLeft, Ui.Semi).rectTransform.Size(-1, 24);
                Ui.Slide(col, "Look sensitivity", 0.2f, 3f, Settings.LookSens, v => Settings.LookSens = v);
                Ui.Slide(col, "Aim sensitivity", 0.1f, 2f, Settings.AimSens, v => Settings.AimSens = v);
                Ui.Check(col, "Invert look up/down", Settings.InvertY, v => Settings.InvertY = v);
                Ui.Button(col, "RESUME", Ui.Style.Primary, "play", () => ShowPause(false, null), 20).GetComponent<RectTransform>().Size(-1, 60);
                Ui.Button(col, "LEAVE MATCH", Ui.Style.Danger, "logout", () => { if (pauseLeave != null) pauseLeave(); }, 20).GetComponent<RectTransform>().Size(-1, 60);
            }
            if (leave != null) pauseLeave = leave;
            pausePanel.gameObject.SetActive(on);
            pausePanel.SetAsLastSibling();
        }
        Action pauseLeave;

        // ---- results ----------------------------------------------------------------
        public void ShowResults(Dictionary<string, object> data, string me, Action again, Action menu)
        {
            touch.Show(false);
            SetDown(false, "", "");
            if (pausePanel != null) pausePanel.gameObject.SetActive(false);
            resultsPanel = Ui.Node(root, "results").Fill();
            var dim = Ui.Img(resultsPanel, new Color(0.03f, 0.035f, 0.045f, 0.9f), null, "dim");
            dim.rectTransform.Fill();
            dim.raycastTarget = true;
            var p = Ui.Panel(resultsPanel, 0.98f, Ui.Orange);
            p.Box(0.5f, 0.5f, 0, 0, 1100, 720);
            p.pivot = new Vector2(0.5f, 0.5f);
            bool pvp = data.Bool("pvp");
            string winner = data.Str("winner");
            string reason = data.Str("reason");
            string title = pvp ? (winner == me ? "VICTORY" : (string.IsNullOrEmpty(winner) ? "DRAW" : "DEFEAT")) : "MATCH OVER";
            Color tc = pvp ? (winner == me ? Ui.Green : (string.IsNullOrEmpty(winner) ? Ui.Amber : Ui.Red)) : Ui.Orange;
            var t = Ui.Label(p, title, 54, tc, TextAnchor.MiddleLeft, Ui.Bold);
            t.rectTransform.Place(0, 1, 1, 1, 36, -90, 36, 24);
            string sub = pvp ? (string.IsNullOrEmpty(winner) ? "Nobody reached the kill target" : winner + " wins") : "Reached wave " + data.Int("wave");
            if (reason == "time_limit") sub += "  ·  time limit";
            var st = Ui.Label(p, sub.ToUpperInvariant(), 18, Ui.Text2, TextAnchor.MiddleLeft, Ui.Bold);
            st.rectTransform.Place(0, 1, 1, 1, 38, -124, 36, 92);

            var table = Ui.Node(p, "table");
            table.Place(0, 0, 1, 1, 36, 120, 36, 150);
            table.VList(6, 0);
            string[] heads = { "PLAYER", "KILLS", "DEATHS", "SCORE", "XP", "COINS", "ACCURACY" };
            float[] widths = { 300, 100, 100, 120, 100, 120, 130 };
            Row(table, heads, widths, Ui.Text3, false, 14);
            var summary = data.L("summary");
            summary.Sort((a, b) => ((b as Dictionary<string, object>).Int("score")).CompareTo((a as Dictionary<string, object>).Int("score")));
            foreach (var o in summary)
            {
                var s = o as Dictionary<string, object>;
                if (s == null) continue;
                bool mine = s.Str("username") == me;
                Row(table, new[] { s.Str("username"), s.Int("kills").ToString(), s.Int("deaths").ToString(), s.Int("score").ToString(), s.Int("xp").ToString(), "+" + s.Int("coins"), s.Int("accuracy") + "%" },
                    widths, mine ? Ui.Orange : Ui.Text, mine, 20);
            }
            var btns = Ui.Node(p, "btns");
            btns.Place(0, 0, 1, 0, 36, 30, 36, -96);
            btns.HList(16, 0, true).childAlignment = TextAnchor.MiddleRight;
            if (again != null) Ui.Button(btns, "PLAY AGAIN", Ui.Style.Primary, "refresh", () => again(), 20).GetComponent<RectTransform>().Size(260, 66);
            Ui.Button(btns, "MAIN MENU", Ui.Style.Secondary, "back", () => menu(), 20).GetComponent<RectTransform>().Size(260, 66);
        }

        static void Row(Transform table, string[] cells, float[] widths, Color col, bool hi, int size)
        {
            var row = Ui.Node(table, "row");
            row.Size(-1, hi ? 46 : 40);
            if (hi) Ui.Img(row, Ui.A(Ui.Orange, 0.12f), Ui.Round(), "hi").rectTransform.Fill();
            var h = Ui.Node(row, "cells").Fill();
            h.HList(0, 0, true).padding = new RectOffset(12, 12, 0, 0);
            for (int i = 0; i < cells.Length; i++)
            {
                var t = Ui.Label(h, cells[i], size, col, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, Ui.Bold);
                t.rectTransform.Size(widths[i], -1, i == 0 ? 1 : 0);
            }
        }
    }
}
