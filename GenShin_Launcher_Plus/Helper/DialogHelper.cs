using System;
using System.Windows;
using GenShin_Launcher_Plus.Views;

namespace GenShin_Launcher_Plus.Helper
{
    public static class DialogHelper
    {
        // Segoe Fluent Icons code points
        private const string IconInfo = "\uE946";
        private const string IconWarning = "\uE7BA";
        private const string IconError = "\uEA39";
        private const string IconQuestion = "\uE9CE";

        /// <summary>
        /// Show an info dialog with OK button.
        /// </summary>
        public static void ShowInfo(string message, string title = "")
        {
            ShowCore(message, string.IsNullOrEmpty(title) ? App.Current.Language?.TipsStr ?? "Info" : title,
                     IconInfo, App.Current.Language?.Determine ?? "OK", null);
        }

        /// <summary>
        /// Show a warning dialog with OK button.
        /// </summary>
        public static void ShowWarning(string message, string title = "")
        {
            ShowCore(message, string.IsNullOrEmpty(title) ? App.Current.Language?.TipsStr ?? "Warning" : title,
                     IconWarning, App.Current.Language?.Determine ?? "OK", null);
        }

        /// <summary>
        /// Show an error dialog with OK button.
        /// </summary>
        public static void ShowError(string message, string title = "")
        {
            ShowCore(message, string.IsNullOrEmpty(title) ? App.Current.Language?.Error ?? "Error" : title,
                     IconError, App.Current.Language?.Determine ?? "OK", null);
        }

        /// <summary>
        /// Show a Yes/No confirmation dialog. Returns true for Yes.
        /// </summary>
        public static bool ShowYesNo(string message, string title = "")
        {
            return ShowCore(message,
                            string.IsNullOrEmpty(title) ? App.Current.Language?.TipsStr ?? "Confirm" : title,
                            IconQuestion,
                            App.Current.Language?.Determine ?? "Yes",
                            App.Current.Language?.Cancel ?? "No");
        }

        private static bool ShowCore(string message, string title, string icon, string primaryText, string? secondaryText)
        {
            var dialog = new DialogWindow(message, title, icon, primaryText, secondaryText);

            // Try to set owner to the main window
            try
            {
                if (Application.Current?.MainWindow?.IsLoaded == true)
                    dialog.Owner = Application.Current.MainWindow;
            }
            catch { /* ignore if owner can't be set */ }

            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}