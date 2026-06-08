# ── Stage 1: Build ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore first — Docker layer cache means this only
# re-runs when the .csproj changes, not on every code change.
COPY Spening/Spening.csproj ./
RUN dotnet restore

# Copy all source and publish
COPY Spening/ ./
RUN dotnet publish Spening.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Non-root user for security
RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Spening.dll"]
