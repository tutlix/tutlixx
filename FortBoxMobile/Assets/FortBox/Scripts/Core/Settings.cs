// Player settings stored on the phone.
using UnityEngine;

namespace FortBox
{
    public static class Settings
    {
        public static int Quality { get { return PlayerPrefs.GetInt("fb_quality", SystemInfo.systemMemorySize >= 5000 ? 2 : 1); } set { PlayerPrefs.SetInt("fb_quality", Mathf.Clamp(value, 0, 2)); Apply(); } }
        public static bool Shadows { get { return PlayerPrefs.GetInt("fb_shadows", Quality >= 1 ? 1 : 0) == 1; } set { PlayerPrefs.SetInt("fb_shadows", value ? 1 : 0); } }
        public static float LookSens { get { return PlayerPrefs.GetFloat("fb_look", 1f); } set { PlayerPrefs.SetFloat("fb_look", Mathf.Clamp(value, 0.2f, 3f)); } }
        public static float AimSens { get { return PlayerPrefs.GetFloat("fb_aimsens", 0.6f); } set { PlayerPrefs.SetFloat("fb_aimsens", Mathf.Clamp(value, 0.1f, 2f)); } }
        public static bool InvertY { get { return PlayerPrefs.GetInt("fb_invert", 0) == 1; } set { PlayerPrefs.SetInt("fb_invert", value ? 1 : 0); } }
        public static float ControlsScale { get { return PlayerPrefs.GetFloat("fb_ctrl_scale", 1f); } set { PlayerPrefs.SetFloat("fb_ctrl_scale", Mathf.Clamp(value, 0.7f, 1.4f)); } }
        public static float ControlsAlpha { get { return PlayerPrefs.GetFloat("fb_ctrl_alpha", 0.85f); } set { PlayerPrefs.SetFloat("fb_ctrl_alpha", Mathf.Clamp(value, 0.3f, 1f)); } }
        public static bool AutoFire { get { return PlayerPrefs.GetInt("fb_autofire", 0) == 1; } set { PlayerPrefs.SetInt("fb_autofire", value ? 1 : 0); } }
        public static int Fps { get { return PlayerPrefs.GetInt("fb_fps", 60); } set { PlayerPrefs.SetInt("fb_fps", value); Apply(); } }
        public static string Difficulty { get { return PlayerPrefs.GetString("fb_difficulty", "normal"); } set { PlayerPrefs.SetString("fb_difficulty", value); } }
        public static string LastMap { get { return PlayerPrefs.GetString("fb_map", "citadel"); } set { PlayerPrefs.SetString("fb_map", value); } }

        public static void Apply()
        {
            Application.targetFrameRate = Fps;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = Quality >= 2 ? 4 : (Quality == 1 ? 2 : 0);
            QualitySettings.lodBias = Quality >= 2 ? 1.5f : 1f;
            float rs = Quality == 0 ? 0.7f : (Quality == 1 ? 0.85f : 1f);
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (w > 0 && h > 0) Screen.SetResolution(Mathf.RoundToInt(w * rs), Mathf.RoundToInt(h * rs), true);
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
