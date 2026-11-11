// 3D figures for enemies and players: the PC game's actor models (every
// enemy type's own gear), with the same walk gaits, aiming arms, head
// tracking, glow pulse, hit flinch and death fall (panda_models.Actors).
using System.Collections.Generic;
using UnityEngine;

namespace FortBox
{
    public class ActorInfo
    {
        public string key, style = "scout", weapon;
        public float x, y, z, scale = 1f;          // world px, centre of the figure
        public Color32? shirt;
        public float? facing;                      // radians, world angle (atan2(dy, dx))
        public bool aiming, moving, local;
        public float anim;                         // walk phase
        public float flash;                        // 0..1 hit flash
        public string tag;
    }

    public class ActorView : MonoBehaviour
    {
        const float HIP = 19f, TOP = HIP + 16f;
        static Material worldMat, glowMat;
        static Dictionary<string, string> gaits;

        public string sig;
        Transform body, head, legL, legR, armL, armR, gun;
        readonly List<Renderer> rends = new List<Renderer>();
        readonly List<Renderer> glows = new List<Renderer>();
        MaterialPropertyBlock mpb;
        float? heading;
        Vector2? lastPos;
        float hit, look, lookP, seed, lastFlash;
        bool armed, isLocal;
        string gait = "normal", style;
        public float lastSeen;

        // death
        bool dying;
        float deathT, fallDir;

        static string GaitOf(string style)
        {
            if (gaits == null)
            {
                gaits = new Dictionary<string, string> {
                    {"scout","quick"},{"heavy","heavy"},{"sniper","sneak"},{"zombie","shamble"},{"boss","stride"},
                    {"charger","quick"},{"guardian","heavy"},{"bomber","quick"},{"juggernaut","heavy"},{"hive_queen","stride"} };
            }
            string g;
            return style != null && gaits.TryGetValue(style, out g) ? g : "normal";
        }

        static void EnsureMats()
        {
            if (worldMat != null) return;
            worldMat = new Material(MapWorld.Sh("FortBoxWorld"));
            worldMat.SetFloat("_SpecK", 0.18f);
            glowMat = new Material(MapWorld.Sh("FortBoxGlow"));
        }

        public static string Sig(ActorInfo a)
        {
            string sh = a.shirt.HasValue ? (a.shirt.Value.r + "," + a.shirt.Value.g + "," + a.shirt.Value.b) : "";
            return a.style + "|" + sh + "|" + a.weapon + "|" + a.local;
        }

        public static ActorView Create(ActorInfo a, Transform parent)
        {
            EnsureMats();
            var parts = Models.Actor(a.local ? "player_fp" : a.style);
            var go = new GameObject("actor " + a.key);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<ActorView>();
            v.sig = Sig(a);
            v.style = a.style;
            v.isLocal = a.local;
            v.gait = GaitOf(a.style);
            v.seed = (Mathf.Abs((a.key ?? "").GetHashCode()) % 1000) / 159f;
            v.mpb = new MaterialPropertyBlock();
            var byName = new Dictionary<string, Transform>();
            if (parts != null)
            {
                // parents first (exported in order body, head, legs, arms, gun)
                foreach (var p in parts)
                {
                    var t = new GameObject(p.name).transform;
                    Transform par;
                    t.SetParent(!string.IsNullOrEmpty(p.parent) && byName.TryGetValue(p.parent, out par) ? par : go.transform, false);
                    t.localPosition = p.pos;
                    byName[p.name] = t;
                    int gi = 0;
                    foreach (var d in p.geos)
                    {
                        var mgo = new GameObject(p.name + "-" + d.kind + gi);
                        mgo.transform.SetParent(t, false);
                        Mesh mesh = a.shirt.HasValue && (a.style == "player" || a.local)
                            ? Recolor.Shirt(d, a.shirt.Value)
                            : Models.CachedMesh("actor:" + a.style + ":" + p.name + ":" + gi, d);
                        mgo.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var r = mgo.AddComponent<MeshRenderer>();
                        bool glow = d.kind == "glow";
                        r.sharedMaterial = glow ? glowMat : worldMat;
                        r.shadowCastingMode = glow ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                        (glow ? v.glows : v.rends).Add(r);
                        gi++;
                    }
                }
            }
            Transform tr;
            byName.TryGetValue("body", out v.body);
            byName.TryGetValue("head", out v.head);
            byName.TryGetValue("legL", out v.legL);
            byName.TryGetValue("legR", out v.legR);
            byName.TryGetValue("armL", out v.armL);
            byName.TryGetValue("armR", out v.armR);
            if (!byName.TryGetValue("gun", out tr) && v.body != null)
            {
                tr = new GameObject("gun").transform;
                tr.SetParent(v.body, false);
            }
            v.gun = tr;
            v.armed = !string.IsNullOrEmpty(a.weapon) && a.weapon != "Fists";
            if (v.armed && v.gun != null && !a.local) v.AttachGun(a.weapon);
            if (a.local)
            {
                // your own body: torso and legs only - arms and gun are the view model
                foreach (var t in new[] { v.armL, v.armR, v.gun }) if (t != null) t.gameObject.SetActive(false);
            }
            return v;
        }

        void AttachGun(string weapon)
        {
            var gm = Models.Gun(weapon);
            if (gm == null) return;
            const float S = MapWorld.S;
            if (gm.melee)
            {
                gun.localPosition = new Vector3(9.7f, TOP - 4f, 14.5f) * S;
                gun.localRotation = Quaternion.Euler(60f, 0, 0);
            }
            else
            {
                gun.localPosition = new Vector3(3.4f, TOP - 5.2f, 12.5f) * S;
                gun.localRotation = Quaternion.identity;
            }
            gun.localScale = Vector3.one * 0.36f;
            int gi = 0;
            foreach (var d in gm.geos)
            {
                var mgo = new GameObject("gun-" + gi);
                mgo.transform.SetParent(gun, false);
                mgo.AddComponent<MeshFilter>().sharedMesh = Models.CachedMesh("gun:" + weapon + ":" + gi, d);
                var r = mgo.AddComponent<MeshRenderer>();
                bool glow = d.kind == "glow";
                r.sharedMaterial = glow ? glowMat : worldMat;
                r.shadowCastingMode = glow ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                (glow ? glows : rends).Add(r);
                gi++;
            }
        }

        static float AngleTo(float a, float b) { return Mathf.Repeat(b - a + 180f, 360f) - 180f; }
        static Quaternion Hpr(float h, float p, float r) { return Quaternion.Euler(-p, -h, -r); }

        /// eye: world px (x, y, height) of the camera, for head tracking.
        public void Pose(ActorInfo a, float dt, Vector3 eye)
        {
            lastSeen = Time.time;
            float x = a.x, y = a.y;
            transform.localPosition = MapWorld.W2U(x, y, a.z);
            transform.localScale = Vector3.one * a.scale;
            float? f = a.facing;
            if (!f.HasValue && lastPos.HasValue)
            {
                float dx = x - lastPos.Value.x, dy = y - lastPos.Value.y;
                if (dx * dx + dy * dy > 0.25f) f = Mathf.Atan2(dy, dx);
            }
            lastPos = new Vector2(x, y);
            if (f.HasValue)
            {
                float target = Mathf.Atan2(-Mathf.Cos(f.Value), -Mathf.Sin(f.Value)) * Mathf.Rad2Deg;
                if (!heading.HasValue || isLocal) heading = target;
                else
                {
                    float step = 720f * Mathf.Max(dt, 0.016f);
                    heading = heading.Value + Mathf.Clamp(AngleTo(heading.Value, target), -step, step);
                }
            }
            transform.localRotation = Hpr(heading ?? 0f, 0, 0);

            float now = Time.time + seed;
            float t = a.anim * 11f;
            bool moving = a.moving;
            float swing = 34f, bob = 1.3f, lean = 0f, sway = 0f, crouch = 0f;
            switch (gait)
            {
                case "quick": swing = 42f; bob = 1.6f; lean = -9f; break;
                case "heavy": swing = 22f; bob = 2.2f; sway = 4f; t *= 0.75f; break;
                case "sneak": swing = 26f; bob = 0.6f; lean = -13f; crouch = 2.6f; break;
                case "shamble": swing = 20f; bob = 1f; lean = -12f; sway = 7f; t *= 0.7f; break;
                case "stride": swing = 30f; bob = 1f; lean = 3f; break;
            }
            float sw = moving ? Mathf.Sin(t) : 0f;
            if (legL != null) legL.localRotation = Hpr(0, sw * swing, 0);
            if (legR != null) legR.localRotation = Hpr(0, -sw * swing, 0);
            float breath = Mathf.Sin(now * 2.2f) * 0.35f;
            if (body != null)
            {
                var bp = body.localPosition;
                bp.y = ((moving ? Mathf.Abs(Mathf.Cos(t)) * bob : breath) - crouch) * MapWorld.S + baseBodyY();
                body.localPosition = bp;
            }
            float baseP = moving ? lean : lean * 0.35f;
            if (gait == "shamble") baseP = lean;
            float baseR = moving ? Mathf.Sin(t) * sway : Mathf.Sin(now * 0.9f) * sway * 0.4f;
            if (armR != null && armL != null)
            {
                if (armed)
                {
                    float lift = a.aiming ? 6f : 0f;
                    armR.localRotation = Hpr(26, 70 + lift - baseP * 0.6f, 0);
                    armL.localRotation = Hpr(-38, 74 + lift - baseP * 0.6f, 0);
                    if (gun != null) gun.localRotation = Hpr(0, lift * 0.5f - baseP * 0.6f, 0);
                }
                else if (gait == "shamble" || style == "zombie" || style == "hive_queen")
                {
                    float reach = Mathf.Sin(now * 1.7f) * 6f;
                    armR.localRotation = Hpr(4, 84 + sw * 8 + reach - baseP, 0);
                    armL.localRotation = Hpr(-4, 84 - sw * 8 - reach - baseP, 0);
                }
                else if (gait == "quick" && moving)
                {
                    armR.localRotation = Hpr(0, -sw * 48 + 18, -6);
                    armL.localRotation = Hpr(0, sw * 48 + 18, 6);
                }
                else
                {
                    armR.localRotation = Hpr(0, sw * 30, -4);
                    armL.localRotation = Hpr(0, -sw * 30, 4);
                }
            }
            // head looks at you, within neck limits; zombies loll
            if (head != null && !isLocal)
            {
                float lookH = 0f, lookPt = 0f;
                float ang = Mathf.Atan2(eye.y - y, eye.x - x);
                float tgt = Mathf.Atan2(-Mathf.Cos(ang), -Mathf.Sin(ang)) * Mathf.Rad2Deg;
                float rel = AngleTo(heading ?? 0f, tgt);
                if (Mathf.Abs(rel) < 110f)
                {
                    lookH = Mathf.Clamp(rel, -55f, 55f);
                    float dist = Mathf.Max(1f, Mathf.Sqrt((eye.x - x) * (eye.x - x) + (eye.y - y) * (eye.y - y)));
                    lookPt = Mathf.Clamp(Mathf.Atan2(eye.z - 50f * a.scale, dist) * Mathf.Rad2Deg, -25f, 25f);
                }
                float k = Mathf.Min(1f, dt * 6f);
                look += (lookH - look) * k;
                lookP += (lookPt - lookP) * k;
                if (gait == "shamble")
                    head.localRotation = Hpr(look * 0.5f + Mathf.Sin(now * 1.3f) * 10f, lookP * 0.5f - 12f, Mathf.Sin(now * 0.8f) * 16f);
                else
                    head.localRotation = Hpr(look, lookP - baseP * 0.5f, 0);
            }
            // glowing parts pulse; hit flash tints the whole figure
            float pulse = 0.82f + 0.18f * Mathf.Sin(now * 3.4f);
            mpb.Clear();
            mpb.SetFloat("_Pulse", pulse);
            foreach (var g in glows) if (g != null) g.SetPropertyBlock(mpb);
            if (a.flash > 0.01f && lastFlash <= 0.01f) hit = 1f;
            lastFlash = a.flash;
            mpb.Clear();
            float fk = a.flash;
            mpb.SetColor("_Tint", new Color(1 + fk * 1.6f, 1 + fk * 0.4f, 1 + fk * 0.4f, 1));
            foreach (var r in rends) if (r != null) r.SetPropertyBlock(mpb);
            if (body != null)
            {
                if (hit > 0f)
                {
                    hit = Mathf.Max(0f, hit - dt * 5f);
                    float h = hit * hit;
                    body.localRotation = Hpr(0, -14f * h + baseP * (1 - h), 6f * h + baseR * (1 - h));
                }
                else body.localRotation = Hpr(0, baseP, baseR);
            }
        }

        float bodyY0 = float.NaN;
        float baseBodyY()
        {
            if (float.IsNaN(bodyY0)) bodyY0 = body.localPosition.y;
            return bodyY0;
        }

        /// Killed: falls away from the shot, lies, then sinks into the ground.
        public void Die(Vector2 from)
        {
            if (dying) return;
            dying = true;
            deathT = 0f;
            var p = MapWorld.U2W(transform.localPosition);
            float away = Mathf.Atan2(p.y - from.y, p.x - from.x);
            float awayH = Mathf.Atan2(-Mathf.Cos(away), -Mathf.Sin(away)) * Mathf.Rad2Deg;
            fallDir = AngleTo(heading ?? 0f, awayH);
            if (gun != null && gun.gameObject.activeSelf)
            {
                // the gun drops to the ground beside the body
                gun.SetParent(transform, true);
            }
        }

        public bool Dying { get { return dying; } }

        void Update()
        {
            if (!dying) return;
            deathT += Time.deltaTime;
            float k = Mathf.Clamp01(deathT / 0.55f);
            k = 1f - (1f - k) * (1f - k) * (1f - k);
            // fall backwards (away from the shot) onto the ground
            bool back = Mathf.Abs(fallDir) > 90f;
            float tip = (back ? 1f : -1f) * 88f * k;
            var baseRot = Hpr(heading ?? 0f, 0, 0);
            transform.localRotation = baseRot * Hpr(0, tip, 0);
            if (legL != null) legL.localRotation = Hpr(0, 10f * k, 0);
            if (legR != null) legR.localRotation = Hpr(0, -14f * k, 0);
            if (armL != null) armL.localRotation = Hpr(-30f * k, 120f * k, 0);
            if (armR != null) armR.localRotation = Hpr(30f * k, 110f * k, 0);
            if (head != null) head.localRotation = Hpr(25f * k, -15f * k, 0);
            if (gun != null && gun.parent == transform)
            {
                var gp = gun.localPosition;
                gp.y = Mathf.Max(0.02f, gp.y - Time.deltaTime * 2.5f);
                gun.localPosition = gp;
            }
            const float LIE = 6.5f, SINK = 1.6f;
            if (deathT > LIE)
            {
                var p = transform.localPosition;
                p.y -= Time.deltaTime * (1.6f / SINK);
                transform.localPosition = p;
            }
            if (deathT > LIE + SINK) Destroy(gameObject);
        }
    }

    /// Keeps one figure per enemy / player, like panda_models.Actors.
    public class ActorSet
    {
        readonly Transform root;
        readonly Dictionary<string, ActorView> live = new Dictionary<string, ActorView>();
        readonly List<ActorView> dying = new List<ActorView>();
        readonly HashSet<string> seen = new HashSet<string>();
        public Vector2 lastShotFrom;

        public ActorSet(Transform parent)
        {
            root = new GameObject("actors").transform;
            root.SetParent(parent, false);
        }

        public ActorView Get(string key) { ActorView v; return live.TryGetValue(key, out v) ? v : null; }

        public void Update(List<ActorInfo> actors, Vector3 eye, Vector2 killerHint)
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            seen.Clear();
            foreach (var a in actors)
            {
                if (a.key == null || !seen.Add(a.key)) continue;
                ActorView v;
                string sig = ActorView.Sig(a);
                if (!live.TryGetValue(a.key, out v) || v == null || v.sig != sig)
                {
                    if (v != null) Object.Destroy(v.gameObject);
                    v = live[a.key] = ActorView.Create(a, root);
                }
                v.Pose(a, dt, eye);
            }
            var gone = new List<string>();
            foreach (var kv in live) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var k in gone)
            {
                var v = live[k];
                live.Remove(k);
                if (v == null) continue;
                var p = MapWorld.U2W(v.transform.localPosition);
                float dx = p.x - eye.x, dy = p.y - eye.y;
                if (dx * dx + dy * dy > 900f * 900f || k.StartsWith("me")) { Object.Destroy(v.gameObject); continue; }
                v.Die(killerHint);
                dying.Add(v);
            }
            dying.RemoveAll(d => d == null);
            while (dying.Count > 24) { if (dying[0] != null) Object.Destroy(dying[0].gameObject); dying.RemoveAt(0); }
        }

        /// Removes a figure without a death animation (e.g. left the match).
        public void Drop(string key)
        {
            ActorView v;
            if (live.TryGetValue(key, out v)) { live.Remove(key); if (v != null) Object.Destroy(v.gameObject); }
        }

        public void Clear()
        {
            foreach (var v in live.Values) if (v != null) Object.Destroy(v.gameObject);
            live.Clear();
            foreach (var v in dying) if (v != null) Object.Destroy(v.gameObject);
            dying.Clear();
        }
    }
}
