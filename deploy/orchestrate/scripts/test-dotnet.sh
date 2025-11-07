#!/bin/bash
set -e

SRC="$1"
WORKDIR=~/helloworld

# Remove previous test project
rm -rf "$WORKDIR"

# Create subfolder and solution
mkdir -p "$WORKDIR"
cd "$WORKDIR"
dotnet new sln -n helloworld-sln

# Create console app
dotnet new console -n helloworld --framework net9.0

# Add console app to solution
dotnet sln helloworld-sln.sln add helloworld/helloworld.csproj

# Copy source file
cp "$SRC" helloworld/Program.cs

# Run the solution
dotnet run --project helloworld/helloworld.csproj