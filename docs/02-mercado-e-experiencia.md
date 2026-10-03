# Direção de experiência do produto

**Revisão vigente (10/09/2026):** [design system](17-design-system-ui-ux.md). Adotados acento azul, modal de conexões, bancos à esquerda e abas com editor acima/resultados abaixo. A representação abaixo foi atualizada para essa decisão.


## Escopo e estado

A direção de UX parte das jornadas do Kapibara Studio e do [design system](17-design-system-ui-ux.md). Referências comparativas de produtos externos foram retiradas. Não se reproduzem identidade, textos ou telas de outro produto. Dependências técnicas e avisos de terceiros permanecem documentados em seus arquivos próprios.

✅ Implementado no recorte atual: Explorer, abas com destino fixo, consulta explícita, resultados abaixo do editor, menus contextuais e temas. 🚧 Em desenvolvimento: revisão produtiva das jornadas na v0.6.0; a homologação integral pertence à Fase 10 / v0.14.0. As jornadas abaixo descrevem a experiência alvo; disponibilidade específica está no [inventário](24-inventario-roadmap.md).

## Direção escolhida

Combinar exploração visual com precisão BSON e profundidade administrativa. As jornadas do produto exigem que conexão, consulta, edição, índices e exportação precisam estar a poucos passos; recursos raros podem estar em painéis específicos e na paleta de comandos. Essa é uma conclusão de projeto, não uma pesquisa quantitativa com usuários.

Na Fase 10 / v0.14.0, validar protótipos com pelo menos três pessoas representando desenvolvimento, operação e análise. Medir conclusão das jornadas, erros de destino, descoberta de funções e clareza das mensagens. Registrar amostra e limitações da avaliação.

## Organização da janela

```text
Conexões (modal) | Nova aba | Abrir | Salvar | Ferramentas | Tema | …
Bancos/explorer | Abas: Editor de consulta / Script
 Conexão aberta| Contexto fixo da aba + estado de acesso
 Banco         | Editor textual com autocomplete
 Coleção       | Resultados abaixo: Extended JSON | Mensagens | Erros
Estado | Origem | Persistência do rascunho
```

Usar painéis redimensionáveis e abas inicialmente; docking complexo depende de necessidade comprovada. Tema claro/escuro, escalonamento DPI, navegação por teclado e foco visível. Verde/vermelho nunca serão o único indicador de estado. Recursos de acessibilidade serão testados nas plataformas de destino.

## Jornadas detalhadas

**Cadastrar conexão:** URI ou formulário → nome/ambiente/cor → autenticação/TLS → testar → mostrar topologia e capacidades → salvar perfil com credencial direta ou referência opcional ao ambiente. Importar perfis de formato público e conhecido com prévia, removendo senhas da exportação padrão.

**Editar documento:** abrir resultado → inspecionar tipo → alterar → visualizar diff → aplicar → mostrar contagens/erros → atualizar versão local. Documento parcial por projeção nunca pode ser usado inadvertidamente para substituir o documento inteiro.

**Executar script:** abrir o editor JavaScript + JSON → escolher o contexto da aba → escrever variáveis e consultas com autocomplete → executar script ou seleção → acompanhar console e resultados → inspecionar erros ou cancelar. O contexto declarado da aba fica visível; o script pode selecionar outro banco explicitamente. A execução é sequencial no mesmo contexto JavaScript durante o job.

**Criar índice:** selecionar campos e ordem → opções compatíveis → estimativa de impacto explicitamente aproximada → comando gerado → criar → acompanhar tarefa → confirmar índice no servidor. Excluir/ocultar são ações distintas.

**Exportar banco:** escolher exportação lógica ou backup BSON → escopo → formato/destino → política de consistência → tarefa → manifesto e relatório. A interface explica a diferença de recuperabilidade no momento da escolha.

**Administrar:** abrir painel de diagnóstico com leituras → selecionar ação → mostrar alvo, requisitos, impacto e plano → executar → verificar pós-condição. O consentimento do operador faz parte do futuro produto em operações destrutivas; não representa uma pendência de aprovação para esta entrega documental.

## Comportamentos comuns

- `Ctrl+Enter`: executar seleção válida ou consulta ativa; nunca executar texto parcial inválido.
- `Ctrl+Space`: sugestões; `Ctrl+Shift+P`: paleta; `Ctrl+S`: salvar consulta; equivalentes de plataforma configuráveis.
- Abas alteradas mantêm indicador e recuperação de rascunho; resultados não são restaurados ao abrir o app; conexões também permanecem fechadas.
- Estados explícitos: carregando, vazio, parcialmente carregado, sem permissão, desconectado, erro e cancelamento com resultado incerto.
- Quantidade carregada, limite, duração e origem visíveis. Contagem estimada tem rótulo próprio.
- Histórico com busca, favoritos, parâmetros tipados e retenção configurável; persistência de valores sensíveis pode ser desabilitada por conexão.
- Erros apresentam explicação em português, código original e detalhes copiáveis com segredos removidos.
