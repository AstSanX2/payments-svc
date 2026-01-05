# ===== STAGE 1: build/publish =====
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

ARG BUILD_CONFIGURATION=Release

COPY src/api/payments-svc.csproj src/api/
RUN dotnet restore src/api/payments-svc.csproj

COPY src/ src/
RUN dotnet publish src/api/payments-svc.csproj -c $BUILD_CONFIGURATION -o /app /p:UseAppHost=false

# ===== STAGE 2: runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# Instalar timezone/ICU (se necessário)
RUN apk add --no-cache icu-libs tzdata

COPY --from=build --chown=app:app /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=false
ENV DOTNET_TieredPGO=1
ENV DOTNET_TC_QuickJitForLoops=1
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "payments-svc.dll"]


