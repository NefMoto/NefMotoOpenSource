# Makefile for NefMotoECUFlasher
# Requires: bash (Cygwin/MSYS/Git Bash) and dotnet
CONFIG ?= Debug
VERSION ?= $(shell git describe --tags --abbrev=4 --always --dirty)
BASE_VERSION := $(shell Installer/version.py $(VERSION))

DEBUG_DIR := ECUFlasher/bin/msil/Debug/net8.0-windows
RELEASE_DIR := ECUFlasher/bin/msil/Release/net8.0-windows

INSTALLER := Installer/bin/Release/NefMotoECUFlasher-$(VERSION).msi

.PHONY: all debug release clean installer help

all: debug

env:
	@echo "Environment variables:"
	@echo "VERSION='$(VERSION)'"
	@echo "BASE_VERSION='$(BASE_VERSION)'"

debug $(DEBUG_DIR)/NefMotoECUFlasher.exe:
	@$(MAKE) CONFIG=Debug build

release $(RELEASE_DIR)/NefMotoECUFlasher.exe:
	@$(MAKE) CONFIG=Release build

build:
	@echo "Building with dotnet ($(CONFIG))..."
	@dotnet build ECUFlasher.sln --configuration $(CONFIG) --verbosity minimal

installer $(INSTALLER): $(RELEASE_DIR)/NefMotoECUFlasher.exe Installer/Product.wxs Makefile
	@echo "Building $(INSTALLER) ($(BASE_VERSION))..."
	@mkdir -p Installer/bin/Release
	@VERSION=$(VERSION) BASE_VERSION=$(BASE_VERSION) ECUFlasher_TargetDir="$(RELEASE_DIR)/" wix build -arch x86 -ext WixToolset.UI.wixext -o $(INSTALLER) Installer/Product.wxs

clean:
	@echo "Cleaning build artifacts..."
	@find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true

help:
	@echo "Available targets:"
	@echo "  make debug     - Build in Debug configuration (default)"
	@echo "  make release   - Build in Release configuration"
	@echo "  make installer - Build the MSI installer"
	@echo "  make clean     - Remove all build artifacts"
	@echo "  make help      - Show this help message"