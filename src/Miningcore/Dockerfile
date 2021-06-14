#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["src/Miningcore/Miningcore.csproj", "src/Miningcore/"]
RUN dotnet restore "src/Miningcore/Miningcore.csproj"
RUN apt-get update -y \
    && apt-get install -y --no-install-recommends cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5
COPY . .
WORKDIR "/src/src/Miningcore"
RUN dotnet build "Miningcore.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Miningcore.csproj" -c Release -o /app/publish
COPY ["src/Miningcore/config.json", "/app/publish/"]

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
#ENTRYPOINT ["dotnet", "Miningcore.dll", "-c", "config.json"]
ENTRYPOINT ["sh"]
