# ── Stage 1: Build ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the project file first so Docker caches the restore layer
# and only re-runs it when dependencies change.
COPY CashOut/CashOut.csproj ./
RUN dotnet restore

COPY CashOut/ ./
RUN dotnet publish CashOut.csproj \
    --configuration Release \
    --output /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Non-root user for security.
# Port 8080 is > 1024 so a non-root user can bind to it on Linux.
RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "CashOut.dll"]