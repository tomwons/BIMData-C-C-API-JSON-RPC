using System.Windows;
using System.Windows.Input;
using BIMData.DBservices;
using System.Collections.Generic;

namespace BIMData
{
    public partial class ProjektyWindow : Window
    {
        private readonly ProjectService _service;

        // Konstruktor przyjmuje teraz istniejący serwis z ExplorerPanel
        public ProjektyWindow(ProjectService sharedService)
        {
            InitializeComponent();
            _service = sharedService;

            OdswiezListe();
        }

        private void OdswiezListe()
        {
            // Zmiana: wywołujemy na instancji '_service', a nie statycznie
            ProjektyListBox.ItemsSource = _service.PobierzProjekty();
        }

        // Wspólna metoda dla przycisku i Double-Click
        private void ZatwierdzWybor()
        {
            if (ProjektyListBox.SelectedItem == null) return;

            dynamic wybrany = ProjektyListBox.SelectedItem;

            // Wszystko zapisujemy bezpośrednio w serwisie, do którego dostęp mają oba okna!
            _service.WybranyProjektId = wybrany.Id;
            _service.WybranaNazwaProjektu = wybrany.Nazwa;

           

            this.DialogResult = true;
            this.Close();
        }

        // 1. Opcja: Kliknięcie przycisku "Wybierz"
        private void OnWybierzProjekt(object sender, RoutedEventArgs e)
        {
            ZatwierdzWybor();
        }
        private void OnWyszukajProjekt(object sender, RoutedEventArgs e)
        {
            // Wyświetlamy okienko do wpisania szukanej frazy
            string fraza = Microsoft.VisualBasic.Interaction.InputBox(
                "Wpisz nazwę lub fragment nazwy projektu, który chcesz zaznaczyć:",
                "Znajdź i zaznacz projekt",
                ""
            );

            // Jeśli użytkownik kliknął "Anuluj" lub nic nie wpisał, nic nie robimy
            if (string.IsNullOrWhiteSpace(fraza)) return;

            fraza = fraza.Trim().ToLower();
            bool znaleziono = false;

            // Przeszukujemy wszystkie elementy aktualnie załadowane do ListBoxa
            foreach (dynamic projekt in ProjektyListBox.Items)
            {
                string nazwaProjektu = (projekt.Nazwa ?? "").ToString().ToLower();

                // Sprawdzamy, czy nazwa zawiera wpisaną frazę
                if (nazwaProjektu.Contains(fraza))
                {
                    // Zaznaczamy element w interfejsie
                    ProjektyListBox.SelectedItem = projekt;

                    // Przewijamy listę automatycznie do zaznaczonego elementu, żeby użytkownik go widział
                    ProjektyListBox.ScrollIntoView(projekt);

                    znaleziono = true;
                    break; // Przerywamy pętlę po znalezieniu pierwszego pasującego projektu
                }
            }

            // Jeśli nic nie pasuje, informujemy użytkownika bez czyszczenia listy
            if (!znaleziono)
            {
                MessageBox.Show($"Nie znaleziono projektu zawierającego frazę: \"{fraza}\"",
                    "Brak wyników", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // 2. Opcja: Podwójne kliknięcie na ListBox
        private void OnListBoxDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ZatwierdzWybor();
        }

        private void OnDodajProjekt(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_service.ConnectionString)) return;

            string nazwaProjektu = Microsoft.VisualBasic.Interaction.InputBox(
                "Podaj nazwę nowego projektu:",
                "Dodaj projekt",
                "Nowy Projekt"
            );

            if (!string.IsNullOrWhiteSpace(nazwaProjektu))
            {
                _service.DodajProjekt(nazwaProjektu.Trim());
                OdswiezListe();
            }
        }
        public event Action? OnProjektUsuniety;
        private void OnUsunProjekt(object sender, RoutedEventArgs e)
        {
            if (ProjektyListBox.SelectedItem == null) return;

            dynamic wybrany = ProjektyListBox.SelectedItem;
            _service.UsunProjekt(wybrany.Id);
            OdswiezListe();

            // Powiadom ExplorerPanel że coś się zmieniło
            OnProjektUsuniety?.Invoke();
        }

        private void OnZmienNazwe(object sender, RoutedEventArgs e)
        {
            if (ProjektyListBox.SelectedItem == null) return;

            dynamic wybrany = ProjektyListBox.SelectedItem;

            string nowaNazwa = Microsoft.VisualBasic.Interaction.InputBox(
                $"Zmień nazwę dla: {wybrany.Nazwa}",
                "Zmień nazwę projektu",
                wybrany.Nazwa
            );

            if (string.IsNullOrWhiteSpace(nowaNazwa) || nowaNazwa.Trim() == wybrany.Nazwa) return;

            _service.EdytujNazweProjektu(wybrany.Id, nowaNazwa.Trim());
            OdswiezListe();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Resetujemy tylko jeśli lista jest pusta — brak projektów w bazie
            if (ProjektyListBox.Items.Count == 0)
            {
                _service.WybranyProjektId = null;
                _service.WybranaNazwaProjektu = null;
            }
            // Jeśli użytkownik zamknął bez wyboru — zostaje ostatni zaznaczony (nic nie robimy)
        }
    }
}