# SmartReptile API image (§05-release/01 §3). Build context is ./backend so that all project
# references resolve; the compose file supplies src/SmartReptile.Api/Dockerfile as the dockerfile path.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.1.0
WORKDIR /src

# Restore first, so a code change does not invalidate the NuGet layer cache.
COPY Directory.Build.props ./
COPY src/SmartReptile.Domain/SmartReptile.Domain.csproj src/SmartReptile.Domain/
COPY src/SmartReptile.Application/SmartReptile.Application.csproj src/SmartReptile.Application/
COPY src/SmartReptile.Infrastructure/SmartReptile.Infrastructure.csproj src/SmartReptile.Infrastructure/
COPY src/SmartReptile.Api/SmartReptile.Api.csproj src/SmartReptile.Api/
RUN dotnet restore src/SmartReptile.Api/SmartReptile.Api.csproj

COPY src/ src/
RUN dotnet publish src/SmartReptile.Api/SmartReptile.Api.csproj \
    --configuration Release \
    --no-restore \
    -p:Version=${VERSION} \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as the non-root user shipped in the base image.
USER $APP_UID

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080 1883 8883

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD ["/bin/sh", "-c", "wget -qO- http://127.0.0.1:8080/health/live || exit 1"]

ENTRYPOINT ["dotnet", "SmartReptile.Api.dll"]
