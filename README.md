# Payments Service (payments-svc)

Serviço **Payments** do FCG — responsável por **processar pagamentos** de forma **assíncrona**.  
Arquitetura composta por **duas Lambdas**:
- **Payments API** (HTTP via API Gateway): endpoints de **health** e **consulta de status** (apoio a testes).
- **Payments Worker** (SQS Trigger): **consome mensagens** da fila `payments-queue` e **atualiza o status** da compra no MongoDB Atlas.

**Segurança** via **JWT** (quando aplicável), **segredos no SSM**, **observabilidade** com **X-Ray** e **CloudWatch Logs**.

---

## Sumário

- [Arquitetura (visão rápida)](#arquitetura-visão-rápida)
- [Stack / Tecnologias](#stack--tecnologias)
- [Fila SQS e contratos](#fila-sqs-e-contratos)
- [Rotas (API de apoio a testes)](#rotas-api-de-apoio-a-testes)
- [Pré-requisitos](#pré-requisitos)
- [Configuração de Segredos (SSM Parameter Store)](#configuração-de-segredos-ssm-parameter-store)
- [Configuração Local (Dev)](#configuração-local-dev)
- [Execução Local](#execução-local)
- [Deploy na AWS (Serverless)](#deploy-na-aws-serverless)
- [Observabilidade (X-Ray + Logs)](#observabilidade-x-ray--logs)
- [Segurança entre microsserviços (IAM/Queue Policy)](#segurança-entre-microsserviços-iamqueue-policy)
- [Testes de ponta a ponta](#testes-de-ponta-a-ponta)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Dicas e Troubleshooting](#dicas-e-troubleshooting)
- [Limpeza de Infra](#limpeza-de-infra)

---

## Arquitetura (visão rápida)

```
Games API → (SendMessage) → Amazon SQS (payments-queue) → (trigger) → Payments Worker
                                                           ↓
                                                    MongoDB Atlas
                                                           ↑
                                          Payments API ←── (consulta status)
```

- O **Games** publica uma mensagem de compra na **SQS** (`payments-queue`).
- O **Payments Worker** é **acionado pela SQS**, valida/processa o pagamento (mock) e **atualiza** a compra no **Atlas** (ex.: `status: PAID` ou `FAILED`).
- A **Payments API** fornece **/health** e **GET /api/Payments/{purchaseId}** para verificar **status** (facilita a demo).

---

## Stack / Tecnologias

- **.NET 8** (ASP.NET Core para a API; Worker com handler SQS).
- **AWS Lambda** + **API Gateway (REST)** (apenas para a API).
- **Amazon SQS** (gatilho da Lambda Worker).
- **MongoDB Atlas** (`MongoDB.Driver`) para persistência do status.
- **JWT** para endpoints que exigirem proteção (opcional na API de teste).
- **SSM Parameter Store** para segredos.
- **AWS X-Ray** (traces) + **CloudWatch Logs**.

---

## Fila SQS e contratos

- **Nome da fila**: `payments-queue` (na região `us-east-1`, a mesma das Lambdas).
- **Produtor**: Games Service (tem permissão apenas de `sqs:SendMessage` na fila).
- **Consumidor**: Payments Worker (permissões de `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:GetQueueAttributes`).

- Caso haja permissão inváida:
  <img width="843" height="143" alt="image" src="https://github.com/user-attachments/assets/9efbe6e9-8a08-4ac8-85f4-835607187757" />

**Contrato esperado da mensagem** (exemplo **típico** gerado pelo Games):

```json
{
  "purchaseId": "650f1e2b0c9f1a3b9b7a1234",
  "userId": "650f1e2b0c9f1a3b9b7a5678",
  "amount": 199.90,
}
```

> Após processar, ele grava no MongoDB Atlas a compra com `status` atualizado (`PAID`) e marca um `updatedAt`.

**Parâmetros operacionais da fila** (recomendado):
- **VisibilityTimeout** ≥ **timeout da Lambda Worker** (ex.: fila **360s**; worker **60–120s**).  
- **Redrive Policy / DLQ** (opcional, recomendado): configurar uma fila de dead-letter para mensagens que falharem várias vezes.

---

## Rotas (API de apoio a testes)

> A API é leve e focada em **teste/consulta** para a apresentação.

| Método | Rota                            | Auth      | Descrição                                      |
|------:|----------------------------------|-----------|------------------------------------------------|
| GET   | `/health`                        | público   | Health check simples.                          |
| GET   | `/api/Payments/{purchaseId}`     | público\* | Retorna o **status** atual da compra. (\*) Pode ser protegido com Bearer se desejado.|

> Em produção, você pode proteger com **JWT Bearer** usando os mesmos `Issuer/Audience/Secret` do Users/Games. Para a apresentação, deixar público simplifica.

---

## Pré-requisitos

- **.NET 8 SDK**
- **AWS CLI** configurado:
  ```bash
  aws configure
  # region us-east-1, output json
  ```
- **Amazon.Lambda.Tools**:
  ```bash
  dotnet tool install -g Amazon.Lambda.Tools
  dotnet tool update -g Amazon.Lambda.Tools
  ```
- **MongoDB Atlas** (cluster e database definidos).
- **SQS**: fila `payments-queue` criada.

---

## Configuração de Segredos (SSM Parameter Store)

Namespace: **`/fcg/...`** — crie **na mesma região** das Lambdas.

```bash
# MongoDB URI (com nome do DB na URI)
aws ssm put-parameter \
  --name "/fcg/MONGODB_URI" \
  --type "SecureString" \
  --value "mongodb+srv://<user>:<pass>@<cluster>.mongodb.net/<db>?retryWrites=true&w=majority&appName=<app>"

# (Opcional) JWT, caso proteja a API
aws ssm put-parameter --name "/fcg/JWT_SECRET" --type "SecureString" --value "<chave-aleatoria-32+>"
aws ssm put-parameter --name "/fcg/JWT_ISS"    --type "String"       --value "fcg-auth"
aws ssm put-parameter --name "/fcg/JWT_AUD"    --type "String"       --value "fcg-clients"

# Fila SQS (um dos dois; a Worker pode resolver por URL ou por nome)
aws ssm put-parameter --name "/fcg/PAYMENTS_QUEUE_URL"  --type "String" --value "https://sqs.us-east-1.<account>.amazonaws.com/<accountId>/payments-queue"
# ou
aws ssm put-parameter --name "/fcg/PAYMENTS_QUEUE_NAME" --type "String" --value "payments-queue"
```

**Permissões IAM**: a role das Lambdas precisa de `ssm:GetParameter` para ler os parâmetros (`AmazonSSMReadOnlyAccess` ou policy mínima).

---

## Configuração Local (Dev)

Você pode definir **fallback** em `appsettings.Development.json` (útil sem AWS creds):

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb+srv://<user>:<pass>@<cluster>.mongodb.net/<db>?retryWrites=true&w=majority&appName=<app>"
  },
  "JwtOptions": {
    "Key": "<chave-aleatoria-32+>",
    "Issuer": "fcg-auth",
    "Audience": "fcg-clients"
  },
  "Payments": {
    "QueueUrl": "https://sqs.us-east-1.<account>.amazonaws.com/<accountId>/payments-queue"
  }
}
```

---

## Execução Local

- **Payments API**:
  ```bash
  cd src/payments-svc
  dotnet run
  # http://localhost:5000/health
  ```

- **Payments Worker**: localmente, você pode **mockar** o consumo lendo mensagens da SQS via AWS CLI e chamando um endpoint interno (se exposto) — porém, a execução ideal do Worker é **na nuvem via trigger SQS**.

---

## Deploy na AWS (Serverless)

> O projeto publica **duas funções** (API e Worker) pelo mesmo stack `payments-svc`.

1. **Bucket de artifacts** (se necessário):
   ```bash
   aws s3 mb s3://lambda-artifacts-payments-fcg-us-east-1 --region us-east-1
   ```

2. **Deploy**:
   ```bash
   # na pasta src/payments-svc
   dotnet lambda deploy-serverless
   # Stack name: payments-svc
   # S3 Bucket: lambda-artifacts-payments-fcg-us-east-1
   ```

3. **Verificar criação**:
   ```bash
   aws cloudformation describe-stacks --stack-name payments-svc \
     --query "Stacks[0].StackStatus" --output text
   ```

4. **Recuperar URL da API (se houver output)**:
   ```bash
   aws cloudformation describe-stacks \
     --stack-name payments-svc \
     --query "Stacks[0].Outputs[?contains(OutputKey,'Api') || contains(OutputKey,'ApiURL')].OutputValue" \
     --output text
   ```

> **X-Ray**: ative **Active tracing** nas duas funções.  
> **SQS Trigger**: o template já cria/associa o evento (ou configure no Console: Lambda Worker → Triggers → SQS → `payments-queue`).  
> **VisibilityTimeout da fila** deve ser **maior** que o **timeout** da Worker (evita reprocesso prematuro).

---

## Observabilidade (X-Ray + Logs)

### Tracing
- Lambda **Payments API** e **Payments Worker** → **Active tracing** habilitado.
- (Opcional) env var `AWS_XRAY_TRACING_NAME` distinta para cada função (`payments-api`, `payments-worker`).

### Service Map
Execute o fluxo **Games → SQS → Worker** e confirme no X-Ray:
```
API GW (games) → games-svc → SQS → payments-worker → MongoDB Atlas
```

### Logs
```bash
# descobrir nomes reais das funções pelo CloudFormation
aws cloudformation describe-stack-resources \
  --stack-name payments-svc \
  --query "StackResources[?ResourceType=='AWS::Lambda::Function'].[LogicalResourceId,PhysicalResourceId]" \
  --output table

# seguir logs da Worker
aws logs tail /aws/lambda/<NOME-REAL-WORKER> --follow
```

---

## Segurança entre microsserviços (IAM/Queue Policy)

**Principais pontos**:
- **games-svc (produtor)**: policy mínima com `sqs:SendMessage` **apenas** no ARN da `payments-queue`.
- **payments-worker (consumidor)**: `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:GetQueueAttributes` **apenas** na `payments-queue`.
- **Queue Policy** (recurso SQS): **restringe** quem pode enviar/consumir por **principal** (role ARN).

**Teste negativo** (com um perfil/usuário sem permissão) deve resultar em **AccessDenied** ao tentar `SendMessage` ou `ReceiveMessage`.

---

## Testes de ponta a ponta

> Recomenda-se testar **via Games API**, pois ela cria a compra e publica o evento automaticamente.

1) **Criar compra** (Games):
```bash
export API_G="https://<id>.execute-api.us-east-1.amazonaws.com/Prod"
export TOKEN="<jwt>"
curl -X POST "$API_G/api/Purchases" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "gameId":"<ObjectId-do-jogo>" }'
# A resposta deve conter o purchaseId
```

2) **Acompanhar processamento** (Worker):
```bash
# Seguir logs da Worker (ver nome real antes)
aws logs tail /aws/lambda/<NOME-REAL-WORKER> --follow
```

3) **Consultar status** (Payments API):
```bash
export API_P="https://<id>.execute-api.us-east-1.amazonaws.com/Prod"
curl "$API_P/api/Payments/<purchaseId>"
# esperado: { "purchaseId": "...", "status": "PAID", "processedAt": "..." }
```

4) **(Opcional) disparo direto de mensagem SQS** (para testes isolados do Worker):
```bash
aws sqs send-message \
  --queue-url "https://sqs.us-east-1.<account>.amazonaws.com/<accountId>/payments-queue" \
  --message-body '{"purchaseId":"650f1e2b0c9f1a3b9b7a1234","userId":"...","gameId":"...","price":99.9,"currency":"BRL","createdAt":"2025-10-01T13:45:00Z"}'
```

---

## Estrutura do Projeto

```
src/payments-svc/
  Application/
    DTO/ (consulta de status, etc.)
    Services/PaymentService.cs         # Atualiza/consulta status no Mongo
  Domain/
    Entities/Purchase.cs               # modelo da compra (status, datas)
    Interfaces/Repositories/IPurchaseRepository.cs
    Interfaces/Services/IPaymentService.cs
  Infraestructure/
    Repositories/PurchaseRepository.cs
  Controllers/
    PaymentsController.cs              # GET /api/Payments/{purchaseId}, /health
  Worker/
    Function.cs / Handler.cs           # EntryPoint da Lambda SQS (Payments Worker)
  Helpers/
    ObjectIdJsonConverter.cs
  Program.cs (API)
  aws-lambda-tools-defaults.json
  serverless.template                  # define PaymentsApi e PaymentsWorker + trigger SQS
  appsettings*.json
  payments-svc.csproj
```

---

## Dicas e Troubleshooting

**1) `InvalidAddressException: arn:aws:sqs ...`**  
- Use **Queue URL** para **enviar mensagens**. **ARN** é para **policies**.  
- Confirme **região** do endpoint (us-east-1).

**2) `Queue visibility timeout < Function timeout`** ao criar o trigger  
- Ajuste a fila para **VisibilityTimeout maior** que o timeout da Worker.

**3) Mensagem não some da fila (reprocesso)**  
- Worker não executa `DeleteMessage` (erro antes de deletar) ou **timeout menor** que o processamento.  
- Verifique logs da Worker e aumente VisibilityTimeout.

**4) `401 Unauthorized` (se proteger a API)**  
- **Issuer/Audience/Secret** devem ser idênticos aos serviços Users/Games e o token deve estar válido.

**5) `Database name must be specified in the connection string`**  
- Inclua o **nome do DB** na URI do Atlas.

**6) X-Ray sem nós esperados**  
- Habilite **Active tracing** nas duas funções. Gere tráfego suficiente e ajuste o range do X-Ray.

---

## Limpeza de Infra

```bash
aws cloudformation delete-stack --stack-name payments-svc
# (se a fila foi criada fora do stack, exclua manualmente ou via CLI)
```

---

**Dúvidas?** Abra uma *issue* com logs/erros e o que foi executado (região, stack, prints do X-Ray/CloudWatch).
