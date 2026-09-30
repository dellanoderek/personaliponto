# Atestado Técnico e Termo de Responsabilidade (REP-P e PTRP)

> **Modelo elaborado para revisão por advogado; não substitui assessoria jurídica.**

## Notas de conformidade (não fazem parte do documento emitido)

- **Base:** art. 89 da Portaria MTP nº 671/2021 e **modelo do Anexo VII** (conteúdo mínimo). Fonte: `docs/legal/portaria-671-arts-73-89.txt` e `docs/legal/portaria-671-anexos-V-VI.txt`.
- **Quem emite:** o fabricante/desenvolvedor (PersonaliPonto), para **cada empresa usuária** (art. 89, caput). Pode ser emitido para o **CNPJ matriz**, valendo para os estabelecimentos; em grupo econômico, um por CNPJ matriz (FAQ MTE, q. 43). Cada CNPJ de ente público cadastrado como empresa recebe o seu.
- **Assinaturas:** do **responsável legal** e do **responsável técnico** da desenvolvedora (art. 89, caput; FAQ q. 45), por **assinatura eletrônica qualificada** (ICP-Brasil) de **pessoa física** (art. 89, § 2º; Lei 14.063/2020, art. 4º, III; MP 2.200-2/2001, art. 10).
- **Formato:** documento eletrônico em **PDF** (art. 89, § 3º), que o empregador deve manter para a Inspeção do Trabalho; sem ele o empregador não pode usar o sistema (art. 89, § 4º).
- **Campos obrigatórios:** razão social e CNPJ/CPF da destinatária não podem ser omitidos (FAQ q. 22).
- **Versão em nuvem:** pode-se informar a versão inicial com "≥" (ex.: ">= 1.2") ou "a partir de dd/mm/aaaa" (FAQ q. 17).
- **Um documento ou dois:** o Anexo VII admite indicar o tipo; como a PersonaliPonto é REP-P **e** PTRP, recomenda-se emitir **um atestado para cada tipo** (ou um único listando ambos, após validação jurídica). O modelo abaixo usa o campo "Tipo" para gerar um de cada.
- **Atenção:** o texto oficial do Anexo VII contém o placeholder "art. 18 da Portaria SEPRT/ME nº XXX/2021"; neste modelo foi substituído pela referência correta (art. 89 da Portaria MTP nº 671/2021). Validar com advogado.
- Pendências para emissão válida: CNPJ, **registro do programa no INPI** (art. 91), certificados ICP-Brasil (e-CPF) dos dois signatários, certificado ICP-Brasil (e-CNPJ) usado na assinatura de AFD/AEJ.

**Legenda:** `{{auto: ...}}` = preenchido automaticamente pelo sistema na geração do PDF por cliente; `[ ]` = preenchido uma única vez pelo dono na configuração do emissor.

---

## ATESTADO TÉCNICO E TERMO DE RESPONSABILIDADE

Na qualidade de responsável técnico e de responsável legal da empresa **[RAZÃO SOCIAL DA PERSONALIPONTO]**, CNPJ nº **[__.___.___/____-__]**, os signatários abaixo, em atenção ao **art. 89 da Portaria MTP nº 671, de 8 de novembro de 2021**, atestam e declaram que o programa identificado abaixo está em conformidade com a **Portaria MTP nº 671/2021** (Seção de registro eletrônico de ponto, arts. 73 a 89 e Anexos V e VI).

| Campo (Anexo VII) | Conteúdo |
|---|---|
| Tipo do REP/PTRP | `{{auto: "REP-P" ou "PTRP"}}` |
| Marca Equipamento | N/A |
| Modelo Equipamento | N/A |
| Certificado de conformidade | N/A |
| Número de fabricação | N/A |
| Número de registro no INPI | `{{auto: nº do registro do programa no INPI, se REP-P; "N/A" se PTRP}}` — valor configurado: [BR 51 20__ ______-_] |
| Identificador do Programa | [PersonaliPonto] `{{auto: identificador configurado}}` |
| Versão do Programa | `{{auto: ">= " + versão vigente na data de adesão do cliente, ou "a partir de dd/mm/aaaa"}}` |
| Assinatura Eletrônica | N/A (somente REP-C) |
| Chave pública | `{{auto: chave pública do certificado ICP-Brasil usado na assinatura de AFD/AEJ/comprovantes, em Base64/PEM}}` |
| Algoritmo de criptografia assimétrica | `{{auto: ex. "RSA 2048"}}` |
| Algoritmo de hash | `{{auto: ex. "SHA-256"}}` |

Declaramos ainda que estamos cientes das consequências legais, cíveis e criminais quanto à falsa declaração, falso atestado e falsidade ideológica. Reiteramos ao usuário que este documento deve ficar disponível para pronta apresentação à Inspeção do Trabalho.

**Empresa/Pessoa Destinatária:**

| Campo | Conteúdo |
|---|---|
| Razão Social | `{{auto: razão social da empresa cliente (CNPJ matriz)}}` |
| CNPJ/CPF | `{{auto: CNPJ/CPF da empresa cliente}}` |

Local e data de emissão: `{{auto: cidade da sede da PersonaliPonto}}`, `{{auto: data de emissão}}` · Código de verificação: `{{auto: hash/ID do documento}}`

___________________________________________
**[Nome do Responsável Legal]** — CPF [___.___.___-__]
*(assinatura eletrônica qualificada ICP-Brasil — `{{auto: carimbo de assinatura PAdES}}`)*

___________________________________________
**[Nome do Responsável Técnico]** — CPF [___.___.___-__]
*(assinatura eletrônica qualificada ICP-Brasil — `{{auto: carimbo de assinatura PAdES}}`)*

---

### Especificação para a geração automática (etapa futura)

| Dado | Origem |
|---|---|
| Tipo | Gerar dois PDFs por cliente: REP-P e PTRP |
| Destinatária | Cadastro da empresa (CNPJ matriz; fallback CNPJ do estabelecimento) |
| Versão | Versão do sistema na data de ativação da empresa |
| Chave pública e algoritmos | Certificado ICP-Brasil configurado para assinatura de AFD/AEJ |
| INPI, identificador, dados dos signatários | Configuração do Owner |
| Assinatura | PAdES com e-CPF dos dois signatários (assinatura em lote ou fluxo de assinatura) |
| Reemissão | Ao trocar certificado/chave, versão relevante ou razão social da destinatária |
| Disponibilização | Painel do RH (download) e do revendedor, com histórico de versões |
