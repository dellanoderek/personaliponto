# Relatório de Impacto à Proteção de Dados Pessoais (RIPD) — Reconhecimento Facial no Registro de Ponto

> **Modelo elaborado para revisão por advogado; não substitui assessoria jurídica.**

Fundamento: arts. 5º, XVII, 10, § 3º, 38 e 55-J, XIII, da Lei 13.709/2018 (LGPD); orientações da ANPD sobre RIPD e Guia de Tratamento pelo Poder Público. O RIPD é de responsabilidade do **controlador**; a operadora PersonaliPonto fornece as informações técnicas.

## 1. Identificação

| Campo | Preenchimento |
|---|---|
| Controlador | [razão social/órgão, CNPJ, endereço] |
| Encarregado (DPO) | [nome, contato] |
| Operadora | [RAZÃO SOCIAL DA PERSONALIPONTO], CNPJ [ ] |
| Suboperadores | [Revendedor]; Supabase (São Paulo); [provedor facial]; [VPS] |
| Elaborado por / data | [nome, cargo] — [data] |
| Versão / revisão prevista | [1.0] — revisão anual ou em mudança relevante |

## 2. Necessidade de elaborar o RIPD

Tratamento de **dado sensível** (biometria) em larga escala, relativo a trabalhadores (relação de subordinação e assimetria de poder), com uso de tecnologia emergente — alto risco segundo os critérios da ANPD.

## 3. Descrição do tratamento

### 3.1 Natureza
- **Coleta:** cadastro facial pelo RH ou pelo próprio titular no app; captura em cada marcação.
- **Processamento:** geração de template, comparação 1:1 com o template do titular, verificação de prova de vida.
- **Armazenamento:** template criptografado em banco no Brasil; imagem de verificação [descartada após comparação / retida por __ dias].
- **Resultado:** marcação aceita ou encaminhada à alternativa; registro do resultado no log.

### 3.2 Escopo
- Titulares: [nº] trabalhadores/servidores; área geográfica: [municípios/unidades].
- Dados: imagem facial, template, resultado de prova de vida, data/hora, geolocalização da marcação (se habilitada), identificadores.
- Frequência: [2 a 4] marcações diárias por titular.
- Retenção: template até desligamento + [30] dias; marcações por 5 anos (sem template).

### 3.3 Contexto
- Relação de trabalho/serviço público; expectativa dos titulares: uso restrito à autenticação do ponto.
- Existência de titulares vulneráveis: [aprendizes, pessoas com deficiência — descrever].
- Alternativa sem biometria disponível: [descrever].

### 3.4 Finalidade
Garantir a autoria das marcações e prevenir fraude ("ponto por terceiro"), assegurando a fidelidade exigida pelo art. 74 da Portaria MTP 671/2021 e, no setor público, a regularidade do controle de frequência [lei/decreto nº __].

## 4. Necessidade e proporcionalidade

| Critério | Análise |
|---|---|
| Base legal | [art. 11, I / II "a" / II "b" / II "g"] — justificar |
| Necessidade | [histórico de fraudes, dispersão geográfica, trabalho externo; por que meios menos invasivos (PIN, foto sem biometria, geolocalização) são insuficientes] |
| Minimização | Somente template 1:1, sem identificação em massa (1:N), sem uso de imagens para outros fins, sem monitoramento contínuo |
| Adequação | Biometria usada apenas no ato da marcação |
| Transparência | Termo de ciência/consentimento, política de privacidade, publicação em sítio oficial (setor público) |
| Direitos do titular | Alternativa sem penalidade, revisão humana de falhas, canais de atendimento |
| Qualidade | Revisão de acurácia do provedor; recadastro facilitado |
| Transferência internacional | [Não há / descrever] |

## 5. Identificação e avaliação de riscos

Escala: Probabilidade (P) e Impacto (I) 1–3; Nível = P×I (1–2 baixo, 3–4 médio, 6–9 alto).

| # | Risco | P | I | Nível |
|---|---|---|---|---|
| R1 | Acesso não autorizado/vazamento de templates | [2] | [3] | [6] |
| R2 | Uso para finalidade diversa (vigilância, disciplina) | [1] | [3] | [3] |
| R3 | Falso negativo impedindo marcação (discriminação por viés de tom de pele, idade, deficiência) | [2] | [2] | [4] |
| R4 | Falso positivo/fraude por foto ou vídeo | [1] | [2] | [2] |
| R5 | Retenção além do necessário (após desligamento) | [2] | [2] | [4] |
| R6 | Consentimento viciado pela subordinação | [2] | [2] | [4] |
| R7 | Compartilhamento indevido com revendedor/parceiro ou provedores | [1] | [3] | [3] |
| R8 | Indisponibilidade do provedor facial impedindo o ponto | [2] | [2] | [4] |

## 6. Medidas para tratar os riscos

| Risco | Medidas | Risco residual |
|---|---|---|
| R1 | Criptografia em trânsito/repouso; segregação por cliente (RLS); acesso restrito; auditoria; plano de incidentes (48h ao controlador) | [baixo/médio] |
| R2 | Restrição contratual (DPA) e técnica — sem exportação de templates; política interna do controlador | [baixo] |
| R3 | Alternativa sem biometria; revisão humana; limiares calibrados; avaliação de viés do provedor | [baixo] |
| R4 | Prova de vida; registro de tentativas; geolocalização opcional | [baixo] |
| R5 | Eliminação automática do template no desligamento (+[30] dias) e relatório de eliminação | [baixo] |
| R6 | Base legal diversa do consentimento quando cabível; recusa sem penalidade; termo claro | [baixo] |
| R7 | Revendedor/parceiro sem acesso a templates; modo suporte auditado; suboperadores listados | [baixo] |
| R8 | Fallback para modo alternativo; SLA | [baixo] |

## 7. Conclusão e aprovação

[ ] Riscos residuais aceitáveis — tratamento aprovado.
[ ] Tratamento aprovado com condições: [ ].
[ ] Tratamento não recomendado.

Parecer do Encarregado: [ ]

| Responsável | Assinatura | Data |
|---|---|---|
| Representante do controlador | | |
| Encarregado (DPO) | | |
