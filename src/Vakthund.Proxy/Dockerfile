FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
# TARGET: upstream URL to proxy (required), e.g. https://api.example.com
ENV TARGET=""
# MAX_BODY_BYTES: request bodies larger than this are not captured (default 64 KB)
ENV MAX_BODY_BYTES=65536
# MAX_QUEUED_ENTRIES: audit queue capacity before oldest entries are dropped (default 10 000)
ENV MAX_QUEUED_ENTRIES=10000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Vakthund.Proxy/Vakthund.Proxy.csproj", "Vakthund.Proxy/"]
COPY ["Vakthund.Shared/Vakthund.Shared.csproj", "Vakthund.Shared/"]
RUN dotnet restore "Vakthund.Proxy/Vakthund.Proxy.csproj"
COPY . .
WORKDIR "/src/Vakthund.Proxy"
RUN dotnet build "./Vakthund.Proxy.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Vakthund.Proxy.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Vakthund.Proxy.dll"]
