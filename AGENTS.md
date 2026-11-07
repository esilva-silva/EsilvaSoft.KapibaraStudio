# Regras de desenvolvimento

## Referências e escopo

- A documentação oficial está em `docs/README.md`, dividida em MVP, Remover e Melhorias. A documentação é autossuficiente; contratos e evidências necessários estão consolidados nessa árvore.
- Leia `docs/MVP/04-design-system.md` antes de alterar UI, navegação, temas, sessão ou atalhos.
- Consulte `docs/MVP/02-requisitos-e-estado.md`, `docs/MVP/08-decisoes-vigentes.md` e `docs/MVP/06-validacao-e-evidencias.md` para distinguir implementação de validação; use os documentos em `docs/MVP/contratos/` para contratos detalhados.
- Produto: Studio desktop — MongoDB Database Management & Development Tool — em .NET 10/Avalonia; Windows e Linux; interface e documentação em pt-BR, identificadores em inglês.
- Mantenha o nome EsilvaSoft.KapibaraStudio e a licença MIT. Não transformar o desktop em site ou adicionar dependência comercial sem pedido específico.

## Integrações de agentes

- Após a limpeza delimitada da fase nova 01 / v0.10.0, priorizar IA local na fase nova 02 / v0.11.0 e depois GitHub Copilot na fase nova 03 / v0.12.0, conforme `docs/MVP/fases/README.md`. Versões-alvo documentais não renumeram tags, binários nem evidências.
- Escopo/aceite Copilot estão em `docs/MVP/fases/fase-03-v0.12.0.md`. Claude, Codex, APIs externas e integração MCP externa ficam em `docs/Melhorias/conceitos.md`, sem fase/versão; implementações, composição e disponibilidade atuais são preservadas. Evidências e limitações estão em `docs/MVP/09-baseline-de-validacao.md`.
- Os cortes aprovados e suas preservações estão em `docs/Remover/inventario-tecnico.md` e `docs/Remover/TODO.md`. Documentar um corte não comprova sua execução; quando executá-lo, preservar consumidores compartilhados e compatibilidade conforme o inventário.
- Não criar providers ou fluxos de autenticação paralelos quando as abstrações atuais puderem ser concluídas.
- Claude Code mantém autenticação oficial. Nunca ler arquivos de credenciais, capturar/reutilizar tokens, acessar endpoints privados ou fazer fallback silencioso para Anthropic API.

## Invariantes

- Tools de agentes nunca consultam documentos nem escrevem diretamente no MongoDB, mesmo com aprovação. Permitir metadados/índices comuns e Atlas Search; documentos, erros e logs somente de execuções humanas já capturadas (`docs/MVP/03-arquitetura-e-invariantes.md`; contrato detalhado em `docs/MVP/contratos/tools-agentes.md`, ADR-066).

- Explorer navega; nunca executa automaticamente uma consulta ao selecionar ou abrir coleção.
- Mudar seleção do explorer não redireciona abas abertas. Um resultado só atualiza a aba/solicitação que o originou.
- Capturar perfil, banco, coleção, texto e opções antes de iniciar operações assíncronas.
- Não compartilhar CancellationTokenSource entre abas. Não afirmar rollback ao cancelar.
- Não abrir uma segunda conexão LiteDB ao arquivo local: use o proprietário registrado em DI e migração aditiva/versionada.
- Rascunhos respeitam opt-out geral/por conexão; entrada JSON respeita opt-in. Não persistir resultados ou credenciais nos snapshots.
- Falha de persistência deve ser visível. Nunca sobrescrever sessão ilegível com uma sessão vazia.
- Preservar BSON/Extended JSON, UUIDs, proteções de escrita, confirmações, auditoria e limites do Console JavaScript. Modos Script/mongosh e Agregação retirados recuperam texto/origem sem executar; não reintroduzir o processo mongosh.

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
- Atualize design system, decisões, fases, guia e estado em `docs` quando decisões ou comportamento mudarem; preserve datas, resultados e limitações das evidências consolidadas. Verifique os links Markdown com `node scripts/check-docs-reader.cjs`.
- Declare conclusão somente com evidência proporcional. Teste Headless não substitui MongoDB real, leitor de tela ou diálogos nativos. Registre pendências com precisão.
