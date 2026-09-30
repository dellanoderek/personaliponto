# Marca PersonaliPonto

## Conceito
O símbolo é um **"P" cujo bojo é um mostrador de relógio**: a haste é a base sólida da
plataforma e o anel em degradê dourado metálico marca a hora do ponto (ponteiros em ~12h/4h).
Formas simples e grossas garantem leitura em 16 px (favicon) e 48 px (ícone de app).
O wordmark "Personali**Ponto**" (DejaVu Sans convertida em curvas) destaca "Ponto" em peso e cor.
Descritor opcional: "PONTO ELETRÔNICO WHITE-LABEL".

Identidade **preto + dourado**: premium, sóbria. Formas, proporções, tipografia e tracking
do descritor são fixos; apenas as cores variam entre as versões.

## Arquivos
- `logo-horizontal-branca.svg` — **versão principal**, para fundo preto (texto marfim + "Ponto" dourado claro)
- `logo-horizontal-preto.svg` — a mesma, já com o fundo preto aplicado
- `logo-horizontal.svg` — versão para fundos claros (preto + dourado escuro)
- `logo-mono.svg` / `logo-mono-branca.svg` — monocromáticas (preto / branco)
- `simbolo.svg` / `simbolo-branco.svg` — símbolo isolado (fundo claro / fundo preto)
- `icone-app.svg` — ícone em bloco preto arredondado (também usado como favicon)
- `previa.png` — prévia de aplicações
- `telas/` — capturas do produto com o tema aplicado
- `gerar_marca.py` — gera todos os SVGs/PNGs da marca e do produto
  (`pip install resvg_py fonttools pillow` e `python docs/marca/gerar_marca.py`)
- `tema-neutro-azul.md` — paleta antiga navy/azul/ciano, reservada como tema padrão dos revendedores

## Cores
| Nome | Hex | Uso |
|---|---|---|
| Preto | `#0b0b0d` | fundo principal da marca, sidebar, haste em fundo claro, títulos |
| Grafite | `#1a1a1f` | superfícies escuras secundárias, degradês |
| Dourado | `#c9a54c` | botões primários (texto preto), elementos gráficos, meio do degradê |
| Dourado claro | `#e6c77a` | "Ponto" e destaques sobre preto, brilho do degradê |
| Dourado escuro | `#9c7a2c` | "Ponto" na versão clara, início do degradê (uso gráfico / texto grande) |
| Dourado texto | `#7a5c1e` | links e texto dourado em fundo claro |
| Marfim | `#f5efe0` | "Personali" e haste sobre preto |
| Texto | `#6b6b73` | texto corrido e descritor em fundo claro |
| Fundo | `#faf8f3` | fundo claro off-white quente |

Degradê metálico do anel: `#9c7a2c → #e6c77a (55%) → #c9a54c`, diagonal de baixo-esquerda para cima-direita.

### Contraste (WCAG)
- Dourado sobre branco **falha** para texto (`#c9a54c` ≈ 2,2:1). Em fundo claro use
  `#7a5c1e` (≈ 5,9:1, AA) para texto; `#9c7a2c` (≈ 4:1) só em texto grande/negrito (≥ 18,66 px) e gráficos.
- Sobre preto: `#e6c77a` ≈ 12:1 e `#c9a54c` ≈ 8,7:1 (AA/AAA); marfim ≈ 17:1.
- Botão primário: texto `#0b0b0d` sobre `#c9a54c` ≈ 8,7:1.
- Estados semânticos (sucesso verde, alerta âmbar, erro vermelho) permanecem os mesmos.

## Área de proteção e tamanhos mínimos
- Margem livre ao redor = altura da haste do "P" dividida por 4 (≈ largura da haste).
- Logo horizontal: mínimo 120 px de largura; abaixo disso use só o símbolo.
- Símbolo: mínimo 16 px (favicon usa o ícone em bloco preto).

## Usos
- Fundo preto: versão principal. Fundo claro: versão clara. Uma cor só: versões mono.
- Não distorcer, não trocar as cores do degradê, não aplicar sombras nem contornos.

## Regra de negócio: preto + dourado é exclusivo da PersonaliPonto
A combinação **preto + dourado** é identidade exclusiva da PersonaliPonto. **Revendedores não
podem usá-la** na personalização white-label (logo, cores de tema, e-mails, PDFs, app).
- O futuro motor de temas (etapa de white-label) deve **validar e recusar** paletas de revendedor
  que combinem fundo/superfície predominante preto/grafite com destaque dourado
  (faixa de matiz ~38°–50°, saturação média/alta, ex.: `#c9a54c`, `#e6c77a`, `#9c7a2c`).
- Revendedor sem personalização recebe o **tema neutro azul** (`tema-neutro-azul.md`).
- Revendedores podem substituir a logo pela própria marca; o símbolo PersonaliPonto permanece
  como assinatura da plataforma.

## Onde os assets são usados
Web `wwwroot`: `img/logo.png`, `img/logo-transparente.png` (claros, 970×365),
`img/logo-escura.png` (sidebar/login/terminal), `favicon.png`, `img/icone.png` (256),
`img/icone-512.png`; Mobile: `Resources/AppIcon/appicon.svg` + `appiconfg.svg`,
`Resources/Splash/splash.svg` (fundo `#0b0b0d` no csproj), `Resources/Images/logo*.png`;
PDFs: `Infrastructure/Documentos/logo.png`.
