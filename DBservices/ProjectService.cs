using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;

namespace BIMData.DBservices
{
    public class ProjectService(string initialConnectionString)
    {
        // initialConnectionString jest użyte bezpośrednio przy inicjalizacji właściwości:
        public string ConnectionString { get; private set; } = $"Data Source={initialConnectionString}";
        public int? WybranyProjektId { get; set; }
        public string? WybranaNazwaProjektu { get; set; }
        public static void UtworzNowaBaze(string sciezka)
        {
            if (string.IsNullOrWhiteSpace(sciezka))
                throw new ArgumentException("Ścieżka do bazy nie może być pusta.");

            Database.Initialize($"Data Source={sciezka}");
        }

        public void WczytajBaze(string sciezka)
        {
            if (!File.Exists(sciezka))
                throw new FileNotFoundException("Plik bazy danych nie istnieje.");

            ConnectionString = $"Data Source={sciezka}";
            WybranyProjektId = null;
            WybranaNazwaProjektu = null;
        }

        public List<dynamic> WyszukajProjekty(string fraza)
        {
            var projekty = new List<dynamic>();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();

            // Używamy %fraza%, aby znaleźć tekst w dowolnym miejscu nazwy projektu
            cmd.CommandText = "SELECT Id, Nazwa FROM Projekty WHERE Nazwa LIKE @fraza";
            cmd.Parameters.AddWithValue("@fraza", $"%{fraza}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dynamic projekt = new ExpandoObject();
                projekt.Id = reader.GetInt32(0);
                projekt.Nazwa = reader.GetString(1);
                projekty.Add(projekt);
            }
            return projekty;
        }
        // ZMIANA: Usunąłem 'static'. Teraz metoda korzysta z aktualnego ConnectionString serwisu!
        public List<dynamic> PobierzProjekty()
        {
            var projekty = new List<dynamic>();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Nazwa FROM Projekty";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dynamic projekt = new ExpandoObject();
                projekt.Id = reader.GetInt32(0);
                projekt.Nazwa = reader.GetString(1);
                projekty.Add(projekt);
            }
            return projekty;
        }


        public DataTable PobierzDaneProjektu(int projektId)
        {
            var dataTable = new DataTable();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var cmdKolumny = connection.CreateCommand();
            cmdKolumny.CommandText = "SELECT DISTINCT NazwaKolumny FROM DaneBIM WHERE ProjektId = @pid";
            cmdKolumny.Parameters.AddWithValue("@pid", projektId);

            var kolumny = new List<string>();
            using (var reader = cmdKolumny.ExecuteReader())
            {
                while (reader.Read())
                {
                    kolumny.Add(reader.GetString(0));
                }
            }

            if (kolumny.Count == 0) return dataTable;

            dataTable.Columns.Add("WierszId", typeof(int));
            foreach (var kolumna in kolumny)
            {
                dataTable.Columns.Add(kolumna, typeof(string));
            }

            var selectParts = new List<string>();
            foreach (var kolumna in kolumny)
            {
                // Bezpieczne uciekanie apostrofów w tekście i cudzysłowów w aliasie kolumny
                string sqlEscaped = kolumna.Replace("'", "''");
                string aliasEscaped = kolumna.Replace("\"", "\"\"");
                selectParts.Add($"MAX(CASE WHEN NazwaKolumny = '{sqlEscaped}' THEN Wartosc END) AS \"{aliasEscaped}\"");
            }

            string sql = $@"
                SELECT WierszId, {string.Join(", ", selectParts)}
                FROM DaneBIM
                WHERE ProjektId = @pid
                GROUP BY WierszId
                ORDER BY WierszId";

            var cmdDane = connection.CreateCommand();
            cmdDane.CommandText = sql;
            cmdDane.Parameters.AddWithValue("@pid", projektId);

            // ZMIEŃ NA:
            using (var reader = cmdDane.ExecuteReader())
            {
                while (reader.Read())
                {
                    var row = dataTable.NewRow();
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        var val = reader[col.ColumnName];
                        row[col.ColumnName] = val == DBNull.Value ? DBNull.Value : val.ToString()!;
                    }
                    dataTable.Rows.Add(row);
                }
            }
            return dataTable;
        }


        public void DodajProjekt(string nazwa)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Projekty (Nazwa) VALUES (@nazwa)";
            cmd.Parameters.AddWithValue("@nazwa", nazwa);
            cmd.ExecuteNonQuery();
        }

        public void UsunProjekt(int id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Projekty WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            if (WybranyProjektId == id) WybranyProjektId = null;
        }

        public void EdytujNazweProjektu(int id, string nowaNazwa)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Projekty SET Nazwa = @nazwa WHERE Id = @id";
            cmd.Parameters.AddWithValue("@nazwa", nowaNazwa);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        public async Task<DataTable> WczytajCsvDoProjektu(int projektId, string sciezkaPliku)
        {
            var linie = await File.ReadAllLinesAsync(sciezkaPliku);
            if (linie.Length < 2)
                throw new InvalidDataException("Plik CSV jest pusty lub zawiera tylko nagłówki.");

            string pierwszaLinia = linie[0];
            int liczbaPrzecinkow = pierwszaLinia.Count(c => c == ',');
            int liczbaSrednikow = pierwszaLinia.Count(c => c == ';');
            char separator = liczbaSrednikow > liczbaPrzecinkow ? ';' : ',';

            string[] naglowki = pierwszaLinia.Split(separator);

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            // POPRAWA WYDAJNOŚCI: Jedna komenda i parametry zdefiniowane przed pętlą
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"INSERT INTO DaneBIM (ProjektId, WierszId, NazwaKolumny, Wartosc) 
                        VALUES (@pid, @wid, @kol, @val)";

            var pPid = cmd.Parameters.Add("@pid", SqliteType.Integer);
            var pWid = cmd.Parameters.Add("@wid", SqliteType.Integer);
            var pKol = cmd.Parameters.Add("@kol", SqliteType.Text);
            var pVal = cmd.Parameters.Add("@val", SqliteType.Text);

            pPid.Value = projektId;

            for (int wierszId = 1; wierszId < linie.Length; wierszId++)
            {
                if (string.IsNullOrWhiteSpace(linie[wierszId])) continue;

                string[] komorki = linie[wierszId].Split(separator);
                int liczbaKolumn = Math.Min(naglowki.Length, komorki.Length);

                pWid.Value = wierszId;

                for (int i = 0; i < liczbaKolumn; i++)
                {
                    pKol.Value = naglowki[i].Trim();
                    pVal.Value = komorki[i].Trim();
                    cmd.ExecuteNonQuery(); // Błyskawiczne wykonanie na tym samym obiekcie komendy
                }
            }
            transaction.Commit();

            return PobierzDaneProjektu(projektId);
        }

        public async Task<DataTable> WczytajJsonArchicadDoProjektu(int projektId, string json)
        {
            // Parsujemy JSON — obsługujemy zarówno tablicę jak i pojedynczy obiekt
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var elementy = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                    elementy.Add(el);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                elementy.Add(root);
            }

            if (elementy.Count == 0)
                throw new InvalidDataException("JSON nie zawiera żadnych elementów.");

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            // POPRAWA: Poprawny blok using dla transakcji zabezpieczający przed blokowaniem bazy
            using var transaction = connection.BeginTransaction();

            // --- KLUCZOWA POPRAWKA: Czyszczenie starych danych projektu ---
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM DaneBIM WHERE ProjektId = @pid";
                deleteCmd.Parameters.AddWithValue("@pid", projektId);
                deleteCmd.ExecuteNonQuery();
            }
            // -------------------------------------------------------------

            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"INSERT INTO DaneBIM (ProjektId, WierszId, NazwaKolumny, Wartosc) 
                         VALUES (@pid, @wid, @kol, @val)";

            var pPid = cmd.Parameters.Add("@pid", SqliteType.Integer);
            var pWid = cmd.Parameters.Add("@wid", SqliteType.Integer);
            var pKol = cmd.Parameters.Add("@kol", SqliteType.Text);
            var pVal = cmd.Parameters.Add("@val", SqliteType.Text);
            pPid.Value = projektId;

            for (int wierszId = 0; wierszId < elementy.Count; wierszId++)
            {
                pWid.Value = wierszId + 1;
                var element = elementy[wierszId];

                foreach (var prop in element.EnumerateObject())
                {
                    // Obsługa zagnieżdżonych Parameters (format z "Name"/"Value")
                    if (prop.Name == "Parameters" && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var param in prop.Value.EnumerateArray())
                        {
                            if (param.TryGetProperty("Name", out var nazwaEl) &&
                                param.TryGetProperty("Value", out var wartoscEl))
                            {
                                pKol.Value = nazwaEl.GetString() ?? "";
                                pVal.Value = wartoscEl.ValueKind == JsonValueKind.String
                                    ? wartoscEl.GetString() ?? ""
                                    : wartoscEl.GetRawText();
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    else
                    {
                        // Płaskie właściwości: ObjectId, Name, Type lub klucz-wartość
                        pKol.Value = prop.Name;
                        pVal.Value = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? ""
                            : prop.Value.GetRawText();
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            transaction.Commit();

            // Zwracamy całkowicie świeżo zbudowaną tabelę z bazy danych
            return PobierzDaneProjektu(projektId);
        }
        // =========================================================================
        // --- FUNKCJE ANALITYCZNE DLA AI (AGREGACJA DANYCH BIM DIRECTLY IN SQL) ---
        // =========================================================================

        /// <summary>
        /// 1. Zestawienie Okien: Grupuje według szerokości i wysokości, zliczając sztuki.
        /// </summary>
        // Wklej to w ProjectService.cs w miejsce starego PobierzZestawienieOkien
        public List<dynamic> PobierzZestawienieOkien(int projektId)
        {
            var zestawienie = new List<dynamic>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();

            cmd.CommandText = @"
        SELECT 
            Szerokosc,
            Wysokosc,
            COUNT(*) AS IloscSztuk
        FROM (
            SELECT 
                WierszId,
                MAX(CASE WHEN NazwaKolumny = 'Width' THEN Wartosc END) AS Szerokosc,
                MAX(CASE WHEN NazwaKolumny = 'Height' THEN Wartosc END) AS Wysokosc
            FROM DaneBIM
            WHERE ProjektId = @pid
            GROUP BY WierszId
            HAVING MAX(CASE WHEN NazwaKolumny = 'Type' THEN Wartosc END) LIKE '%Okno%' 
                OR MAX(CASE WHEN NazwaKolumny = 'Type' THEN Wartosc END) LIKE '%Window%'
        )
        GROUP BY Szerokosc, Wysokosc
        ORDER BY Szerokosc, Wysokosc;";

            cmd.Parameters.AddWithValue("@pid", projektId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dynamic pozycja = new ExpandoObject();
                pozycja.Szerokosc = reader.IsDBNull(0) ? "Brak" : reader.GetString(0);
                pozycja.Wysokosc = reader.IsDBNull(1) ? "Brak" : reader.GetString(1);
                pozycja.IloscSztuk = reader.GetInt32(2);
                zestawienie.Add(pozycja);
            }
            return zestawienie;
        }

        // Wklej to w ProjectService.cs w miejsce starego PobierzZestawienieDrzwi
        public List<dynamic> PobierzZestawienieDrzwi(int projektId)
        {
            var zestawienie = new List<dynamic>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();

            cmd.CommandText = @"
        SELECT 
            Szerokosc,
            Wysokosc,
            COUNT(*) AS IloscSztuk
        FROM (
            SELECT 
                WierszId,
                MAX(CASE WHEN NazwaKolumny = 'Width' THEN Wartosc END) AS Szerokosc,
                MAX(CASE WHEN NazwaKolumny = 'Height' THEN Wartosc END) AS Wysokosc
            FROM DaneBIM
            WHERE ProjektId = @pid
            GROUP BY WierszId
            HAVING MAX(CASE WHEN NazwaKolumny = 'Type' THEN Wartosc END) LIKE '%Drzwi%' 
                OR MAX(CASE WHEN NazwaKolumny = 'Type' THEN Wartosc END) LIKE '%Door%'
        )
        GROUP BY Szerokosc, Wysokosc
        ORDER BY Szerokosc, Wysokosc;";

            cmd.Parameters.AddWithValue("@pid", projektId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dynamic pozycja = new ExpandoObject();
                pozycja.Szerokosc = reader.IsDBNull(0) ? "Brak" : reader.GetString(0);
                pozycja.Wysokosc = reader.IsDBNull(1) ? "Brak" : reader.GetString(1);
                pozycja.IloscSztuk = reader.GetInt32(2);
                zestawienie.Add(pozycja);
            }
            return zestawienie;
        }

        /// <summary>
        /// 3. Zestawienie Ścian: Sumuje długość oraz oblicza powierzchnię (Mnożenie Height * Length) w podziale na grubość (Thickness).
        /// </summary>
        public List<dynamic> PobierzZestawienieScian(int projektId)
        {
            var zestawienie = new List<dynamic>();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();

            // Podzapytanie (Subquery) najpierw wyciąga wiersze (ściany) i rzutuje tekst na wartości liczbowe (REAL),
            // a główne zapytanie agreguje wyniki (SUM) według grubości ściany.
            cmd.CommandText = @"
                SELECT 
                    Grubosc,
                    ROUND(SUM(Dlugosc), 2) AS CalkowitaDlugosc,
                    ROUND(SUM(Dlugosc * Wysokosc), 2) AS CalkowitaPowierzchnia,
                    COUNT(*) AS IloscScian
                FROM (
                    SELECT 
                        WierszId,
                        CAST(MAX(CASE WHEN NazwaKolumny = 'Thickness' THEN Wartosc END) AS REAL) AS Grubosc,
                        CAST(MAX(CASE WHEN NazwaKolumny = 'Length' THEN Wartosc END) AS REAL) AS Dlugosc,
                        CAST(MAX(CASE WHEN NazwaKolumny = 'Height' THEN Wartosc END) AS REAL) AS Wysokosc
                    FROM DaneBIM
                    WHERE ProjektId = @pid
                    GROUP BY WierszId
                    HAVING MAX(CASE WHEN NazwaKolumny = 'Type' THEN Wartosc END) LIKE '%Ściana%' 
                        OR MAX(CASE WHEN NazwaKolumny = 'Type' THEN Wartosc END) LIKE '%Wall%'
                )
                GROUP BY Grubosc
                ORDER BY Grubosc;";

            cmd.Parameters.AddWithValue("@pid", projektId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dynamic pozycja = new ExpandoObject();
                pozycja.Grubosc = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
                pozycja.CalkowitaDlugosc = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                pozycja.CalkowitaPowierzchnia = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
                pozycja.IloscScian = reader.GetInt32(3);
                zestawienie.Add(pozycja);
            }
            return zestawienie;
        }
    }
}