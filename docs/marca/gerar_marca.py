# -*- coding: utf-8 -*-
"""Gera os SVGs/PNGs da marca PersonaliPonto (preto + dourado).

Uso (na raiz do repositório ou em qualquer pasta):
    pip install resvg_py fonttools pillow
    python docs/marca/gerar_marca.py

Formas, proporções, tipografia e tracking são fixos; só a paleta abaixo define as cores.
"""
import base64
import io
import os

import resvg_py
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from PIL import Image

D = os.path.dirname(os.path.abspath(__file__))           # docs/marca
R = os.path.normpath(os.path.join(D, "..", ".."))         # raiz do repo
FON = os.path.join(R, "src", "PersonaliPonto.Infrastructure", "Documentos", "Fontes")

# ---- paleta PersonaliPonto (exclusiva) ----
PRETO = "#0b0b0d"
GRAFITE = "#1a1a1f"
OURO = "#c9a54c"
OURO_CLARO = "#e6c77a"
OURO_ESCURO = "#9c7a2c"
MARFIM = "#f5efe0"      # "branco" quente sobre preto
TEXTO = "#6b6b73"
TEXTO_ESCURO = "#a9a49a"  # descritor sobre preto
FUNDO = "#faf8f3"


def texto_path(txt, fonte, x, y, size, spacing=0.0):
    f = TTFont(os.path.join(FON, fonte)); gs = f.getGlyphSet(); cmap = f.getBestCmap()
    upm = f["head"].unitsPerEm; s = size / upm; out = []; cx = x
    for ch in txt:
        g = cmap[ord(ch)]; pen = SVGPathPen(gs)
        gs[g].draw(TransformPen(pen, (s, 0, 0, -s, cx, y)))
        out.append(pen.getCommands()); cx += gs[g].width * s + spacing * size
    return " ".join(out), cx - x - spacing * size


def largura(txt, fonte, size, spacing=0.0):
    return texto_path(txt, fonte, 0, 0, size, spacing)[1]


def cores(modo):
    """modo: 'claro' (fundo claro), 'escuro' (fundo preto), 'mono' (preto), 'mono-branco'."""
    if modo == "claro":
        return dict(haste=PRETO, ponteiro=PRETO, ponto=OURO_ESCURO, anel=(OURO_ESCURO, OURO_CLARO, OURO),
                    c1=PRETO, c2=OURO_ESCURO, c3=TEXTO)
    if modo == "escuro":
        return dict(haste=MARFIM, ponteiro=MARFIM, ponto=OURO_CLARO, anel=(OURO_ESCURO, OURO_CLARO, OURO),
                    c1=MARFIM, c2=OURO_CLARO, c3=TEXTO_ESCURO)
    c = PRETO if modo == "mono" else "#ffffff"
    return dict(haste=c, ponteiro=c, ponto=c, anel=(c, c, c), c1=c, c2=c, c3=c)


# ---- símbolo (viewBox 100x100) — geometria inalterada ----
def simbolo(modo="claro", uid="s"):
    k = cores(modo)
    a0, a1, a2 = k["anel"]
    ponteiro = k["ponteiro"]
    return f'''<defs><linearGradient id="{uid}g" x1="0" y1="1" x2="1" y2="0">
<stop offset="0" stop-color="{a0}"/><stop offset=".55" stop-color="{a1}"/><stop offset="1" stop-color="{a2}"/></linearGradient></defs>
<rect x="12" y="8" width="17" height="86" rx="8.5" fill="{k["haste"]}"/>
<circle cx="57" cy="40" r="27" fill="none" stroke="url(#{uid}g)" stroke-width="15"/>
<path d="M57 40 V25 M57 40 L68 47" fill="none" stroke="{ponteiro}" stroke-width="6.5" stroke-linecap="round" stroke-linejoin="round"/>
<circle cx="57" cy="40" r="5" fill="{k["ponto"]}"/>'''


def svg(w, h, corpo, fundo=None):
    bg = f'<rect width="{w}" height="{h}" fill="{fundo}"/>' if fundo else ""
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}">{bg}{corpo}</svg>'


def logo(modo="claro", fundo=None, uid="l"):
    w, h = 970, 365
    sx, sy, sc = 34, 62, 2.4
    corpo = f'<g transform="translate({sx} {sy}) scale({sc})">{simbolo(modo, uid)}</g>'
    x0 = 318; maxw = 612
    a, b, tag = "Personali", "Ponto", "PONTO ELETRÔNICO WHITE-LABEL"
    wa = largura(a, "DejaVuSans.ttf", 100); wb = largura(b, "DejaVuSans-Bold.ttf", 100)
    size = 100 * maxw / (wa + wb)
    pa, la = texto_path(a, "DejaVuSans.ttf", x0, 205, size)
    pb, _ = texto_path(b, "DejaVuSans-Bold.ttf", x0 + la, 205, size)
    ts = 25; tw = largura(tag, "DejaVuSans.ttf", ts, 0.16)
    ts = ts * maxw / tw
    pt, _ = texto_path(tag, "DejaVuSans.ttf", x0 + 3, 262, ts * 0.98, 0.16)
    k = cores(modo)
    corpo += f'<path d="{pa}" fill="{k["c1"]}"/><path d="{pb}" fill="{k["c2"]}"/><path d="{pt}" fill="{k["c3"]}"/>'
    return svg(w, h, corpo, fundo)


def icone(tile=True, uid="i", pad=18):
    s = (100 - 2 * pad) / 100
    corpo = (f'<rect width="100" height="100" rx="22" fill="{PRETO}"/>' if tile else "") + \
        f'<g transform="translate({pad + 1.5} {pad}) scale({s})">{simbolo("escuro" if tile else "claro", uid)}</g>'
    return svg(100, 100, corpo)


def fg():  # foreground MAUI (transparente), zona segura
    return svg(100, 100, f'<g transform="translate(18 17) scale(0.66)">{simbolo("escuro", "f")}</g>')


def render(s, w_, h_, bg=None):
    return bytes(resvg_py.svg_to_bytes(svg_string=s, width=w_, height=h_, background=bg))


def png(s, path, w_, h_, bg=None):
    open(path, "wb").write(render(s, w_, h_, bg))


def w(path, s):
    os.makedirs(os.path.dirname(path), exist_ok=True); open(path, "w", encoding="utf-8").write(s)


def b64(data):
    return "data:image/png;base64," + base64.b64encode(data).decode()


def main():
    j = os.path.join
    w(j(D, "logo-horizontal.svg"), logo("claro"))
    w(j(D, "logo-horizontal-branca.svg"), logo("escuro"))       # versão principal (fundo preto)
    w(j(D, "logo-horizontal-preto.svg"), logo("escuro", PRETO))  # com fundo aplicado
    w(j(D, "logo-mono.svg"), logo("mono"))
    w(j(D, "logo-mono-branca.svg"), logo("mono-branco"))
    w(j(D, "simbolo.svg"), svg(100, 100, simbolo("claro", "s")))
    w(j(D, "simbolo-branco.svg"), svg(100, 100, simbolo("escuro", "s")))
    w(j(D, "icone-app.svg"), icone(True))

    W = j(R, "src", "PersonaliPonto.Web", "wwwroot")
    M = j(R, "src", "PersonaliPonto.Mobile", "Resources")
    png(logo("claro"), j(W, "img", "logo.png"), 970, 365, "#ffffff")
    png(logo("claro"), j(W, "img", "logo-transparente.png"), 970, 365)
    png(logo("escuro"), j(W, "img", "logo-escura.png"), 970, 365)  # sidebar/login/terminal (fundo preto)
    png(logo("claro"), j(M, "Images", "logo.png"), 970, 365, "#ffffff")
    png(logo("claro"), j(M, "Images", "logo_transparente.png"), 970, 365)
    png(logo("claro"), j(R, "src", "PersonaliPonto.Infrastructure", "Documentos", "logo.png"), 970, 365, "#ffffff")
    png(icone(True), j(W, "favicon.png"), 64, 64)
    png(icone(True), j(W, "img", "icone.png"), 256, 256)
    png(icone(True), j(W, "img", "icone-512.png"), 512, 512)
    w(j(M, "AppIcon", "appicon.svg"), svg(100, 100, f'<rect width="100" height="100" fill="{PRETO}"/>'))
    w(j(M, "AppIcon", "appiconfg.svg"), fg())
    w(j(M, "Splash", "splash.svg"), logo("escuro"))  # splash com fundo preto (Color no csproj)

    # ---- prévia ----
    PW, PH = 1400, 1120
    c = f'<rect width="{PW}" height="{PH}" fill="{FUNDO}"/>'
    c += f'<rect x="40" y="40" width="640" height="360" rx="16" fill="{PRETO}"/>'
    c += f'<image x="70" y="100" width="580" height="218" href="{b64(render(logo("escuro"), 970, 365))}"/>'
    c += f'<rect x="720" y="40" width="640" height="360" rx="16" fill="#ffffff"/>'
    c += f'<image x="750" y="100" width="580" height="218" href="{b64(render(logo("claro"), 970, 365))}"/>'
    c += f'<rect x="40" y="440" width="640" height="220" rx="16" fill="#ffffff"/>'
    c += f'<image x="120" y="460" width="480" height="180" href="{b64(render(logo("mono"), 970, 365))}"/>'
    c += f'<rect x="720" y="440" width="640" height="220" rx="16" fill="{GRAFITE}"/>'
    c += f'<image x="800" y="460" width="480" height="180" href="{b64(render(logo("mono-branco"), 970, 365))}"/>'
    c += f'<rect x="40" y="700" width="1320" height="380" rx="16" fill="#ffffff"/>'
    x = 90; cy = 890
    for sz in (192, 96, 48):
        c += f'<image x="{x}" y="{cy - sz // 2}" width="{sz}" height="{sz}" href="{b64(render(icone(True), sz, sz))}"/>'; x += sz + 50
    for sz in (64, 32, 16):
        c += f'<image x="{x}" y="{cy - sz // 2}" width="{sz}" height="{sz}" href="{b64(render(svg(100, 100, simbolo("claro", "p")), sz, sz))}"/>'; x += sz + 40
    for sz in (32, 16):  # favicon real ampliado 6x
        im = Image.open(io.BytesIO(render(icone(True), sz, sz))).resize((sz * 6, sz * 6), Image.NEAREST)
        bio = io.BytesIO(); im.save(bio, "PNG")
        c += f'<image x="{x}" y="{cy - sz * 3}" width="{sz * 6}" height="{sz * 6}" style="image-rendering:pixelated" href="{b64(bio.getvalue())}"/>'; x += sz * 6 + 40
    png(svg(PW, PH, c), j(D, "previa.png"), PW, PH)
    print("ok")


if __name__ == "__main__":
    main()
