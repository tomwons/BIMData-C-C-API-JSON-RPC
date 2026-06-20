using System;
using System.Threading.Tasks; // DODANO: Wymagane dla Task
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BIMData.DBservices;

namespace BIMData
{
    public partial class MainWindow : Window
    {
        private readonly ProjectService _projectService = new(string.Empty);
        private readonly AiService _aiService;

        public MainWindow()
        {
            InitializeComponent();
            ExcelPanelContent.Initialize(_projectService);
            ExplorerContent.Initialize(_projectService);
            SearchContent.Initialize(_projectService);
            ExplorerContent.Visibility = Visibility.Visible;

            _aiService = new AiService(_projectService);

            // Rejestracja zdarzenia PreviewKeyDown dla pola tekstowego
            ChatInputTextBox.PreviewKeyDown += ChatInputTextBox_PreviewKeyDown;
        }

        private void ChatInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Shift + Enter pozwala na przejście do nowej linii
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    return;
                }

                // Blokujemy domyślne zachowanie Enter
                e.Handled = true;

                // Wywołujemy wysyłanie wiadomości
                ExecuteSendMessage();
            }
        }

        private async void OnSendButtonClick(object sender, RoutedEventArgs e)
        {
            await ExecuteSendMessageAsync();
        }

        private async Task ExecuteSendMessageAsync()
        {
            string userText = ChatInputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(userText)) return;

            AppendMessage(userText, isUser: true);
            ChatInputTextBox.Text = string.Empty;

            try
            {
                string aiResponse = await _aiService.SendMessageAsync(userText);
                AppendMessage(aiResponse, isUser: false);
            }
            catch (Exception ex)
            {
                AppendMessage($"Błąd AI: {ex.Message}", isUser: false);
            }
        }

        // Metoda typu "fire and forget" do uruchomienia async z KeyDown
        private void ExecuteSendMessage()
        {
            _ = ExecuteSendMessageAsync();
        }

        // ZMIANA: Dostosowanie metody do nowych stylów TextBox (umożliwiających kopiowanie)
        private void AppendMessage(string text, bool isUser)
        {
            // Tworzymy bezpośrednio TextBox, bo styl w XAML obsługuje teraz TargetType="TextBox"
            TextBox messageBox = new()
            {
                Text = text,
                Style = (Style)FindResource(isUser ? "UserMessageStyle" : "AgentMessageStyle")
            };

            ChatHistoryStack.Children.Add(messageBox);

            // ZMIANA: Pewniejsze wyszukiwanie ScrollViewer w hierarchii WPF
            DependencyObject parent = VisualTreeHelper.GetParent(ChatHistoryStack);
            while (parent != null && parent is not ScrollViewer)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is ScrollViewer scrollViewer)
            {
                // Wywołanie ScrollToBottom() po aktualizacji układu (Layoutu), 
                // aby uwzględnić wysokość nowo dodanej wiadomości
                Dispatcher.BeginInvoke(new Action(() => scrollViewer.ScrollToBottom()));
            }
        }

        private void OnSidebarButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Tag?.ToString() ?? "";

            ExplorerContent.Visibility = Visibility.Collapsed;
            SearchContent.Visibility = Visibility.Collapsed;
            ExcelPanelContent.Visibility = Visibility.Collapsed;

            if (tag == "Tools") ExcelPanelContent.Visibility = Visibility.Visible;
            if (tag == "Explorer") ExplorerContent.Visibility = Visibility.Visible;
            if (tag == "Search") SearchContent.Visibility = Visibility.Visible;
        }
    }
}