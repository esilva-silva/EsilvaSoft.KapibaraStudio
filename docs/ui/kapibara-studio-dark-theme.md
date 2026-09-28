# Kapibara Studio --- Dark Theme Color Palette

Paleta de cores para o tema escuro do **Kapibara Studio for MongoDB**,
seguindo a identidade visual azul/roxo do projeto.

## Paleta principal

  Uso                    Cor                Hex
  ---------------------- ------------------ -----------
  Background principal   Navy quase preto   `#0B1020`
  Painéis / Sidebar      Navy escuro        `#111827`
  Editor / Surface       Azul-carvão        `#151D2E`
  Surface elevada        Azul ardósia       `#1C263A`
  Hover                  Azul acinzentado   `#26334A`
  Selecionado            Azul profundo      `#193B67`
  Bordas                 Slate              `#334155`
  Borda suave            Slate escuro       `#253147`
  Texto principal        Branco azulado     `#F1F5F9`
  Texto secundário       Cinza azulado      `#94A3B8`
  Texto desabilitado     Cinza escuro       `#64748B`
  Primary                Azul elétrico      `#38A8FF`
  Primary hover          Azul claro         `#60B9FF`
  Primary pressed        Azul               `#2589E8`
  Accent                 Roxo               `#7C5CFC`
  Accent claro           Violeta            `#9B7BFF`
  Success                Verde              `#3DDC97`
  Warning                Âmbar              `#FBBF24`
  Error                  Vermelho coral     `#FB7185`
  Info                   Ciano              `#22D3EE`

## Hierarquia de superfícies

``` text
#0B1020  App background
   └── #111827  Sidebar / toolbar
        └── #151D2E  Editor / panels
             └── #1C263A  Cards / dialogs / elevated surfaces

Border       #334155
Hover        #26334A
Selected     #193B67

Primary      #38A8FF
Accent       #7C5CFC
```

A combinação `#0B1020` + `#111827` + `#151D2E` cria profundidade sem
utilizar preto puro. O azul é a cor principal de interação, enquanto o
roxo funciona como acento da identidade visual.

## Seleção, botões e foco

Para itens selecionados, como bancos, collections, abas e resultados,
prefira um azul profundo no fundo em vez de preencher toda a área com
azul elétrico.

``` css
--selection-bg:       #193B67;
--selection-border:   #38A8FF;
--selection-text:     #F1F5F9;

--button-primary:     #2589E8;
--button-hover:       #38A8FF;

--focus-ring:         #7C5CFC;
```

## Gradiente da marca

O gradiente azul → roxo deve ser usado com moderação, principalmente no
logo, loading, progress bars e elementos especiais da marca.

``` css
--slop-gradient: linear-gradient(
    135deg,
    #22B8FF 0%,
    #397BFF 50%,
    #7C3AED 100%
);
```

## Variáveis base sugeridas

``` css
:root {
    /* Backgrounds */
    --slop-bg: #0B1020;
    --slop-sidebar: #111827;
    --slop-surface: #151D2E;
    --slop-surface-elevated: #1C263A;
    --slop-hover: #26334A;
    --slop-selected: #193B67;

    /* Borders */
    --slop-border: #334155;
    --slop-border-subtle: #253147;

    /* Text */
    --slop-text: #F1F5F9;
    --slop-text-secondary: #94A3B8;
    --slop-text-disabled: #64748B;

    /* Brand */
    --slop-primary: #38A8FF;
    --slop-primary-hover: #60B9FF;
    --slop-primary-pressed: #2589E8;
    --slop-accent: #7C5CFC;
    --slop-accent-light: #9B7BFF;

    /* Semantic */
    --slop-success: #3DDC97;
    --slop-warning: #FBBF24;
    --slop-error: #FB7185;
    --slop-info: #22D3EE;

    /* Focus */
    --slop-focus-ring: #7C5CFC;

    /* Branding */
    --slop-gradient: linear-gradient(
        135deg,
        #22B8FF 0%,
        #397BFF 50%,
        #7C3AED 100%
    );
}
```

## Direção visual

O tema deve manter uma aparência de IDE profissional, com superfícies
escuras azuladas e contraste suficiente para longos períodos de uso.

O **azul elétrico** identifica ações primárias e seleção. O **roxo**
deve aparecer como acento e elemento de identidade, sem competir com o
conteúdo. O gradiente azul/roxo deve permanecer relativamente raro para
que continue associado à marca Kapibara Studio.
