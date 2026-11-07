# Execução e TODOs da limpeza

Execução iniciada em 06/11/2026 pela Task-R 1. Marcas representam somente o escopo comprovado; implementação e homologação permanecem distintas. Os IDs e os critérios estão em [README](README.md) e [inventário técnico](inventario-tecnico.md). Cada lote deve atualizar sua evidência e os documentos afetados quando for efetivamente realizado.

## L1 — Inventário e compatibilidade

- [x] **L1-T01** Confirmar consumidores, símbolos, projetos, pacotes, DI, UI, localização, testes e dados de REM-001 a REM-009; distinguir implementação, parcial, meta e item não encontrado.
- [x] **L1-T02** Projetar recuperação explícita de rascunhos Script e Agregação: preservar texto, origem, perfil, banco, coleção, arquivo, codificação e estado sujo; nunca converter ou executar silenciosamente.
- [x] **L1-T03** Definir política de versões, migração aditiva quando necessária, falhas visíveis, sessão ilegível e recuperação, sem segunda conexão LiteDB; preservar opt-outs e opt-in de JSON.
- [x] **L1-T04** Definir retenção e acesso aos dados de profiler existentes sem exclusão silenciosa; registrar dependências de agentes e contratos mantidos.
- [x] **L1-T05** Conferir referências dos requisitos MVP 001–015, fase 01 e conceitos de melhorias, preservando providers externos e infraestrutura compartilhada.

## L2 — Modos separados (REM-006)

- [x] **L2-T01** Implementar e testar primeiro a recuperação explícita dos rascunhos legados, inclusive falha, concorrência, recuperação, privacidade e isolamento de abas.
- [x] **L2-T02** Retirar pontos de entrada, execução e código exclusivos dos modos, após comprovar consumidores e compatibilidade; revisar interface, localização e DI.
- [x] **L2-T03** Comprovar Console JavaScript, `aggregate`, abertura e gravação de scripts, histórico, explain e inferência mantidos conforme seus consumidores; executar testes relevantes e inspecionar PNGs reais afetados.

## L3 — Administração avançada (REM-001)

- [x] **L3-T01** Remover seletivamente comandos, UI, contratos e implementações exclusivas do recorte; preservar topologia e serviços usados por operações mantidas.
- [x] **L3-T02** Aplicar a política definida para capturas existentes e validar falha/recuperação; preservar segurança, auditoria e ferramentas de agentes permitidas.
- [x] **L3-T03** Verificar índices, coleções, views e validadores e revisar testes e renderizações afetados.

## L4 — IA automática (REM-007)

- [x] **L4-T01** Separar geração automática dos serviços compartilhados de configuração, modelo e diagnóstico; limpar apenas referências exclusivas, DI e testes correspondentes.
- [x] **L4-T02** Validar que digitação não provoca inferência automática e que ghost determinístico, `Ctrl+;`, agente local, prioridade e cancelamento continuam operacionais.
- [x] **L4-T03** Tratar preferências existentes e revisar interface, localização e renderizações quando afetadas.

## L5 — Metas e verificação (REM-002–005, REM-008–009)

- [x] **L5-T01** Revisar catálogo, fases, backlog e guia para retirar as metas definidas, identificando eventuais trechos parciais antes de qualquer remoção técnica.
- [x] **L5-T02** Preservar conexões a topologias suportadas, configuração de ambientes e credenciais, painel Agente IA, importação/exportação lógica e inferência de schema.
- [x] **L5-T03** Alinhar design system, ADRs, plano e acompanhamento com o comportamento efetivamente entregue, mantendo histórico e evidências.
- [x] **L5-T04** Executar restore locked, build e testes prescritos; executar guardas e IntegrationTests manuais aplicáveis antes de push/release e registrar limites de homologação.
- [x] **L5-T05** Verificar explicitamente que Claude, Codex, APIs externas e MCP externo continuam com composição e disponibilidade existentes.

## Evidência L1 — Task-R 1

Baseline: `8361f73518b0ed2a0c931ea4348d557daf863786`; alteração preexistente: diretório `tasks/` não rastreado. Inventário revisado por Sol, Terra e Luna: [modos legados](compatibilidade-modos-legados.md), [profiler](evidencias-task-r1-profiler.md) e [metas/preservações](evidencias-task-r1-metas.md). Recuperação aditiva de origem em rascunhos, sem execução/conversão silenciosa; tickets do profiler preservados com restauração humana protegida. Reutilizar proprietário LiteDB, opt-outs e proteção da sessão ilegível. L1 é definição e inspeção; os testes de implementação pertencem aos lotes seguintes.

Restore locked e build baseline passaram em Windows/.NET 10.0.402, build com zero avisos/erros. Execução fora do isolamento necessária porque o SDK recebeu acesso negado ao Service Control Manager no sandbox. Testes baseline ainda em consolidação; não constituem validação dos cortes.

## Evidência L2 — Task-R 1

Modos separados e cadeia mongosh removidos; rascunhos legados recuperados em Texto com origem aditiva, JSON opt-in acessível e adaptação explícita sem executar. Arquivos JS normais, Console/aggregate e histórico preservados. Build sem avisos/erros; 37 testes unitários focados e 13 de integração passaram, além do teste visual de entrada legada em quatro idiomas, dois temas e três escalas. TRX locais `task-r1-l2-sol.trx` nas pastas TestResults das duas suítes e `task-r1-l2-input-sol.trx` na integração. PNGs finais em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/Debug/net10.0/ui-evidence/render-f560418649e9414da831efb7687657ae`: inspeção de pt-BR/claro/100% e zh-CN/escuro/200% confirma editor, opt-in e ação de adaptação legíveis, sem corte.

A primeira rodada completa revelou testes ainda acoplados aos modos retirados, corrigidos nos focados; a rodada completa final permanece em L5. Duas falhas de locks temporários em testes LiteDB/crash passaram em repetição isolada (6 casos). Não houve homologação MongoDB real/Linux/diálogos nativos. Explain de agregação conserva contrato interno, sem alegar entrada pelo Console.

## Evidência L3 — Task-R 1

Corte administrativo implementado; ver [evidência e preservações](evidencias-task-r1-profiler.md). Build final da solução passou sem avisos/erros; 50 testes unitários e 16 de integração focados passaram, dois cenários MongoDB real/worker não executados por opt-in. Imagens de recuperação claro/escuro inspecionadas em `render-27bdb5df3d284811a65176a9d4b4b401` na pasta de evidências da integração: ticket, confirmação e ação acessíveis. A fixture mostra também falha visível de repositório ausente; não é homologação nativa nem prova de restauração em servidor real. Preservados validadores, índices, views, transferência, diagnóstico compartilhado e dados LiteDB; API de restauração limitada ao ticket. Rodada completa permanece em L5.

## Evidência L4 — Task-R 1

[Evidências L4](evidencias-task-r1-l4.md): geração/cache automático removidos do serviço Desktop; configuração/status/teste recebem o modelo compartilhado diretamente. Preferências antigas continuam legíveis, IA explícita/agente e provider de avaliação KapiLab preservados. Build solução e KapiLab sem avisos/erros, restore KapiLab locked; 109 testes unitários focados, um adicional de configuração e 28 de integração passaram. Nenhum modelo real ou benchmark executado.

## Fechamento L5 — 06/11/2026

- Metas REM-002–005/008–009 canceladas no escopo oficial, sem exclusões fictícias nem reescrita do histórico. Inventário atualizado para os nove IDs, com links atuais e nomes de arquivos removidos como evidência.
- Removidos também método `EnsureCompactSupported`, partial de scripts e handlers órfãos da janela de ferramentas. Histórico/arquivos das abas permanecem. Retiradas 133 chaves de tradução exclusivas sem consumidor; mensagens de topologia do validador corrigidas nos quatro idiomas.
- `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode`: aprovado, sem mudanças de pacotes/lockfiles.
- `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false -m:1 -nr:false`: aprovado, zero avisos/erros. A opção Avalonia desvia a tarefa externa; analisadores continuam ativos. Compilação serial/sem reutilização contornou esperas do ambiente.
- `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore --logger 'trx;LogFilePrefix=task-r1-final'`: aprovado. UnitTests **3.738 aprovados / 20 ignorados**; Infrastructure.Agents.Tests **403 aprovados**; IntegrationTests **1.127 aprovados / 20 ignorados**. Total **5.268 aprovados**, zero falhas. Os TRX ficam em `tests/<projeto>/TestResults/task-r1-final_net10.0_20261106*.trx`. As guardas de fronteiras e de acesso a modelos fazem parte desta execução local.
- Após remover as duas últimas traduções sem consumidor, build final novamente aprovado sem avisos/erros e filtro `FullyQualifiedName~Localization` aprovado: 34 testes, zero falhas/ignorados. TRX `task-r1-localization-final_net10.0_20261106213029.trx` em UnitTests/TestResults.
- KapiLab: restore locked e build aprovados em L4; nenhum benchmark/modelo real foi executado. CI, providers, pacotes, versões e tags não foram alterados. Fontes de providers e registry de agentes permanecem sem diff.
- Verificados 279 links locais/âncoras/capitalização em docs-new, entradas e tarefa. `git diff --check` passou. Como `docs/` não foi alterado nesta execução, não foi necessário regenerar o leitor histórico.

A suíte final passou também nos testes que haviam apresentado locks transitórios em rodada anterior; isso não elimina a limitação histórica de homologação concorrente prolongada. Os testes opt-in de MongoDB/modelos/contas, Linux nativo, diálogos, IME físico e leitor de tela **não foram homologados**. Permanecem em F01-E04 e nas fases de homologação aplicáveis; a fase 01 não é declarada integralmente concluída. A restauração do profiler não promete atomicidade entre comandos de rede. A UI ainda mostra o estado técnico do ticket (`Uncertain`) e a fixture visual também demonstra falha de repositório; esse polimento continua no recorte de UX.
