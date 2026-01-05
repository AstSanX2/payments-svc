# Payments Service (`payments-svc`)

Serviço responsável por **processar pagamentos de forma assíncrona** e expor uma **API de status**.

No Kubernetes/EKS:
- `payments-api`: API HTTP (consulta de status)
- `payments-worker`: worker que consome mensagens da fila `payments-queue`

## Arquitetura (visão rápida)

```
games-api → Outbox → SQS payments-queue → payments-worker → MongoDB (Purchase + DomainEvents)
                                   │
                                   └→ DLQ payments-dlq (após maxReceiveCount)

Client → API Gateway HTTP API (/payments/*) → payments-api → MongoDB
```

## Endpoints (API)
- `GET /health`, `GET /ready`
- `GET /api/Payments/{purchaseId}` (status da compra)

## Processamento (Worker)
O `payments-worker`:
- consome mensagens do SQS
- aplica **idempotência** via `SqsMessageId` (índice único em `Events`)
- atualiza status de purchase (ex.: `PAID`)
- grava `DomainEvent` de processamento

## Eventos (SQS) + DLQ
- **payments-queue** (+ DLQ `payments-dlq`)
  - recebe `PaymentInitiated` (envelope padrão) ou payload legado compatível
- **payments-events-queue** (+ DLQ `payments-events-dlq`)
  - destino opcional para publicar eventos de integração como `PaymentProcessed` (publicação via Outbox)

Configuração do destino `payments-events-queue`:
- `Sqs:PaymentsEventsQueueUrl` (no appsettings/Secret)

## Configuração (Kubernetes)
- O pod espera um `Secret` **`payments-appsettings`** contendo `appsettings.Production.json` montado em `/app/appsettings.Production.json`.
- Passo-a-passo: `fcg-domain/k8s/SECRETS.md`
- Manifests do serviço: `fcg-domain/k8s/payments/*`

## Testes

```bash
dotnet test test/payments-svc.Tests/payments-svc.Tests.csproj -c Release
```

## Docker
- API: `src/payments-svc/Dockerfile`
- Worker: `src/Worker/Dockerfile`
- Imagens rodam como **non-root** (`USER app`) e sem `HEALTHCHECK` (probes são do Kubernetes).


