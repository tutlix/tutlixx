// Mobile controls: floating move joystick (left), drag-to-look (right half,
// also while holding Fire), and buttons for Fire, Aim (zoom), Jump, Grenade,
// Reload and weapon switch. Pure multi-touch, independent of the UI event
// system so every finger works at the same time.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FortBox
{
    public class TouchControls : MonoBehaviour
    {
        // ---- outputs read by the match ------------------------------------------
        public static Vector2 Move;            // -1..1, y = forward
        public static Vector2 Look;            // this frame's look drag, in screen heights
        public static bool Fire;
        public static bool Aim;                // toggled by the Aim button
        public static bool JumpPressed, GrenadePressed, WeaponPressed, ReloadPressed, PausePressed;

        class Pad
        {
            public string name, icon;
            public Vector2 anchor;             // 0..1 of the canvas
            public Vector2 offset;             // canvas px from the anchor
            public float radius;               // canvas px
            public RectTransform rt;
            public Image bg, ic, ring;
            public int finger = -1;
            public Text label;
        }

        RectTransform root;
        readonly List<Pad> pads = new List<Pad>();
        Pad fire, fire2, aim, jump, grenade, reload, weapon, pause;
        RectTransform stickBase, stickKnob;
        Image stickBaseImg, stickKnobImg;
        int stickFinger = -1, lookFinger = -1;
        Vector2 stickOrigin, lookLast;
        readonly Dictionary<int, Vector2> fireLook = new Dictionary<int, Vector2>();
        const float StickRadius = 110f;
        float scale = 1f;

        public static TouchControls Create(RectTransform parent)
        {
            var rt = Ui.Node(parent, "touch").Fill();
            var tc = rt.gameObject.AddComponent<TouchControls>();
            tc.root = rt;
            tc.Build();
            return tc;
        }

        Pad AddPad(string name, string icon, Vector2 anchor, Vector2 offset, float radius, Color col, string label = null)
        {
            var p = new Pad { name = name, icon = icon, anchor = anchor, offset = offset * scale, radius = radius * scale };
            p.rt = Ui.Node(root, "pad-" + name);
            p.rt.Box(anchor.x, anchor.y, p.offset.x, p.offset.y, p.radius * 2, p.radius * 2);
            p.rt.pivot = new Vector2(0.5f, 0.5f);
            p.bg = Ui.Img(p.rt, new Color(0.04f, 0.045f, 0.06f, 0.55f), Ui.Circle(), "bg");
            p.bg.rectTransform.Fill();
            p.ring = Ui.Img(p.rt, Ui.A(col, 0.9f), RingSprite(), "ring");
            p.ring.rectTransform.Fill();
            p.ic = Ui.Img(p.rt, Color.white, Ui.Icon(icon), "icon");
            p.ic.preserveAspect = true;
            p.ic.rectTransform.Fill(p.radius * 0.52f);
            if (label != null)
            {
                p.label = Ui.Label(p.rt, label, 14, Ui.Text2, TextAnchor.UpperCenter, Ui.Bold);
                p.label.rectTransform.Place(0, 0, 1, 0, -40, -26, -40, 0);
                p.label.rectTransform.sizeDelta = new Vector2(80, 22);
            }
            pads.Add(p);
            return p;
        }

        static Sprite ring;
        static Sprite RingSprite()
        {
            if (ring != null) return ring;
            int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f));
                    float a = Mathf.Clamp01(63.5f - d) * Mathf.Clamp01(d - 59.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            ring = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return ring;
        }

        void Build()
        {
            scale = Settings.ControlsScale;
            var cg = root.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = Settings.ControlsAlpha;
            cg.blocksRaycasts = false;
            cg.interactable = false;
            // right-hand cluster (thumb arc around the big fire button)
            fire = AddPad("fire", "crosshair", new Vector2(1, 0), new Vector2(-170, 190), 82, Ui.Orange);
            aim = AddPad("aim", "eye", new Vector2(1, 0), new Vector2(-330, 120), 50, Ui.Text2, "AIM");
            jump = AddPad("jump", "chevron_down", new Vector2(1, 0), new Vector2(-72, 340), 50, Ui.Text2, "JUMP");
            reload = AddPad("reload", "refresh", new Vector2(1, 0), new Vector2(-310, 290), 40, Ui.Text2);
            grenade = AddPad("grenade", "bolt", new Vector2(1, 0), new Vector2(-200, 400), 40, Ui.Amber);
            weapon = AddPad("weapon", "ammo", new Vector2(1, 0), new Vector2(-64, 60), 40, Ui.Text2);
            // second fire button top-left, for shooting while steering with the right thumb
            fire2 = AddPad("fire2", "crosshair", new Vector2(0, 1), new Vector2(150, -250), 52, Ui.Orange);
            pause = AddPad("pause", "gear", new Vector2(1, 1), new Vector2(-46, -46), 30, Ui.Text3);
            if (Ui.Icon("eye") == null) aim.ic.sprite = Ui.Icon("crosshair");
            jump.ic.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);

            stickBase = Ui.Node(root, "stick-base");
            stickBase.sizeDelta = Vector2.one * StickRadius * 2 * scale;
            stickBaseImg = stickBase.gameObject.AddComponent<Image>();
            stickBaseImg.sprite = RingSprite();
            stickBaseImg.color = Ui.A(Ui.Text, 0.45f);
            stickBaseImg.raycastTarget = false;
            var fillBg = Ui.Img(stickBase, new Color(0.04f, 0.045f, 0.06f, 0.35f), Ui.Circle(), "bg");
            fillBg.rectTransform.Fill();
            fillBg.transform.SetAsFirstSibling();
            stickKnob = Ui.Node(root, "stick-knob");
            stickKnob.sizeDelta = Vector2.one * 88 * scale;
            stickKnobImg = stickKnob.gameObject.AddComponent<Image>();
            stickKnobImg.sprite = Ui.Circle();
            stickKnobImg.color = Ui.A(Ui.Text, 0.8f);
            stickKnobImg.raycastTarget = false;
            RestStick();
        }

        void RestStick()
        {
            // resting position bottom-left (shown dimmed until touched)
            stickOrigin = new Vector2(250 * scale, 230 * scale);
            stickBase.anchorMin = stickBase.anchorMax = stickKnob.anchorMin = stickKnob.anchorMax = Vector2.zero;
            stickBase.anchoredPosition = stickKnob.anchoredPosition = stickOrigin;
            stickBaseImg.color = Ui.A(Ui.Text, 0.28f);
            stickKnobImg.color = Ui.A(Ui.Text, 0.45f);
        }

        Vector2 CanvasSize() { return root.rect.size; }

        /// Screen pixels -> canvas px (origin bottom-left).
        Vector2 ToCanvas(Vector2 screen)
        {
            var size = CanvasSize();
            return new Vector2(screen.x / Screen.width * size.x, screen.y / Screen.height * size.y);
        }

        Vector2 PadCenter(Pad p)
        {
            var size = CanvasSize();
            return new Vector2(p.anchor.x * size.x + p.offset.x, p.anchor.y * size.y + p.offset.y);
        }

        Pad HitPad(Vector2 c)
        {
            Pad best = null;
            float bd = float.MaxValue;
            foreach (var p in pads)
            {
                if (!p.rt.gameObject.activeSelf) continue;
                float d = Vector2.Distance(c, PadCenter(p));
                if (d <= p.radius * 1.18f && d < bd) { best = p; bd = d; }
            }
            return best;
        }

        struct T { public int id; public TouchPhase phase; public Vector2 pos; }
        readonly List<T> touches = new List<T>();

        void Gather()
        {
            touches.Clear();
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                touches.Add(new T { id = t.fingerId, phase = t.phase, pos = t.position });
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            // editor testing: the mouse acts as one finger
            if (Input.touchCount == 0)
            {
                if (Input.GetMouseButtonDown(0)) touches.Add(new T { id = 99, phase = TouchPhase.Began, pos = Input.mousePosition });
                else if (Input.GetMouseButton(0)) touches.Add(new T { id = 99, phase = TouchPhase.Moved, pos = Input.mousePosition });
                else if (Input.GetMouseButtonUp(0)) touches.Add(new T { id = 99, phase = TouchPhase.Ended, pos = Input.mousePosition });
            }
#endif
        }

        public void SetWeaponLabel(string w) { if (weapon.label == null) { weapon.label = Ui.Label(weapon.rt, "", 13, Ui.Text2, TextAnchor.UpperCenter, Ui.Bold); weapon.label.rectTransform.Place(0, 0, 1, 0, -60, -24, -60, 0); weapon.label.rectTransform.sizeDelta = new Vector2(120, 22); } weapon.label.text = (w ?? "").ToUpperInvariant(); }
        public void SetGrenades(int n) { if (grenade.label == null) { grenade.label = Ui.Label(grenade.rt, "", 14, Ui.Amber, TextAnchor.UpperCenter, Ui.Bold); grenade.label.rectTransform.Place(0, 0, 1, 0, -40, -24, -40, 0); grenade.label.rectTransform.sizeDelta = new Vector2(80, 22); } grenade.label.text = "x" + n; grenade.ic.color = n > 0 ? Color.white : Ui.Text3; }
        public void SetMelee(bool melee) { aim.rt.gameObject.SetActive(!melee); reload.rt.gameObject.SetActive(!melee); if (melee) Aim = false; }
        public void Show(bool on) { root.gameObject.SetActive(on); if (!on) ResetAll(); }

        public void ResetAll()
        {
            Move = Look = Vector2.zero;
            Fire = false;
            stickFinger = lookFinger = -1;
            fireLook.Clear();
            foreach (var p in pads) p.finger = -1;
            if (stickBase != null) RestStick();
        }

        void Update()
        {
            JumpPressed = GrenadePressed = WeaponPressed = ReloadPressed = PausePressed = false;
            Look = Vector2.zero;
            Gather();
            float h = CanvasSize().y;
            foreach (var t in touches)
            {
                var c = ToCanvas(t.pos);
                if (t.phase == TouchPhase.Began)
                {
                    var p = HitPad(c);
                    if (p != null)
                    {
                        p.finger = t.id;
                        if (p == aim) Aim = !Aim;
                        else if (p == jump) JumpPressed = true;
                        else if (p == grenade) GrenadePressed = true;
                        else if (p == reload) ReloadPressed = true;
                        else if (p == weapon) WeaponPressed = true;
                        else if (p == pause) PausePressed = true;
                        if (p == fire || p == fire2) fireLook[t.id] = c;
                    }
                    else if (c.x < CanvasSize().x * 0.42f && stickFinger < 0)
                    {
                        stickFinger = t.id;
                        stickOrigin = c;
                        stickBase.anchoredPosition = stickKnob.anchoredPosition = c;
                        stickBaseImg.color = Ui.A(Ui.Text, 0.45f);
                        stickKnobImg.color = Ui.A(Ui.Text, 0.85f);
                    }
                    else if (lookFinger < 0)
                    {
                        lookFinger = t.id;
                        lookLast = c;
                    }
                }
                else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    if (t.id == stickFinger)
                    {
                        var d = c - stickOrigin;
                        float r = StickRadius * scale;
                        if (d.magnitude > r)
                        {
                            // drag the base along (floating stick)
                            stickOrigin += d - d.normalized * r;
                            stickBase.anchoredPosition = stickOrigin;
                            d = d.normalized * r;
                        }
                        stickKnob.anchoredPosition = stickOrigin + d;
                        Move = d / r;
                    }
                    else if (t.id == lookFinger)
                    {
                        Look += (c - lookLast) / h;
                        lookLast = c;
                    }
                    else if (fireLook.ContainsKey(t.id))
                    {
                        Look += (c - fireLook[t.id]) / h;     // aim while shooting
                        fireLook[t.id] = c;
                    }
                }
                else // Ended / Canceled
                {
                    if (t.id == stickFinger) { stickFinger = -1; Move = Vector2.zero; RestStick(); }
                    if (t.id == lookFinger) lookFinger = -1;
                    fireLook.Remove(t.id);
                    foreach (var p in pads) if (p.finger == t.id) p.finger = -1;
                }
            }
            Fire = fire.finger >= 0 || fire2.finger >= 0;
#if UNITY_EDITOR || UNITY_STANDALONE
            // editor-only test keys (the phone build uses touch only)
            var k = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            if (k != Vector2.zero) Move = k.normalized;
            if (Input.GetMouseButton(1)) Look += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 0.02f;
            if (Input.GetKey(KeyCode.F)) Fire = true;
            if (Input.GetKeyDown(KeyCode.Space)) JumpPressed = true;
            if (Input.GetKeyDown(KeyCode.G)) GrenadePressed = true;
            if (Input.GetKeyDown(KeyCode.Q)) WeaponPressed = true;
            if (Input.GetKeyDown(KeyCode.R)) ReloadPressed = true;
            if (Input.GetKeyDown(KeyCode.E)) Aim = !Aim;
#endif
            // visuals: pressed pads light up, aim stays lit while toggled
            foreach (var p in pads)
            {
                bool lit = p.finger >= 0 || (p == aim && Aim);
                p.bg.color = lit ? new Color(1f, 0.42f, 0.12f, 0.55f) : new Color(0.04f, 0.045f, 0.06f, 0.55f);
                p.rt.localScale = p.finger >= 0 ? Vector3.one * 0.94f : Vector3.one;
            }
        }

        void OnDisable() { ResetAll(); Aim = false; }
    }
}
