# Acordo de Tratamento de Dados Pessoais (DPA)

> **Modelo elaborado para revisão por advogado; não substitui assessoria jurídica.**

**Versão do modelo:** [1.0] · **Data:** [dd/mm/aaaa]

## Partes

**CONTROLADOR:** [RAZÃO SOCIAL / ÓRGÃO], CNPJ [ ], endereço [ ], representado por [nome, cargo, CPF] ("Controlador").
**OPERADORA:** [RAZÃO SOCIAL DA PERSONALIPONTO], CNPJ [ ], endereço [ ], representada por [nome, cargo, CPF] ("PersonaliPonto").
**SUBOPERADOR:** [RAZÃO SOCIAL DO REVENDEDOR], CNPJ [ ], endereço [ ], representado por [nome, cargo, CPF] ("Revendedor"). [Se houver: PARCEIRO [razão social, CNPJ], suboperador subsequente vinculado ao Revendedor.]

Fundamento: Lei 13.709/2018 (LGPD), arts. 5º, VI e VII, 37 a 42, 46 a 49; regulamentos da ANPD. Este acordo integra os contratos de licença/revenda e de prestação de serviços entre as partes e prevalece sobre eles em matéria de dados pessoais.

---

## Cláusula 1 — Objeto e papéis

1.1. Regular o tratamento de dados pessoais de trabalhadores/servidores e usuários do Controlador na plataforma de ponto eletrônico PersonaliPonto.
1.2. O **Controlador** define finalidades e meios essenciais; a **PersonaliPonto** trata dados em nome do Controlador (operadora); o **Revendedor** (e Parceiro) auxilia na implantação e suporte (suboperador), sob as mesmas obrigações da operadora.

## Cláusula 2 — Descrição do tratamento

| Item | Descrição |
|---|---|
| Titulares | Trabalhadores, servidores, estagiários, gestores e administradores do Controlador |
| Categorias | Identificação (nome, CPF, PIS), vínculo e jornada, marcações de ponto, geolocalização na marcação, foto (opcional), dados de dispositivo e acesso |
| Dados sensíveis | Biometria facial (template e prova de vida), quando habilitada |
| Finalidades | Registro e tratamento de jornada (CLT art. 74; Portaria 671/2021), emissão de comprovantes, AFD, AEJ, espelho, relatórios e exportação à folha |
| Operações | Coleta, armazenamento, processamento, assinatura, exportação, backup, eliminação |
| Duração | Vigência do contrato + prazos de guarda (Cláusula 9) |

## Cláusula 3 — Obrigações do Controlador

3.1. Definir base legal para cada tratamento, inclusive art. 11 para biometria; dar transparência aos titulares; elaborar RIPD quando exigido (modelo `ripd-reconhecimento-facial.md`); colher consentimentos quando essa for a base; oferecer alternativa sem biometria.
3.2. Fornecer instruções lícitas e documentadas; as configurações feitas na Plataforma constituem instruções.
3.3. Atender titulares, com apoio da operadora.

## Cláusula 4 — Obrigações da Operadora e do Suboperador

4.1. Tratar dados **somente conforme instruções** do Controlador e para as finalidades acima, comunicando instrução que julgar ilícita.
4.2. Garantir confidencialidade dos profissionais com acesso (termo de sigilo) e acesso mínimo necessário.
4.3. Adotar as medidas de segurança do Anexo A (art. 46 LGPD).
4.4. **Revendedor/Parceiro** acessam apenas cadastro e financeiro; dados de ponto e biometria somente em **modo suporte**, com justificativa, tempo limitado e trilha de auditoria disponível ao Controlador.
4.5. Não usar dados para finalidade própria, nem vendê-los ou compartilhá-los fora deste acordo.
4.6. Auxiliar o Controlador em pedidos de titulares (art. 18), RIPD, incidentes e demandas da ANPD, encaminhando pedidos recebidos em até [5] dias úteis.
4.7. Manter registro das operações de tratamento (art. 37).

## Cláusula 5 — Suboperadores

5.1. O Controlador autoriza, de forma geral, os suboperadores abaixo:

| Suboperador | Serviço | Local |
|---|---|---|
| [Revendedor] | Implantação, suporte N1 | Brasil |
| [Parceiro, se houver] | Comercialização, suporte N1 | Brasil |
| Supabase, Inc. | Banco de dados e hospedagem | São Paulo, Brasil (AWS sa-east-1) |
| [Provedor VPS] | Servidores de aplicação | [ ] |
| Asaas Gestão Financeira S.A. | Cobrança e pagamentos | Brasil |
| Anthropic, PBC | Assistente de IA (sem dados pessoais de titulares) | EUA |
| [Provedor de reconhecimento facial] | Biometria e prova de vida | [ ] |
| [Provedor de e-mail transacional] | E-mails | [ ] |

5.2. Inclusão ou troca de suboperador será comunicada com **[30] dias** de antecedência; o Controlador poderá opor-se fundamentadamente e, não havendo solução, rescindir sem multa.
5.3. A operadora impõe aos suboperadores obrigações equivalentes às deste acordo e responde por eles perante o Controlador, nos termos do art. 42 da LGPD.

## Cláusula 6 — Transferência internacional

6.1. Transferências para fora do Brasil (ex.: Anthropic) ocorrerão somente nas hipóteses do art. 33 da LGPD, preferencialmente mediante cláusulas-padrão contratuais da ANPD (Resolução CD/ANPD nº 19/2024). Dados biométricos e registros de ponto são armazenados no Brasil.

## Cláusula 7 — Incidentes de segurança

7.1. A operadora comunicará ao Controlador incidente que possa acarretar risco ou dano relevante aos titulares em até **48 (quarenta e oito) horas** da ciência; o suboperador comunicará à operadora em até **24 horas**.
7.2. A comunicação conterá, no que souber: natureza e categorias de dados e titulares, quantidade, medidas adotadas, riscos, contato e cronologia, complementando-a à medida que apurar.
7.3. A comunicação à ANPD e aos titulares cabe ao Controlador (art. 48 LGPD; Resolução CD/ANPD nº 15/2024 — prazo de 3 dias úteis), com apoio da operadora.

## Cláusula 8 — Auditoria

8.1. O Controlador poderá solicitar, até [1] vez por ano (ou após incidente), evidências de conformidade: relatórios, políticas, questionários e certificações dos provedores. Auditorias presenciais ou por terceiro independente, sob confidencialidade, mediante aviso de [30] dias e às custas do Controlador, sem acesso a dados de outros clientes.
8.2. Órgãos públicos poderão exercer controle na forma da legislação aplicável (inclusive tribunais de contas).

## Cláusula 9 — Término: devolução e eliminação

9.1. No término, a operadora disponibilizará por **30 dias** a exportação de AFD, AEJ, espelhos, comprovantes e cadastros.
9.2. Após, eliminará os dados, **exceto** os que deva conservar (art. 16, I, LGPD): registros de ponto e arquivos legais, guardados por **5 anos**, com acesso restrito e disponibilização ao Controlador sob pedido.
9.3. **Templates biométricos** serão eliminados em até [30] dias do término ou do desligamento do titular, com emissão de declaração de eliminação sob pedido.

## Cláusula 10 — Responsabilidade

10.1. Cada parte responde pelos danos que causar por violação da LGPD, nos termos dos arts. 42 a 45; a operadora e o suboperador respondem solidariamente quando descumprirem a lei ou as instruções lícitas do Controlador (art. 42, § 1º, I).

## Cláusula 11 — Vigência e foro

11.1. Vigora enquanto houver tratamento. Foro: [Comarca/UF] [ou, para entes públicos, foro da sede do Controlador].

## Anexo A — Medidas técnicas e organizacionais

- TLS 1.2+ em trânsito; criptografia em repouso.
- Isolamento multi-inquilino por políticas de segurança em nível de linha (RLS) e contexto de sessão.
- Autenticação com senha forte, hash seguro, [MFA para administradores].
- Perfis de acesso por hierarquia (Owner, Revendedor, Parceiro, RH, Colaborador) e princípio do menor privilégio.
- Modo suporte com justificativa e trilha de auditoria imutável.
- Marcações imutáveis; AFD/AEJ assinados com certificado ICP-Brasil.
- Backups diários com retenção de [ ] dias e teste de restauração.
- Registros de acesso (Marco Civil, art. 15).
- Gestão de vulnerabilidades e atualizações; plano de resposta a incidentes.
- Biometria: armazenamento do template (não da imagem), segregado e criptografado; prova de vida.

[Cidade], [data].

| Controlador | PersonaliPonto | Revendedor |
|---|---|---|
| ____________ | ____________ | ____________ |
