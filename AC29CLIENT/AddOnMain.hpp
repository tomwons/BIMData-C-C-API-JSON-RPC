
#include "APIEnvir.h"
#include "ACAPinc.h"
#include "DG.h"
#include "DGModule.hpp"

#include "DisposableObject.hpp"

#define EXAMPLEPALETTE_RESID 32590


class ExamplePalette;

class ExamplePaletteManager : public GS::DisposeHandler
{
private:
	ExamplePalette *examplePalette;

	ExamplePaletteManager();
	ExamplePaletteManager(const ExamplePaletteManager &source); // disabled

	ExamplePaletteManager operator=(const ExamplePaletteManager &source); // disabled

public:
	~ExamplePaletteManager();

	static ExamplePaletteManager &GetInstance();

	static void OpenExamplePalette();
	static void CloseExamplePalette();
	static bool IsExamplePaletteOpened();

	ExamplePalette *GetExamplePalette();

	virtual void DisposeRequested(GS::DisposableObject &source) override;
};
