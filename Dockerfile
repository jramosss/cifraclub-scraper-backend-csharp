FROM mcr.microsoft.com/playwright/dotnet:v1.50.0-jammy AS base
WORKDIR /app
EXPOSE 8015
ENV ASPNETCORE_HTTP_PORTS=8015
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["cifraclub-scraper-backend-csharp/cifraclub-scraper-backend-csharp.csproj", "cifraclub-scraper-backend-csharp/"]
RUN dotnet restore "cifraclub-scraper-backend-csharp/cifraclub-scraper-backend-csharp.csproj"
COPY . .
WORKDIR "/src/cifraclub-scraper-backend-csharp"
RUN dotnet build "cifraclub-scraper-backend-csharp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "cifraclub-scraper-backend-csharp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chmod +x /app/fetcher.js && find /app/.playwright -type f -exec chmod 755 {} +

ENTRYPOINT ["dotnet", "cifraclub-scraper-backend-csharp.dll"]
