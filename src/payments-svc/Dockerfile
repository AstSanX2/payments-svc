# ===== STAGE 1: build/publish =====
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

ARG BUILD_CONFIGURATION=Release

# Visual Studio usa o contexto de build como: payments-svc/src
# e este Dockerfile fica em: payments-svc/src/payments-svc/Dockerfile
# Portanto, os arquivos do projeto ficam em /src/payments-svc/...
COPY payments-svc/payments-svc.csproj ./payments-svc/
RUN dotnet restore ./payments-svc/payments-svc.csproj

COPY payments-svc/. ./payments-svc/
WORKDIR /src/payments-svc
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app /p:UseAppHost=false

# ===== STAGE 2: runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# Instalar certificados, timezone e wget (usado no HEALTHCHECK)
RUN apk add --no-cache icu-libs tzdata wget

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=false
ENV DOTNET_TieredPGO=1
ENV DOTNET_TC_QuickJitForLoops=1
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "payments-svc.dll"]


