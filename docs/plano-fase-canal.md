# Plano da fase de canal

Etapas em ordem de execução.

| Etapa | Escopo |
|---|---|
| **E0** Fundação ops | *Feito:* CI, README, marca. |
| **E1** Hierarquia + RLS de canal | *Feito:* ver detalhes abaixo (migration `CanaisHierarquia`, RLS v2 em `SegurancaBanco.FuncoesCanal`). |
| **E2** Apuração de uso + faturamento | *Feito:* ver detalhes abaixo (migration `FaturamentoCanais`, RLS v3 em `SegurancaBanco.FuncoesCanalV3`). |
| **E3** Asaas da plataforma | *Feito:* ver detalhes abaixo (migration `PagamentosAsaas`, RLS v4 em `SegurancaBanco.FuncoesPagamentos`). |
| **E4** White-label v1 | Tokens, host→marca, subdomínio, editor com prévia, menu registrado, textos; bloqueio preto+dourado; PDFs legais inalterados. |
| **E5** App com marca dinâmica | Multi-vínculo + bloqueio offline sem âncora. |
| **E6** Documentos legais | Atestado/termo por cliente + modelos jurídicos em `docs/legal/modelos`. |
| **E7** Prefeitura RH | Vínculos, plantões, frequência, exportação folha, link "Bater ponto". |
| **E8** Domínio próprio + e-mail | |
| **E9** Asaas do revendedor | *Feito:* ver detalhes abaixo (mesma migration da E3). |
| **E10** Nali | |
| **E11** Importação REP-C | |
| **E12** Site + diretório + página própria | |
| **E13** Facial com prova de vida | |
| **E14** Pipeline app próprio multi-marca | Inclui Dockerfile/Caddyfile de deploy. |

## E1 — detalhes
- `Canal` com tipo Owner/Revendedor/Parceiro e caminho materializado.
- `Tenant.CanalDonoId`, `MunicipioId`, `TipoEntidade`.
- `Usuario` generalizado com Escopo Plataforma/Canal/Tenant.
- Papéis: AdminRevendedor, SuporteRevendedor, AdminParceiro, SuporteParceiro.
- `RequestContext.DefinirCanal` com lista de tenants visíveis em `set_config` (`app.tenants_canal`) e política RLS `tenant_id = ANY(...)`.
- Tabelas de canal com `canal_id`.
- Suporte generalizado por nível.
- `IsSystem` só para Owner/rotinas.
- Páginas `Canal/*` separadas das `Plataforma/*`.
- Testes de isolamento entre irmãos no Postgres com role `personaliponto_app`.

### E1 — decisões de implementação
- Carteira visível ao canal = `assinaturas` e `faturas` (+ `tenants`); tabelas de ponto/RH seguem só do tenant — o canal (revendedor ou parceiro) as lê apenas em modo suporte auditado.
- Profundidade e caminho materializado calculados por trigger (`canais_hierarquia`); pai/tipo imutáveis; situação do canal só alterada por ascendente ou plataforma; transferência de cliente entre canais só dentro da hierarquia (`tenants_canal_dono`).
- Unicidade global (e-mail, CNPJ, slugs) via `IUnicidadeGlobal` (consulta pontual em modo sistema, responde só sim/não).
- ~~Pendente para E2: efeitos de `StatusCanal`~~ — feito na E2.

## E2 — detalhes
- **Apuração** (`ApuracaoCanalService`, rotina `RotinasPlataforma`): no início de cada mês apura a competência anterior por revendedor (inclui clientes dos parceiros) e grava snapshot imutável (`apuracoes_uso_canal` + `apuracoes_uso_tenant`, triggers de imutabilidade, índice único canal+competência → idempotente).
- **Tabela de preço versionada** (`tabelas_preco_canal`, vigência por competência; semente R$ 12/empresa, 0,30 até 2.000, 0,25 de 2.001 a 5.000, 0,20 acima, mínimo R$ 290, Premium R$ 149, app próprio R$ 1.500 + R$ 200/mês). Cálculo puro em `CalculadoraUsoCanal` (faixas marginais), testado nas bordas 2000/2001/5000/5001 e no mínimo.
- **Faturas de canal** (`faturas_canal` + `itens_fatura_canal`): Owner → revendedor (uso + complemento ao mínimo + Premium/isenção + app próprio) e revendedor → parceiro (funcionários ativos × preço do revendedor, mínimo opcional, isenção por ≥ N clientes públicos ativos avaliada sobre o snapshot). Vencimento dia 10 do mês seguinte. Baixa/isenção/estorno manual pelo recebedor (Owner na plataforma; revendedor para parceiros) até a E3.
- **Premium** (`assinaturas_premium`): ativar/cancelar pelo admin do revendedor em "Minha conta PersonaliPonto" (só web). `FaturamentoCanalService.PremiumAtivoAsync` é a flag para as próximas etapas. Isenção configurável até data (`canais.premium_isento_ate`, botão "24m" para fundador) só pela plataforma.
- **Régua de canal**: D+5 `Aviso` (banner em todas as páginas do canal), D+15 `PainelBloqueado` e D+30 `Suspenso` (páginas do canal redirecionam para `/canal/minha-conta`; `DbContext.Validar` recusa qualquer gravação do usuário de canal, inclusive em modo suporte). Cliente cujo canal (ou ascendente) está suspenso vê aviso no painel de RH. Regularizou → `Ativo`. Situação definida manualmente (`canal.status_alterado`) não é desfeita pela régua, só agravada. A régua nunca altera tenants: marcação, comprovante, espelho e AFD/AEJ seguem (teste `Canal_suspenso_nunca_bloqueia_...`).
- **Telas**: `/plataforma/faturamento-canais` (grade revendedores × competências, apuração detalhada, isenções/avulsos, tabela de preço), `/canal/minha-conta` (revendedor: faturas, uso estimado do mês, Premium; parceiro: suas faturas), `/canal/parceiros` (preço por funcionário, mínimo, isenção e faturas dos parceiros).

### E2 — decisões de implementação
- Empresa ativa = cliente `Ativo`/`Inadimplente` com ≥ 1 funcionário ativo; funcionário ativo = CPF distinto com ≥ 1 marcação REP-P (tipo 7) na competência (fuso America/Sao_Paulo), contado por cliente. Clientes em teste/suspensos/cancelados não contam. O status do cliente é o do momento da apuração.
- O mínimo de R$ 290 vale sobre o uso (empresas + funcionários); Premium e serviços avulsos somam por fora.
- Sem pró-rata: canal (revendedor ou parceiro) não recebe fatura na competência de entrada (`canais.criado_em`). Premium é cobrado mês cheio em toda competência em que esteve ativo; isento quando a competência ≤ data de isenção. Implantação do app próprio cobrada uma única vez (`app_proprio_implantacao_cobrada`).
- Fatura do parceiro só é gerada quando o revendedor define preço por funcionário; isenção vira item negativo e status `NaoCobrada`.
- A contagem de marcações para a estimativa do revendedor roda em modo sistema controlado e devolve só números agregados (o canal continua sem ler dados de ponto).
- **RLS v3 por comando** (defesa em profundidade, políticas `p_select/p_insert/p_update/p_delete`; `aplicar_rls` agora remove todas as políticas antes de recriar): faturas/assinaturas de cliente — UPDATE só pelo canal (cliente não altera o próprio financeiro), DELETE só sistema; `audit_logs` e `acessos_suporte` só SELECT/INSERT fora do sistema; `acessos_suporte` visível apenas ao canal de origem (cliente não vê); tenants/canais DELETE só sistema; `faturas_canal` visível a pagador e recebedor (parceiro não vê a do revendedor com o Owner), INSERT/DELETE só sistema, UPDATE só recebedor e só campos de baixa (trigger `faturas_canal_protecao`); itens seguem a fatura; condições comerciais (`premium_isento_ate`, app próprio) só sistema e preço do parceiro só ascendente (trigger `canais_hierarquia` v3).
- Correções da revisão da E1: suporte da plataforma em painel de canal recebe papel por origem (Super Admin → Admin do canal; Suporte → Suporte do canal); no cliente o suporte segue como `AdminEmpresa` (precisa operar cadastro/jornada; tudo auditado). Acessos de suporte da plataforma registram canal de origem = Owner (invisível a revendedores/parceiros). Encerramento de acesso roda em modo sistema restrito ao registro do próprio usuário. Down da `CanaisHierarquia` remove todas as políticas antes de reaplicar a v1 e apaga acessos sem tenant antes do NOT NULL. Interceptor já reaplicava o contexto em comandos escalares — coberto por teste.
- Pendências: ~~pagamento real/baixa automática (E3 Asaas)~~ e ~~NFS-e~~ feitos na E3; efeitos visuais do Premium (E4); e-mail de aviso da régua de canal.

## E3/E9 — detalhes (Asaas)
- **Gateway**: `IGatewayPagamento` (Infrastructure/Pagamentos) com `AsaasGateway` (API v3: `customers`, `payments` com `billingType=UNDEFINED`, `pixQrCode`, `identificationField`, `webhooks`, `myAccount/commercialInfo`, `invoices`). Header `access_token` + `User-Agent`; chave nunca em log/exceção; timeout configurável.
- **Cobrança na fatura**: campos `gateway_*` em `faturas_canal` e `faturas` (status Nenhuma/Pendente/Emitida/Falha/Cancelada/Estornada, id do pagamento, conta emissora, link, boleto, linha digitável, Pix copia-e-cola e QR). Emissão via outbox (`gateway.cobranca`, retentativa exponencial) na mesma transação da fatura; referência externa `canal:{id}`/`cliente:{id}` torna a retentativa idempotente (reaproveita cobrança existente). Asaas fora do ar não impede a geração.
- **E3**: faturas recebidas pelo Owner → conta da plataforma (`Asaas:Owner:ChaveApi`). Compra do Premium (`ComprasCanalService`, só painel web) gera fatura avulsa imediata (`TipoFaturaCanal.Avulsa`, vence em 3 dias) e a mensal da competência não repete o Premium; contratação do app próprio (pela plataforma) cobra a implantação na hora. NFS-e opcional (`Asaas:NotaFiscal:Habilitada`, desligada).
- **E9**: `ContaGatewayService` (admin do revendedor, Premium ativo): valida a chave (myAccount), cria webhook na conta dele com authToken aleatório (48 chars), guarda chave cifrada (Data Protection, propósito dedicado) + 4 finais + hash SHA-256 do token (`contas_gateway`, uma ativa por canal); desconectar remove o webhook e apaga a chave cifrada. Faturas de clientes do revendedor e revendedor → parceiro passam a ser cobradas na conta dele. Cliente `Publica` nunca é cobrado automaticamente. Sem split: dinheiro nunca passa pela conta do Owner.
- **Webhook** `POST /api/pagamentos/asaas/webhook` no projeto **Web** (mesmo host de `Asaas:UrlPublica`): conta identificada pelo authToken (`asaas-access-token`; Owner por comparação em tempo constante, revendedor por hash); idempotente por (origem, id do evento) com índice único em `eventos_gateway`; evento mais antigo que o último aplicado na fatura é registrado e ignorado (fora de ordem); a conta de origem precisa ser a recebedora da fatura (roteamento); RECEIVED/CONFIRMED → baixa (`BaixarViaGatewayAsync`), REFUNDED/chargeback → estorno, DELETED/RESTORED → estado da cobrança; régua reavaliada; falha ao aplicar → outbox `gateway.evento`. Limite de taxa por IP; corpo até 256 KB.
- **RLS v4**: `contas_gateway` visível/gravável só pelo próprio canal (DELETE só sistema), `eventos_gateway` só sistema; trigger de `faturas_canal` impede o recebedor de alterar os campos do gateway.
- **Auditoria**: `gateway.conta_conectada`/`gateway.conta_desconectada` (sem a chave), `fatura_canal.paga_gateway`/`estornada_gateway`, `fatura.paga_gateway`/`estornada_gateway`, `fatura_canal.avulsa_gerada`.
- **Rotina**: varredura enfileira faturas "pendentes" sem mensagem viva no outbox (ex.: geradas pelo canal ao cadastrar cliente, quando o outbox não é gravável).
- Pendências: cadastro manual do webhook da conta da plataforma e chaves reais (dono); reenvio manual de cobrança em falha definitiva (após 10 tentativas); Owner cobrando clientes diretos pelo Asaas; e-mail de aviso da régua.

## Riscos principais
- Pooler Supabase em *transaction mode* quebra `SET ROLE`.
- Vazamento de dados via `IsSystem`.
- Índices únicos por CPF (CPF com vários vínculos).
- Apple guideline 4.3 para apps clonados.
- Custódia de chave Asaas de terceiros.
- LGPD: biometria e transferência internacional (Anthropic, provedor facial).
