# Decisões do dono (fonte de verdade para o desenvolvimento)

## 1. Produto e marca
- PersonaliPonto é a plataforma **white-label** de ponto **REP-P (Portaria 671/2021)** do dono (Owner).
- Marca própria **preto + dourado**, exclusiva (ver `docs/marca`). Revendedores **não podem** usar essa combinação.
- A paleta azul antiga é o **tema padrão dos revendedores** (`docs/marca/tema-neutro-azul.md`).

## 2. Hierarquia
Owner → Revendedor (Super Admin, marca própria) → Parceiro opcional (Admin do parceiro) → Empresa/entidade (RH) → Funcionário.

- Máximo de **3 níveis de canal** (parceiro não tem parceiros).
- Parceiro só vê sua carteira; vê cadastro + financeiro, **não** dados de ponto (para isso usa modo suporte auditado).
- Entidade pública: cada CNPJ = uma empresa, agrupadas por **Município (IBGE)**.
- Campo **"canal dono"** em cada empresa.
- Sem marca de venda direta do Owner por ora (foco em revenda; leads diretos repassados a revendedores). Tecnicamente o Owner pode ter um "revendedor da casa".
- Fornecedor padrão exibido no painel do revendedor = nome da marca do próprio revendedor.

## 3. Revendedor de referência: Tempo Certo
- Revendedor "Tempo Certo" (sogro), foco em prefeituras e empresas; **Premium isento por 24 meses** (fundador).
- **Info Public** = cliente pagante do Tempo Certo. Se virar parceira e tiver ≥1 prefeitura ativa, mensalidade interna isenta.
- Integração mínima: link **"Bater ponto"** na tela de login dela.
- Integração com folha (se parceira): exportação da frequência mensal com de-para de verbas por município, via API/tabela intermediária — **nunca escrever no banco deles** (a folha deles lê um banco por município).
- Futuro: SSO de parceiro com município/entidade.

## 4. Cobrança
### Owner → revendedor (por uso)
- R$ 12 por empresa ativa.
- Por funcionário ativo, em **faixas marginais**:

| Faixa | Preço/funcionário |
|---|---|
| até 2.000 | R$ 0,30 |
| 2.001 a 5.000 | R$ 0,25 |
| acima de 5.000 | R$ 0,20 |

- Mínimo **R$ 290/mês**; a soma inclui funcionários dos parceiros do revendedor.
- Funcionário ativo = ≥1 marcação na competência.
- Sem pró-rata: cobrança começa no mês seguinte à entrada.

### Revendedor → parceiro
Fatura agrupada por funcionários ativos (preço definido pelo revendedor).

### Revendedor → clientes
Grade atual.

### Premium do revendedor — R$ 149/mês
Remove "by PersonaliPonto", domínio próprio, cobrança automática dos clientes (Asaas do revendedor), relatórios avançados, destaque no diretório, página própria gerada. Compras de Premium/adicionais apenas no **painel web** (nunca no app mobile).

### Serviço avulso
App próprio nas lojas: R$ 1.500 + R$ 200/mês.

## 5. Pagamentos (Asaas)
- Owner cobra revendedores (Pix/boleto/cartão, webhook, baixa automática, NFS-e se viável).
- Revendedor Premium conecta a **própria** conta Asaas colando a chave API (opção 1). Dinheiro dos clientes do revendedor **nunca** passa pela PersonaliPonto.
- Clientes públicos: cobrança manual/empenho, régua tolerante.

## 6. Inadimplência de revendedor/parceiro
- D+5: aviso · D+15: painel bloqueado · D+30: suspensão.
- **NUNCA** bloquear a marcação do trabalhador nem o acesso a AFD/AEJ/comprovantes.

## 7. White-label
- Personalizável: cores, logo, favicon, fontes, imagem de login, estilo de menu (lateral/superior/compacto), ordem/visibilidade do menu, layout de cards, textos; **prévia ao vivo**.
- Conteúdo legal travado (comprovante, AFD, AEJ, espelho — espelho pode ter só o logo no cabeçalho).
- "by PersonaliPonto" no padrão.
- Subdomínio automático `{slug}.personaliponto.com.br`; domínio próprio via CNAME (ex.: `ponto.tempocerto.com.br`) com TLS automático; hospedagem VPS + Caddy on-demand TLS.
- E-mails transacionais com domínio do revendedor (SPF/DKIM).

## 8. App mobile
- Um único app "PersonaliPonto" nas lojas (ícone e nome fixos, sem troca de ícone).
- Marca do revendedor aplicada dinamicamente via link/QR da empresa ou após login; cache offline.
- CPF com vários vínculos → tela de escolha.
- iOS e Android nativos (MAUI); builds iOS em Mac na nuvem (dono não tem Mac).
- App próprio opcional: publicado pelo Owner (binário) nas contas do revendedor, pipeline multi-marca automatizado; código nunca compartilhado.
- Relógio: hora oficial do servidor (NTP) + relógio monotônico; offline após reinício sem âncora deve **bloquear até ressincronizar**; não exigir horário automático do celular.

## 9. Nali (assistente IA)
Para todos os usuários (Claude Haiku 4.5), avatar feminino ilustrado, RAG no manual + contexto da tela, só explica, sem dados pessoais, nome renomeável pelo revendedor, cotas de custo.

## 10. Prefeituras
- Reconhecimento facial com prova de vida (LGPD, dado sensível: consentimento/termo, RIPD, retenção/exclusão, alternativa sem biometria).
- Vínculos (efetivo/comissionado/contratado), plantões 12x36/24x72, relatório de frequência mensal, exportação para folha.

## 11. Relógios físicos REP-C
Clientes do sogro usam EZPoint/CotiPonto com relógio: importar AFD de REP-C (arquivo e APIs como Control iD) no tratamento. Alternativa: tablet com terminal web.

## 12. Site e diretório
- `personaliponto.com.br` ("Personalize e seja revendedor", planos).
- Diretório "Encontre um revendedor": perfil, cidades, lead com consentimento LGPD, rodízio + destaque, aviso ao revendedor.
- Página própria gerada para revendedor Premium sem site.
- Subdomínios: `app.`, `api.`.

## 13. Documentação legal a produzir
- Atestado Técnico e Termo de Responsabilidade gerados por cliente (fabricante = PersonaliPonto; INPI/certificado ICP-Brasil no CNPJ da PersonaliPonto).
- Modelos de contrato (revenda, parceiro, app próprio), termos de uso, política de privacidade, acordo de tratamento de dados (LGPD), SLA, termo de consentimento biométrico, RIPD, modelo de atestado de capacidade técnica, cláusula anticorrupção.
- Suporte: N1 do revendedor; N2/N3 da PersonaliPonto.

## 14. Pendências do dono
- CNPJ (ME, CNAE de software; regularizar DAS).
- INPI (marca e programa).
- Certificado ICP-Brasil.
- D-U-N-S / contas Apple e Google.
- Supabase em sa-east-1 (Session pooler).
- Revisão jurídica paga (futura).
