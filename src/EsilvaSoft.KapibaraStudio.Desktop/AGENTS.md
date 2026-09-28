# Regras do Desktop

Aplicam-se também as regras do AGENTS.md da raiz e o design system em `docs/17-design-system-ui-ux.md`.

- Preserve a composição: explorer de bancos à esquerda; editor acima; resultados/mensagens/erros abaixo; conexões em modal proprietária.
- Não recolocar formulários de conexão ou administração permanentemente acima do editor.
- Use recursos semânticos de App.axaml; não fixe cores locais, especialmente texto branco sobre azul claro no tema escuro.
- Interface 13, metadados 12, títulos de modal 16; código 14 configurável com entrelinha proporcional. Não herdar títulos de abas de 24 do tema.
- Mantenha controles compactos, alvos de interação de pelo menos 28 e rolagem por região. Preserve a janela mínima 960 × 620.
- Views tratam foco, seleção, janelas e seletores de arquivos; viewmodels mantêm estado e operações. Views não acessam MongoDB ou LiteDB diretamente.
- WorkspaceViewModel coordena; WorkspaceTabViewModel isola edição/execução; ExplorerNodeViewModel carrega nós; ConnectionsViewModel controla cadastro e seleção. Ferramentas existentes conservam contexto próprio.
- Toda janela/popup precisa de caminho por teclado; Escape da modal não pode cancelar a consulta da janela proprietária. Preserve F6 para sair do editor que aceita Tab.
- Use bindings tipados nos novos componentes; nunca adicionar handlers async void fora dos limites de eventos sem tratamento de falhas.
- Verifique texto longo, fonte ampliada, estado desconectado, erro e somente leitura. Não ocultar ação indisponível sem explicar o contexto pendente.
- Renderize os dois temas nos três tamanhos e escalas definidos no design system. Examine as imagens, não apenas o resultado verde do teste.
