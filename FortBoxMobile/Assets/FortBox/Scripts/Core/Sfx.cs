// Sound, using the PC game's own files: Background.mp3 music, per-weapon
// gunshots (WEAPON_SOUND_KEYS incl. the Gun Pack mapping), the looping
// flamethrower, explosions, game over and the menu click. A few small
// extra cues (hit marker, pickup, melee swing, message ping) are synthesized.
using System.Collections.Generic;
using UnityEngine;

namespace FortBox
{
    public class Sfx : MonoBehaviour
    {
        public static Sfx I;
        AudioSource music, ui, loop;
        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
        static Dictionary<string, object> weapons;
        float loopUntil;

        public static float MusicVol { get { return PlayerPrefs.GetFloat("fb_vol_music", 0.7f); } set { PlayerPrefs.SetFloat("fb_vol_music", Mathf.Clamp01(value)); if (I != null) I.music.volume = value * 0.6f; } }
        public static float EffectsVol { get { return PlayerPrefs.GetFloat("fb_vol_fx", 0.8f); } set { PlayerPrefs.SetFloat("fb_vol_fx", Mathf.Clamp01(value)); } }
        public static float UiVol { get { return PlayerPrefs.GetFloat("fb_vol_ui", 0.6f); } set { PlayerPrefs.SetFloat("fb_vol_ui", Mathf.Clamp01(value)); } }

        public static void Init(GameObject host)
        {
            if (I != null) return;
            I = host.AddComponent<Sfx>();
            I.music = host.AddComponent<AudioSource>();
            I.music.loop = true;
            I.music.playOnAwake = false;
            I.music.volume = MusicVol * 0.6f;
            I.ui = host.AddComponent<AudioSource>();
            I.ui.playOnAwake = false;
            I.loop = host.AddComponent<AudioSource>();
            I.loop.loop = true;
            I.loop.playOnAwake = false;
            for (int i = 0; i < 14; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                I.pool.Add(s);
            }
            I.clips["hit"] = Synth("hit", 0.05f, t => Mathf.Sin(t * 2 * Mathf.PI * 1900f) * Mathf.Exp(-t * 70f));
            I.clips["kill"] = Synth("kill", 0.14f, t => (Mathf.Sin(t * 2 * Mathf.PI * 1250f) + 0.6f * Mathf.Sin(t * 2 * Mathf.PI * 1875f)) * 0.6f * Mathf.Exp(-t * 22f));
            I.clips["pickup"] = Synth("pickup", 0.22f, t => Mathf.Sin(t * 2 * Mathf.PI * (660f + 900f * t)) * Mathf.Exp(-t * 12f));
            I.clips["swing"] = Synth("swing", 0.2f, t => (Random.value * 2 - 1) * Mathf.Sin(Mathf.PI * t / 0.2f) * 0.5f);
            I.clips["ping"] = Synth("ping", 0.25f, t => (Mathf.Sin(t * 2 * Mathf.PI * 880f) * 0.7f + Mathf.Sin(t * 2 * Mathf.PI * 1320f) * 0.3f) * Mathf.Exp(-t * 14f));
            I.clips["hurt"] = Synth("hurt", 0.12f, t => Mathf.Sin(t * 2 * Mathf.PI * (180f - 300f * t)) * Mathf.Exp(-t * 18f));
            I.clips["jump"] = Synth("jump", 0.09f, t => (Random.value * 2 - 1) * 0.35f * Mathf.Exp(-t * 40f));
        }

        static AudioClip Synth(string name, float secs, System.Func<float, float> f)
        {
            int rate = 22050, n = Mathf.CeilToInt(secs * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)rate), -1f, 1f) * 0.8f;
            var c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        AudioClip Clip(string name)
        {
            AudioClip c;
            if (clips.TryGetValue(name, out c)) return c;
            c = Resources.Load<AudioClip>("FortBox/sounds/" + name);
            clips[name] = c;
            return c;
        }

        static string WeaponSound(string weapon)
        {
            if (weapons == null)
            {
                var ta = Resources.Load<TextAsset>("FortBox/data/weapons");
                weapons = ta != null ? (Json.Obj(ta.text) ?? new Dictionary<string, object>()) : new Dictionary<string, object>();
            }
            var w = weapons.O(weapon ?? "Pistol");
            if (w != null && w.Bool("melee")) return null;
            return w != null ? w.Str("sound", "shoot") : "shoot";
        }

        AudioSource Free()
        {
            foreach (var s in pool) if (!s.isPlaying) return s;
            AudioSource oldest = pool[0];
            foreach (var s in pool) if (s.time > oldest.time) oldest = s;
            return oldest;
        }

        /// One-shot with volume 0..1 (already scaled by distance) and pitch.
        public static void Play(string name, float vol = 1f, float pitch = 1f, float minGap = 0.03f)
        {
            if (I == null || vol <= 0.01f) return;
            float last;
            if (I.lastPlay.TryGetValue(name, out last) && Time.unscaledTime - last < minGap) return;
            var c = I.Clip(name);
            if (c == null) return;
            I.lastPlay[name] = Time.unscaledTime;
            var s = I.Free();
            s.clip = c;
            s.volume = Mathf.Clamp01(vol * EffectsVol);
            s.pitch = pitch;
            s.Play();
        }

        /// Your own gun firing. The flamethrower loops while you hold fire.
        public static void Shoot(string weapon)
        {
            if (I == null) return;
            if (weapon == "Flamethrower")
            {
                var c = I.Clip("flamethrower");
                if (c == null) return;
                if (!I.loop.isPlaying || I.loop.clip != c) { I.loop.clip = c; I.loop.time = 0; I.loop.Play(); }
                I.loop.volume = EffectsVol * 0.8f;
                I.loopUntil = Time.unscaledTime + 0.3f;
                return;
            }
            string snd = WeaponSound(weapon);
            if (snd == null) Play("swing", 0.7f, Random.Range(0.9f, 1.1f));
            else Play(snd, 0.75f, Random.Range(0.96f, 1.04f), 0.02f);
        }

        /// Someone else's shot at a distance (px).
        public static void ShotAt(string weapon, float dist)
        {
            string snd = WeaponSound(weapon);
            if (snd == null || dist > 1500f) return;
            Play(snd, 0.5f * Falloff(dist), Random.Range(0.94f, 1.04f), 0.05f);
        }

        public static void Explosion(float dist) { Play("explosion", Mathf.Max(0.15f, Falloff(dist)), Random.Range(0.92f, 1.05f), 0.05f); }

        public static float Falloff(float dist) { return Mathf.Clamp01(1f - dist / 1600f) * Mathf.Clamp01(380f / Mathf.Max(380f, dist)); }

        public static void Click()
        {
            if (I == null) return;
            var c = I.Clip("menu_click");
            if (c != null) I.ui.PlayOneShot(c, UiVol);
        }

        public static void Ping() { if (I != null) { var c = I.Clip("ping"); if (c != null) I.ui.PlayOneShot(c, UiVol); } }

        public static void Music(bool on)
        {
            if (I == null) return;
            if (on)
            {
                if (I.music.clip == null) I.music.clip = I.Clip("Background");
                I.music.volume = MusicVol * 0.6f;
                if (I.music.clip != null && !I.music.isPlaying) I.music.Play();
            }
            else I.music.Stop();
        }

        /// Music a bit quieter during matches so gunfire reads clearly.
        public static void Duck(bool inMatch) { if (I != null) I.music.volume = MusicVol * (inMatch ? 0.3f : 0.6f); }

        public static void StopLoops() { if (I != null) I.loop.Stop(); }

        void Update()
        {
            if (loop.isPlaying && Time.unscaledTime > loopUntil)
            {
                loop.volume = Mathf.Max(0f, loop.volume - Time.unscaledDeltaTime * 6f);
                if (loop.volume <= 0.01f) loop.Stop();
            }
        }
    }
}
