using System;
using System.IO;

namespace BIMData.DBservices
{
    public static class SettingsManager
    {
        // Nazwa pliku, w którym będziemy zapisywać ścieżkę
        private static readonly string SettingsFilePath = "settings.txt";

        // Metoda zapisująca ścieżkę do pliku
        public static void SaveLastDatabasePath(string path)
        {
            try
            {
                File.WriteAllText(SettingsFilePath, path);
            }
            catch (Exception ex)
            {
                // Możesz tutaj dodać logowanie błędu, jeśli zapis się nie uda
                Console.WriteLine($"Błąd zapisu ustawień: {ex.Message}");
            }
        }

        // Metoda odczytująca ścieżkę z pliku
        public static string GetLastDatabasePath()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string path = File.ReadAllText(SettingsFilePath);
                    return File.Exists(path) ? path : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
            return string.Empty; // Brak pliku ustawień
        }
    }
}