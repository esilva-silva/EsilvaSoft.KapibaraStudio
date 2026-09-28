# Kapibara Studio — Light Theme Color Palette

Paleta de cores para o tema claro do **Kapibara Studio for MongoDB**, mantendo a identidade visual azul/roxo do projeto com aparência limpa de IDE.

## Paleta principal

| Uso | Cor | Hex |
|---|---|---|
| Background principal | Branco azulado | `#F7F9FC` |
| Painéis / Sidebar | Azul muito claro | `#EEF3F9` |
| Editor / Surface | Branco | `#FFFFFF` |
| Surface elevada | Cinza-azulado claro | `#F3F6FA` |
| Hover | Azul suave | `#E6EEF8` |
| Selecionado | Azul claro | `#D9EAFE` |
| Bordas | Slate claro | `#CBD5E1` |
| Borda suave | Cinza azulado | `#E2E8F0` |
| Texto principal | Navy escuro | `#162033` |
| Texto secundário | Slate | `#64748B` |
| Texto desabilitado | Cinza | `#94A3B8` |
| Primary | Azul | `#2684FF` |
| Primary hover | Azul vivo | `#1D73E8` |
| Primary pressed | Azul profundo | `#155EC4` |
| Accent | Roxo | `#7357E8` |
| Accent claro | Violeta | `#8B6CF6` |
| Success | Verde | `#159A6A` |
| Warning | Âmbar | `#D99000` |
| Error | Vermelho coral | `#E05265` |
| Info | Ciano escuro | `#0E9FB5` |

## Hierarquia de superfícies

```text
#F7F9FC  App background
   └── #EEF3F9  Sidebar / toolbar
        └── #FFFFFF  Editor / panels
             └── #F3F6FA  Cards / dialogs / elevated surfaces

Border       #CBD5E1
Hover        #E6EEF8
Selected     #D9EAFE

Primary      #2684FF
Accent       #7357E8
```

A ideia é evitar branco puro em toda a aplicação. O fundo geral usa um branco levemente azulado, enquanto os editores e áreas de conteúdo usam branco puro para criar profundidade sem depender de sombras fortes.

## Seleção, botões e foco

Para itens selecionados, como bancos, collections, abas e resultados:

```css
--selection-bg:       #D9EAFE;
--selection-border:   #2684FF;
--selection-text:     #162033;

--button-primary:     #2684FF;
--button-hover:       #1D73E8;
--button-pressed:     #155EC4;

--focus-ring:         #7357E8;
```

## Gradiente da marca

O mesmo gradiente do tema escuro pode ser mantido para preservar a identidade visual entre os dois temas.

```css
--slop-gradient: linear-gradient(
    135deg,
    #22B8FF 0%,
    #397BFF 50%,
    #7C3AED 100%
);
```

## Variáveis base sugeridas

```css
:root {
    /* Backgrounds */
    --slop-bg: #F7F9FC;
    --slop-sidebar: #EEF3F9;
    --slop-surface: #FFFFFF;
    --slop-surface-elevated: #F3F6FA;
    --slop-hover: #E6EEF8;
    --slop-selected: #D9EAFE;

    /* Borders */
    --slop-border: #CBD5E1;
    --slop-border-subtle: #E2E8F0;

    /* Text */
    --slop-text: #162033;
    --slop-text-secondary: #64748B;
    --slop-text-disabled: #94A3B8;

    /* Brand */
    --slop-primary: #2684FF;
    --slop-primary-hover: #1D73E8;
    --slop-primary-pressed: #155EC4;
    --slop-accent: #7357E8;
    --slop-accent-light: #8B6CF6;

    /* Semantic */
    --slop-success: #159A6A;
    --slop-warning: #D99000;
    --slop-error: #E05265;
    --slop-info: #0E9FB5;

    /* Focus */
    --slop-focus-ring: #7357E8;

    /* Branding */
    --slop-gradient: linear-gradient(
        135deg,
        #22B8FF 0%,
        #397BFF 50%,
        #7C3AED 100%
    );
}
```

## Sugestão para editor JSON

Para manter boa legibilidade sem ficar excessivamente colorido:

```css
--json-property: #2457A7;
--json-string: #0F7B6C;
--json-number: #7A4DD8;
--json-boolean: #B45309;
--json-null: #D1435B;
--json-punctuation: #475569;
--json-comment: #94A3B8;
```

## Direção visual

O tema claro deve parecer uma extensão natural do tema escuro, não um tema separado.

O **azul** continua sendo a cor principal de interação, enquanto o **roxo** funciona como acento da marca. As superfícies devem permanecer frias e levemente azuladas para combinar com o logo e evitar o aspecto genérico de uma aplicação baseada apenas em branco e cinza.

Evite usar o gradiente em botões comuns. Reserve-o para elementos de branding, loading, progress indicators e pontos especiais da interface.
