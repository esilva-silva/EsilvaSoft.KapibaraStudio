# Registro de transferência de homologação — v0.8.0

A release [v0.8.0](README.md) foi arquivada por escopo funcional. As verificações que dependem do sistema operacional foram transferidas para a [Fase 10 / v0.14.0](../../phases/phase-10-v0.14.0/README.md); a transferência não as encerra.

- Homologar Abrir, Salvar e Salvar como nos diálogos nativos de Windows e Linux, inclusive caminhos Unicode, extensões digitadas, arquivos bloqueados e permissões recusadas.
- Confirmar a lixeira real disponível e indisponível nos dois sistemas, para arquivos e diretórios, sem perda do buffer aberto.
- Exercitar foco, navegação por teclado, árvore, menus contextuais e mensagens de falha com leitor de tela.
- Reiniciar a aplicação em ambiente real e confirmar a recuperação de raiz, abas e conflito de arquivo sem incluir resultados, credenciais ou conteúdo que tenha opt-out.

Os testes automatizados que cobrem codificação, concorrência, recuperação e limites de caminho estão na [matriz](../../15-matriz-de-validacao.md). Eles não substituem esses cenários nativos.
