FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
# HUB: SignalR hub URL of the proxy's management port, e.g. http://proxy-host:8081/connect/audit
ENV HUB=""
# MAX_AUDIT_ENTRIES: maximum number of requests to retain (default 50 000; 0 = unlimited)
ENV MAX_AUDIT_ENTRIES=50000
# STORAGE_MODE: Memory (default) or Disk — use Disk to persist requests across restarts
ENV STORAGE_MODE=""
# STORAGE_PATH: path to the SQLite database file when STORAGE_MODE=Disk (default /app/data/audit.db)
ENV STORAGE_PATH=""
# RETENTION: how long to keep requests, e.g. 30m, 24h, 10d (default: no time limit)
ENV RETENTION=""
# JWE_KEY_TYPE: global fallback key type for JWE decryption — Rsa | Ec | Symmetric | Password
ENV JWE_KEY_TYPE=""
# JWE_KEY: global fallback decryption key — PEM string (Rsa/Ec), base64-encoded bytes (Symmetric), or plain password (Password)
ENV JWE_KEY=""

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Vakthund.UI/Vakthund.UI.csproj", "Vakthund.UI/"]
COPY ["Vakthund.Shared/Vakthund.Shared.csproj", "Vakthund.Shared/"]
RUN dotnet restore "Vakthund.UI/Vakthund.UI.csproj"
COPY . .
WORKDIR "/src/Vakthund.UI"
RUN dotnet build "./Vakthund.UI.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Vakthund.UI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Vakthund.UI.dll"]
