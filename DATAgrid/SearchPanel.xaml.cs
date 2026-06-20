using BIMData.DBservices;
using System;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace BIMData
{
    public partial class SearchPanel : UserControl
    {
        private ProjectService _projectService = null!;

        public SearchPanel()
        {
            InitializeComponent();
        }

        // NOWA METODA
        public void Initialize(ProjectService projectService)
        {
            _projectService = projectService;
        }

        // Metoda wywoływana z poziomu ExplorerPanel po załadowaniu projektu lub CSV
        public void OdswiezDane(DataTable dane)
        {
            Dispatcher.Invoke(() =>
            {
                MainDataGrid.ItemsSource = null;
                MainDataGrid.Columns.Clear();

                if (dane == null || dane.Rows.Count == 0) return; // ← czyści grid i wychodzi

                MainDataGrid.AutoGenerateColumns = true;
                MainDataGrid.ItemsSource = dane.DefaultView;
                MainDataGrid.Items.Refresh();
            });
        }
    }
}