PLUGIN_DIR   := $(HOME)/.local/share/versekit/plugins
APP_PROJECT  := src/VerseKit.App
WRM_PROJECT  := plugins/WebResourcesManager
VERSION      := $(shell grep '<Version>' $(APP_PROJECT)/VerseKit.App.csproj | grep -o '[0-9][^<]*')

.PHONY: run build install-plugins icon bundle clean list-plugins

## Build + install plugins + launch app (default dev target)
run: install-plugins
	dotnet run --project $(APP_PROJECT)

## Build the full solution
build:
	dotnet build VerseKit.slnx -c Debug --nologo

## Copy plugin build outputs to the user plugin directory
install-plugins: build
	mkdir -p $(PLUGIN_DIR)/WebResourcesManager
	cp -r $(WRM_PROJECT)/bin/Debug/net10.0/. $(PLUGIN_DIR)/WebResourcesManager/
	@echo "Installed plugins to $(PLUGIN_DIR)"

## Generate AppIcon-1024.png + AppIcon.icns
## Prerequisites: pip install Pillow  (sips and iconutil are built into macOS)
icon:
	python3 scripts/create-icon.py
	bash scripts/generate-icon.sh

## Build a local .app bundle for testing (Release, native arch)
## Output: dist/VerseKit.app
bundle: icon
	dotnet publish $(APP_PROJECT) \
		-r osx-arm64 --self-contained true \
		-c Release \
		-p:Version=$(VERSION) \
		-o publish/osx-arm64
	dotnet publish $(WRM_PROJECT) \
		--self-contained false \
		-c Release -o publish/plugins/WebResourcesManager
	mkdir -p publish/osx-arm64/plugins/WebResourcesManager
	cp -r publish/plugins/WebResourcesManager/. \
	      publish/osx-arm64/plugins/WebResourcesManager/
	bash scripts/bundle-app.sh publish/osx-arm64 $(VERSION) dist
	@echo ""
	@echo "Bundle ready: dist/VerseKit.app"
	@echo "To install: cp -r 'dist/VerseKit.app' /Applications/"

## Remove build outputs and installed plugins
clean:
	dotnet clean VerseKit.slnx --nologo
	rm -rf publish dist $(PLUGIN_DIR)
	@echo "Cleaned build outputs and installed plugins"

## Show plugin install location and installed files
list-plugins:
	@ls -lA $(PLUGIN_DIR) 2>/dev/null || echo "No plugins installed yet (run: make install-plugins)"
