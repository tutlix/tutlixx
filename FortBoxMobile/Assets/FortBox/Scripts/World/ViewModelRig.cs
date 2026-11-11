// The gun in your hands (panda_models.ViewModel): drawn by its own camera on
// top of the world, with the same sway, walk bob, breathing, aim-down-sights
// pose, recoil kick, reload dip and muzzle flash as the PC.
using System.Collections.Generic;
using UnityEngine;

namespace FortBox
{
    public class ViewModelRig : MonoBehaviour
    {
        public const int Layer = 31;
        const float VM_SCALE = 0.0086f, VM_HFOV = 72f;
        static readonly Dictionary<string, Vector3> Base = new Dictionary<string, Vector3> {
            {"pistol", new Vector3(0.15f, 0.42f, -0.15f)}, {"smg", new Vector3(0.16f, 0.41f, -0.165f)},
            {"rifle", new Vector3(0.17f, 0.41f, -0.17f)}, {"shotgun", new Vector3(0.17f, 0.41f, -0.17f)},
            {"heavy", new Vector3(0.19f, 0.43f, -0.19f)}, {"crossbow", new Vector3(0.16f, 0.41f, -0.165f)},
            {"melee", new Vector3(0.19f, 0.46f, -0.24f)}, {"fists", new Vector3(0.13f, 0.38f, -0.17f)} };

        public Camera vmCam;
        Transform pivot;
        GameObject model;
        Transform flash;
        string curKey;
        ViewModelData cur;
        float sx, sz, sh, walk;
        float? lastYaw, lastPitch;
        Material worldMat, glowMat, flashMat;

        public static ViewModelRig Attach(Camera main)
        {
            var rig = main.gameObject.AddComponent<ViewModelRig>();
            var go = new GameObject("vm-camera");
            go.transform.SetParent(main.transform, false);
            rig.vmCam = go.AddComponent<Camera>();
            rig.vmCam.clearFlags = CameraClearFlags.Depth;
            rig.vmCam.cullingMask = 1 << Layer;
            rig.vmCam.depth = main.depth + 1;
            rig.vmCam.nearClipPlane = 0.01f;
            rig.vmCam.farClipPlane = 20f;
            rig.vmCam.allowMSAA = main.allowMSAA;
            main.cullingMask &= ~(1 << Layer);
            rig.pivot = new GameObject("vm-pivot").transform;
            rig.pivot.SetParent(go.transform, false);
            rig.worldMat = new Material(MapWorld.Sh("FortBoxWorld"));
            rig.worldMat.SetFloat("_SpecK", 0.35f);
            rig.glowMat = new Material(MapWorld.Sh("FortBoxGlow"));
            rig.flashMat = new Material(MapWorld.Sh("FortBoxFx"));
            rig.flashMat.SetTexture("_MainTex", FlashTex());
            rig.flashMat.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            rig.flashMat.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.One);
            return rig;
        }

        static Texture2D flashTex;
        static Texture2D FlashTex()
        {
            if (flashTex != null) return flashTex;
            int n = 64;
            flashTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - 31.5f, dy = y - 31.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d / 30f), 1.6f);
                    // six star points
                    float ang = Mathf.Atan2(dy, dx);
                    float star = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f + 0.3f)), 18f) * Mathf.Clamp01(1f - d / 31f) * 0.7f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a + star) * 255));
                }
            flashTex.SetPixels32(px);
            flashTex.Apply();
            return flashTex;
        }

        void Build(string weapon, Color32 sleeve)
        {
            if (model != null) Destroy(model);
            cur = ViewModelData.Get(weapon);
            model = new GameObject("vm-model");
            model.layer = Layer;
            model.transform.SetParent(pivot, false);
            if (cur == null) return;
            float size = cur.kind == "heavy" ? 0.82f : (cur.kind == "melee" ? 0.8f : 1f);
            model.transform.localScale = Vector3.one * (VM_SCALE / MapWorld.S) * size;
            int gi = 0;
            foreach (var d in cur.geos)
            {
                var go = new GameObject("g" + gi++);
                go.layer = Layer;
                go.transform.SetParent(model.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Recolor.Shirt(d, sleeve);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = d.kind == "glow" ? glowMat : worldMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            flash = null;
            if (!cur.melee && cur.flash > 0f)
            {
                var f = new GameObject("flash");
                f.layer = Layer;
                f.transform.SetParent(model.transform, false);
                f.transform.localPosition = cur.muzzle;
                var quad = QuadMesh();
                Color fc = cur.energy ? cur.accent : new Color(1f, 0.81f, 0.47f);
                for (int k = 0; k < 3; k++)
                {
                    var q = new GameObject("fl" + k);
                    q.layer = Layer;
                    q.transform.SetParent(f.transform, false);
                    // quads facing the camera (normal along the barrel), rotated 60deg apart
                    q.transform.localRotation = Quaternion.Euler(0, 0, k * 60f);
                    q.transform.localScale = Vector3.one * 11f * cur.flash * MapWorld.S;
                    q.AddComponent<MeshFilter>().sharedMesh = quad;
                    var mr = q.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = flashMat;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_Color", fc);
                    mr.SetPropertyBlock(mpb);
                }
                foreach (var roll in new[] { 0f, 90f })
                {
                    var q = new GameObject("streak");
                    q.layer = Layer;
                    q.transform.SetParent(f.transform, false);
                    q.transform.localRotation = Quaternion.AngleAxis(roll, Vector3.forward) * Quaternion.Euler(90f, 0, 0);
                    q.transform.localPosition = new Vector3(0, 0, 5f * cur.flash * MapWorld.S);
                    q.transform.localScale = new Vector3(4.4f * cur.flash, 14f * cur.flash, 1f) * MapWorld.S;
                    q.AddComponent<MeshFilter>().sharedMesh = quad;
                    var mr = q.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = flashMat;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_Color", fc);
                    mr.SetPropertyBlock(mpb);
                }
                flash = f.transform;
                f.SetActive(false);
            }
        }

        static Mesh quad;
        static Mesh QuadMesh()
        {
            if (quad != null) return quad;
            quad = new Mesh();
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return quad;
        }

        public bool IsMelee { get { return cur != null && cur.melee; } }

        /// yaw/pitch radians; ads 0..1; shotAge ms since the last shot (-1 none);
        /// tilt: reload/swap dip in degrees; swing 0..1 melee swing progress.
        public void UpdateGun(string weapon, Color32 sleeve, float yaw, float pitch, bool moving, float walkT,
                              float ads, float shotAge, float tilt, float swing, bool visible)
        {
            string key = (weapon ?? "_fists") + "|" + sleeve.r + "," + sleeve.g + "," + sleeve.b;
            if (key != curKey) { curKey = key; Build(weapon, sleeve); }
            vmCam.enabled = visible;
            if (!visible || cur == null) return;
            var main = GetComponent<Camera>();
            float aspect = main.aspect;
            float hf = VM_HFOV * Mathf.Deg2Rad;
            vmCam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(hf / 2f) / aspect) * Mathf.Rad2Deg;

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            float dyaw = 0, dpitch = 0;
            if (lastYaw.HasValue)
            {
                dyaw = Mathf.Repeat(yaw - lastYaw.Value + Mathf.PI, 2 * Mathf.PI) - Mathf.PI;
                dpitch = pitch - lastPitch.Value;
            }
            lastYaw = yaw; lastPitch = pitch;
            float k = Mathf.Min(1f, dt * 10f);
            sx += (Mathf.Clamp(-dyaw * 0.55f, -0.035f, 0.035f) - sx) * k;
            sz += (Mathf.Clamp(-dpitch * 0.45f, -0.025f, 0.025f) - sz) * k;
            sh += (Mathf.Clamp(dyaw * 120f, -6f, 6f) - sh) * k;
            walk += ((moving ? 1f : 0f) - walk) * Mathf.Min(1f, dt * 6f);
            float t = walkT * 5.5f;
            float bx = Mathf.Sin(t) * 0.011f * walk;
            float bz = -Mathf.Abs(Mathf.Cos(t)) * 0.012f * walk;
            float breath = Mathf.Sin(Time.time * 2.1f) * 0.0028f;
            Vector3 b;
            if (!Base.TryGetValue(cur.kind, out b)) b = Base["rifle"];
            float a = cur.melee ? 0f : Mathf.Clamp01(ads);
            a = a * a * (3f - 2f * a);
            float calm = 1f - 0.75f * a;
            float x = b.x * (1f - a) + (sx + bx) * calm;
            float y = b.y - 0.03f * a;
            float z = b.z + 0.055f * a + (sz + bz + breath) * calm;
            float h = (3f + sh) * (1f - a), p = 0f, r = 0f;
            if (cur.melee)
            {
                // resting: blade up over the right shoulder; a swing sweeps it across
                float sw = swing > 0f ? Mathf.Sin(swing * Mathf.PI) : 0f;
                float tl = tilt + sw * 70f;
                h += 10f - tl * 0.55f;
                p += -30f - tl * 0.35f;
                r += 16f - tl * 0.6f;
                x -= tl * 0.0009f;
            }
            else
            {
                p -= tilt;
                r += Mathf.Max(0f, tilt) * 0.35f;
                if (shotAge >= 0f && shotAge < 130f)
                {
                    float e = (1f - shotAge / 130f); e *= e;
                    y -= 0.028f * e;
                    z += 0.004f * e;
                }
            }
            // PC view-model space: x right, y forward, z up -> Unity (x, z, y)
            pivot.localPosition = new Vector3(x, z, y);
            pivot.localRotation = Quaternion.Euler(-p, -h, -r);
            if (flash != null)
            {
                bool on = shotAge >= 0f && shotAge < 55f;
                flash.gameObject.SetActive(on);
                if (on)
                {
                    flash.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                    flash.localScale = Vector3.one * Random.Range(0.8f, 1.25f);
                }
            }
        }
    }
}
