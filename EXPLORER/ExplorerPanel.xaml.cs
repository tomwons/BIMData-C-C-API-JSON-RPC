using BIMData.DBservices;
using Microsoft.Win32;
using System;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIMData
{
    public partial class ExplorerPanel : UserControl
    {

        private ProjectService _projectService = null!;
        
        public ExplorerPanel()
        {
            InitializeComponent();
            
        }
        public void Initialize(ProjectService projectService)
        {
            _projectService = projectService;
            InitializeDatabaseContext(); // dopiero teraz, gdy serwis jest gotowy
        }

        /// <summary>
        /// Sprawdza zapisaną konfigurację i przywraca stan poprzedniej bazy danych.
        /// </summary>
        private void InitializeDatabaseContext()
        {
            try
            {
                string lastPath = SettingsManager.GetLastDatabasePath();

                // Sprawdzamy czy ścieżka istnieje ORAZ czy plik fizycznie jest na dysku
                if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
                {
                    _projectService.WczytajBaze(lastPath);
                    BazaInterface(lastPath);
                }
                else
                {
                    // Bezpieczny stan początkowy, jeśli aplikacja jest uruchamiana pierwszy raz
                    AktualnaBazaLabel.Text = "Brak aktywnej bazy";
                    AktualnaBazaBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4E2E2")); // Delikatny czerwony
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas inicjalizacji bazy: {ex.Message}", "Błąd startu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── FRONTEND (Zwijanie / Rozwijanie sekcji) ───────────────────────────────

        private void ToggleBazyDanych(object sender, RoutedEventArgs e)
        {
            bool v = BazyDanychContent.Visibility == Visibility.Visible;
            BazyDanychContent.Visibility = v ? Visibility.Collapsed : Visibility.Visible;
            BazyDanychArrow.Text = v ? "▶" : "▼";
        }

        private void ToggleProjekt(object sender, RoutedEventArgs e)
        {
            bool v = ProjektContent.Visibility == Visibility.Visible;
            ProjektContent.Visibility = v ? Visibility.Collapsed : Visibility.Visible;
            ProjektArrow.Text = v ? "▶" : "▼";
        }

        private void ToggleWczytajDane(object sender, RoutedEventArgs e)
        {
            bool v = WczytajDaneContent.Visibility == Visibility.Visible;
            WczytajDaneContent.Visibility = v ? Visibility.Collapsed : Visibility.Visible;
            WczytajDaneArrow.Text = v ? "▶" : "▼";
        }

        // ── BAZA DANYCH ──────────────────────────────────────────────────────────

        private void BazaInterface(string sciezka)
        {
            string nazwaPliku = Path.GetFileName(sciezka);
            AktualnaBazaLabel.Text = nazwaPliku;
            AktualnaBazaBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2F4E2")); // Delikatny zielony
        }

        private void OnUtworzBaze(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new() { Filter = "Baza SQLite (*.db)|*.db" };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    // 1. Inicjalizacja fizycznego pliku i struktur tabel
                    ProjectService.UtworzNowaBaze(sfd.FileName);

                    // 2. Aktywacja nowo utworzonej bazy w serwisie i zapis w konfiguracji
                    _projectService.WczytajBaze(sfd.FileName);
                    SettingsManager.SaveLastDatabasePath(sfd.FileName);

                    // 3. Aktualizacja UI
                    BazaInterface(sfd.FileName);

                    MessageBox.Show("Nowa baza została utworzona i ustawiona jako aktywna.", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Błąd podczas tworzenia bazy: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnWczytajBaze(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new()
            {
                DefaultExt = ".db",
                Filter = "Pliki bazy danych (*.db)|*.db|Wszystkie pliki (*.*)|*.*",
                Title = "Wybierz istniejącą bazę danych SQLite"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    // 1. Aktualizacja połączenia w serwisie i konfiguracji użytkownika
                    _projectService.WczytajBaze(dlg.FileName);
                    SettingsManager.SaveLastDatabasePath(dlg.FileName);

                    // 2. Aktualizacja UI
                    BazaInterface(dlg.FileName);

                    MessageBox.Show($"Pomyślnie wczytano bazę: {Path.GetFileName(dlg.FileName)}", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Błąd podczas wczytywania bazy: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ── PROJEKT ──────────────────────────────────────────────────────────────

        private void OnWczytajProjekt(object sender, RoutedEventArgs e)
        {
            // Walidacja połączenia przed otwarciem okna projektów
            if (string.IsNullOrEmpty(_projectService.ConnectionString) || _projectService.ConnectionString == "Data Source=")
            {
                MessageBox.Show("Musisz najpierw utworzyć lub wczytać bazę danych!", "Brak aktywnej bazy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Przekazujemy ten sam, współdzielony serwis do okna modalnego
            ProjektyWindow okno = new(_projectService) { Owner = Window.GetWindow(this) };

            // ↓ TUTAJ — przed ShowDialog()
            okno.OnProjektUsuniety += () =>
            {
                var oknoGlowne = Window.GetWindow(this);
                if (oknoGlowne?.FindName("SearchContent") is SearchPanel searchPanel)
                    searchPanel.OdswiezDane(new System.Data.DataTable());
            };


            if (okno.ShowDialog() == true)
            {
                AktualnyProjektLabel.Text = $"Projekt: {_projectService.WybranaNazwaProjektu}";
                AktualnyProjektBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2F4E2"));

                if (_projectService.WybranyProjektId.HasValue)
                {
                    try
                    {
                        // Pobieramy spivotowane dane strukturalne z bazy danych
                        System.Data.DataTable dt = _projectService.PobierzDaneProjektu(_projectService.WybranyProjektId.Value);

                        // Bezpieczne poszukiwanie kontrolki SearchPanel w oknie głównym i odświeżenie danych
                        var oknoGlowne = Window.GetWindow(this);
                        if (oknoGlowne != null && oknoGlowne.FindName("SearchContent") is SearchPanel searchPanel)
                        {
                            searchPanel.OdswiezDane(dt);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Błąd podczas ładowania danych projektu: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                // Resetujemy UI tylko jeśli faktycznie nie ma wybranego projektu (lista była pusta)
                if (!_projectService.WybranyProjektId.HasValue)
                {
                    AktualnyProjektLabel.Text = "Brak aktywnego projektu";
                    AktualnyProjektBorder.Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#F4E2E2"));
                }
                // Jeśli WybranyProjektId ma wartość — użytkownik tylko zamknął okno bez zmiany wyboru,
                // zostawiamy poprzedni stan UI bez zmian
            }
        }

        // ── DANE ──────────────────────────────────────────────────────────────────

        private async void OnWczytajCsv(object sender, RoutedEventArgs e)
        {
            // 1. Walidacja kontekstu wybranego projektu
            if (!_projectService.WybranyProjektId.HasValue)
            {
                MessageBox.Show("Musisz najpierw wybrać projekt z listy!", "Nie wybrano projektu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Pobranie ścieżki pliku źródłowego CSV
            OpenFileDialog ofd = new() { Filter = "Pliki CSV (*.csv)|*.csv" };
            if (ofd.ShowDialog() != true) return;

            try
            {
                // Zmiana kursora na czas przetwarzania (aplikacja nie zamarza dzięki async/await)
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                // 3. Wywołanie zoptymalizowanego importu wielowierszowego w serwisie
                System.Data.DataTable dt = await _projectService.WczytajCsvDoProjektu(_projectService.WybranyProjektId.Value, ofd.FileName);

                // 4. Aktualizacja widoku w SearchPanelu
                var oknoGlowne = Window.GetWindow(this);
                if (oknoGlowne != null && oknoGlowne.FindName("SearchContent") is SearchPanel searchPanel)
                {
                    searchPanel.OdswiezDane(dt);
                }

                MessageBox.Show("Dane z pliku CSV zostały pomyślnie zaimportowane i przetworzone!", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd importu pliku CSV: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Przywrócenie standardowego kursora myszy niezależnie od wyniku operacji
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        private void OnPolaczRevit(object sender, RoutedEventArgs e) { /* Implementacja w przyszłości */ }
        private async void OnPolaczArchicad(object sender, RoutedEventArgs e)
        {
            if (!_projectService.WybranyProjektId.HasValue)
            {
                MessageBox.Show("Musisz najpierw wybrać projekt!", "Brak aktywnego projektu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                string json = await ArchicadConnectionService.FetchElementsAsJsonAsync();

                if (string.IsNullOrWhiteSpace(json))
                {
                    MessageBox.Show("Brak danych pobranych z ArchiCAD.", "BIMData",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DataTable dt = await _projectService.WczytajJsonArchicadDoProjektu(
                    _projectService.WybranyProjektId.Value, json);

                var oknoGlowne = Window.GetWindow(this);
                if (oknoGlowne?.FindName("SearchContent") is SearchPanel searchPanel)
                    searchPanel.OdswiezDane(dt);

                MessageBox.Show("Pomyślnie pobrano dane z ArchiCAD.", "BIMData",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas pobierania danych z ArchiCAD:\n{ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }
    }
}