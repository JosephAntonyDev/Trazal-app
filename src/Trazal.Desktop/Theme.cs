using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Trazal.Desktop
{
    public static class Theme
    {
        private static string preference = "Sistema";
        private static bool attached;
        public static void Apply(string value)
        {
            preference = value;
            if (!attached) { SystemEvents.UserPreferenceChanged += OnPreferenceChanged; attached = true; }
            bool dark = value == "Oscuro";
            if (value == "Sistema")
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            Set("Canvas", dark ? "#171B17" : "#FAFAF6");
            Set("Surface", dark ? "#20251F" : "#FFFFFF");
            Set("Tint", dark ? "#2A3025" : "#F2F4E9");
            Set("Ink", dark ? "#EDF0E7" : "#30342B");
            Set("Muted", dark ? "#B5BEAA" : "#69725D");
            Set("Line", dark ? "#444E3E" : "#DDE2D2");
            Set("Hover", dark ? "#343B30" : "#F4F5EC");
            Set("AccentSoft", dark ? "#423B20" : "#FBEDAA");
            Set("Danger", dark ? "#F0B198" : "#A23B29");
            Set("Disabled", dark ? "#383D35" : "#E5E6E1");
        }
        private static void Set(string key, string hex) => Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        private static void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        { if (preference == "Sistema") Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(preference))); }
        public static void Detach() { if (attached) SystemEvents.UserPreferenceChanged -= OnPreferenceChanged; }
    }
}
