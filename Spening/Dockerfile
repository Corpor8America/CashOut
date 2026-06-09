# ── Stage 1: Build ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Spening/Spening.csproj ./
RUN dotnet nuget locals all --clear && dotnet restore

COPY Spening/ ./
RUN dotnet publish Spening.csproj \
    --configuration Release \
    --output /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Non-root user for security.
# Port 8080 is > 1024 so a non-root user can bind to it on Linux.
# If you ever change EXPOSE to a privileged port (< 1024, e.g. 80),
# you will need to either run as root or grant NET_BIND_SERVICE capability.
RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Spening.dll"]