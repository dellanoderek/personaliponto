# Planejamento — Plataforma SaaS de Controle de Ponto

> **Documento inicial de visão, requisitos funcionais e arquitetura**
>
> Status: **Draft / Especificação inicial**
>
> Objetivo: servir como base para transformar a ideia da plataforma em requisitos claros, arquitetura técnica, backlog e roadmap de desenvolvimento.

---

## 1. Visão geral

A proposta é criar uma **plataforma SaaS de controle de jornada/ponto eletrônico**, voltada para empresas contratantes, seus funcionários e para o administrador da própria plataforma.

O produto terá quatro interfaces principais:

1. **Terminal Web de Batida de Ponto**
2. **Aplicativo Mobile do Funcionário**
3. **Dashboard Web da Empresa/RH**
4. **Dashboard Web do Super Admin da Plataforma**

O produto deve ser concebido desde o início considerando o modelo **REP-P — Registrador Eletrônico de Ponto via Programa**, previsto na Portaria MTP nº 671/2021.

O Ministério do Trabalho e Emprego informa que o SREP via programa é composto por:

- REP-P;
- coletores de marcações;
- armazenamento de registro de ponto;
- Programa de Tratamento de Registro de Ponto (PTRP).

O MTE também informa que o REP-P permite o uso de tecnologias como marcação de ponto mobile.

**Importante:** requisitos legais devem ser validados continuamente contra a versão vigente da Portaria e dos leiautes oficiais publicados pelo MTE. Este documento é uma especificação de produto/engenharia, não substitui validação jurídica ou de conformidade.

---

# 2. Objetivo do produto

Criar uma solução moderna, responsiva e multiplataforma para:

- registrar jornadas de trabalho;
- permitir marcações via aplicativo;
- permitir marcações por terminal Web sem login convencional;
- funcionar em situações de indisponibilidade temporária de internet;
- preservar os registros originais;
- permitir tratamento de ponto conforme as regras aplicáveis;
- disponibilizar comprovantes;
- gerar espelho de ponto;
- gerar AFD e AEJ conforme os requisitos aplicáveis;
- administrar jornadas, escalas, funcionários e regras;
- fornecer indicadores gerenciais;
- permitir que múltiplas empresas utilizem a mesma plataforma com isolamento de dados;
- oferecer um painel administrativo para o proprietário/operador da plataforma;
- permitir futuramente cobrança recorrente, planos e gestão de assinaturas.

---

# 3. Modelo conceitual do produto

```text
                         ┌─────────────────────┐
                         │     SUPER ADMIN     │
                         │ Dono da plataforma  │
                         └──────────┬──────────┘
                                    │
                  ┌─────────────────┴─────────────────┐
                  │                                   │
          ┌───────▼────────┐                 ┌────────▼─────────┐
          │   EMPRESA A    │                 │    EMPRESA B     │
          │     CLIENTE    │                 │      CLIENTE     │
          └───────┬────────┘                 └────────┬─────────┘
                  │                                   │
        ┌─────────┴─────────┐               ┌─────────┴─────────┐
        │                   │               │                   │
   Dashboard RH        Funcionários    Dashboard RH        Funcionários
        │                   │               │                   │
        │              ┌────▼────┐          │              ┌────▼────┐
        │              │   APP   │          │              │   APP   │
        │              └────┬────┘          │              └────┬────┘
        │                   │               │                   │
        └─────────────┬─────┴───────────────┴─────┬─────────────┘
                      │                           │
                      ▼                           ▼
                 ┌────────────────────────────────────┐
                 │              REP-P                  │
                 │                                    │
                 │ Registro de marcações              │
                 │ Armazenamento                      │
                 │ Integridade                        │
                 │ AFD / AEJ                          │
                 │ Comprovantes                       │
                 │ Sincronização                      │
                 └────────────────────────────────────┘
```

---

# 4. As quatro interfaces

## 4.1. Terminal Web de Batida de Ponto

Esta é uma das características importantes do produto.

O funcionário poderá abrir uma página específica para marcar o ponto **sem realizar login no dashboard**.

A ideia é:

```text
┌─────────────────────────────────────────────────┐
│                                                 │
│                 LOGO DA EMPRESA                │
│                                                 │
│              Olá, funcionário!                 │
│                                                 │
│             ┌─────────────────┐                 │
│             │    SEU PIN      │                 │
│             └─────────────────┘                 │
│                                                 │
│             ┌─────────────────┐                 │
│             │   BATER PONTO   │                 │
│             └─────────────────┘                 │
│                                                 │
│                  08:37:21                       │
│                                                 │
│             Domingo, 27/09/2026                 │
│                                                 │
└─────────────────────────────────────────────────┘
```

### Fluxo

```text
Funcionário
    ↓
Abre terminal Web
    ↓
Informa PIN individual
    ↓
Sistema identifica funcionário
    ↓
Coleta informações configuradas
    ├── data/hora
    ├── localização
    ├── dispositivo
    └── foto, se configurada
    ↓
Registra marcação
    ↓
Exibe confirmação
    ↓
Disponibiliza comprovante
```

### Princípio importante

O PIN deve ser entendido como **mecanismo de identificação/autenticação da marcação**, e não como substituto de todos os controles de segurança.

A empresa poderá configurar políticas adicionais de segurança.

---

# 5. Aplicativo Mobile do Funcionário

O funcionário poderá utilizar um aplicativo próprio para Android e iOS.

### Características

- login persistente;
- identificação do funcionário;
- bater ponto;
- visualizar marcações;
- visualizar comprovantes;
- consultar espelho, conforme permissões;
- acompanhar solicitações;
- solicitar correção;
- visualizar jornada;
- funcionar parcialmente offline;
- sincronizar automaticamente quando houver conexão.

### Exemplo

```text
┌─────────────────────────┐
│ Olá, João               │
│                         │
│ Jornada de hoje         │
│ 08:00 → ?               │
│                         │
│ ┌─────────────────────┐ │
│ │     BATER PONTO     │ │
│ └─────────────────────┘ │
│                         │
│ Últimas marcações       │
│                         │
│ 08:02 Entrada           │
│ 12:01 Saída intervalo   │
│ 13:03 Entrada intervalo │
│                         │
│ Meu espelho             │
│ Solicitações             │
└─────────────────────────┘
```

---

# 6. Terminal Web x Aplicativo

Os dois mecanismos devem compartilhar o mesmo núcleo de registro, mas possuem experiências diferentes.

| Característica | Terminal Web | Aplicativo |
|---|---|---|
| Login persistente | Não | Sim |
| PIN | Sim | Pode ser usado na ativação/segurança |
| Bater ponto | Sim | Sim |
| GPS | Configurável | Configurável |
| Foto | Configurável | Configurável |
| Offline | Pode ser planejado | Essencial |
| Histórico | Opcional | Sim |
| Comprovante | Sim | Sim |
| Solicitação de correção | Não prioritário | Sim |
| Espelho | Não prioritário | Sim |

---

# 7. Registro de ponto e modelo REP-P

O núcleo do produto deve ser construído em torno do conceito de REP-P.

A arquitetura não deve tratar o registro de ponto como uma simples tabela que o RH pode editar.

Deve existir uma separação conceitual entre:

```text
MARCAÇÃO ORIGINAL
        ↓
ARMAZENAMENTO
        ↓
TRATAMENTO
        ↓
APURAÇÃO
        ↓
ESPELHO / AEJ / RELATÓRIOS
```

O registro original deve ser preservado.

---

# 8. Imutabilidade dos registros

Exemplo:

```text
27/09/2026
08:03:17
Entrada
João da Silva
```

O RH não deve simplesmente transformar:

```text
08:03 → 08:00
```

apagando o histórico original.

A estrutura conceitual deve permitir:

```text
MARCAÇÃO ORIGINAL
27/09/2026 08:03:17

        +

TRATAMENTO / COMPLEMENTAÇÃO
Motivo: esquecimento
Horário tratado: conforme solicitação/apuração

        +

AUDITORIA
Quem solicitou
Quem aprovou
Quando
Motivo
```

O Programa de Tratamento de Registro de Ponto deve trabalhar sobre os dados registrados, preservando a rastreabilidade.

---

# 9. NSR e identificação das marcações

O modelo de dados deverá prever o **Número Sequencial de Registro (NSR)** e as regras correspondentes ao REP-P.

O MTE informa que, no REP-P, cada estabelecimento possui sua própria sequência de NSR, com numeração sequencial iniciando em 1 na primeira operação do REP em relação ao estabelecimento.

Isso deve ser tratado como requisito técnico do núcleo de registro, e não como detalhe de interface.

---

# 10. Hora e sincronização

Não se deve depender simplesmente do relógio configurado no aparelho do funcionário.

## Online

```text
Servidor / fonte oficial
        ↓
Hora de referência
        ↓
Marcação
```

## Offline

```text
Dispositivo
    ↓
Timestamp local
    +
informações necessárias à sincronização
    +
proteção contra alteração indevida
    ↓
Fila local segura
    ↓
Internet volta
    ↓
Sincronização
    ↓
Servidor
```

A arquitetura deve definir claramente:

- fonte de hora;
- tolerâncias;
- política para relógio incorreto do dispositivo;
- identificação de eventos offline;
- ordem de sincronização;
- tratamento de conflitos;
- prevenção de duplicidade.

---

# 11. Sincronização Offline

O aplicativo deve conseguir registrar uma marcação mesmo sem conexão, quando isso for permitido pela política operacional do produto.

A marcação deve ser armazenada localmente em área segura e entrar em uma fila de sincronização.

### Exemplo

```text
08:03:17
↓
Usuário bate ponto
↓
Sem internet
↓
Evento armazenado localmente
↓
Usuário continua sua jornada
↓
Internet retorna
↓
Fila é enviada
↓
Servidor confirma recebimento
↓
Evento recebe status sincronizado
```

### Estados possíveis

```text
PENDING
SYNCING
SYNCED
FAILED
RETRY
CONFLICT
```

---

# 12. Geolocalização

A geolocalização deve ser um recurso configurável.

Exemplo:

```text
Empresa A

Permitir ponto:
☑ Dentro do estabelecimento

Raio:
100 metros
```

Outro caso:

```text
Funcionário externo

☑ Permitir trabalho externo
☑ Registrar localização
☐ Exigir geofence
```

### Fluxo

```text
GPS
 ↓
Latitude / Longitude
 ↓
Geofence
 ↓
Dentro?
 ├── SIM → continua
 └── NÃO → aplica política da empresa
```

Não se deve assumir que todo trabalhador estará fisicamente dentro de um estabelecimento.

---

# 13. Foto na marcação

A fotografia deve ser tratada como recurso configurável de segurança.

Possíveis políticas:

```text
○ PIN
○ PIN + localização
○ PIN + foto
○ PIN + localização + foto
```

A fotografia não deve ser confundida com o requisito central que caracteriza a validade do REP-P.

Caso seja usada, deve haver:

- finalidade definida;
- política de retenção;
- controle de acesso;
- proteção;
- auditoria;
- tratamento adequado de dados pessoais;
- avaliação de LGPD.

Reconhecimento facial/biometria pode ser considerado futuramente, mas não deve ser requisito do MVP sem análise específica.

---

# 14. Comprovante de registro

Após uma marcação, o sistema deve apresentar confirmação.

Exemplo:

```text
✓ Ponto registrado

08:03:17

Entrada

João da Silva

Empresa XYZ

[ Ver comprovante ]
```

O trabalhador deve conseguir consultar seus comprovantes eletronicamente.

O MTE informa que, no REP-P, o comprovante não precisa necessariamente ser impresso no momento da marcação quando houver disponibilização eletrônica após cada marcação, e que deve existir possibilidade de extração dos comprovantes das marcações realizadas nas últimas 48 horas, no mínimo.

---

# 15. AFD

O produto deverá implementar a geração do **Arquivo Fonte de Dados (AFD)** conforme o leiaute aplicável ao REP-P.

O AFD é parte do núcleo de conformidade e não deve ser tratado como um simples relatório CSV.

A implementação deve respeitar:

- leiaute vigente;
- identificação do empregador;
- identificação do REP-P;
- NSR;
- registros exigidos;
- assinatura eletrônica quando aplicável;
- nomenclatura;
- integridade.

O MTE disponibiliza leiautes oficiais e atualizados.

---

# 16. AEJ

O **Arquivo Eletrônico de Jornada (AEJ)** é gerado pelo Programa de Tratamento de Registro de Ponto.

Ele deve representar o pós-processamento dos dados do registro.

A arquitetura deverá separar claramente:

```text
AFD
=
dados originados do registro

AEJ
=
resultado do tratamento
```

O AEJ deve ser implementado conforme o leiaute oficial vigente.

---

# 17. Espelho de Ponto Eletrônico

O sistema deverá possuir geração do espelho de ponto.

O espelho deve contemplar as informações exigidas para o período, trabalhador e empregador, além das marcações e informações de jornada aplicáveis.

Exemplo conceitual:

```text
EMPRESA XYZ
CNPJ: XX.XXX.XXX/XXXX-XX

Funcionário:
João da Silva

Período:
01/09/2026 - 30/09/2026

Data       Entrada   Saída Int.   Entrada Int.   Saída
01/09      08:02     12:01        13:02          18:04
02/09      07:59     12:00        13:00          18:01
...
```

O espelho deve ser gerado a partir do modelo de tratamento/apuração, e não de dados alteráveis manualmente sem rastreabilidade.

---

# 18. Programa de Tratamento de Registro de Ponto

O PTRP deve ser tratado como um módulo de negócio próprio.

Responsabilidades:

- analisar marcações;
- identificar inconsistências;
- complementar informações permitidas;
- tratar ausências;
- tratar banco de horas;
- indicar marcações indevidas;
- gerar espelho;
- gerar AEJ;
- manter histórico de alterações/tratamentos.

A Portaria deve ser usada como referência para definir exatamente quais operações são permitidas.

---

# 19. Solicitação de correção

Uma funcionalidade essencial.

## Funcionário

```text
Esqueci de bater o ponto

Data:
27/09/2026

Horário solicitado:
18:02

Motivo:
Esquecimento

[ Enviar solicitação ]
```

## RH

```text
João da Silva
27/09/2026

08:01
12:00
13:00
18:02 ← solicitação

Motivo:
Esquecimento

[ Aprovar ]
[ Recusar ]
```

O sistema deve registrar:

- solicitante;
- data/hora;
- motivo;
- valor solicitado;
- responsável pela análise;
- decisão;
- data da decisão;
- justificativa;
- histórico.

---

# 20. Jornada

A jornada deve ser um módulo próprio.

Entidades conceituais:

```text
Jornada
Escala
Turno
Calendário
Feriados
Intervalos
Tolerâncias
Banco de horas
```

Deve haver suporte para cenários como:

- 5x2;
- 6x1;
- 12x36;
- turnos;
- jornadas noturnas;
- jornadas com intervalos;
- jornadas flexíveis;
- banco de horas;
- trabalho externo.

A implementação detalhada deve ser baseada nas regras trabalhistas aplicáveis ao contexto de cada cliente.

---

# 21. Banco de horas

O sistema deve prever:

- saldo;
- créditos;
- débitos;
- origem;
- compensações;
- histórico;
- regras de validade;
- relatórios.

Exemplo:

```text
Saldo atual
+04:32

Créditos
+08:00

Débitos
-03:28

Saldo
+04:32
```

O mecanismo deve ser auditável.

---

# 22. Horas extras

O sistema deve permitir apuração e relatórios de:

- horas extras;
- jornadas excedentes;
- períodos;
- funcionários;
- centros/locais, se aplicável;
- banco de horas.

O dashboard gerencial poderá apresentar:

```text
Horas extras no período
127h32
```

---

# 23. Faltas, atrasos e absenteísmo

Módulo gerencial:

```text
Faltas
Atrasos
Saídas antecipadas
Intervalos
Absenteísmo
```

O absenteísmo pode ser apresentado no dashboard, mas deve ser tratado como indicador gerencial e não como parte essencial do núcleo REP-P.

---

# 24. Atestados e justificativas

O sistema deve permitir:

- envio de justificativa;
- upload de documento;
- análise pelo RH;
- aprovação/reprovação;
- histórico;
- associação à jornada;
- auditoria.

Deve existir política de:

- retenção;
- acesso;
- segurança;
- privacidade.

---

# 25. Dashboard Web da Empresa / RH

O dashboard deverá ser inspirado em sistemas existentes, incluindo a referência visual apresentada, mas com arquitetura funcional própria.

## Dashboard

Exemplos de cards:

```text
┌────────────────┐ ┌────────────────┐ ┌────────────────┐
│ Funcionários   │ │ Trabalhando    │ │ Ausentes       │
│     147        │ │      92        │ │      8         │
└────────────────┘ └────────────────┘ └────────────────┘

┌────────────────┐ ┌────────────────┐ ┌────────────────┐
│ Horas Extras   │ │ Banco de Horas │ │ Atrasos        │
│    127h        │ │      38h       │ │      14        │
└────────────────┘ └────────────────┘ └────────────────┘
```

---

# 26. Estrutura sugerida do Dashboard Empresa

```text
Dashboard

Ponto
 ├── Marcações de hoje
 ├── Espelho de ponto
 ├── Inconsistências
 ├── Banco de horas
 ├── Horas extras
 ├── Faltas
 ├── Atrasos
 └── Intervalos

Funcionários
 ├── Colaboradores
 ├── Admissões
 ├── Demissões
 ├── Jornadas
 ├── Escalas
 ├── Turnos
 └── Locais de trabalho

Solicitações
 ├── Ajustes de ponto
 ├── Justificativas
 ├── Atestados
 └── Aprovações

Relatórios
 ├── Espelho
 ├── AFD
 ├── AEJ
 ├── Comprovantes
 └── Gerenciais

Configurações
 ├── Empresa
 ├── Usuários
 ├── Permissões
 ├── Regras
 ├── Tolerâncias
 ├── Geolocalização
 └── Dispositivos
```

---

# 27. Autenticação da Empresa

A tela de login do dashboard poderá seguir o conceito da referência:

```text
Empresa
Usuário
Senha

[ Entrar ]

Esqueci minha senha
```

A autenticação do dashboard deve ser separada do mecanismo de marcação do funcionário.

---

# 28. Controle de permissões

O sistema deve possuir RBAC (Role-Based Access Control).

Exemplos:

```text
Super Admin
Administrador da Empresa
RH
Gestor
Supervisor
Funcionário
Auditor / consulta
```

Permissões podem ser definidas por:

- módulo;
- ação;
- empresa;
- estabelecimento;
- departamento;
- funcionário.

---

# 29. Multi-tenancy

Este é um dos pilares da plataforma.

A plataforma será um **SaaS multi-tenant**.

Modelo conceitual:

```text
Tenant
│
├── Users
├── Employees
├── WorkSchedules
├── TimeRecords
├── Adjustments
├── Locations
├── Devices
├── Reports
└── Billing
```

Toda operação deverá respeitar o Tenant.

### Requisito crítico

Nenhuma consulta ou operação deve permitir acesso acidental a dados de outro tenant.

O `TenantId` deve ser tratado como requisito arquitetural central.

---

# 30. Estabelecimentos

Uma empresa pode possuir mais de um estabelecimento.

Exemplo:

```text
Empresa ABC
│
├── Matriz
│    └── CNPJ/Estabelecimento
│
├── Filial 01
│
└── Filial 02
```

Isso deve ser considerado também na lógica de:

- NSR;
- trabalhadores;
- locais;
- marcações;
- relatórios;
- AFD;
- configurações.

---

# 31. Super Admin da Plataforma

O painel do proprietário da plataforma não deve funcionar como um RH de todas as empresas.

Ele deve controlar o negócio SaaS.

## Indicadores

```text
Clientes
147

Clientes ativos
139

Clientes inadimplentes
5

Clientes em teste
3

Funcionários
18.492

Marcações hoje
35.281

Receita recorrente
R$ XX.XXX

Disponibilidade
99,98%
```

---

# 32. Gestão de clientes

O Super Admin deverá possuir:

```text
Clientes
 ├── Cadastrar
 ├── Editar
 ├── Ativar
 ├── Suspender
 ├── Cancelar
 ├── Consultar
 └── Impersonação/suporte, se adotada
```

Qualquer recurso de acesso administrativo a um tenant deve ser altamente auditado.

---

# 33. Assinaturas e cobrança

Mesmo que o pagamento não seja implementado no MVP, o modelo de dados deve prever:

```text
Tenant
│
├── Plano
├── Limite de funcionários
├── Funcionários ativos
├── Valor mensal
├── Vencimento
├── Status
└── Histórico financeiro
```

Possíveis modelos:

### Por quantidade de funcionários

```text
Plano Start
até 20 funcionários

Plano Business
até 100

Plano Pro
até 500

Enterprise
negociado
```

Ou:

```text
R$ X por funcionário / mês
```

A escolha comercial pode ser feita posteriormente.

---

# 34. Inadimplência

O Super Admin poderá:

- identificar clientes inadimplentes;
- suspender recursos conforme política;
- reativar;
- acompanhar histórico;
- consultar faturamento.

As regras de suspensão devem ser cuidadosamente definidas para evitar perda ou indisponibilidade indevida de dados trabalhistas.

---

# 35. Métricas de uso

O Super Admin deverá acompanhar:

- empresas ativas;
- funcionários ativos;
- marcações por dia;
- dispositivos;
- aplicativos;
- sincronizações;
- eventos offline;
- erros;
- consumo de armazenamento;
- uso de API;
- disponibilidade;
- filas de processamento.

---

# 36. Auditoria

O sistema deve possuir auditoria em nível de plataforma.

Exemplos:

```text
LOGIN
ALTERAÇÃO DE FUNCIONÁRIO
ALTERAÇÃO DE JORNADA
SOLICITAÇÃO DE CORREÇÃO
APROVAÇÃO
REJEIÇÃO
EXPORTAÇÃO
GERAÇÃO DE AFD
GERAÇÃO DE AEJ
ALTERAÇÃO DE CONFIGURAÇÃO
ACESSO ADMINISTRATIVO
```

Cada evento deve considerar, quando aplicável:

- usuário;
- tenant;
- estabelecimento;
- data/hora;
- IP;
- dispositivo;
- ação;
- entidade;
- identificador;
- valores relevantes;
- resultado.

---

# 37. Auditoria de segurança

Além da auditoria funcional, deve existir observabilidade técnica:

```text
Logs
Métricas
Traces
Erros
Alertas
Disponibilidade
Filas
Sincronização
```

O objetivo é permitir suporte proativo.

---

# 38. LGPD

O sistema tratará dados pessoais de funcionários.

O projeto deverá contemplar desde o início:

- minimização;
- finalidade;
- controle de acesso;
- criptografia;
- retenção;
- descarte;
- auditoria;
- backups;
- segregação de tenants;
- gestão de incidentes;
- controle de documentos;
- proteção de localização;
- proteção de fotos;
- políticas de privacidade.

Se forem utilizados dados biométricos ou reconhecimento facial, deverá haver análise específica de LGPD e requisitos adicionais de segurança e governança.

---

# 39. Segurança

Requisitos iniciais:

- HTTPS;
- senhas com hashing forte;
- tokens seguros;
- expiração/rotação de sessão;
- MFA para administradores;
- proteção contra brute force;
- rate limiting;
- auditoria;
- criptografia em repouso para dados sensíveis quando apropriado;
- backups;
- segregação de tenants;
- secrets fora do código;
- princípio do menor privilégio.

---

# 40. Arquitetura tecnológica sugerida

Considerando um ecossistema .NET, uma opção coerente seria:

## Backend

**ASP.NET Core**

Responsável por:

- API;
- autenticação;
- regras de negócio;
- REP-P;
- tratamento;
- relatórios;
- multi-tenancy;
- administração.

## Dashboard Web

**Blazor**

Pode proporcionar integração forte com o ecossistema .NET.

## Aplicativo

**.NET MAUI**

Para:

- Android;
- iOS.

## Banco

Possibilidades:

- SQL Server;
- PostgreSQL.

A decisão final deve considerar custos, hospedagem, equipe, experiência e requisitos de escala.

## Storage

Storage de objetos para:

- documentos;
- fotos;
- comprovantes;
- arquivos gerados.

## Infraestrutura

Possível arquitetura:

```text
Internet
   ↓
Load Balancer / Reverse Proxy
   ↓
ASP.NET Core API
   ↓
┌───────────────┬───────────────┬───────────────┐
│ SQL           │ Cache         │ Object Storage│
│ Database      │ Redis opcional│               │
└───────────────┴───────────────┴───────────────┘
```

---

# 41. Arquitetura funcional

```text
                    ┌───────────────────┐
                    │   WEB / BROWSER   │
                    └─────────┬─────────┘
                              │
                    ┌─────────▼─────────┐
                    │       API         │
                    └─────────┬─────────┘
                              │
       ┌──────────────────────┼───────────────────────┐
       │                      │                       │
       ▼                      ▼                       ▼
 Authentication          REP-P Core              Tenant
       │                      │                       │
       │                 ┌────┴────┐                 │
       │                 │         │                 │
       │                AFD       AEJ                 │
       │                           │                  │
       │                           ▼                  │
       │                     Time Records             │
       │                                              │
       └──────────────────────┬───────────────────────┘
                              │
                              ▼
                       Treatment Engine
                              │
                  ┌───────────┼───────────┐
                  ▼           ▼           ▼
              Espelho       AEJ        Relatórios
```

---

# 42. Arquitetura do aplicativo

```text
.NET MAUI
   │
   ├── Authentication
   ├── Clock
   ├── GPS
   ├── Camera
   ├── Offline Store
   ├── Sync Engine
   └── REP-P Collector
```

---

# 43. Separação entre núcleo REP-P e módulos gerenciais

Esta separação é importante.

## Núcleo REP-P

- marcação;
- armazenamento;
- integridade;
- NSR;
- AFD;
- tratamento;
- AEJ;
- espelho;
- comprovante;
- auditoria;
- conformidade.

## Gestão de RH

- funcionários;
- jornadas;
- escalas;
- banco de horas;
- atestados;
- solicitações;
- permissões.

## Analytics

- absenteísmo;
- horas extras;
- atrasos;
- turnover;
- aniversariantes;
- indicadores.

## SaaS

- clientes;
- planos;
- assinaturas;
- faturamento;
- inadimplência;
- uso;
- infraestrutura.

Isso evita misturar requisitos legais do núcleo de ponto com funcionalidades comerciais/gerenciais.

---

# 44. Modelo de dados conceitual

Entidades iniciais:

```text
Tenant
Establishment
Company
User
Role
Permission

Employee
EmployeePin
EmployeeDevice

WorkSchedule
Shift
Calendar
Holiday
Tolerance
BreakRule
TimeBank

TimeRecord
TimeRecordEvidence
TimeRecordLocation
TimeRecordPhoto

AdjustmentRequest
AdjustmentDecision
Justification
MedicalDocument

AttendanceCalculation
TimeBankEntry

ProofOfRegistration
Timesheet

AFDExport
AEJExport

AuditLog
SecurityEvent

Subscription
Plan
Invoice
Payment
```

A modelagem definitiva deve ser criada depois da especificação detalhada da Portaria e dos fluxos.

---

# 45. Funcionário e PIN

O funcionário terá um PIN individual.

Requisitos possíveis:

- PIN exclusivo por funcionário;
- PIN armazenado de forma segura;
- possibilidade de troca;
- bloqueio após tentativas;
- rate limiting;
- auditoria;
- possibilidade de desativação;
- regeneração;
- política de complexidade.

O PIN não deve ser armazenado em texto puro.

---

# 46. Dispositivos

O sistema poderá identificar dispositivos associados ao funcionário.

Exemplo:

```text
Employee
   │
   ├── Device A
   └── Device B
```

Isso pode ajudar em:

- segurança;
- auditoria;
- suporte;
- detecção de uso anômalo.

Não deve ser usado para criar restrições que impeçam indevidamente a marcação sem uma regra de negócio válida.

---

# 47. Dashboard de conformidade

Pode ser útil existir uma área específica para a empresa:

```text
Conformidade

✓ Dados da empresa
✓ Estabelecimento
✓ Configuração do REP-P
✓ Atestado técnico
✓ Certificado / assinatura
✓ Último AFD
✓ Último AEJ
✓ Último backup
✓ Integridade
```

Isso facilita suporte e implantação.

---

# 48. Fluxo completo de uma marcação

```text
Funcionário
    ↓
Terminal Web / App
    ↓
Identificação
    ↓
PIN / sessão autenticada
    ↓
Verificações configuradas
    ├── localização
    ├── dispositivo
    └── foto
    ↓
Coletor de marcação
    ↓
Registro original
    ↓
NSR
    ↓
Armazenamento
    ↓
Comprovante
    ↓
Tratamento
    ↓
Apuração
    ↓
Espelho / AEJ / relatórios
```

---

# 49. Fluxo offline

```text
Funcionário
    ↓
App
    ↓
Sem internet
    ↓
Registro local seguro
    ↓
Fila
    ↓
Internet retorna
    ↓
Sincronização
    ↓
Servidor
    ↓
Validação
    ↓
Registro persistido
    ↓
Confirmação
```

---

# 50. Fluxo de correção

```text
Funcionário
    ↓
Solicita correção
    ↓
Motivo
    ↓
RH recebe
    ↓
Analisa
    ├── Aprova
    └── Recusa
    ↓
Tratamento
    ↓
Auditoria
    ↓
Espelho atualizado
```

---

# 51. Fluxo do Super Admin

```text
Super Admin
    ↓
Dashboard
    ↓
Clientes
    ├── Ativos
    ├── Teste
    ├── Suspensos
    └── Cancelados
    ↓
Cliente
    ├── Plano
    ├── Funcionários
    ├── Uso
    ├── Faturamento
    └── Saúde técnica
```

---

# 52. MVP recomendado

O maior risco do projeto é tentar implementar toda a plataforma de uma vez.

O MVP deve priorizar o núcleo.

## Fase 1 — Núcleo REP-P

- multi-tenant;
- empresas;
- estabelecimentos;
- funcionários;
- usuários/RH;
- jornadas básicas;
- registro de ponto;
- PIN;
- terminal Web;
- aplicativo;
- offline;
- sincronização;
- hora de referência;
- registro original;
- comprovante;
- espelho;
- tratamento;
- AFD;
- AEJ;
- auditoria.

## Fase 2 — Gestão

- solicitações de ajuste;
- aprovação;
- atestados;
- banco de horas;
- horas extras;
- faltas;
- atrasos;
- feriados;
- escalas avançadas;
- geofence.

## Fase 3 — Gestão empresarial

- dashboard;
- indicadores;
- absenteísmo;
- turnover;
- relatórios;
- exportações.

## Fase 4 — SaaS

- planos;
- assinaturas;
- cobrança;
- inadimplência;
- limites;
- métricas;
- Super Admin.

## Fase 5 — Segurança avançada

- foto;
- device binding;
- detecção de fraude;
- recursos biométricos, se fizer sentido após análise específica.

---

# 53. O que NÃO deve ser feito no início

Evitar começar com:

- reconhecimento facial;
- dezenas de dashboards;
- cobrança completa;
- automações complexas;
- analytics avançado;
- múltiplas integrações;
- recursos de RH que não tenham relação com o núcleo;
- UI extremamente elaborada antes da definição do modelo de dados.

Primeiro deve funcionar corretamente o fluxo:

```text
Funcionário
→ Marca
→ Registro original
→ Comprovante
→ Tratamento
→ Espelho
→ AEJ / AFD
```

---

# 54. Avaliação do planejamento inicial

| Área | Avaliação | Observação |
|---|---|---|
| App funcionário | Boa | Deve ser parte central do produto |
| Dashboard empresa | Boa | Precisa ser expandido para gestão completa |
| Super Admin | Boa | Deve administrar SaaS, não substituir o RH |
| Multi-tenant | Essencial | Deve estar no centro da arquitetura |
| Geolocalização | Boa | Deve ser configurável |
| Foto | Opcional | Deve ter finalidade e governança |
| Offline | Essencial | Especialmente no app |
| PIN | Viável | Deve ser tratado como mecanismo de identificação seguro |
| Jornadas | Precisa detalhar | É um módulo importante |
| Banco de horas | Necessário | Deve entrar no escopo |
| Tratamento de ponto | Crítico | Precisa de especificação própria |
| REP-P | Crítico | Deve ser o eixo do projeto |
| AFD | Crítico | Deve entrar no núcleo |
| AEJ | Crítico | Deve entrar no núcleo |
| Espelho | Crítico | Deve ser especificado detalhadamente |
| Comprovante | Crítico | Deve fazer parte do fluxo de marcação |
| Assinaturas | Crítico | Deve ser considerado desde a arquitetura |
| Atestado Técnico | Crítico | Deve fazer parte do processo de implantação/conformidade |
| Auditoria | Essencial | Deve existir em nível funcional e técnico |
| LGPD | Essencial | Deve ser requisito desde o início |
| Cobrança | Boa | Pode ser posterior ao MVP |
| Dashboard gerencial | Boa | Pode ser evoluído após o núcleo |
| Turnover | Secundário | Módulo gerencial |
| Arquitetura | Em definição | Deve ser detalhada antes da implementação |

---

# 55. Principais riscos

## Risco 1 — Começar pelo layout

O maior risco é começar fazendo telas antes de definir o modelo de dados e o fluxo REP-P.

**Mitigação:** especificar primeiro os processos e regras.

## Risco 2 — Tratar ponto como CRUD

Um sistema de ponto não deve ser:

```text
INSERT
UPDATE
DELETE
```

sem histórico.

**Mitigação:** separar registro original, tratamento e auditoria.

## Risco 3 — Ignorar AFD/AEJ

Deixar os arquivos para o final pode obrigar uma remodelagem significativa.

**Mitigação:** modelar desde o início.

## Risco 4 — Multi-tenancy inadequado

Uma falha de isolamento pode expor dados de clientes diferentes.

**Mitigação:** TenantId, autorização centralizada, testes de isolamento e revisão de todas as queries.

## Risco 5 — Offline mal projetado

Marcação offline pode gerar duplicidade ou inconsistência.

**Mitigação:** fila idempotente, identificador único do evento, estados de sincronização e testes de reconexão.

## Risco 6 — Regras de jornada excessivamente simplificadas

Diferentes clientes terão diferentes jornadas.

**Mitigação:** criar motor de regras extensível.

## Risco 7 — Segurança adicionada tarde

Localização, fotos e documentos podem conter dados sensíveis.

**Mitigação:** segurança e LGPD desde o desenho.

---

# 56. Decisões arquiteturais que precisam ser tomadas

Antes do desenvolvimento definitivo:

1. SQL Server ou PostgreSQL?
2. Modelo de multi-tenancy:
   - banco compartilhado;
   - schema por tenant;
   - banco por tenant?
3. Cloud/hospedagem.
4. Estratégia de storage.
5. Estratégia de cache.
6. Estratégia de filas.
7. Autenticação.
8. MFA.
9. Estratégia de offline.
10. Fonte de hora.
11. Modelo de NSR.
12. Estratégia de assinatura.
13. Geração de AFD.
14. Geração de AEJ.
15. Estratégia de relatórios.
16. Retenção de documentos.
17. Backup.
18. Disaster Recovery.
19. Observabilidade.
20. Estratégia de versionamento do REP-P.
21. Registro de programa no INPI.
22. Processo de Atestado Técnico e Termo de Responsabilidade.
23. Política de atualização do sistema.
24. Processo de auditoria de conformidade.

---

# 57. Documentos que devem ser produzidos depois deste

A partir deste documento, recomenda-se criar:

## 01 — Visão Geral do Produto

- objetivo;
- personas;
- interfaces;
- fluxos principais;
- escopo.

## 02 — Requisitos REP-P / Portaria 671

- requisitos legais;
- requisitos técnicos;
- AFD;
- AEJ;
- espelho;
- comprovante;
- assinaturas;
- armazenamento;
- integridade;
- auditoria.

## 03 — Requisitos Funcionais

- funcionários;
- jornadas;
- escalas;
- ponto;
- banco de horas;
- faltas;
- atestados;
- solicitações;
- relatórios.

## 04 — Arquitetura

- backend;
- frontend;
- app;
- banco;
- API;
- storage;
- filas;
- cache;
- observabilidade;
- segurança;
- multi-tenancy.

## 05 — Modelo de Dados

Definição detalhada das entidades, relacionamentos, índices e regras.

## 06 — UX/UI

- fluxos;
- wireframes;
- componentes;
- responsividade;
- acessibilidade;
- estados de erro.

## 07 — Roadmap

- MVP;
- Fase 2;
- Fase 3;
- Fase 4;
- Fase 5.

## 08 — Plano de Testes

- testes unitários;
- integração;
- segurança;
- multi-tenancy;
- offline;
- sincronização;
- AFD;
- AEJ;
- jornada;
- carga;
- auditoria.

---

# 58. Referências oficiais

A implementação deve ser conferida contra as fontes oficiais vigentes do Ministério do Trabalho e Emprego.

### Página oficial do REP

https://www.gov.br/trabalho-e-emprego/pt-br/assuntos/inspecao-do-trabalho/fiscalizacao-do-trabalho/rep

A página do MTE informa que foi atualizada em 31/07/2026 e disponibiliza, entre outros materiais, os leiautes de AFD e AEJ e a Portaria nº 671/2021.

### Perguntas e Respostas da Portaria 671/2021

https://www.gov.br/trabalho-e-emprego/pt-br/assuntos/inspecao-do-trabalho/fiscalizacao-do-trabalho/Perguntas%20e%20Respostas%20REP

### Portarias vigentes

https://www.gov.br/trabalho-e-emprego/pt-br/assuntos/legislacao/portarias-1/portarias-vigentes-3

### Portaria MTP nº 671/2021 — versão compilada

Consultar sempre a versão vigente disponibilizada pelo MTE antes de fechar qualquer requisito de conformidade.

---

# 59. Observação sobre conformidade

Este documento deve ser tratado como **planejamento técnico e funcional inicial**.

A conformidade legal não deve ser presumida apenas porque uma funcionalidade foi implementada.

Antes de comercializar o produto como solução de registro eletrônico de ponto, é necessário validar:

- versão vigente da Portaria;
- anexos e leiautes;
- requisitos técnicos do REP-P;
- AFD;
- AEJ;
- comprovantes;
- espelho;
- assinaturas;
- Atestado Técnico e Termo de Responsabilidade;
- registro do programa no INPI;
- requisitos de segurança;
- requisitos de proteção de dados;
- procedimentos de atualização;
- documentação técnica.

---

# 60. Visão final do produto

A visão de longo prazo pode ser resumida assim:

```text
                         PLATAFORMA SaaS
                              │
          ┌───────────────────┼───────────────────┐
          │                   │                   │
          ▼                   ▼                   ▼
     SUPER ADMIN          EMPRESAS            REP-P
          │                   │                   │
          │            ┌──────┴──────┐            │
          │            │             │            │
          │            ▼             ▼            │
          │          RH             APP           │
          │                          │             │
          │                    ┌─────▼─────┐       │
          │                    │ MARCAÇÃO  │       │
          │                    └─────┬─────┘       │
          │                          │             │
          └──────────────────────────┼─────────────┘
                                     │
                              REGISTRO ORIGINAL
                                     │
                               TRATAMENTO
                                     │
                    ┌────────────────┼────────────────┐
                    ▼                ▼                ▼
                 ESPELHO            AFD              AEJ
                    │
                    ▼
               RELATÓRIOS
                    │
                    ▼
             GESTÃO / ANALYTICS
```

O objetivo não é apenas construir um aplicativo que "bate ponto".

O objetivo é construir uma **plataforma de registro e tratamento de jornada**, com:

- experiência simples para o funcionário;
- ferramentas completas para o RH;
- núcleo técnico preparado para REP-P;
- isolamento seguro entre empresas;
- capacidade de operação offline;
- rastreabilidade;
- auditoria;
- relatórios;
- possibilidade de crescimento para um SaaS comercial.

---

# 61. Próximo passo recomendado

Antes de iniciar a implementação, transformar este documento em uma especificação mais detalhada, começando por:

```text
01. Requisitos REP-P
        ↓
02. Fluxos de marcação
        ↓
03. Modelo de dados
        ↓
04. Multi-tenancy
        ↓
05. API
        ↓
06. App
        ↓
07. Dashboard
        ↓
08. Super Admin
        ↓
09. Testes de conformidade
        ↓
10. MVP
```

A prioridade deve ser **definir corretamente o núcleo do registro de ponto antes de investir pesado na interface**.

