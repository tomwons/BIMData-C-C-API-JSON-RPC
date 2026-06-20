using System;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace BIMData.DBservices
{
    public class AiService
    {
        private readonly ProjectService _projectService;
        private Kernel? _kernel;
        private IChatCompletionService? _chatCompletion;
        private readonly string _systemPrompt;

        public AiService(ProjectService projectService)
        {
            _projectService = projectService;

            // Definicja instrukcji systemowej gwarantującej, że AI polega wyłącznie na udostępnionych narzędziach
            _systemPrompt =
                "Jesteś zaawansowanym asystentem BIM. Twoja wiedza o projekcie opiera się WYŁĄCZNIE na gotowych funkcjach dostarczonych w sekcji narzędzi.\n" +
                "NIE posiadasz, NIE możesz żądać i NIE masz technicznej możliwości bezpośredniego dostępu do bazy danych, tabel ani surowych wierszy.\n" +
                "Twoim jedynym źródłem danych są funkcje: 'GetWindowsSummary', 'GetDoorsSummary' oraz 'GetWallsSummary'.\n" +
                "Jeśli użytkownik zapyta o coś, czego te funkcje nie zwracają (np. o instalacje lub materiały), odpowiedz wprost, że nie masz dostępu do takich danych.\n" +
                "W swojej odpowiedzi podaj TYLKO ostateczny wniosek, interpretację lub podsumowanie w języku polskim. Bądź zwięzły i konkretny.";

            InitializeAiAgent();
        }

        private void InitializeAiAgent()
        {
            var builder = Kernel.CreateBuilder();

            // Konfiguracja połączenia z Groq za pomocą konektora OpenAI
            builder.AddOpenAIChatCompletion(
                modelId: "meta-llama/llama-4-scout-17b-16e-instruct",
                apiKey: "put_AI_key_here", // Wklej tutaj swój klucz gsk_...
                endpoint: new Uri("https://api.groq.com/openai/v1")
            );

            // Rejestracja bezpiecznego pluginu narzędziowego
            builder.Plugins.AddFromObject(new BimModelPlugin(_projectService), "BimTools");

            _kernel = builder.Build();
            _chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
        }

        public async Task<string> SendMessageAsync(string userMessage)
        {
            if (_chatCompletion == null || _kernel == null)
                return "Błąd: Serwis AI nie został poprawnie zainicjalizowany.";

            // Tworzymy nową, odizolowaną historię tylko dla tego jednego, bieżącego zapytania
            var singleExecutionHistory = new ChatHistory();
            singleExecutionHistory.AddSystemMessage(_systemPrompt);
            singleExecutionHistory.AddUserMessage(userMessage);

            OpenAIPromptExecutionSettings settings = new()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            // Model przetwarza zapytanie, wywołuje natywne funkcje wtyczki, generuje raport i kończy sesję
            var response = await _chatCompletion.GetChatMessageContentAsync(singleExecutionHistory, settings, _kernel);
            return response.ToString();
        }

        // Metoda pozostawiona dla zachowania kompatybilności wywołań w aplikacji, nie przechowuje już stanu
        public void ClearHistory() { }
    }

    /// <summary>
    /// Bezpieczny plugin operujący wyłącznie na predefiniowanych zestawieniach inżynierskich.
    /// </summary>
    public class BimModelPlugin
    {
        private readonly ProjectService _projectService;

        public BimModelPlugin(ProjectService projectService)
        {
            _projectService = projectService;
        }

        private string SprawdzAktywnyProjekt(out int projektId)
        {
            if (_projectService.WybranyProjektId == null)
            {
                projektId = 0;
                return "Błąd: Brak wybranego (aktywnego) projektu w systemie. Poproś użytkownika o wybranie projektu.";
            }
            projektId = _projectService.WybranyProjektId.Value;
            return string.Empty;
        }

        [KernelFunction]
        [Description("Pobiera gotowe zestawienie ilościowe okien z podziałem na wymiary (Szerokość i Wysokość). Zwraca informacje o wymiarach i liczbie sztuk.")]
        public string GetWindowsSummary()
        {
            string blad = SprawdzAktywnyProjekt(out int projektId);
            if (!string.IsNullOrEmpty(blad)) return blad;

            var dane = _projectService.PobierzZestawienieOkien(projektId);
            if (dane == null || dane.Count == 0) return "Brak danych o oknach w tym projekcie.";

            var sb = new StringBuilder();
            sb.AppendLine($"Zestawienie okien dla projektu: {_projectService.WybranaNazwaProjektu}");

            foreach (var element in dane)
            {
                var wiersz = (IDictionary<string, object>)element;
                sb.AppendLine($"- Wymiary: {wiersz["Szerokosc"]}x{wiersz["Wysokosc"]} | Ilość: {wiersz["IloscSztuk"]} szt.");
            }
            return sb.ToString();
        }

        [KernelFunction]
        [Description("Pobiera gotowe zestawienie ilościowe drzwi z podziałem na wymiary (Szerokość i Wysokość). Zwraca informacje o gabarytach i liczbie sztuk.")]
        public string GetDoorsSummary()
        {
            string blad = SprawdzAktywnyProjekt(out int projektId);
            if (!string.IsNullOrEmpty(blad)) return blad;

            var dane = _projectService.PobierzZestawienieDrzwi(projektId);
            if (dane == null || dane.Count == 0) return "Brak danych o drzwiach w tym projekcie.";

            var sb = new StringBuilder();
            sb.AppendLine($"Zestawienie drzwi dla projektu: {_projectService.WybranaNazwaProjektu}");

            foreach (var element in dane)
            {
                var wiersz = (IDictionary<string, object>)element;
                sb.AppendLine($"- Wymiary: {wiersz["Szerokosc"]}x{wiersz["Wysokosc"]} | Ilość: {wiersz["IloscSztuk"]} szt.");
            }
            return sb.ToString();
        }

        [KernelFunction]
        [Description("Pobiera zaawansowane zestawienie ścian zgrupowane według grubości (Thickness). Zwraca zsumowaną całkowitą długość, wyliczoną sumaryczną powierzchnię oraz liczbę ścian dla każdej grubości.")]
        public string GetWallsSummary()
        {
            string blad = SprawdzAktywnyProjekt(out int projektId);
            if (!string.IsNullOrEmpty(blad)) return blad;

            var dane = _projectService.PobierzZestawienieScian(projektId);
            if (dane == null || dane.Count == 0) return "Brak danych o ścianach w tym projekcie.";

            var sb = new StringBuilder();
            sb.AppendLine($"Zestawienie ścian dla projektu: {_projectService.WybranaNazwaProjektu}");

            foreach (var element in dane)
            {
                var wiersz = (IDictionary<string, object>)element;
                sb.AppendLine($"- Grubość: {wiersz["Grubosc"]} m | Całkowita długość: {wiersz["CalkowitaDlugosc"]} m | Powierzchnia łączna: {wiersz["CalkowitaPowierzchnia"]} m² | Liczba przegród: {wiersz["IloscScian"]} szt.");
            }
            return sb.ToString();
        }
    }
}