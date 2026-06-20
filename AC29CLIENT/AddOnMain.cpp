#include "APIEnvir.h"
#include "ACAPinc.h"
#include "ResourceIds.hpp"
#include "DGModule.hpp"
#include "DisposableObject.hpp"
#include <thread>
#include <windows.h>
#include "AddOnMain.hpp"
#include <atomic>
#include <mutex>
#include <iostream>
#include <fstream>
#include <string>
#include <vector>
#include <algorithm>
#include <sstream>
#include <cmath>
#include <iomanip>
// --- WYCISZENIE OSTRZEŻEŃ DLA ZEWNĘTRZNYCH BIBLIOTEK ---
#if defined(_MSVC_LANG) || defined(_MSC_VER)
    #pragma warning(push, 0) // Zapisz obecne ustawienia i ustaw poziom ostrzeżeń na 0
#endif

#include "httplib.h"
#include "json.hpp"

#if defined(_MSVC_LANG) || defined(_MSC_VER)
    #pragma warning(pop) // Przywróć domyślny, rygorystyczny poziom ostrzeżeń dla reszty Twojego kodu
#endif


#define EXAMPLEPALETTE_RESID 32590

static const GSResID AddOnInfoID        = ID_ADDON_INFO;
static const Int32   AddOnNameID        = 1;
static const Int32   AddOnDescriptionID = 2;

static const short   AddOnMenuID        = ID_ADDON_MENU;
static const Int32   AddOnCommandID     = 1;
////////////////////////////////////////////////////////////////////////////////

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// --- GLOBALNE ZMIENNE SERWERA i CACHE ---
static std::unique_ptr<httplib::Server> g_httpServer = nullptr;
static std::thread g_serverThread;
static std::string g_cachedDataJsonRPC = ""; // Tutaj ląduje gotowy JSON w pamięci RAM

// --- WYCISZENIE OSTRZEŻEŃ DLA CAŁEGO BLOKU SIECIOWEGO ---
#if defined(_MSC_VER)
    #pragma warning(push, 0)
#endif

// --- GŁÓWNA FUNKCJA: Pobiera listę elementów i ich parametry w jednym miejscu ---
static nlohmann::json PobierzPayloadElementow()
{
    nlohmann::json elementsArray = nlohmann::json::array();

    GS::Array<API_Guid> elementList;
    if (ACAPI_Element_GetElemList(API_ZombieElemID, &elementList) != NoError || elementList.IsEmpty())
        return elementsArray;

    for (const auto& guid : elementList) {
        API_Element element = {};
        element.header.guid = guid;
        if (ACAPI_Element_Get(&element) != NoError) // Pobiera od razu nagłówek i dane geometryczne
            continue;

        API_ElemTypeID typeId = element.header.type.typeID;
        if (typeId != API_WallID && typeId != API_DoorID && typeId != API_WindowID)
            continue;

        // 1. Podstawowe metadane elementu
        GS::UniString typeName;
        ACAPI_Element_GetElemTypeName(element.header.type, typeName);
        std::string typeStd(typeName.ToCStr().Get());
        std::string guidStd(APIGuidToString(guid).ToCStr().Get());

        // 2. Pobieranie specyficznych parametrów geometrycznych (dawny switch)
        nlohmann::json parameters = nlohmann::json::array();
        auto addP = [&](const std::string& n, double v, const std::string& p) {
            parameters.push_back({{"Name", n}, {"Value", std::round(v * 1000.0) / 1000.0}, {"Property", p}});
        };

        if (typeId == API_WallID) {
            double dx = element.wall.begC.x - element.wall.endC.x;
            double dy = element.wall.begC.y - element.wall.endC.y;
            addP("Height",    element.wall.height,    "height");
            addP("Thickness", element.wall.thickness, "thickness");
            addP("Length",    sqrt(dx * dx + dy * dy), "length");
        } 
        else if (typeId == API_DoorID) {
            addP("Width",  element.door.openingBase.width,  "width");
            addP("Height", element.door.openingBase.height, "height");
        } 
        else if (typeId == API_WindowID) {
            addP("Width",  element.window.openingBase.width,  "width");
            addP("Height", element.window.openingBase.height, "height");
        }

        // 3. Agregacja do głównej tabeli
        elementsArray.push_back({
            {"ObjectId", guidStd},
            {"Name", typeStd + "_" + guidStd.substr(0, 8)},
            {"Type", typeStd},
            {"Parameters", parameters}
        });
    }

    return elementsArray;
}

// --- 3. CACHE: Funkcja odświeżająca pamięć podręczną ---
void OdswiezCacheProjektu()
{
    // Generujemy aktualny stan modelu BIM
    nlohmann::json elementsArray = PobierzPayloadElementow(); 
    
    // Budujemy kontener struktury zgodnej z protokołem JSON-RPC 2.0
    nlohmann::json responseJson;
    responseJson["jsonrpc"] = "2.0";
    responseJson["id"] = 1;
    responseJson["result"] = elementsArray;

    // Zrzucamy strukturę do tekstu i zapisujemy w pamięci RAM
    g_cachedDataJsonRPC = responseJson.dump(); 
}

static std::string ObsluzZapytanieJsonRPC(const std::string& /*requestBody*/)
{
    try {
        // Jeśli z jakiegoś powodu pamięć podręczna była pusta, budujemy ją
        if (g_cachedDataJsonRPC.empty()) {
            OdswiezCacheProjektu();
        }
        
        // Błyskawiczny zwrot gotowego pakietu z pamięci RAM
        return g_cachedDataJsonRPC; 
    } 
    catch (...) {
        return "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32700,\"message\":\"Parse error\"},\"id\":null}";
    }
}

// --- 5. SERWER: Kontrola cyklu życia serwera HTTP ---
void UruchomSerwer()
{
    // Jeśli serwer już działa, kliknięcie przycisku "Uruchom" ma TYLKO odświeżyć payload w locie
    if (g_httpServer != nullptr) {
        OdswiezCacheProjektu();
        ACAPI_WriteReport("Serwer już działa. Dane w pamięci podręcznej RAM zostały pomyślnie odświeżone.", true);
        return;
    }

    g_httpServer = std::make_unique<httplib::Server>();

    // Przywracamy bezpieczną wartość 1. Próba ustawienia 0 w niektórych wersjach 
    // httplib powoduje błąd wewnętrzny biblioteki przy natychmiastowym rozłączeniu.
    g_httpServer->set_keep_alive_max_count(1); 

    // Rejestracja punktu końcowego pod żądania POST
    g_httpServer->Post("/", [](const httplib::Request& req, httplib::Response& res) {
        try {
            std::string rpcResponse = ObsluzZapytanieJsonRPC(req.body);
            
            // Informujemy klienta, że to koniec transmisji
            res.set_header("Connection", "close");
            res.set_content(rpcResponse, "application/json");
        } 
        catch (const std::exception& e) {
            // Logujemy standardowe wyjątki do konsoli Archicada dla diagnostyki
            std::string errMsg = "Blad serwera: " + std::string(e.what());
            ACAPI_WriteReport(GS::UniString(errMsg.c_str()), false);
            
            res.status = 500;
            res.set_header("Connection", "close");
            res.set_content("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Internal error\"},\"id\":null}", "application/json");
        }
        catch (...) {
            res.status = 500;
            res.set_header("Connection", "close");
            res.set_content("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Internal error\"},\"id\":null}", "application/json");
        }
    });

    // ZAWSZE przygotowujemy świeże dane w pamięci podręcznej przy PIERWSZYM starcie serwera
    OdswiezCacheProjektu();

    // Uruchomienie pętli nasłuchiwania w bezpiecznym wątku tła
    g_serverThread = std::thread([]() {
        try {
            g_httpServer->listen("127.0.0.1", 8080);
        } catch (...) {
            // Tłumimy błędy, aby uchronić Archicada przed nagłym wyłączeniem
            ACAPI_WriteReport("Wyjątek krytyczny w pętli listen() serwera.", false);
        }
    });

    ACAPI_WriteReport("Szybki serwer JSON-RPC został pomyślnie uruchomiony na porcie 8080 ze świeżym payloadem.", true);
}

void StopSerwer()
{
    // Sprawdzamy, czy serwer w ogóle istnieje
    if (g_httpServer != nullptr) {
        
        // 1. Nakazujemy pętli listen() natychmiastowe przerwanie pracy i zamknięcie portu
        g_httpServer->stop(); 

        // 2. Czekamy, aż wątek tła (std::thread) bezpiecznie i całkowicie się zakończy
        if (g_serverThread.joinable()) {
            g_serverThread.join();
        }

        // 3. Czyścimy wskaźnik i zwalniamy pamięć RAM po JSON-ie
        g_httpServer = nullptr;
        g_cachedDataJsonRPC.clear(); 
        
        ACAPI_WriteReport("Serwer JSON-RPC został bezpiecznie zatrzymany.", true);
    }
}

#if defined(_MSC_VER)
    #pragma warning(pop)
#endif


////////////////////////////////////////////////////////////////////////////////
// SEKCJA 3: Klasa ExamplePalette (pełna definicja)
////////////////////////////////////////////////////////////////////////////////

class ExamplePalette :	public DG::Palette,
						public DG::PanelObserver,
						public DG::ButtonItemObserver,
						public DG::CompoundItemObserver,
						public GS::DisposableObject
{
public:
	enum DialogResourceIds
	{
		ExampleDialogResourceId = ID_ADDON_DLG,
		HideButtonId = 1,
		ShowButtonId = 2

	};

	ExamplePalette () :
		DG::Palette (ACAPI_GetOwnResModule(), EXAMPLEPALETTE_RESID, ACAPI_GetOwnResModule(), paletteGuid()),
		hideButton (GetReference (), HideButtonId),
		showButton (GetReference (), ShowButtonId)

	{
		//SetTitle (ADDON_NAME " " ADDON_VERSION);
		AttachToAllItems (*this);
		Attach (*this);
		SetDefaultGarbageCollector();
		SetDisposeHandler(ExamplePaletteManager::GetInstance());
	}


	~ExamplePalette ()
	{
		Detach (*this);
		DetachFromAllItems (*this);
	}
	static GSErrCode PaletteAPIControlCallBack(Int32 referenceID, API_PaletteMessageID messageID, GS::IntPtr param);
	static Int32 paletteRefIdd();
	static const GS::Guid &paletteGuid();



////////////////////////////////////////////////////////////////////////////////
/////// to jest umozliwiewnie zmiany kształt + wywowalnie funkcji.//////////////
////////////////////////////////////////////////////////////////////////////////
private:
	virtual void PanelOpened(const DG::PanelOpenEvent & /*ev*/)
	{
	}
	virtual void PanelResized (const DG::PanelResizeEvent& ev) override
	{
		short vGrow = ev.GetVerticalChange();
		short hGrow = ev.GetHorizontalChange();

		if (vGrow != 0 || hGrow != 0)
		{
			BeginMoveResizeItems();

			hideButton.Move(hGrow, vGrow);
			showButton.Move(hGrow, vGrow);

			EndMoveResizeItems();
		}
	}
	virtual void PanelCloseRequested(const DG::PanelCloseRequestEvent &ev, bool * /*accept*/)
	{
		if (ev.GetSource() != this)
			return;

		Hide();
		EndEventProcessing();
		MarkAsDisposable();
	}
	virtual void ButtonClicked (const DG::ButtonClickEvent& ev) override
	{
		if (ev.GetSource() == &hideButton)
		{
			StopSerwer();
		}
		if (ev.GetSource() == &showButton)
		{
			UruchomSerwer();
		}

	}

	DG::Button hideButton;
	DG::Button showButton;


};

const GS::Guid &ExamplePalette::paletteGuid()
{
	// We need a fix and unique GUID to construct the palette to make it dockable.
	static GS::Guid guid("E8C52F23-A7B4-4A9B-9913-4D9E6A41DF58");
	return guid;
}

Int32 ExamplePalette::paletteRefIdd()
{
	static Int32 paletteRefId(GS::CalculateHashValue(paletteGuid()));
	return paletteRefId;
}

////////////////////////////////////////////////////////////////////////////////
// SEKCJA 4: Klasa ExamplePaletteManager (pełna definicja + implementacje)
////////////////////////////////////////////////////////////////////////////////

ExamplePaletteManager &ExamplePaletteManager::GetInstance()
{
	static ExamplePaletteManager instance;

	return instance;
}

ExamplePaletteManager::ExamplePaletteManager() : examplePalette(NULL)
{
}

ExamplePaletteManager::~ExamplePaletteManager()
{
	if (DBERROR(examplePalette != NULL))
	{
		examplePalette->EndEventProcessing();
		delete examplePalette;
		examplePalette = NULL;
	}
}

void ExamplePaletteManager::OpenExamplePalette()
{
	ExamplePaletteManager &manager = GetInstance();

	if (manager.examplePalette == NULL)
	{
		manager.examplePalette = new ExamplePalette();
		if (DBERROR(manager.examplePalette == NULL))
			return;
	}

	manager.examplePalette->BeginEventProcessing();
	manager.examplePalette->Show();
}

void ExamplePaletteManager::CloseExamplePalette()
{
	ExamplePaletteManager &manager = GetInstance();

	if (manager.examplePalette == NULL)
		return;

	manager.examplePalette->Hide();
	manager.examplePalette->EndEventProcessing();
	delete manager.examplePalette;
	manager.examplePalette = NULL;
}

bool ExamplePaletteManager::IsExamplePaletteOpened(void)
{
	return GetInstance().examplePalette != NULL;
}

void ExamplePaletteManager::DisposeRequested(GS::DisposableObject &source)
{
	ExamplePaletteManager &manager = GetInstance();
	if (&source == manager.examplePalette)
	{
		manager.examplePalette = NULL;
	}
}

ExamplePalette *ExamplePaletteManager::GetExamplePalette()
{
	return this->examplePalette;
}
////////////////////////////////////////////////////////////////////////////////
// SEKCJA 5: PaletteAPIControlCallBack — musi być po obu klasach
////////////////////////////////////////////////////////////////////////////////

GSErrCode ExamplePalette::PaletteAPIControlCallBack(Int32 referenceID, API_PaletteMessageID messageID, GS::IntPtr param)
{
	GSErrCode err = NoError;

	if (referenceID == ExamplePalette::paletteRefIdd())
	{
		switch (messageID)
		{
		case APIPalMsg_OpenPalette:
			ExamplePaletteManager::OpenExamplePalette();
			break;

		case APIPalMsg_ClosePalette:
			ExamplePaletteManager::CloseExamplePalette();
			break;

		case APIPalMsg_HidePalette_Begin:
			if (ExamplePaletteManager::IsExamplePaletteOpened())
				ExamplePaletteManager::GetInstance().GetExamplePalette()->Hide();
			break;

		case APIPalMsg_HidePalette_End:
			if (ExamplePaletteManager::IsExamplePaletteOpened())
				ExamplePaletteManager::GetInstance().GetExamplePalette()->Show();
			break;

		case APIPalMsg_DisableItems_Begin:
			if (ExamplePaletteManager::IsExamplePaletteOpened())
				ExamplePaletteManager::GetInstance().GetExamplePalette()->DisableItems();
			break;

		case APIPalMsg_DisableItems_End:
			if (ExamplePaletteManager::IsExamplePaletteOpened())
				ExamplePaletteManager::GetInstance().GetExamplePalette()->EnableItems();
			break;

		case APIPalMsg_IsPaletteVisible:
			*(reinterpret_cast<bool *>(param)) = (ExamplePaletteManager::IsExamplePaletteOpened() && ExamplePaletteManager::GetInstance().GetExamplePalette()->IsVisible());
			break;

		default:
			break;
		}
	}

	return err;
}

////////////////////////////////////////////////////////////////////////////////
// SEKCJA 6: Menu i funkcje główne Archicada
////////////////////////////////////////////////////////////////////////////////
static void Open_ExamplePalette(void)
{
	if (!ExamplePaletteManager::IsExamplePaletteOpened())
		ExamplePaletteManager::OpenExamplePalette();
	return;
}


static GSErrCode MenuCommandHandler (const API_MenuParams *menuParams)
{
	switch (menuParams->menuItemRef.menuResID) {
		case AddOnMenuID:
			switch (menuParams->menuItemRef.itemIndex) {
				case AddOnCommandID:
					{
						Open_ExamplePalette();
						break;
					}
					break;
			}
			break;
	}
	return NoError;
}

API_AddonType CheckEnvironment (API_EnvirParams* envir)
{
	RSGetIndString (&envir->addOnInfo.name, AddOnInfoID, AddOnNameID, ACAPI_GetOwnResModule ());
	RSGetIndString (&envir->addOnInfo.description, AddOnInfoID, AddOnDescriptionID, ACAPI_GetOwnResModule ());

	return APIAddon_Normal;
}

GSErrCode RegisterInterface (void)
{
#ifdef ServerMainVers_2700
	return ACAPI_MenuItem_RegisterMenu (AddOnMenuID, 0, MenuCode_UserDef, MenuFlag_Default);
#else
	return ACAPI_Register_Menu (AddOnMenuID, 0, MenuCode_UserDef, MenuFlag_Default);
#endif
}

GSErrCode Initialize (void)
{
	GSErrCode err = NoError;
#ifdef ServerMainVers_2700
	err = ACAPI_MenuItem_InstallMenuHandler (AddOnMenuID, MenuCommandHandler);
#else
	err =ACAPI_Install_MenuHandler (AddOnMenuID, MenuCommandHandler);
#endif

	// If the palette is constructed and registered, the add-on will be not unloaded from the memory while Archicad runs.
	ACAPI_RegisterModelessWindow(ExamplePalette::paletteRefIdd(),
								 ExamplePalette::PaletteAPIControlCallBack,
								 API_PalEnabled_FloorPlan + API_PalEnabled_3D,
								 GSGuid2APIGuid(ExamplePalette::paletteGuid()));
	if (err != NoError)
		DBPrintf("DG_Test:: Initialize() ACAPI_MenuItem_InstallMenuHandler failed\n");

	return err;
}

GSErrCode FreeData (void)
{
	void StopSerwer();
	ACAPI_UnregisterModelessWindow(ExamplePalette::paletteRefIdd());
	return NoError;
}
////////////////////////////////////////////////////////////////////////////////
/////// main cały zamkniety w tej przestrzeni.///////////////////////////////////
////////////////////////////////////////////////////////////////////////////////