# Makefile for NefMotoECUFlasher
# Requires: bash (Cygwin/MSYS/Git Bash) and dotnet
CONFIG ?= Debug
VERSION ?= $(shell git describe --tags --abbrev=4 --always --dirty)

DEBUG_DIR := ECUFlasher/bin/msil/Debug/net8.0-windows
RELEASE_DIR := ECUFlasher/bin/msil/Release/net8.0-windows

INSTALLER := Installer/bin/Release/NefMotoECUFlasher-$(VERSION).msi

.PHONY: all debug release test clean installer help force

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
	@# DO NOT export VERSION: the SDK will use it (incorrectly) as a version expected to be sanitized to 1.2.3.4
	FULL_VERSION=$(VERSION) dotnet build ECUFlasher.sln --configuration $(CONFIG) --verbosity minimal

installer $(INSTALLER): $(RELEASE_DIR)/NefMotoECUFlasher.exe Installer/Product.wxs Makefile
	@echo "Building $(INSTALLER) ($(VERSION))..."
	@mkdir -p Installer/bin/Release
	@ECUFlasher_TargetDir="$(RELEASE_DIR)/" \
	FULL_VERSION=$(VERSION) wix build -arch x86 -ext WixToolset.UI.wixext -ext WixToolset.NetFx.wixext -o $(INSTALLER) Installer/Product.wxs

clean:
	@echo "Cleaning build artifacts..."
	@find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true

help:
	@echo "Available targets:"
	@echo "  make debug     - Build in Debug configuration (default)"
	@echo "  make release   - Build in Release configuration"
	@echo "  make test      - Build and run unit tests"
	@echo "  make installer - Build the MSI installer"
	@echo "  make clean     - Remove all build artifacts"
	@echo "  make help      - Show this help message"
