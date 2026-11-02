using UnityEngine;

namespace DogfightThreatAI.UI.Menu
{
    /// <summary>Local presentation preferences only; no battle state is stored here.</summary>
    internal struct MenuPreferences
    {
        const string Prefix = "Dogfight.Menu.";
        internal float Volume;
        internal bool Fullscreen;
        internal bool VSync;
        internal bool ReduceMotion;

        // 从本地设置读取偏好，并为缺失项采用默认值。
        internal static MenuPreferences Load()
        {
            return new MenuPreferences
            {
                Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Volume", 1f)),
                Fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1,
                VSync = PlayerPrefs.GetInt(Prefix + "VSync", QualitySettings.vSyncCount > 0 ? 1 : 0) == 1,
                ReduceMotion = PlayerPrefs.GetInt(Prefix + "ReduceMotion", 0) == 1
            };
        }

        internal static MenuPreferences Defaults => new MenuPreferences
        {
            Volume = 1f,
            Fullscreen = true,
            VSync = true,
            ReduceMotion = false
        };

        // 应用当前设置，并写入本地偏好存储。
        internal void ApplyAndSave()
        {
            AudioListener.volume = Volume;
            SetDisplay();
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            PlayerPrefs.SetFloat(Prefix + "Volume", Volume);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ReduceMotion", ReduceMotion ? 1 : 0);
            PlayerPrefs.Save();
        }

        // 游戏启动时应用已经保存的界面偏好。
        internal static void ApplySaved()
        {
            var saved = Load();
            AudioListener.volume = saved.Volume;
            // Do not change the user's launch display mode before they save a preference.
            if (PlayerPrefs.HasKey(Prefix + "Fullscreen"))
            {
                saved.SetDisplay();
            }
            if (PlayerPrefs.HasKey(Prefix + "VSync"))
            {
                QualitySettings.vSyncCount = saved.VSync ? 1 : 0;
            }
        }

        // 根据偏好切换窗口或全屏显示方式。
        void SetDisplay()
        {
            if (Screen.fullScreen != Fullscreen)
            {
                Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            }
        }
    }
}
