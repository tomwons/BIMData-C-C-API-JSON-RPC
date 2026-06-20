using Microsoft.Data.Sqlite;

namespace BIMData.DBservices
{
    public static class Database
    {
        public static void Initialize(string connectionString)
        {
            // Zamiast bloku { }, używamy deklaracji w jednej linii
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var pragmaCommand = connection.CreateCommand();
            pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
            pragmaCommand.ExecuteNonQuery();

            using var transaction = connection.BeginTransaction();

            var command = connection.CreateCommand();
            command.Transaction = transaction;

            // 1. Projekty
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Projekty (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nazwa TEXT NOT NULL,
                    DataUtworzenia DATETIME DEFAULT CURRENT_TIMESTAMP
                );";
            command.ExecuteNonQuery();

            // 2. DaneBIM
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS DaneBIM (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProjektId INTEGER NOT NULL,
                    WierszId INTEGER NOT NULL,
                    NazwaKolumny TEXT NOT NULL,
                    Wartosc TEXT,
                    FOREIGN KEY(ProjektId) REFERENCES Projekty(Id) ON DELETE CASCADE
                );";
            command.ExecuteNonQuery();

            // 3. Indeksy
            command.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_projekt_id ON DaneBIM(ProjektId);
                CREATE INDEX IF NOT EXISTS idx_wiersz_id ON DaneBIM(WierszId);";
            command.ExecuteNonQuery();

            transaction.Commit();
            // Obiekty 'transaction' i 'connection' zostaną automatycznie 
            // zwolnione na końcu metody (po wyjściu z zakresu).
        }
    }
}