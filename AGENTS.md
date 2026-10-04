# Regras de desenvolvimento

## Referências e escopo

- Leia `docs/17-design-system-ui-ux.md` antes de alterar UI, navegação, temas, sessão ou atalhos.
- Consulte o catálogo funcional, os ADRs e a matriz de validação para distinguir implementado de planejado.
- Produto: IDE desktop MongoDB em .NET 10/Avalonia; Windows e Linux; interface e documentação em pt-BR, identificadores em inglês.
- Mantenha o nome EsilvaSoft.KapibaraStudio e a licença MIT. Não transformar o desktop em site ou adicionar dependência comercial sem pedido específico.

## Integrações de agentes

- A Fase 5 / v0.9.0 concentra a conclusão de GitHub Copilot e da infraestrutura de agentes necessária. Claude, Codex, APIs externas e integração MCP externa ficam no backlog `docs/backlog/bkl-06-integracoes-agentes.md`, sem fase/versão. Suas implementações, composição e disponibilidade continuam como estão. IA local na Fase 8 / v0.12.0 permanece a prioridade atual.
- Escopo/aceite Copilot estão em `docs/phases/phase-05-v0.9.0/README.md`; estado e evidências das demais integrações em `docs/backlog/integracoes-agentes/`. Use o código e os ADRs vigentes como referência para contratos existentes; mover ao backlog não remove nem desativa código/UI.
- Não criar providers ou fluxos de autenticação paralelos quando as abstrações atuais puderem ser concluídas.
- Claude Code mantém autenticação oficial. Nunca ler arquivos de credenciais, capturar/reutilizar tokens, acessar endpoints privados ou fazer fallback silencioso para Anthropic API.

## Invariantes

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
- Atualize design system, ADRs, plano, guia e acompanhamento quando decisões ou comportamento mudarem.
- Declare conclusão somente com evidência proporcional. Teste Headless não substitui MongoDB real, leitor de tela ou diálogos nativos. Registre pendências com precisão.
