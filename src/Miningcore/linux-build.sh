#!/bin/bash
echo ""
echo "The following dev-dependencies will be installed"
echo "Ubuntu: apt-get install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5"
echo ""
sudo apt-get install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5

echo ""
echo "Installing dotnet SDK core 5.0"
sudo apt-get update; \
  sudo apt-get install -y apt-transport-https && \
  sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-5.0

echo ""
echo "Building Miningcore 2.0 (.NET 5.0)"
BUILDIR=${1:-../../build}
echo "Build folder: $BUILDIR"
dotnet publish -c Release --framework net5.0 -o $BUILDIR
