// Project setup + one-click builds. Runs when the project opens: applies the
// player settings the game needs (Gamma colour space like the PC renderer,
// landscape, IL2CPP ARM64, internet + plain-HTTP access to the game server),
// creates the boot scene, and tunes texture imports.
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.Android;

namespace FortBox.EditorTools
{
    [InitializeOnLoad]
    public static class FortBoxBuild
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string BundleId = "com.fortbox.fortbox3d";

        static FortBoxBuild()
        {
            EditorApplication.delayCall += () => { if (!SessionState.GetBool("fb_setup_done", false)) { Setup(); SessionState.SetBool("fb_setup_done", true); } };
        }

        [MenuItem("FortBox/Apply project settings")]
        public static void Setup()
        {
            PlayerSettings.companyName = "FortBox";
            PlayerSettings.productName = "FortBox 3D";
            ApplyVersionAndSigning();
            PlayerSettings.colorSpace = ColorSpace.Gamma;                     // the shaders do their own sRGB -> linear, like the PC GLSL
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed; // the game server speaks http/ws
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.iOS.allowHTTPDownload = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, true);
            PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

            QualitySettings.shadowDistance = 70f;
            QualitySettings.shadowCascades = 1;

            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);   // the app builds itself at runtime (App.Boot)
                AssetDatabase.Refresh();
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[FortBox] project settings applied");
        }

        static string[] Scenes() { return new[] { ScenePath }; }

        // ---- version + signing ---------------------------------------------------
        // Every build that should update players' phones needs a higher version
        // and the SAME signing key (Android refuses updates signed differently).
        const string KeystorePath = "Signing/fortbox.keystore";
        const string KeyAlias = "fortbox";

        static string Arg(string name, string env)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            var e = System.Environment.GetEnvironmentVariable(env);
            return string.IsNullOrEmpty(e) ? null : e;
        }

        static void ApplyVersionAndSigning()
        {
            string v = Arg("-fbVersion", "FORTBOX_VERSION");
            if (string.IsNullOrEmpty(v) && File.Exists("VERSION")) v = File.ReadAllText("VERSION").Trim();
            if (string.IsNullOrEmpty(v)) v = "1.0.0";
            PlayerSettings.bundleVersion = v;
            var p = v.Split('.');
            int a = p.Length > 0 ? int.Parse(p[0]) : 1, b = p.Length > 1 ? int.Parse(p[1]) : 0, c = p.Length > 2 ? int.Parse(p[2]) : 0;
            PlayerSettings.Android.bundleVersionCode = Mathf.Clamp(a * 1000000 + b * 1000 + c, 1, 2100000000);
            PlayerSettings.iOS.buildNumber = PlayerSettings.Android.bundleVersionCode.ToString();

            string pass = Arg("-fbKeystorePass", "FORTBOX_KEYSTORE_PASS");
            if (string.IsNullOrEmpty(pass) && File.Exists("Signing/password.txt")) pass = File.ReadAllText("Signing/password.txt").Trim();
            if (File.Exists(KeystorePath) && !string.IsNullOrEmpty(pass))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(KeystorePath);
                PlayerSettings.Android.keystorePass = pass;
                PlayerSettings.Android.keyaliasName = KeyAlias;
                PlayerSettings.Android.keyaliasPass = pass;
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[FortBox] No signing password - this APK uses a debug key and can't update installs made by the cloud build.");
            }
        }

        [MenuItem("FortBox/Build Android APK")]
        public static void BuildApk() { BuildAndroid(false); }

        [MenuItem("FortBox/Build Android App Bundle (Play Store)")]
        public static void BuildAab() { BuildAndroid(true); }

        static void BuildAndroid(bool aab)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Setup();
            EditorUserBuildSettings.buildAppBundle = aab;
            Directory.CreateDirectory("Builds");
            string path = "Builds/FortBox3D." + (aab ? "aab" : "apk");
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes(), locationPathName = path, target = BuildTarget.Android, options = BuildOptions.None
            });
            Debug.Log("[FortBox] Android build: " + r.summary.result + " -> " + path);
            if (Application.isBatchMode && r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        [MenuItem("FortBox/Build iOS (Xcode project, needs a Mac)")]
        public static void BuildIos()
        {
            Setup();
            Directory.CreateDirectory("Builds/iOS");
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes(), locationPathName = "Builds/iOS", target = BuildTarget.iOS, options = BuildOptions.None
            });
            Debug.Log("[FortBox] iOS build: " + r.summary.result);
        }
    }

    /// Texture / audio import rules for the exported game data.
    public class FortBoxTextureImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("Resources/FortBox/sounds/")) return;
            var ai = (AudioImporter)assetImporter;
            var st = ai.defaultSampleSettings;
            bool music = assetPath.EndsWith(".mp3");
            st.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            st.compressionFormat = AudioCompressionFormat.Vorbis;
            st.quality = music ? 0.6f : 0.8f;
            ai.defaultSampleSettings = st;
            ai.forceToMono = !music;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("Resources/FortBox/")) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Contains("/icons/"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else if (assetPath.EndsWith("_ground.png"))
            {
                ti.maxTextureSize = 4096;                            // 3200 x 1716 ground painting
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            else if (assetPath.Contains("/thumbs/"))
            {
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            else
            {
                ti.maxTextureSize = 2048;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
            }
        }
    }

    /// Adds the in-app updater to the Android build: the install permission,
    /// a tiny content provider that hands the downloaded APK to Android's
    /// installer, and the Java helper the C# updater calls.
    public class FortBoxAndroidUpdater : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder { get { return 10; } }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
            var xml = File.ReadAllText(manifest);
            if (!xml.Contains("REQUEST_INSTALL_PACKAGES"))
            {
                int i = xml.IndexOf("<application");
                xml = xml.Insert(i, "<uses-permission android:name=\"android.permission.REQUEST_INSTALL_PACKAGES\" />\n  ");
            }
            if (!xml.Contains("com.fortbox.updater.ApkProvider"))
            {
                int i = xml.LastIndexOf("</application>");
                xml = xml.Insert(i, "  <provider android:name=\"com.fortbox.updater.ApkProvider\" android:authorities=\"${applicationId}.fbupdate\" android:exported=\"false\" android:grantUriPermissions=\"true\" />\n  ");
            }
            File.WriteAllText(manifest, xml);
            string dir = Path.Combine(path, "src/main/java/com/fortbox/updater");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ApkProvider.java"), ProviderJava);
            File.WriteAllText(Path.Combine(dir, "Installer.java"), InstallerJava);
        }

        const string ProviderJava = @"package com.fortbox.updater;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileNotFoundException;

/** Serves the downloaded update APK to the system package installer. */
public class ApkProvider extends ContentProvider {
    static volatile File file;

    static File current(android.content.Context ctx) {
        File f = file;
        if (f == null && ctx != null) f = new File(ctx.getExternalFilesDir(null), ""updates/FortBox3D.apk"");
        return f;
    }

    @Override public boolean onCreate() { return true; }

    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        File f = current(getContext());
        if (f == null || !f.exists()) throw new FileNotFoundException(""no update downloaded"");
        return ParcelFileDescriptor.open(f, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override public String getType(Uri uri) { return ""application/vnd.android.package-archive""; }

    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        MatrixCursor c = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
        File f = current(getContext());
        if (f != null && f.exists()) c.addRow(new Object[] { ""FortBox3D.apk"", f.length() });
        return c;
    }

    @Override public Uri insert(Uri uri, ContentValues values) { return null; }
    @Override public int delete(Uri uri, String selection, String[] args) { return 0; }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { return 0; }
}
";

        const string InstallerJava = @"package com.fortbox.updater;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;
import java.io.File;

/** Opens Android's installer for the downloaded update. */
public class Installer {
    /** 0 = installer opened, 1 = user must first allow installs from this app. */
    public static int install(Activity act, String path) {
        if (Build.VERSION.SDK_INT >= 26 && !act.getPackageManager().canRequestPackageInstalls()) {
            Intent s = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse(""package:"" + act.getPackageName()));
            act.startActivity(s);
            return 1;
        }
        File f = new File(path);
        ApkProvider.file = f;
        Uri uri = Build.VERSION.SDK_INT >= 24
            ? Uri.parse(""content://"" + act.getPackageName() + "".fbupdate/FortBox3D.apk"")
            : Uri.fromFile(f);
        Intent i = new Intent(Intent.ACTION_VIEW);
        i.setDataAndType(uri, ""application/vnd.android.package-archive"");
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        act.startActivity(i);
        return 0;
    }
}
";
    }
}
