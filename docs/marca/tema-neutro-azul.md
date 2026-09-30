# Tema neutro azul — tema padrão para revendedores

Paleta original do produto (navy / azul / ciano), substituída na marca PersonaliPonto pelo
preto + dourado. Fica reservada como **tema padrão dos revendedores** no futuro motor de temas
(white-label): revendedor sem personalização recebe este tema; o preto + dourado é exclusivo
da PersonaliPonto (ver `README.md`).

## Tokens (Web — `app.css`, mesmos nomes de variáveis)
```css
:root {
    --marinho: #082352;
    --marinho-2: #0d2e66;
    --azul: #1166d6;
    --azul-escuro: #0b4fae;
    --ciano: #21b8ef;
    --texto: #59708e;
    --titulo: #082452;
    --fundo: #f4f8fb;
    --fundo-azul: #e6f4fd;
    --borda: #dbe6f0;
    --borda-2: #d8e4ef;
}
```
Neste tema `--azul` é usado tanto em texto/links quanto em botões primários (texto branco);
`--ciano` é o destaque sobre o navy. Sombras: `rgba(8, 35, 82, …)`; foco: `rgba(17, 102, 214, .12)`.

## Literais usados no tema azul
| Onde | Valor |
|---|---|
| Texto da sidebar / hero | `#c9d6ea` |
| Rodapé da sidebar | `#7f93b5` |
| Texto secundário em fundo navy | `#9fb4d3`, `#9fc2ea`, `#bfe6fb` |
| Degradê do terminal | `linear-gradient(160deg, #082352 0%, #0d3a86 55%, #1166d6 100%)` |
| Botão "bater ponto" | `radial-gradient(circle at 30% 30%, #2b8af0, #0b4fae)` |
| Arco decorativo do login | `#1f6fe0` |
| Gráficos (séries) | `#1166d6`, `#21b8ef`, `#c0392b` |
| Grade dos gráficos | `#dbe6f0` |
| `theme-color` / manifest | `#082352` |
| Cabeçalho de planilhas (ClosedXML) | `#082352` |

## Mobile (`Colors.xaml` / `Ui.cs`)
Marinho `#082352`, Azul `#1166D6`, AzulEscuro `#0B4FAE`, Ciano `#21B8EF`, Texto `#59708E`,
Titulo `#082452`, Fundo `#F4F8FB`, FundoAzul `#E6F4FD`, Borda `#DBE6F0`, Gray950 `#061A3D`;
degradê `#1166D6 → #0D3A86 → #082352`; botão primário azul com texto branco.

## PDFs (`Pdf.cs`, classe `Marca`)
Marinho `#082352`, Azul `#1166D6`, Ciano `#21B8EF`, Texto `#59708E`, Fundo `#F4F8FB`,
FundoAzul `#E6F4FD`, Borda `#DBE6F0`.

Estados semânticos (verde/amarelo/vermelho) são iguais nos dois temas.
