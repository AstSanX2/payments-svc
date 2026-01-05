# ===== STAGE 1: build/publish =====
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

ARG BUILD_CONFIGURATION=Release

# Copia os csproj necessários para restaurar com ProjectReference
COPY src/Worker/PaymentsWorker.csproj src/Worker/
COPY src/api/payments-svc.csproj src/api/

RUN dotnet restore src/Worker/PaymentsWorker.csproj

COPY src/ src/
RUN dotnet publish src/Worker/PaymentsWorker.csproj -c $BUILD_CONFIGURATION -o /app /p:UseAppHost=false

# ===== STAGE 2: runtime =====
# O worker referencia o projeto `payments-svc` (ASP.NET Core), então precisa do shared framework `Microsoft.AspNetCore.App`.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# Instalar certificados e timezone
RUN apk add --no-cache icu-libs tzdata

COPY --from=build --chown=app:app /app .

ENV DOTNET_EnableDiagnostics=false
ENV DOTNET_TieredPGO=1
ENV DOTNET_TC_QuickJitForLoops=1
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

USER app

ENTRYPOINT ["dotnet", "PaymentsWorker.dll"]

