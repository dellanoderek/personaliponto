# PersonaliPonto

Plataforma SaaS multiempresa de controle de ponto eletrônico. Implementa um **REP-P** (Registrador Eletrônico de Ponto via Programa) nos termos da **Portaria MTP nº 671/2021**, com gestão de RH (jornadas, tratamentos, banco de horas, espelho), painel da plataforma para gestão de clientes e aplicativo móvel para marcação de ponto.

## Arquitetura

Solução .NET 10 organizada em módulos:

| Projeto | Responsabilidade |
|---|---|
| `src/PersonaliPonto.Shared` | Contratos (DTOs), papéis (`Roles`), claims e utilitários comuns. |
| `src/PersonaliPonto.Core.RepP` | Núcleo legal do REP-P: marcações, NSR, comprovantes, geração dos arquivos **AFD (leiaute 004)** e **AEJ (leiaute 002)**, identificação do PTRP (`RepPOptions`). |
| `src/PersonaliPonto.Modules.RH` | Funcionários, jornadas, tratamentos de ponto, banco de horas, espelho. |
| `src/PersonaliPonto.Modules.SaaS` | Clientes (tenants), planos, importação da planilha de clientes. |
| `src/PersonaliPonto.Modules.Analytics` | Indicadores e relatórios. |
| `src/PersonaliPonto.Infrastructure` | EF Core/PostgreSQL (`PersonaliPontoDbContext` + `SistemaDbContext`), segurança de banco (RLS), autenticação/JWT/MFA, assinatura digital CAdES, NTP, storage. |
| `src/PersonaliPonto.Api` | API REST (JWT) consumida pelo app móvel. |
| `src/PersonaliPonto.Web` | Portal Blazor (empresa, funcionário e plataforma/Super Admin). |
| `src/PersonaliPonto.Mobile` | App .NET MAUI (Android; Windows em dev). Não faz parte de `PersonaliPonto.sln`. |
| `tests/*` | `Core.Tests` (unitários), `IntegrationTests` e `Web.Tests` (usam PostgreSQL real). |

### Multi-tenant com RLS forçado

- Toda tabela com `tenant_id` tem **Row-Level Security habilitada e FORÇADA** (`SegurancaBanco.Aplicar`).
- A cada conexão, o `TenantSessionInterceptor` executa `SET ROLE personaliponto_app` (papel `NOLOGIN NOBYPASSRLS`) e `set_config('app.tenant_id', …)` / `set_config('app.bypass_rls', …)` **na sessão** do PostgreSQL.
- Tabelas legais (`registros_rep`, `marcacoes_contexto`, `tratamentos`, `audit_logs`, `banco_horas`, `assinaturas_espelho`) são imutáveis: UPDATE/DELETE/TRUNCATE bloqueados por trigger.
- Os arquivos AFD/AEJ são assinados em CAdES com certificado ICP-Brasil do desenvolvedor; a hora oficial vem do NTP.br.

## Requisitos

- .NET 10 SDK
- PostgreSQL 17
- (Mobile) workload MAUI: `dotnet workload install maui-android` (e `maui-windows` no Windows)
- `dotnet-ef` para migrations: `dotnet tool install -g dotnet-ef`

## Rodando localmente

1. **Banco**: o projeto usa um Postgres portátil em `.local/pgsql`, com dados em `.local/data`, na porta **54329** (usuário `postgres`, senha `devpass` — ver `appsettings.Development.json`).
   ```bash
   .local/pgsql/bin/pg_ctl -D .local/data start
   ```
   (Qualquer Postgres 17 serve; ajuste a connection string.)
2. **Web** (porta 5116):
   ```bash
   dotnet run --project src/PersonaliPonto.Web
   ```
   Em Development as migrations rodam na inicialização (`Banco:MigrarNaInicializacao=true`) e o Super Admin é criado:
   - e-mail: `admin@personaliponto.local`
   - senha inicial: `PersonaliPonto2026` (troca obrigatória no primeiro acesso)

   No painel da plataforma há o botão para criar uma **empresa demo** (dados fictícios) e entrar nela.
3. **API** (porta 5174):
   ```bash
   dotnet run --project src/PersonaliPonto.Api
   ```
4. **Mobile**:
   ```bash
   dotnet build src/PersonaliPonto.Mobile -t:Run -f net10.0-windows10.0.19041.0   # Windows
   dotnet build src/PersonaliPonto.Mobile -t:Run -f net10.0-android                # Android (emulador/dispositivo)
   ```

Arquivos locais (`.local/`, `*.pfx`, `appsettings.*.local.json`, `*.xlsx`) estão no `.gitignore`.

## Testes

```bash
dotnet test tests/PersonaliPonto.Core.Tests
dotnet test tests/PersonaliPonto.IntegrationTests
dotnet test tests/PersonaliPonto.Web.Tests
```

Os testes de integração e Web criam um banco temporário por fixture. Servidor padrão: `Host=localhost;Port=54329;Username=postgres;Password=devpass`; para outro, defina `PERSONALIPONTO_TEST_DB` (connection string **sem** `Database`). O usuário precisa poder criar bancos e roles (superusuário em dev/CI).

CI: `.github/workflows/ci.yml` (Postgres 17 como service, build de `PersonaliPonto.Backend.slnf` e os 3 projetos de teste; job Android opcional).

## Migrations

Dois contextos no projeto Infrastructure: `PersonaliPontoDbContext` (pasta `Persistence/Migrations`) e `SistemaDbContext` (pasta `Persistence/MigrationsSistema`).

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/PersonaliPonto.Infrastructure --startup-project src/PersonaliPonto.Api \
  --context PersonaliPontoDbContext --output-dir Persistence/Migrations

dotnet ef migrations add NomeDaMigration \
  --project src/PersonaliPonto.Infrastructure --startup-project src/PersonaliPonto.Api \
  --context SistemaDbContext --output-dir Persistence/MigrationsSistema
```

O startup project deve ser `src/PersonaliPonto.Api` (o Web não referencia `Microsoft.EntityFrameworkCore.Design`).

**Regra obrigatória:** migration que cria tabela nova deve terminar com
```csharp
migrationBuilder.Sql(SegurancaBanco.Aplicar);
```
para que a RLS forçada e os grants do papel da aplicação sejam aplicados (tabelas imutáveis também devem constar em `SegurancaBanco.TabelasImutaveis`).

Em produção, aplique migrations explicitamente com a flag `--migrar` (executa e encerra):
```bash
dotnet PersonaliPonto.Api.dll --migrar
```

## Configuração de produção

Variáveis de ambiente (`__` separa seções no .NET):

| Chave | Descrição |
|---|---|
| `PERSONALIPONTO_DB` ou `ConnectionStrings__PersonaliPonto` | Connection string do PostgreSQL (`PERSONALIPONTO_DB` tem prioridade). |
| `Banco__MigrarNaInicializacao` | `false` em produção (use `--migrar`). |
| `Banco__PapelAplicacao` | Papel sem BYPASSRLS usado no `SET ROLE` (padrão `personaliponto_app`). |
| `Jwt__Chave` | **Obrigatória.** Chave HMAC com ≥ 32 bytes. |
| `Jwt__Emissor`, `Jwt__Audiencia` | Padrão `personaliponto`. |
| `Jwt__AcessoMinutos`, `Jwt__RefreshDias` | Padrão 15 min / 30 dias. |
| `Assinatura__CertificadoBase64` | Certificado A1 ICP-Brasil (PFX) em base64 (preferencial). |
| `Assinatura__CertificadoArquivo` | Alternativa: caminho do PFX. |
| `Assinatura__CertificadoSenha` | Senha do PFX. |
| `Storage__Provedor` | `supabase` ou `local`. |
| `Storage__SupabaseUrl` | URL do projeto Supabase. |
| `Storage__SupabaseServiceKey` | Chave `service_role` (somente servidor). |
| `Storage__DiretorioLocal` | Diretório para o provedor `local`. |
| `SuperAdmin__Email`, `SuperAdmin__Senha`, `SuperAdmin__Nome` | Seed do primeiro Super Admin (só se não houver nenhum). |
| `RepP__NumeroRegistroInpi`, `RepP__IdentificadorDesenvolvedor`, `RepP__TipoIdentificadorDesenvolvedor`, `RepP__RazaoSocialDesenvolvedor`, `RepP__EmailDesenvolvedor`, `RepP__VersaoPrograma` | Identificação do PTRP gravada no AFD/AEJ. |
| `Ntp__Habilitado`, `Ntp__Servidores__0…` | Sincronização com NTP.br (tolerância 30 s). |

## Pagamentos (Asaas)

Integração com a API v3 do Asaas (`src/PersonaliPonto.Infrastructure/Pagamentos`), isolada atrás de `IGatewayPagamento` (os testes usam um fake).

- **Owner → revendedor (E3):** toda fatura de canal recebida pela plataforma (mensal, e avulsa na compra do Premium ou do app próprio) gera cobrança na conta Asaas da plataforma com `billingType=UNDEFINED`: o revendedor paga por Pix (copia-e-cola/QR), boleto ou cartão em `/canal/minha-conta`.
- **Revendedor Premium → clientes e parceiros (E9):** o admin do revendedor cola a chave API da **própria** conta em `/canal/minha-conta`. A chave é validada (`GET /myAccount/commercialInfo`), guardada cifrada (Data Protection, propósito `PersonaliPonto.Pagamentos.ChaveAsaasRevendedor.v1`) e nunca mais exibida (só os 4 últimos caracteres). O webhook é criado automaticamente na conta dele com authToken próprio. O dinheiro vai direto para a conta do revendedor (sem split). Clientes públicos nunca geram cobrança automática.
- A emissão passa pelo outbox (retentativa exponencial): se o Asaas estiver fora, a fatura é gerada normalmente e fica "pendente de envio".
- NFS-e das cobranças da plataforma: opcional (`Asaas:NotaFiscal:Habilitada`, desligada por padrão).

### Configuração

| Chave (variável de ambiente) | Descrição |
|---|---|
| `Asaas:Ambiente` (`Asaas__Ambiente`) | `Sandbox` (padrão, `https://api-sandbox.asaas.com/v3`) ou `Producao` (`https://api.asaas.com/v3`). |
| `Asaas:Owner:ChaveApi` (`Asaas__Owner__ChaveApi`) | **Segredo.** Chave API da conta Asaas da plataforma. Vazia = cobrança automática desligada (baixa manual). |
| `Asaas:Owner:WebhookToken` (`Asaas__Owner__WebhookToken`) | **Segredo.** authToken do webhook da conta da plataforma (32–255 caracteres, sem espaços; gere com `openssl rand -base64 36`). |
| `Asaas:UrlPublica` | URL pública do painel Web (ex.: `https://app.personaliponto.com.br`), usada no webhook criado nas contas dos revendedores. |
| `Asaas:EmailWebhook` | E-mail de contato do webhook (avisos de fila interrompida). |
| `Asaas:WebhookLimitePorMinuto` | Limite de requisições por IP no webhook (padrão 300). |
| `Asaas:NotaFiscal:*` | `Habilitada`, `DescricaoServico`, `CodigoServicoMunicipal`, `NomeServicoMunicipal`, `AliquotaIss`, `RetemIss`. |

**Webhook a cadastrar na conta da plataforma** (Asaas → Integrações → Webhooks, ou `POST /v3/webhooks`):

- URL: `https://<painel>/api/pagamentos/asaas/webhook` (endpoint do projeto **Web**, anônimo, com limite de taxa);
- Token de autenticação: o mesmo valor de `Asaas:Owner:WebhookToken` (chega no header `asaas-access-token`);
- Versão da API 3, envio sequencial; eventos `PAYMENT_CREATED`, `PAYMENT_UPDATED`, `PAYMENT_CONFIRMED`, `PAYMENT_RECEIVED`, `PAYMENT_OVERDUE`, `PAYMENT_DELETED`, `PAYMENT_RESTORED`, `PAYMENT_REFUNDED`, `PAYMENT_RECEIVED_IN_CASH_UNDONE`, `PAYMENT_CHARGEBACK_REQUESTED`.

Os webhooks das contas dos revendedores são criados/removidos pelo próprio painel. O webhook é idempotente (id do evento), tolerante a eventos fora de ordem e dá baixa/estorno automáticos, reavaliando a régua do canal.

## Supabase

- Crie o projeto na região **São Paulo (`sa-east-1`)**.
- Conecte usando **Session pooler (porta 5432)** ou a **conexão direta**. **Nunca use o Transaction pooler (porta 6543)**: o sistema depende de `SET ROLE` e `set_config` por sessão, que se perdem/vazam entre clientes em modo transação.
- A role de login da aplicação **não pode ter BYPASSRLS** (nem ser superusuário); ela precisa ser membro de `personaliponto_app` para o `SET ROLE`. Rode as migrations (`--migrar`) com um usuário com permissão de DDL.
- As migrations revogam acesso de `anon`/`authenticated` aos schemas do sistema; não exponha as tabelas via API REST do Supabase.
- Storage: crie buckets privados e configure `Storage__Provedor=supabase`, `Storage__SupabaseUrl` e `Storage__SupabaseServiceKey` (chave `service_role`, apenas no servidor — nunca no app/navegador).

## Deploy (visão geral)

- Publicar Web e Api como imagens Docker (`dotnet publish` → `mcr.microsoft.com/dotnet/aspnet:10.0`) em uma VPS.
- **Caddy** como proxy reverso com HTTPS automático (ex.: `app.dominio` → Web:8080, `api.dominio` → Api:8080), repassando `X-Forwarded-*`.
- Segredos via variáveis de ambiente/arquivo `.env` fora do repositório.
- Fluxo de release: backup → `--migrar` → subir novas imagens.
- Fuso: `TZ=America/Sao_Paulo`; hora legal via NTP.br.

## Pendências legais

- **Registro do programa no INPI** (art. 91 da Portaria 671) — preencher `RepP__NumeroRegistroInpi`.
- **Atestado Técnico e Termo de Responsabilidade** do REP-P emitido pelo desenvolvedor.
- **Certificado digital ICP-Brasil (A1)** do desenvolvedor para assinar AFD/AEJ.
