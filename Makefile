# Makefile for NefMotoECUFlasher
# Requires: bash (Cygwin/MSYS/Git Bash) and dotnet
CONFIG ?= Debug

# NEVER export VERSION: the SDK will use it (incorrectly) as a version expected to be sanitized to 1.2.3.4
FULL_VERSION ?= $(shell git describe --tags --abbrev=4 --always --dirty)

# Same source as Directory.Build.props <NetTfm>
NET_TFM := $(shell sed -n 's/.*<NetTfm>\([^<]*\)<\/NetTfm>.*/\1/p' Directory.Build.props | head -1)
ifeq ($(strip $(NET_TFM)),)
$(error Could not read NetTfm from Directory.Build.props)
endif
DOTNET_MAJOR := $(shell echo "$(NET_TFM)" | sed -n 's/^net\([0-9][0-9]*\).*/\1/p')

DEBUG_DIR := ECUFlasher/bin/msil/Debug
RELEASE_DIR := ECUFlasher/bin/msil/Release

INSTALLER := Installer/bin/Release/NefMotoECUFlasher-$(FULL_VERSION).msi
PUBLISH_DIR := publish/NefMotoECUFlasher

.PHONY: all debug release test clean installer publish help force

all: debug

debug $(DEBUG_DIR)/NefMotoECUFlasher.exe: force
	@$(MAKE) CONFIG=Debug build

release $(RELEASE_DIR)/NefMotoECUFlasher.exe: force
	@$(MAKE) CONFIG=Release build

test: build
	@echo "Running tests ($(CONFIG))..."
	@dotnet test Tests/NefMotoOpenSource.Tests.csproj --configuration $(CONFIG) --no-build --verbosity normal

build:
	@echo "Building with dotnet ($(CONFIG))..."
	FULL_VERSION=$(FULL_VERSION) dotnet build ECUFlasher.sln --configuration $(CONFIG) --verbosity minimal

installer $(INSTALLER): $(RELEASE_DIR)/NefMotoECUFlasher.exe Installer/Product.wxs Makefile Directory.Build.props
	@echo "Building $(INSTALLER) ($(FULL_VERSION), $(NET_TFM))..."
	@mkdir -p Installer/bin/Release
	@ECUFlasher_TargetDir="$(RELEASE_DIR)/" \
	FULL_VERSION=$(FULL_VERSION) wix build -arch x86 \
		-d RuntimeTfm=$(NET_TFM) -d DotNetMajor=$(DOTNET_MAJOR) \
		-ext WixToolset.UI.wixext -ext WixToolset.NetFx.wixext \
		-o $(INSTALLER) Installer/Product.wxs

# Framework-dependent publish folder (not single-file, not the MSI). Still needs the Desktop runtime matching NetTfm.
publish:
	@echo "Publishing to $(PUBLISH_DIR) ($(FULL_VERSION))..."
	@FULL_VERSION=$(FULL_VERSION) dotnet publish ECUFlasher/ECUFlasher.csproj --configuration Release --self-contained false -p:PublishSingleFile=false -o $(PUBLISH_DIR) --verbosity minimal
	@test -d "$(PUBLISH_DIR)/MemoryLayouts" || { echo "error: MemoryLayouts missing from $(PUBLISH_DIR)"; exit 1; }
	@ls "$(PUBLISH_DIR)/MemoryLayouts"/*.MemoryLayout.xml >/dev/null

clean:
	@echo "Cleaning build artifacts..."
	@find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true
	@rm -rf publish

help:
	@echo "Available targets:"
	@echo "  make debug     - Build in Debug configuration (default)"
	@echo "  make release   - Build in Release configuration"
	@echo "  make test      - Build and run unit tests"
	@echo "  make publish   - Framework-dependent publish folder (not MSI)"
	@echo "  make installer - Build the MSI installer"
	@echo "  make clean     - Remove all build artifacts"
	@echo "  make help      - Show this help message"
