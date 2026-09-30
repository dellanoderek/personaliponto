# Anexo — Acordo de Nível de Serviço (SLA)

> **Modelo elaborado para revisão por advogado; não substitui assessoria jurídica.**

**Versão:** [1.0] · Integra o Contrato de Licença e Revenda de [data] entre [PERSONALIPONTO] e [REVENDEDOR].

## 1. Disponibilidade

1.1. Meta de **disponibilidade mensal de 99,5%** dos serviços de marcação de ponto (app/terminal web/API de marcação) e do painel web.
1.2. Cálculo: `Disponibilidade = (minutos do mês − minutos de indisponibilidade) ÷ minutos do mês × 100`, excluídos os eventos da Cláusula 5. Indisponibilidade = falha generalizada verificada pelo monitoramento da PERSONALIPONTO em intervalos de até [1] minuto.
1.3. 99,5% equivale a cerca de 3h39min de indisponibilidade tolerada por mês.
1.4. O aplicativo mantém marcações feitas offline (quando sincronizado ao relógio oficial) e as envia no restabelecimento; tais períodos não impedem o registro.

## 2. Manutenções

2.1. **Programadas:** em janela de [domingo, 00h00–06h00, horário de Brasília], avisadas com **48 horas** de antecedência no painel/e-mail, limitadas a [8] horas por mês. Não contam como indisponibilidade.
2.2. **Emergenciais:** correções de segurança críticas, com aviso tão logo possível; contam como indisponibilidade se ultrapassarem [1] hora.

## 3. Severidades e prazos (suporte N2/N3 da PersonaliPonto)

Horário útil: dias úteis, [08h00–18h00], horário de Brasília. Chamados abertos pelo Revendedor (N1) no canal oficial [URL/e-mail].

| Severidade | Definição | 1ª resposta | Solução ou contorno |
|---|---|---|---|
| **1 — Crítico** | Marcação de ponto indisponível para um ou mais clientes; impossibilidade de gerar AFD/AEJ; incidente de segurança | 1 hora útil (24x7 para indisponibilidade geral) | **4 horas úteis** |
| **2 — Alto** | Funcionalidade importante degradada sem contorno (ex.: espelho incorreto, falha de sincronização) | 4 horas úteis | 2 dias úteis |
| **3 — Médio** | Falha com contorno disponível; problemas de relatório/personalização | 1 dia útil | 5 dias úteis |
| **4 — Baixo** | Dúvida, melhoria, ajuste cosmético | 2 dias úteis | Avaliado no roadmap |

3.1. Prazos suspensos enquanto aguardar informação do Revendedor ou cliente.

## 4. Créditos

| Disponibilidade mensal | Crédito sobre a fatura do mês |
|---|---|
| 99,0% a < 99,5% | 5% |
| 98,0% a < 99,0% | 10% |
| 95,0% a < 98,0% | 20% |
| < 95,0% | 30% |

4.1. Crédito solicitado pelo Revendedor em até [30] dias do fim do mês, abatido na fatura seguinte; não conversível em dinheiro. Teto: 30% da fatura mensal.
4.2. Os créditos são a compensação exclusiva por descumprimento de disponibilidade, sem prejuízo da rescisão por justa causa se a disponibilidade ficar abaixo de 95% por [3] meses consecutivos ou [4] em 12 meses.

## 5. Exclusões

Não contam como indisponibilidade: manutenções programadas; falhas de internet, dispositivos, redes ou energia do cliente/usuário; indisponibilidade das lojas de aplicativos; falhas de DNS/domínio próprio sob controle do Revendedor; uso contrário à documentação ou aos Termos; ataques de negação de serviço de grande escala e caso fortuito/força maior (art. 393 CC), desde que adotadas medidas razoáveis de mitigação; suspensão por inadimplência (respeitada a garantia de marcação); falhas de provedores terceiros escolhidos pelo cliente (ex.: conta Asaas do Revendedor, integração de folha do Parceiro).

## 6. Relatórios

6.1. Página de status em [URL] e relatório mensal de disponibilidade disponível ao Revendedor mediante solicitação.
