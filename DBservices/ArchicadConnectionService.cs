using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BIMData.DBservices;

/// <summary>
/// Serwis komunikacji z Add-onem Archicada (C++) poprzez protokół JSON-RPC 2.0 (HTTP).
/// </summary>
public sealed class ArchicadConnectionService
{
    // Bazowy adres Twojego lokalnego serwera C++ w Archicadzie
    private const string BaseServerUrl = "http://127.0.0.1:8080/";

    // Współdzielona instancja HttpClient (zgodnie z dobrymi praktykami .NET redukuje zużycie gniazd)
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// Pobiera dane elementów z Add-onu Archicada przy użyciu komendy JSON-RPC.
    /// </summary>
    /// <returns>Surowy JSON string zawierający listę elementów (zawartość pola "result")</returns>
    public static async Task<string> FetchElementsAsJsonAsync()
    {
        // 1. Konstruujemy obiekt żądania zgodny ze standardem JSON-RPC 2.0
        var rpcRequest = new
        {
            jsonrpc = "2.0",
            method = "GetElements",
            id = 1
        };

        try
        {
            // 2. Serializacja żądania do formatu JSON tekstowego
            string requestJson = JsonSerializer.Serialize(rpcRequest);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            // --- KLUCZOWA ZMIANA: CACHE-BUSTER ---
            // Dodajemy unikalny znacznik (Ticks), aby Windows nie serwował starych danych z pamięci podręcznej HTTP.
            string dynamicUrl = $"{BaseServerUrl}?t={DateTime.Now.Ticks}";

            // 3. Wysyłamy żądanie POST na dynamiczny adres URL
            HttpResponseMessage response = await _httpClient.PostAsync(dynamicUrl, content);

            // Rzuci wyjątek, jeśli serwer zwróci status np. 404 lub 500
            response.EnsureSuccessStatusCode();

            // 4. Odczytujemy pełną odpowiedź tekstową z serwera
            string responseJson = await response.Content.ReadAsStringAsync();

            // 5. Parsujemy odpowiedź opakowania JSON-RPC, aby wyciągnąć czyste dane lub obsłużyć błąd
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;

            // Sprawdzamy, czy serwer Archicada zgłosił błąd wewnętrzny (JSON-RPC Error)
            if (root.TryGetProperty("error", out JsonElement errorProp))
            {
                string errMsg = errorProp.TryGetProperty("message", out JsonElement msgProp)
                    ? msgProp.GetString()
                    : "Nieznany błąd serwera RPC";
                throw new InvalidOperationException($"Błąd z serwera Archicad (JSON-RPC): {errMsg}");
            }

            // Wyciągamy czysty wynik (tablicę obiektów), o który prosiliśmy
            if (root.TryGetProperty("result", out JsonElement resultProp))
            {
                // Zwracamy surowy string reprezentujący tablicę elementów
                return resultProp.GetRawText();
            }

            throw new InvalidOperationException("Odpowiedź serwera nie zawierała pola 'result' ani 'error'.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Nie udało się połączyć z Archicadem przez HTTP. Upewnij się, że serwer wtyczki działa na porcie 8080.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Wystąpił błąd podczas przetwarzania formatu JSON z odpowiedzi serwera.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Niespodziewany błąd podczas komunikacji z Archicadem: {ex.Message}", ex);
        }
    }
}