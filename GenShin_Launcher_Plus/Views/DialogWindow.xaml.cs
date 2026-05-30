using System.Windows;

namespace GenShin_Launcher_Plus.Views
{
    public partial class DialogWindow : Window
    {
        public bool Result { get; private set; }

        public DialogWindow(string message, string title, string icon, string primaryText, string? secondaryText)
        {
            InitializeComponent();

            MessageText.Text = message;
            TitleText.Text = title;
            IconText.Text = icon;
            PrimaryBtn.Content = primaryText;

            if (!string.IsNullOrEmpty(secondaryText))
            {
                SecondaryBtn.Content = secondaryText;
                SecondaryBtn.Visibility = Visibility.Visible;
            }
        }

        private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void SecondaryBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}