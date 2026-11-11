// Auto-update: on start (and every 30 min in the menus) the app asks the game
// server for the newest published version (GET /mobile/version). When it is
// newer than this install, an update prompt shows the release notes; the new
// APK downloads inside the app with a progress bar, its SHA-256 is checked,
// and Android's installer opens. Required updates (or installs older than the
// server's minimum version) can't be skipped.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace FortBox
{
    public class AppUpdater : MonoBehaviour
    {
        public static AppUpdater I;
        Dictionary<string, object> release;
        RectTransform box;
        Text status, notes;
        Image bar;
        Ui.Btn updateBtn, laterBtn;
        bool busy, waitingForPermission;
        string apkPath;
        float nextCheck;
        string skipped;

        public static void Init(GameObject host)
        {
            if (I != null) return;
            I = host.AddComponent<AppUpdater>();
            I.nextCheck = Time.realtimeSinceStartup + 1.5f;
        }

        /// "1.0.12" -> comparable number (same rule as the server).
        public static long Code(string v)
        {
            var p = (v ?? "0").Split('.');
            long c = 0;
            for (int i = 0; i < 4; i++)
            {
                int n = 0;
                if (i < p.Length) int.TryParse(p[i], out n);
                c = c * 1000 + Math.Min(999, Math.Max(0, n));
            }
            return c;
        }

        void Update()
        {
            if (busy || box != null || Time.realtimeSinceStartup < nextCheck || MatchController.Active != null) return;
            nextCheck = Time.realtimeSinceStartup + 1800f;
            Check();
        }

        public async void Check(bool manual = false)
        {
            var raw = await Net.Get("/mobile/version");
            if (this == null) return;
            var r = raw != null ? Json.Obj(raw) : null;
            string latest = r != null ? r.Str("version") : "";
            if (string.IsNullOrEmpty(latest))
            {
                if (manual) Toast.Show(r == null ? "Couldn't reach the update server" : "No update published yet");
                return;
            }
            long mine = Code(Application.version), theirs = Code(latest);
            if (theirs <= mine)
            {
                if (manual) Toast.Show("FortBox 3D is up to date (" + Application.version + ")", Ui.Green);
                return;
            }
            bool required = r.Bool("force") || (!string.IsNullOrEmpty(r.Str("min_version")) && mine < Code(r.Str("min_version")));
            if (!required && !manual && skipped == latest) return;
            release = r;
            release["required"] = required;
            ShowDialog();
        }

        void ShowDialog()
        {
            if (box != null) Destroy(box.gameObject);
            bool required = release.Bool("required");
            box = Ui.Node(App.Overlay, "update").Fill();
            var dim = Ui.Img(box, new Color(0.03f, 0.035f, 0.045f, 0.9f), null, "dim");
            dim.rectTransform.Fill();
            dim.raycastTarget = true;
            var p = Ui.Panel(box, 0.99f, Ui.Orange);
            p.Box(0.5f, 0.5f, 0, 0, 820, 560);
            p.pivot = new Vector2(0.5f, 0.5f);
            Ui.Section(p, required ? "Update required" : "Update available", Ui.Orange).Place(0, 1, 1, 1, 28, -50, 28, 26);
            var t = Ui.Label(p, "FORTBOX 3D " + release.Str("version"), 40, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            t.rectTransform.Place(0, 1, 1, 1, 28, -110, 28, 56);
            double mb = release.Num("size") / 1048576.0;
            Ui.Label(p, ("You have " + Application.version + "  ·  download " + mb.ToString("0.0") + " MB").ToUpperInvariant(), 15, Ui.Text3, TextAnchor.MiddleLeft, Ui.Bold)
                .rectTransform.Place(0, 1, 1, 1, 30, -140, 28, 112);
            string n = release.Str("notes");
            notes = Ui.Label(p, string.IsNullOrEmpty(n) ? "New version of FortBox 3D." : n, 19, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular);
            notes.rectTransform.Place(0, 0, 1, 1, 30, 190, 28, 160);
            status = Ui.Label(p, required ? "This version is required to keep playing." : "", 16, Ui.Amber, TextAnchor.MiddleLeft, Ui.Semi);
            status.rectTransform.Place(0, 0, 1, 0, 30, 146, 28, -180);
            var track = Ui.Img(p, Ui.C(38, 41, 49), null, "track");
            track.rectTransform.Place(0, 0, 1, 0, 30, 120, 30, -128);
            bar = Ui.Img(track.transform, Ui.Orange, null, "bar");
            bar.rectTransform.Place(0, 0, 0, 1);
            track.gameObject.SetActive(false);
            var btns = Ui.Node(p, "btns").Place(0, 0, 1, 0, 28, 26, 28, -98);
            btns.HList(14, 0, true);
            if (!required)
            {
                laterBtn = Ui.Button(btns, "LATER", Ui.Style.Secondary, "clock", () =>
                {
                    skipped = release.Str("version");
                    Destroy(box.gameObject);
                    box = null;
                }, 18);
                laterBtn.GetComponent<RectTransform>().Size(220, -1);
            }
            updateBtn = Ui.Button(btns, "UPDATE NOW", Ui.Style.Primary, "refresh", StartUpdate, 22);
            updateBtn.GetComponent<RectTransform>().Size(-1, -1, 1);
            box.SetAsLastSibling();
        }

        void StartUpdate()
        {
            if (busy) return;
            string url = Config.HttpUrl.TrimEnd('/') + release.Str("url", "/mobile/FortBox3D.apk");
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(Download(url));
#else
            Application.OpenURL(url);     // other platforms: download in the browser
#endif
        }

        IEnumerator Download(string url)
        {
            busy = true;
            updateBtn.SetDisabled(true);
            if (laterBtn != null) laterBtn.SetDisabled(true);
            bar.transform.parent.gameObject.SetActive(true);
            string dir = Path.Combine(Application.persistentDataPath, "updates");
            Directory.CreateDirectory(dir);
            apkPath = Path.Combine(dir, "FortBox3D.apk");
            string part = apkPath + ".part";
            if (File.Exists(part)) File.Delete(part);
            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(part) { removeFileOnAbort = true };
                req.timeout = 0;
                var op = req.SendWebRequest();
                double total = Math.Max(1.0, release.Num("size"));
                while (!op.isDone)
                {
                    float f = Mathf.Clamp01((float)(req.downloadedBytes / total));
                    bar.rectTransform.anchorMax = new Vector2(f, 1);
                    status.text = "Downloading... " + (req.downloadedBytes / 1048576.0).ToString("0.0") + " / " + (total / 1048576.0).ToString("0.0") + " MB";
                    status.color = Ui.Text2;
                    yield return null;
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Fail("Download failed: " + req.error);
                    yield break;
                }
            }
            bar.rectTransform.anchorMax = Vector2.one;
            status.text = "Checking the download...";
            yield return null;
            string want = release.Str("sha256");
            if (!string.IsNullOrEmpty(want) && !string.Equals(Sha256(part), want, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                Fail("The download was damaged - try again");
                yield break;
            }
            if (File.Exists(apkPath)) File.Delete(apkPath);
            File.Move(part, apkPath);
            busy = false;
            Install();
        }

        static string Sha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var h = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        void Fail(string msg)
        {
            busy = false;
            status.text = msg;
            status.color = Ui.Red;
            updateBtn.SetDisabled(false);
            updateBtn.SetLabel("TRY AGAIN");
            if (laterBtn != null) laterBtn.SetDisabled(false);
        }

        void Install()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var inst = new AndroidJavaClass("com.fortbox.updater.Installer"))
                {
                    int r = inst.CallStatic<int>("install", act, apkPath);
                    if (r == 1)
                    {
                        waitingForPermission = true;
                        status.text = "Allow \"Install unknown apps\" for FortBox 3D, then come back.";
                        status.color = Ui.Amber;
                    }
                    else
                    {
                        status.text = "Opening the installer - tap Update.";
                        status.color = Ui.Green;
                    }
                    updateBtn.SetDisabled(false);
                    updateBtn.SetLabel("INSTALL");
                    updateBtn.button.onClick.RemoveAllListeners();
                    updateBtn.button.onClick.AddListener(() => { Sfx.Click(); Install(); });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[update] installer failed: " + e.Message);
                // fall back to the browser download
                Application.OpenURL(Config.HttpUrl.TrimEnd('/') + release.Str("url", "/mobile/FortBox3D.apk"));
                Fail("Opened the download in your browser - install it from there.");
            }
#endif
        }

        void OnApplicationFocus(bool focus)
        {
            // back from the "install unknown apps" settings page
            if (focus && waitingForPermission && apkPath != null && File.Exists(apkPath))
            {
                waitingForPermission = false;
                Install();
            }
        }

        /// Settings button: check right now.
        public static void CheckNow() { if (I != null) I.Check(true); }
    }
}
