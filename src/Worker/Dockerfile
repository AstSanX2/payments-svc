# ===== STAGE 1: build/publish =====
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

ARG BUILD_CONFIGURATION=Release

# Build context esperado: ./src (repo root)
# Copia os csproj necessários para restaurar com ProjectReference
COPY Worker/PaymentsWorker.csproj Worker/
COPY payments-svc/payments-svc.csproj payments-svc/

RUN dotnet restore Worker/PaymentsWorker.csproj

COPY . .
RUN dotnet publish Worker/PaymentsWorker.csproj -c $BUILD_CONFIGURATION -o /app /p:UseAppHost=false

# ===== STAGE 2: runtime =====
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
WORKDIR /app

# Instalar certificados e timezone
RUN apk add --no-cache icu-libs tzdata

COPY --from=build /app .

ENV DOTNET_EnableDiagnostics=false
ENV DOTNET_TieredPGO=1
ENV DOTNET_TC_QuickJitForLoops=1
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "PaymentsWorker.dll"]

