# Modelos jurídicos — PersonaliPonto

> **Todos os modelos foram elaborados para revisão por advogado; não substituem assessoria jurídica.** Revise antes do primeiro uso (ver `docs/decisoes-negocio.md`, item 14 — revisão jurídica paga).

Campos entre `[ ]` devem ser preenchidos; `{{auto: ...}}` são preenchidos pelo sistema.

| Documento | Partes | Quando usar | O que o dono precisa preencher |
|---|---|---|---|
| [contrato-revenda.md](contrato-revenda.md) | PersonaliPonto ↔ Revendedor | Ao credenciar cada revendedor | Dados da PersonaliPonto (CNPJ, sede, representante), foro, multa de rescisão antecipada, prazos de contestação/fatura, opção de isenção de fundador (ex.: Tempo Certo, 24 meses) |
| [contrato-parceiro.md](contrato-parceiro.md) | Revendedor ↔ Parceiro | Quando um revendedor habilita um parceiro (ex.: Info Public) | Modelo entregue ao revendedor: preço por funcionário ativo, mínimo, destino da carteira na saída |
| [contrato-app-proprio.md](contrato-app-proprio.md) | PersonaliPonto ↔ Revendedor | Venda do app com marca própria nas lojas | Forma de pagamento da implantação, prazos de publicação, política de chaves |
| [termos-de-uso.md](termos-de-uso.md) | Plataforma ↔ Empresas e usuários | Aceite no primeiro acesso (web e app) | Contatos de suporte e DPO, foro, prazo de aviso de alterações |
| [politica-de-privacidade.md](politica-de-privacidade.md) | Público | Publicar no site, app e fichas das lojas | DPO, lista de cookies, provedores VPS/e-mail/facial, mecanismo de transferência internacional (Anthropic), prazos de retenção de fotos/logs |
| [acordo-tratamento-dados.md](acordo-tratamento-dados.md) | Cliente (controlador) × PersonaliPonto (operadora) × Revendedor (suboperador) | Anexo de todo contrato com cliente final; exigido por prefeituras | Suboperadores pendentes, medidas técnicas (MFA, backups), foro |
| [sla.md](sla.md) | Anexo I do contrato de revenda | Junto com o contrato de revenda | Janela de manutenção, horário útil, canal de chamados, URL de status |
| [termo-consentimento-biometrico.md](termo-consentimento-biometrico.md) | Empregador/órgão ↔ Trabalhador/servidor | Antes de habilitar reconhecimento facial para cada pessoa | Modelo do cliente: base legal escolhida, lei municipal (setor público), alternativa sem biometria, provedor facial |
| [ripd-reconhecimento-facial.md](ripd-reconhecimento-facial.md) | Controlador (com apoio da PersonaliPonto) | Antes de ativar biometria em um cliente | Cliente preenche escopo, riscos e aprovação; PersonaliPonto fornece seção técnica |
| [atestado-capacidade-tecnica.md](atestado-capacidade-tecnica.md) | Cliente atesta o revendedor | Revendedor pede ao cliente satisfeito para usar em licitações | Nada — o cliente emite em seu papel timbrado |
| [atestado-tecnico-termo-responsabilidade.md](atestado-tecnico-termo-responsabilidade.md) | PersonaliPonto → cada empresa usuária | Obrigatório (art. 89 e Anexo VII da Portaria 671/2021) antes do uso do sistema | Dados da PersonaliPonto, nº INPI, identificador do programa, nomes/CPFs do responsável legal e técnico; demais campos automáticos |

## Pré-requisitos antes do uso real

1. CNPJ ativo (CNAE de software).
2. Registro do programa (e marca) no **INPI** — exigido para REP-P (arts. 78 e 91 da Portaria 671/2021).
3. Certificados **ICP-Brasil**: e-CNPJ para assinar AFD/AEJ/comprovantes; e-CPF do responsável legal e técnico para o Atestado Técnico.
4. Nomeação do **encarregado (DPO)**.
5. Contratos/termos com suboperadores (Supabase, Asaas, Anthropic, provedor facial) e definição do mecanismo de transferência internacional.
6. Revisão de todos os modelos por advogado.

## Pontos de atenção para o advogado

- Base legal da biometria (consentimento × art. 11, II, "g" × setor público) e validade de consentimento na relação de emprego.
- Placeholder "art. 18 da Portaria SEPRT/ME nº XXX/2021" no texto oficial do Anexo VII, substituído por art. 89 da Portaria MTP 671/2021.
- Emissão de um Atestado Técnico por tipo (REP-P e PTRP) ou documento único.
- Prazo de guarda de 5 anos (prescrição trabalhista) e compatibilidade com prazos de entes públicos (tabelas de temporalidade arquivística).
- Cláusulas de limitação de responsabilidade e não aliciamento frente ao CDC, quando aplicável.
