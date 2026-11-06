# Regras de desenvolvimento

## Referências e escopo

- A documentação oficial está em `docs-new/README.md`, dividida em MVP, Remover e Melhorias. `docs` preserva histórico e evidências; suas fases antigas não definem a sequência atual.
- Leia `docs-new/MVP/04-design-system.md` antes de alterar UI, navegação, temas, sessão ou atalhos.
- Consulte `docs-new/MVP/02-requisitos-e-estado.md`, `docs-new/MVP/08-decisoes-vigentes.md` e `docs-new/MVP/06-validacao-e-evidencias.md` para distinguir implementação de validação; use os ADRs históricos referenciados para contratos detalhados.
- Produto: Studio desktop — MongoDB Database Management & Development Tool — em .NET 10/Avalonia; Windows e Linux; interface e documentação em pt-BR, identificadores em inglês.
- Mantenha o nome EsilvaSoft.KapibaraStudio e a licença MIT. Não transformar o desktop em site ou adicionar dependência comercial sem pedido específico.

## Integrações de agentes

- Após a limpeza delimitada da fase nova 01 / v0.10.0, priorizar IA local na fase nova 02 / v0.11.0 e depois GitHub Copilot na fase nova 03 / v0.12.0, conforme `docs-new/MVP/fases/README.md`. Versões-alvo documentais não renumeram tags, binários nem evidências.
- Escopo/aceite Copilot estão em `docs-new/MVP/fases/fase-03-v0.12.0.md`. Claude, Codex, APIs externas e integração MCP externa ficam em `docs-new/Melhorias/conceitos.md`, sem fase/versão; implementações, composição e disponibilidade atuais são preservadas. Evidências anteriores permanecem em `docs/backlog/integracoes-agentes/`.
- Os cortes aprovados e suas preservações estão em `docs-new/Remover/inventario-tecnico.md` e `docs-new/Remover/TODO.md`. Documentar um corte não comprova sua execução; quando executá-lo, preservar consumidores compartilhados e compatibilidade conforme o inventário.
- Não criar providers ou fluxos de autenticação paralelos quando as abstrações atuais puderem ser concluídas.
- Claude Code mantém autenticação oficial. Nunca ler arquivos de credenciais, capturar/reutilizar tokens, acessar endpoints privados ou fazer fallback silencioso para Anthropic API.

## Invariantes

- Tools de agentes nunca consultam documentos nem escrevem diretamente no MongoDB, mesmo com aprovação. Permitir metadados/índices comuns e Atlas Search; documentos, erros e logs somente de execuções humanas já capturadas (`docs-new/MVP/03-arquitetura-e-invariantes.md`; contrato detalhado histórico em `docs/architecture/agent-tool-safety.md`, ADR-066).

- Explorer navega; nunca executa automaticamente uma consulta ao selecionar ou abrir coleção.
- Mudar seleção do explorer não redireciona abas abertas. Um resultado só atualiza a aba/solicitação que o originou.
- Capturar perfil, banco, coleção, texto e opções antes de iniciar operações assíncronas.
- Não compartilhar CancellationTokenSource entre abas. Não afirmar rollback ao cancelar.
- Não abrir uma segunda conexão LiteDB ao arquivo local: use o proprietário registrado em DI e migração aditiva/versionada.
- Rascunhos respeitam opt-out geral/por conexão; entrada JSON respeita opt-in. Não persistir resultados ou credenciais nos snapshots.
- Falha de persistência deve ser visível. Nunca sobrescrever sessão ilegível com uma sessão vazia.
- Preservar BSON/Extended JSON, UUIDs, proteções de escrita, confirmações, auditoria e limites do runtime mongosh.

## Verificação e documentação

- Restore: `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode`.
- Build: `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore`.
- Testes: `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`.
- Em ambientes isolados que bloqueiam a telemetria de build do Avalonia, `-p:UsedAvaloniaProducts=` permite validar sem essa tarefa externa; não desabilita analisadores nem testes.
- Alterações de sessão/contexto exigem testes de falha, concorrência e recuperação. Alterações visuais exigem inspeção dos PNGs reais gerados pelos testes de renderização.
- Não alterar golden files ou asserções apenas para esconder regressões. Não criar testes que apenas repitam a implementação.
- Benchmarks e medições de latência/alocação são ferramentas manuais de desenvolvimento; nunca executá-los no CI ou na release. Testes de ferramentas Benchmarks exigem opt-in local `EnableBenchmarkTests=true`.
- O CI nunca baixa nem carrega modelos. Testes que acessam o Hugging Face real ou pesos reais são `[Explicit]` com `Category("LocalModelIntegration")`, categoria excluída no CI; os demais usam `HttpMessageHandler` falso. A guarda `RealModelHubAccessIsConfinedToExplicitLocalModelTests` (projeto `IntegrationTests`) impõe a regra e deve ser executada localmente.
- O CI (Windows e Linux, também quando reutilizado pela release) roda apenas `UnitTests` e `Infrastructure.Agents.Tests`. `IntegrationTests`, inclusive as guardas estáticas de `UnitSystemBoundaryIntegrationTests`, é validação manual antes de push/release.
- Atualize design system, decisões, fases, guia e estado em `docs-new` quando decisões ou comportamento mudarem; não reescreva evidências históricas em `docs`. Se alterar documentos históricos ou seus índices, regenere o leitor com `node scripts/build-docs-index.cjs`.
- Declare conclusão somente com evidência proporcional. Teste Headless não substitui MongoDB real, leitor de tela ou diálogos nativos. Registre pendências com precisão.
