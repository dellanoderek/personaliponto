# Plano de Ação — Tempo Certo

> Baseado em `planejamento_plataforma_controle_de_ponto.md` e na apresentação comercial `DOC-20260804-WA0057.pdf`.
> Documento de execução: transforma a visão já registrada em fases, entregáveis, critérios de pronto e decisões que precisam ser fechadas antes de codar.

---

## 1. Sumário executivo

O produto é um SaaS multiempresa de controle de ponto, com núcleo técnico baseado no modelo **REP-P** (Portaria MTP nº 671/2021). Quatro superfícies: terminal Web de marcação, app do funcionário, painel RH/empresa e dashboard do Super Admin (SaaS).

O planejamento já existente é bom e completo em requisitos — o risco agora é de **execução**: começar pela UI, tratar ponto como CRUD, ou deixar AFD/AEJ/multi-tenancy para depois. Este plano organiza o trabalho em fases sequenciais, cada uma com escopo fechado e critério objetivo de conclusão, e isola as decisões de arquitetura que precisam ser tomadas **antes** da Fase 1.

---

## 2. Decisões de arquitetura (fechadas por Derek em 2026-09-28)

| # | Decisão | Definição | Observação |
|---|---|---|---|
| 1 | Banco de dados | **PostgreSQL**, hospedado no **Supabase** | Supabase é Postgres gerenciado — mantém a recomendação técnica original e resolve hospedagem no mesmo passo. |
| 2 | Modelo de multi-tenancy | **Banco compartilhado + `TenantId` em toda tabela + Row-Level Security** | Supabase tem suporte nativo a RLS — decisão se encaixa bem na plataforma escolhida. |
| 3 | Backend | **ASP.NET Core (C#)** | — |
| 4 | Dashboard Web (RH + Super Admin) | **Blazor Server** | Ver observação de risco abaixo — Blazor Server depende de conexão persistente (SignalR), o que exige atenção especial no terminal Web de marcação. |
| 5 | App do funcionário | **.NET MAUI** | — |
| 6 | Fonte de hora | **NTP do servidor como referência única; dispositivo nunca é fonte de verdade** | Confirmado. |
| 7 | Storage de arquivos | **Supabase Storage**, imagens em **WebP** e comprimidas ao máximo | Fotos, comprovantes, AFD/AEJ gerados, atestados. Monitorar consumo desde a Fase 1 — plano gratuito/inicial do Supabase tem limite de storage e de banda; definir rotina de compressão (WebP + resize) no upload, antes de persistir o arquivo. |
| 8 | Fila/mensageria para sincronização offline | **Fila simples via banco (outbox pattern) no MVP; RabbitMQ/SQS só se o volume exigir** | Confirmado. |

### Ponto de atenção: Blazor Server no Terminal Web de marcação

Blazor Server mantém um circuito ativo via SignalR entre navegador e servidor. Se a conexão cair (rede instável em loja/fábrica/campo), o terminal perde estado e pode travar no meio de uma marcação. Como o terminal Web foi definido como **sem login persistente** e crítico para o fluxo de ponto (seção 4.1 do planejamento), recomendo tratar isso já na Fase 1:

- Implementar reconexão automática do circuito Blazor com feedback visual claro ("reconectando...") em vez de tela travada;
- Para o terminal Web especificamente (não o dashboard), considerar isolar a tela de marcação como um componente com fallback local mínimo (grava a tentativa de marcação no `localStorage` do navegador e reenvia ao reconectar) — evita perda de marcação por queda de circuito;
- O app mobile (MAUI) já resolve isso via fila offline nativa (decisão 8) — o terminal Web é o único ponto que precisa desse cuidado extra por causa do Blazor Server.

Isso é um ajuste de implementação dentro da Fase 1, não uma mudança de escopo.

---

## 3. Ordem de execução (visão geral)

```
Fase 0 — Fundação técnica
Fase 1 — Núcleo REP-P (MVP legal/técnico)
Fase 2 — Gestão de RH
Fase 3 — Gestão empresarial / Analytics
Fase 4 — SaaS (Super Admin, planos, cobrança)
Fase 5 — Segurança avançada / diferenciais
```

Cada fase só começa com a anterior **em produção ou validável**. Não paralelizar Fase 1 com Fase 3+ — é o erro mais comum em projetos desse tipo (motor de ponto instável derruba confiança em todo o resto).

---

## 4. Fase 0 — Fundação técnica (1–2 semanas)

Não está no documento original, mas precisa existir antes da Fase 1.

- [ ] Repositório(s) criado(s) e CI básico (build + lint + testes)
- [ ] Estrutura de solução: separar claramente `Core.RepP` (núcleo de registro/conformidade), `Modules.RH`, `Modules.SaaS`, `Modules.Analytics` (ver seção 43 do planejamento — não misturar núcleo legal com módulos comerciais)
- [ ] Banco de dados provisionado (PostgreSQL no Supabase) + estratégia de migrations (EF Core Migrations ou similar)
- [ ] `TenantId` como coluna obrigatória desde a primeira migration, com índice e RLS habilitado
- [ ] Ambiente de dev/staging/produção definidos
- [ ] Padrão de autenticação decidido (JWT + refresh token; MFA para admin fica para Fase 5, mas o hook de suporte já deve existir no schema de `User`)

**Critério de pronto:** dá para subir a API vazia, autenticar um usuário fake, e a query mais simples já filtra por `TenantId` automaticamente (sem depender do desenvolvedor lembrar de filtrar).

---

## 5. Fase 1 — Núcleo REP-P (6–10 semanas)

Este é o coração do produto. Sem isto, não há produto vendável como "sistema de ponto".

### 5.1 Escopo
- Empresas, estabelecimentos, funcionários, usuários/RH (CRUD básico, sem UI sofisticada)
- Jornadas básicas (turno fixo, 5x2, 6x1)
- PIN do funcionário (hash seguro, nunca texto puro; bloqueio após N tentativas)
- Terminal Web de marcação (sem login persistente, conforme seção 4.1 do planejamento)
- App mobile: tela de marcação + últimas marcações (sem espelho completo ainda)
- Motor de registro: marcação → registro original imutável → NSR sequencial por estabelecimento
- Offline no app: fila local, estados `PENDING/SYNCING/SYNCED/FAILED/RETRY/CONFLICT`, idempotência por UUID gerado no cliente
- Fonte de hora do servidor (NTP), dispositivo nunca decide o horário oficial
- Comprovante eletrônico por marcação (disponível ao menos 48h, conforme MTE)
- Espelho de ponto (leitura simples, sem edição manual — gerado a partir do registro + tratamento)
- Módulo de Tratamento (PTRP): complementação de marcação com motivo, sem apagar o original
- Geração de AFD conforme leiaute vigente do MTE
- Geração de AEJ conforme leiaute vigente do MTE
- Auditoria: toda ação sensível grava quem/quando/o quê

### 5.2 Fora de escopo nesta fase
Reconhecimento facial, cobrança, dashboards gerenciais, múltiplos dashboards, geofence avançado, banco de horas, atestados — tudo isso é Fase 2+.

### 5.3 Critério de pronto
- Um funcionário bate ponto pelo terminal Web e pelo app, offline e online
- O registro original nunca é sobrescrito — qualquer ajuste gera um segundo registro vinculado
- AFD e AEJ são gerados e batem com o leiaute oficial atual do MTE (validar formato byte a byte, não só "parece certo")
- Teste de isolamento de tenant: query maliciosa/erro de código não retorna dado de outro tenant (escrever teste automatizado específico para isso, não só revisão manual)

**Antes de fechar esta fase:** validar o leiaute AFD/AEJ contra a página oficial do MTE (seção 58 do documento de planejamento) — não presumir que o modelo de dados da seção 44 já cobre todos os campos exigidos.

---

## 6. Fase 2 — Gestão de RH (4–6 semanas)

- Solicitação de correção de ponto (funcionário → RH, com aprovação/recusa auditada)
- Atestados e justificativas (upload, análise, aprovação)
- Banco de horas (créditos, débitos, compensação, saldo auditável)
- Faltas, atrasos, intervalos (cálculo, não só exibição)
- Feriados e calendário por estabelecimento
- Escalas avançadas (12x36, turnos, jornada noturna)
- Geofence configurável por empresa/funcionário

**Critério de pronto:** RH consegue fechar o mês de um funcionário real (ajustes, banco de horas, atestado) sem sair da plataforma nem editar planilha.

---

## 7. Fase 3 — Gestão empresarial / Analytics (3–4 semanas)

- Dashboard com indicadores (horas extras, banco de horas, atrasos, absenteísmo — seção 25/26 do planejamento)
- Relatórios exportáveis
- Estrutura de menu completa do painel RH (seção 26)

**Critério de pronto:** RH abre o dashboard e entende a saúde da operação sem gerar relatório manual.

---

## 8. Fase 4 — SaaS / Super Admin (4–6 semanas)

- Painel Super Admin: métricas de clientes (ativos, teste, inadimplentes), funcionários, marcações/dia
- Gestão de clientes (cadastrar, ativar, suspender, cancelar)
- Planos e assinaturas (mesmo que cobrança automática venha depois, o modelo de dados de `Plan`/`Subscription` deve existir)
- Regras de suspensão por inadimplência — **cuidado:** nunca bloquear acesso a dados trabalhistas já registrados, apenas funcionalidades de uso (seção 34 do planejamento)
- Auditoria de acesso administrativo a tenant (todo acesso de suporte a dados de cliente é logado)

**Critério de pronto:** dá para vender e operar comercialmente sem intervenção manual no banco de dados.

---

## 9. Fase 5 — Segurança avançada e diferenciais (contínuo, sem prazo fixo)

- Foto na marcação (com política de retenção e avaliação LGPD prévia)
- Device binding
- Detecção de fraude / padrões anômalos
- Biometria/reconhecimento facial — **somente após análise jurídica e de LGPD específica**, não é requisito de MVP

---

## 10. Riscos críticos e como este plano os mitiga

| Risco (do documento original) | Mitigação neste plano |
|---|---|
| Começar pelo layout | Fase 0 e Fase 1 não têm UI polida — é CRUD funcional primeiro |
| Tratar ponto como CRUD editável | Registro original imutável desde a primeira migration da Fase 1 |
| Ignorar AFD/AEJ até o fim | AFD/AEJ estão dentro da Fase 1, não em fase posterior |
| Multi-tenancy inadequado | `TenantId` + RLS obrigatórios desde a Fase 0, com teste automatizado de isolamento |
| Offline mal projetado | Idempotência por UUID e máquina de estados definida já na Fase 1 |
| Segurança adicionada tarde | Hash de PIN, HTTPS, JWT definidos na Fase 0 |

---

## 11. Próximos passos imediatos

1. Provisionar o projeto no Supabase (banco Postgres + Storage) e habilitar RLS desde a primeira migration
2. Criar repositório e estrutura de Fase 0 (ASP.NET Core + Blazor Server + MAUI)
3. Validar o leiaute AFD/AEJ vigente diretamente na página do MTE antes de modelar as tabelas de exportação
4. Definir a rotina de compressão/conversão para WebP no upload de imagens (fotos, comprovantes) antes de codar a Fase 1
5. Se necessário, produzir os documentos 02 (Requisitos REP-P) e 05 (Modelo de Dados) citados na seção 57 do planejamento original **antes** de iniciar a Fase 1 — eles destravam o detalhamento técnico que este plano de ação não substitui

---

*Este plano de ação organiza a execução do planejamento já existente. Não substitui validação jurídica de conformidade com a Portaria MTP nº 671/2021, conforme já alertado no documento original (seção 59).*
