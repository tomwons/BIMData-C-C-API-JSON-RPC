using System;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using unvell.ReoGrid;
using unvell.ReoGrid.DataFormat; // Wymagane do formatowania miejsc po przecinku
using BIMData.DBservices;

namespace BIMData
{
    public partial class ExcelPanel : UserControl
    {
        private ProjectService _projectService = null!;

        public ExcelPanel()
        {
            InitializeComponent();
            
            InicjalizujInteraktywnosc();
        }

        public void Initialize(ProjectService projectService)
        {
            _projectService = projectService;
        }

        private void InicjalizujInteraktywnosc()
        {
            MySpreadsheet.Focusable = true;

            // Odpinamy żeby uniknąć duplikatów
            MySpreadsheet.KeyDown -= MySpreadsheet_KeyDown;
            MySpreadsheet.PreviewMouseLeftButtonDown -= MySpreadsheet_PreviewMouseLeftButtonDown;
            MySpreadsheet.PreviewMouseRightButtonUp -= MySpreadsheet_PreviewMouseRightButtonUp;

            MySpreadsheet.KeyDown += MySpreadsheet_KeyDown;
            MySpreadsheet.PreviewMouseLeftButtonDown += MySpreadsheet_PreviewMouseLeftButtonDown;
            MySpreadsheet.PreviewMouseRightButtonUp += MySpreadsheet_PreviewMouseRightButtonUp;

            // ✅ NOWE: Podpinamy dla każdego nowo utworzonego arkusza
            MySpreadsheet.WorksheetCreated -= MySpreadsheet_WorksheetCreated;
            MySpreadsheet.WorksheetCreated += MySpreadsheet_WorksheetCreated;

            // Podepnij dla bieżącego arkusza
            PodepnijZdarzeniaArkusza(MySpreadsheet.CurrentWorksheet);
        }
        // ✅ NOWE: Gdy użytkownik doda nowy arkusz, od razu go inicjalizujemy



        private void MySpreadsheet_WorksheetCreated(object sender, unvell.ReoGrid.Events.WorksheetCreatedEventArgs e)
        {
            PodepnijZdarzeniaArkusza(e.Worksheet);
        }

        // ✅ NOWE: Wydzielona metoda podpinająca CellMouseDown dla konkretnego arkusza
        private void PodepnijZdarzeniaArkusza(Worksheet sheet)
        {
            sheet.CellMouseDown += (s, e) =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!MySpreadsheet.IsFocused)
                    {
                        MySpreadsheet.Focus();
                        Keyboard.Focus(MySpreadsheet);
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            };
        }


        private void MySpreadsheet_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var sheet = MySpreadsheet.CurrentWorksheet;

                if (!sheet.IsEditing)
                {
                    sheet.StartEdit();
                    e.Handled = true;
                }
            }
        }

        // Zarządzanie menu kontekstowym dla kolumn, wierszy oraz dowolnie zaznaczonych pól
        private void MySpreadsheet_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var sheet = MySpreadsheet.CurrentWorksheet;
            var selection = sheet.SelectionRange;
            var workbook = sheet.Workbook;

            var wpfContextMenu = new ContextMenu();

            // --- SEKCJA SCHOWKA (Ostateczna próba) ---
            var menuKopiuj = new MenuItem { Header = "Kopiuj", Icon = "📋" };
            menuKopiuj.Click += (s, args) =>
            {
                // Wywołanie wbudowanej metody kopiowania zaznaczenia
                // Jeśli nie widzisz Clipboard w ReoGrid, użyj tego:
                sheet.Copy();
            };
            wpfContextMenu.Items.Add(menuKopiuj);

            var menuWklej = new MenuItem { Header = "Wklej", Icon = "📥" };
            menuWklej.Click += (s, args) =>
            {
                // Wywołanie wbudowanej metody wklejania do zaznaczenia
                sheet.Paste();
            };
            wpfContextMenu.Items.Add(menuWklej);
            wpfContextMenu.Items.Add(new Separator());

            // --- SCENARIUSZ A: Kliknięto nagłówek KOLUMNY ---
            if (selection.Rows >= sheet.RowCount && selection.Cols < sheet.ColumnCount)
            {
                var menuitemWstaw = new MenuItem { Header = "Wstaw kolumnę", Icon = "➕" };
                menuitemWstaw.Click += (s, args) => sheet.InsertColumns(selection.Col, selection.Cols);

                var menuitemUsun = new MenuItem { Header = "Usuń kolumnę", Icon = "❌" };
                menuitemUsun.Click += (s, args) => sheet.DeleteColumns(selection.Col, selection.Cols);

                wpfContextMenu.Items.Add(menuitemWstaw);
                wpfContextMenu.Items.Add(menuitemUsun);
                wpfContextMenu.Items.Add(new Separator());
            }
            // --- SCENARIUSZ B: Kliknięto nagłówek WIERSZA ---
            else if (selection.Cols >= sheet.ColumnCount && selection.Rows < sheet.RowCount)
            {
                var menuitemWstawRow = new MenuItem { Header = "Wstaw wiersz", Icon = "➕" };
                menuitemWstawRow.Click += (s, args) => sheet.InsertRows(selection.Row, selection.Rows);

                var menuitemUsunRow = new MenuItem { Header = "Usuń wiersz", Icon = "❌" };
                menuitemUsunRow.Click += (s, args) => sheet.DeleteRows(selection.Row, selection.Rows);

                wpfContextMenu.Items.Add(menuitemWstawRow);
                wpfContextMenu.Items.Add(menuitemUsunRow);
                wpfContextMenu.Items.Add(new Separator());
            }

            // --- ZARZĄDZANIE ARKUSZAMI ---
            var menuZmienNazwe = new MenuItem { Header = "Zmień nazwę arkusza", Icon = "✏️" };
            menuZmienNazwe.Click += (s, args) => {
                Window nameDialog = new Window { Title = "Nowa nazwa", Width = 250, Height = 130, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                StackPanel sp = new StackPanel { Margin = new Thickness(10) };
                TextBox txt = new TextBox { Text = sheet.Name };
                Button btn = new Button { Content = "Zatwierdź", Margin = new Thickness(0, 10, 0, 0) };
                btn.Click += (s1, e1) => { sheet.Name = txt.Text; nameDialog.Close(); };
                sp.Children.Add(new TextBlock { Text = "Wpisz nazwę:" });
                sp.Children.Add(txt);
                sp.Children.Add(btn);
                nameDialog.Content = sp;
                nameDialog.ShowDialog();
            };
            wpfContextMenu.Items.Add(menuZmienNazwe);

            if (workbook.Worksheets.Count > 1)
            {
                var menuUsunArkusz = new MenuItem { Header = "Usuń arkusz", Icon = "🗑️" };
                menuUsunArkusz.Click += (s, args) => {
                    if (MessageBox.Show($"Czy na pewno usunąć arkusz '{sheet.Name}'?", "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        workbook.Worksheets.Remove(sheet);
                    }
                };
                wpfContextMenu.Items.Add(menuUsunArkusz);
            }

            wpfContextMenu.Items.Add(new Separator());

            // --- FORMATOWANIE LICZB ---
            var menuitemFormatujLiczbe = new MenuItem { Header = "Miejsca po przecinku...", Icon = "🔢" };
            menuitemFormatujLiczbe.Click += (s, args) => PokazOknoMiejscPoPrzecinku(sheet, selection);
            wpfContextMenu.Items.Add(menuitemFormatujLiczbe);

            MySpreadsheet.ContextMenu = wpfContextMenu;
            wpfContextMenu.IsOpen = true;
            e.Handled = true;
        }
        private void PokazOknoMiejscPoPrzecinku(Worksheet sheet, RangePosition range)
        {
            var cell = sheet.Cells[range.Row, range.Col];
            short domyslneMiejsca = 2;

            // Próbujemy odczytać aktualny format, aby podpowiedzieć użytkownikowi jego obecny stan
            if (cell.DataFormat == CellDataFormatFlag.Number && cell.DataFormatArgs is NumberDataFormatter.NumberFormatArgs staryFormat)
            {
                domyslneMiejsca = staryFormat.DecimalPlaces;
            }

            // Konstruujemy dynamiczne okienko modalne w kodzie C#
            Window dialog = new Window
            {
                Title = "Formatowanie liczb",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            Grid grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Label label = new Label { Content = "Podaj liczbę miejsc po przecinku (0-30):", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(label, 0);

            TextBox textBox = new TextBox
            {
                Text = domyslneMiejsca.ToString(),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5, 2, 5, 2)
            };
            Grid.SetRow(textBox, 1);

            Button btnOk = new Button { Content = "Zatwierdź", Width = 80, Height = 25, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(btnOk, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(btnOk);
            dialog.Content = grid;

            btnOk.Click += (s, e) =>
            {
                if (short.TryParse(textBox.Text, out short noweMiejsca) && noweMiejsca >= 0 && noweMiejsca <= 30)
                {
                    var formatArgs = new NumberDataFormatter.NumberFormatArgs
                    {
                        DecimalPlaces = noweMiejsca,
                        UseSeparator = true // Ładny separator tysięcy, np. 1 000,00
                    };

                    // 1. Nakładamy format liczbowy na wybrany zakres pól
                    sheet.SetRangeDataFormat(range, CellDataFormatFlag.Number, formatArgs);

                    // 2. ROZWIĄZANIE DYNAMICZNE: Przepisujemy wartości w zaznaczonym obszarze "w miejscu",
                    // aby zmusić wewnętrzny cache ReoGrid do natychmiastowego przerysowania ekranu.
                    for (int r = range.Row; r <= range.EndRow; r++)
                    {
                        for (int c = range.Col; c <= range.EndCol; c++)
                        {
                            var biezacaWartosc = sheet[r, c];
                            if (biezacaWartosc != null)
                            {
                                // Ponowne przypisanie tej samej wartości natychmiast aktualizuje widok formatu
                                sheet[r, c] = biezacaWartosc;
                            }
                        }
                    }

                    // 3. Przeliczamy i odświeżamy kontrolkę WPF
                    sheet.Recalculate();
                    MySpreadsheet.InvalidateVisual();

                    dialog.DialogResult = true;
                    dialog.Close();
                }
                else
                {
                    MessageBox.Show("Wprowadź poprawną liczbę całkowitą z zakresu od 0 do 30.", "Błędna wartość", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            dialog.ShowDialog();
        }
        private void MySpreadsheet_KeyDown(object sender, KeyEventArgs e)
        {
            var sheet = MySpreadsheet.CurrentWorksheet;
            var selection = sheet.SelectionRange;

            if (sheet.IsEditing) return;

            // ✅ Zabezpieczenie przed pustym/nieprawidłowym zakresem
            if (selection.Rows <= 0 || selection.Cols <= 0) return;

            if (e.Key == Key.Delete)
            {
                sheet.Ranges[selection].Data = null;
                sheet.Recalculate();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                var focus = sheet.FocusPos;
                int nastepnyWiersz = focus.Row + 1;
                if (nastepnyWiersz < sheet.RowCount)
                {
                    sheet.SelectionRange = new RangePosition(nastepnyWiersz, focus.Col, 1, 1);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F2)
            {
                if (!sheet.IsEditing)
                {
                    sheet.StartEdit();
                    e.Handled = true;
                }
            }
        }


        private void LoadFromDbButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectService == null)
            {
                MessageBox.Show("Serwis bazy danych nie został zainicjalizowany.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Pobieramy listę projektów
            var listaProjektow = _projectService.WyszukajProjekty("");

            if (listaProjektow == null || listaProjektow.Count == 0)
            {
                MessageBox.Show("Brak projektów w bazie danych.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. Tworzymy okno wyboru projektu
            Window selectWindow = new Window
            {
                Title = "Wybierz projekt",
                Width = 350,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            StackPanel sp = new StackPanel { Margin = new Thickness(15) };

            ComboBox combo = new ComboBox { DisplayMemberPath = "Nazwa", Margin = new Thickness(0, 5, 0, 15) };
            foreach (var p in listaProjektow) combo.Items.Add(p);
            combo.SelectedIndex = 0;

            Button btnOk = new Button { Content = "Wczytaj dane do arkusza", Height = 35, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 0, 128)), Foreground = System.Windows.Media.Brushes.White };

            btnOk.Click += (s, args) =>
            {
                // Pobieramy wybrany obiekt
                var wybrany = (dynamic)combo.SelectedItem;
                _projectService.WybranyProjektId = (int)wybrany.Id;
                _projectService.WybranaNazwaProjektu = (string)wybrany.Nazwa;

                // Wykonujemy wczytanie danych
                WczytajDaneZBazyDoArkusza();

                selectWindow.Close();
            };

            sp.Children.Add(new TextBlock { Text = "Wybierz projekt z listy:" });
            sp.Children.Add(combo);
            sp.Children.Add(btnOk);
            selectWindow.Content = sp;
            selectWindow.ShowDialog();
        }

        // Pomocnicza metoda wykonująca właściwe operacje na arkuszu
        private void WczytajDaneZBazyDoArkusza()
        {
            try
            {
                var sheet = MySpreadsheet.CurrentWorksheet;
                sheet.Reset();
                InicjalizujInteraktywnosc();

                DataTable dt = _projectService.PobierzDaneProjektu(_projectService.WybranyProjektId.Value);

                if (dt.Rows.Count == 0)
                {
                    MessageBox.Show("Wybrany projekt nie posiada danych.");
                    return;
                }

                // Ustawienie rozmiaru arkusza
                sheet.Resize(dt.Rows.Count + 50, dt.Columns.Count + 5);

                // NAGŁÓWKI: Zaczynamy od 0, aby wczytać wszystkie kolumny z bazy
                for (int col = 0; col < dt.Columns.Count; col++)
                {
                    sheet[0, col] = dt.Columns[col].ColumnName;
                }

                var rangeNaglowkow = new RangePosition(0, 0, 1, dt.Columns.Count);
                sheet.SetRangeStyles(rangeNaglowkow, new WorksheetRangeStyle { Bold = true });

                // DANE: Zaczynamy od 0, aby wczytać wszystkie kolumny z bazy
                for (int row = 0; row < dt.Rows.Count; row++)
                {
                    for (int col = 0; col < dt.Columns.Count; col++)
                    {
                        var cellValue = dt.Rows[row][col];
                        if (cellValue != DBNull.Value)
                        {
                            string strVal = cellValue.ToString()!;
                            if (double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numericVal))
                            {
                                sheet[row + 1, col] = numericVal;
                            }
                            else
                            {
                                sheet[row + 1, col] = strVal;
                            }
                        }
                    }
                }

                // Autofit dla wszystkich wczytanych kolumn
                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    sheet.AutoFitColumnWidth(c);
                }

                sheet.Recalculate();
                MessageBox.Show($"Pomyślnie załadowano dane projektu: '{_projectService.WybranaNazwaProjektu}'!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas ładowania:\n{ex.Message}");
            }
        }

        private void LoadExcelButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Pliki Excela (*.xlsx)|*.xlsx|Wszystkie pliki (*.*)|*.*";
            openFileDialog.Title = "Wybierz plik projektu/zestawienia Excel";

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    MySpreadsheet.Load(openFileDialog.FileName);
                    InicjalizujInteraktywnosc();
                    MessageBox.Show("Plik został pomyślnie załadowany!", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Błąd podczas wczytywania: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveExcelButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Pliki Excela (*.xlsx)|*.xlsx";
            saveFileDialog.DefaultExt = "xlsx";
            saveFileDialog.FileName = "Eksport_BIM_Data";
            saveFileDialog.Title = "Zapisz arkusz jako plik Excel";

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    MySpreadsheet.Save(saveFileDialog.FileName);
                    MessageBox.Show("Plik został pomyślnie zapisany!", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Błąd podczas zapisywania: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var sheet = MySpreadsheet.CurrentWorksheet;
            var selection = sheet.SelectionRange;

            if (selection.Rows < 2)
            {
                MessageBox.Show("Zaznacz obszar z danymi (co najmniej 2 wiersze).");
                return;
            }

            // 1. Pobieramy dane z zaznaczonego zakresu do DataTable
            DataTable dt = new DataTable();
            for (int c = 0; c < selection.Cols; c++) dt.Columns.Add("Col" + c);

            for (int r = 0; r < selection.Rows; r++)
            {
                DataRow dr = dt.NewRow();
                for (int c = 0; c < selection.Cols; c++)
                {
                    dr[c] = sheet[selection.Row + r, selection.Col + c];
                }
                dt.Rows.Add(dr);
            }

            // 2. Proste okno wyboru kolumny
            Window sortDialog = new Window { Title = "Sortowanie", Width = 250, Height = 140, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            StackPanel sp = new StackPanel { Margin = new Thickness(10) };
            ComboBox combo = new ComboBox();
            for (int i = 0; i < selection.Cols; i++) combo.Items.Add($"Kolumna {i + 1}");
            combo.SelectedIndex = 0;

            Button btnSort = new Button { Content = "Sortuj", Margin = new Thickness(0, 10, 0, 0) };
            btnSort.Click += (s, args) =>
            {
                // 3. Sortowanie DataTable
                string colName = "Col" + combo.SelectedIndex;
                dt.DefaultView.Sort = colName + " ASC";
                DataTable sortedDt = dt.DefaultView.ToTable();

                // 4. Wrzucamy posortowane dane z powrotem do arkusza
                for (int r = 0; r < sortedDt.Rows.Count; r++)
                {
                    for (int c = 0; c < sortedDt.Columns.Count; c++)
                    {
                        sheet[selection.Row + r, selection.Col + c] = sortedDt.Rows[r][c];
                    }
                }
                sheet.Recalculate();
                sortDialog.Close();
            };

            sp.Children.Add(new TextBlock { Text = "Wybierz kolumnę do sortowania:" });
            sp.Children.Add(combo);
            sp.Children.Add(btnSort);
            sortDialog.Content = sp;
            sortDialog.ShowDialog();
        }
        private void QuickSumButton_Click(object sender, RoutedEventArgs e)
        {
            var sheet = MySpreadsheet.CurrentWorksheet;
            var selection = sheet.SelectionRange;

            if (selection.Rows <= 0 || selection.Cols <= 0)
            {
                MessageBox.Show("Najpierw zaznacz zakres liczb, które chcesz zsumować.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                int wierszPodZaznaczeniem = selection.Row + selection.Rows;
                int kolumnaZaznaczenia = selection.Col;

                string adresObszaru = selection.ToString();

                sheet.SetCellFormula(wierszPodZaznaczeniem, kolumnaZaznaczenia, $"SUM({adresObszaru})");
                sheet.Recalculate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się włożyć formuły sumującej: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}