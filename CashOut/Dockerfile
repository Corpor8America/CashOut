# ── Stage 1: Build ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy VERSION file and project file first for better caching
COPY VERSION ./
COPY CashOut/CashOut.csproj ./CashOut/
RUN dotnet restore CashOut/CashOut.csproj

# Copy the rest of the source
COPY CashOut/ ./CashOut/

RUN dotnet publish CashOut/CashOut.csproj \
    --configuration Release \
    --output /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "CashOut.dll"]
