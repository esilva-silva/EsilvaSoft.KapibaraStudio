# Fase 6 — v0.10.0: administração e manutenção

**Situação:** Em desenvolvimento. **A fase não está ativa e não tem entrada na interface.**

## Objetivo

Cobrir tarefas rotineiras de manutenção diretamente na aplicação: coleções, views, validação, índices, estatísticas, usuários, papéis e exportação/importação lógica.

## Escopo incluído (IDs do catálogo)

- DAT-02/10/11 — criar banco, criar/renomear coleção, views e validação de coleção.
- IDX-01/02/03/04 — listar, criar, remover índices, opções suportadas, visibilidade e scripts.
- ADM-01/02/03/04/09 — `serverStatus`, `hello`, `currentOp`, `killOp`, leitura da configuração do profiler, usuários/papéis, `validate` e `compact`.
- TRF-02/03 — exportação e importação lógica com manifesto.

## Fora de escopo

Administração de clusters distribuídos, provisionamento de nuvem, automação de SO, backup operacional completo e sincronização entre servidores.

## Antecipações técnicas presentes no código

O código desta fase **existe e está integrado**: `MongoWorkspaceService`, `ExplorerMetadataService` e a janela `WorkspaceToolsWindow` (abas Coleções, Documentos, Índices, Administração, Transferir, CRUD em lote, Análise).

Como a fase não é a atual, os pontos de entrada visuais foram removidos: o botão **Ferramentas** da barra superior e os itens de menu do Explorer que abriam essa janela. O código, os contratos e os testes são preservados e continuam exercitados pela suíte. Ver [`backlog/bkl-02-ferramentas-fora-de-fase.md`](../../backlog/bkl-02-ferramentas-fora-de-fase.md) para o recorte que não pertence sequer a esta fase.

A reativação dos pontos de entrada é parte do aceite desta fase.

## Critério de aceite

Criar, inspecionar, renomear e remover coleção; criar, alterar opção suportada e remover índice com releitura; script gerado corresponde ao alvo e não executa ao copiar; estatísticas distinguem estimativa de contagem exata. Confirmação obrigatória para `dropCollection`, `dropIndex`, `deleteMany` e operações de banco; respeitar somente leitura, permissões, proteção de `_id_`, auditoria e resultado incerto após cancelamento.

## Dependências

Base funcional das Fases 1 a 4; contratos de escrita e capacidade por servidor/permissão. Administração não exige modelo de IA local nem homologação de agentes.

## Documentos relacionados

- [Segurança e administração](../../07-dados-seguranca-e-administracao.md) · [Explorer](../../19-database-explorer.md) · [Transferência lógica](../../13-exportacao-logica.md) · [Fase 10 — homologação manual](../phase-10-v0.14.0/README.md)

## Validação manual transferida

RBAC, topologias reais, profiler além da leitura de configuração e operações destrutivas em servidor real são critérios da [Fase 10 / v0.14.0](../phase-10-v0.14.0/README.md).
