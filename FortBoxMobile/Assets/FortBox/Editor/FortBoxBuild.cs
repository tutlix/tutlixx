// Project setup + one-click builds. Runs when the project opens: applies the
// player settings the game needs (Gamma colour space like the PC renderer,
// landscape, IL2CPP ARM64, internet + plain-HTTP access to the game server),
// creates the boot scene, and tunes texture imports.
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

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
            PlayerSettings.bundleVersion = "1.0.0";
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
            PlayerSettings.Android.bundleVersionCode = 1;
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

        [MenuItem("FortBox/Build Android APK")]
        public static void BuildApk() { BuildAndroid(false); }

        [MenuItem("FortBox/Build Android App Bundle (Play Store)")]
        public static void BuildAab() { BuildAndroid(true); }

        static void BuildAndroid(bool aab)
        {
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

    /// Texture import rules for the exported game data.
    public class FortBoxTextureImport : AssetPostprocessor
    {
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
}
