FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER root
WORKDIR /app
EXPOSE 8015
ENV ASPNETCORE_HTTP_PORTS=8015

# Install system dependencies required by Playwright / Chromium
RUN apt-get update && apt-get install -y --no-install-recommends \
    libnss3 \
    libnspr4 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libdbus-1-3 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libpango-1.0-0 \
    libcairo2 \
    libasound2 \
    fonts-liberation \
    libx11-xcb1 \
    libxshmfence1 \
    && rm -rf /var/lib/apt/lists/*

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

# Install Playwright browser dynamically for the detected architecture
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN node_bin=$(find /app/.playwright/node -name node -type f | head -n 1) \
    && "$node_bin" /app/.playwright/package/cli.js install chromium \
    && chmod -R 755 /app/.playwright \
    && chmod -R 755 /ms-playwright \
    && chmod +x /app/fetcher.js

USER $APP_UID
ENTRYPOINT ["dotnet", "cifraclub-scraper-backend-csharp.dll"]
