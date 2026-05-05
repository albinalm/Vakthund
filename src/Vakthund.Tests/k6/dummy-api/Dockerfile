FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Vakthund.LoadTest.DummyApi.csproj", "./"]
RUN dotnet restore "Vakthund.LoadTest.DummyApi.csproj"
COPY . .
RUN dotnet publish "Vakthund.LoadTest.DummyApi.csproj" -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vakthund.LoadTest.DummyApi.dll"]
