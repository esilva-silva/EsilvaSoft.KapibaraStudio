# Task-R 1 - Limpeza e remoção de código

**Estado:** implementação concluída nos lotes L1–L5; validação automatizada registrada, homologação real/nativa separada. **Prioridade:** fase nova 01. **Versão-alvo:** v0.10.0, sem alterar automaticamente versões de binários ou tags.

## Objetivo

Implementar os cortes aprovados de REM-001 a REM-009, eliminando código exclusivo, caminhos de execução obsoletos e referências sem uso. Preservar as capacidades do MVP do **EsilvaSoft.KapibaraStudio — MongoDB Database Management & Development Tool**, os dados existentes e a infraestrutura compartilhada.

Criar este arquivo não executa a limpeza. Ao executar a tarefa, entregar alterações de código, regressões, documentação e evidências; não encerrar apenas com outro plano ou ocultando botões enquanto a implementação exclusiva permanece sem justificativa.

## Referências obrigatórias

- [Regras do repositório](../AGENTS.md).
- [Escopo de remoção](../docs-new/Remover/README.md), [inventário técnico e fontes](../docs-new/Remover/inventario-tecnico.md) e [TODOs L1–L5](../docs-new/Remover/TODO.md).
- [Fase 01 — v0.10.0](../docs-new/MVP/fases/fase-01-v0.10.0.md), entregas F01-E01 a F01-E04.
- [Requisitos mantidos](../docs-new/MVP/02-requisitos-e-estado.md), [arquitetura e invariantes](../docs-new/MVP/03-arquitetura-e-invariantes.md), [design system](../docs-new/MVP/04-design-system.md) e [validação](../docs-new/MVP/06-validacao-e-evidencias.md).
- [Melhorias adiadas](../docs-new/Melhorias/conceitos.md): adiamento não autoriza exclusão de capacidades existentes.

O inventário é a base de navegação no código. Revalidar seus caminhos e consumidores no checkout da execução; não excluir arquivos por nome, pasta ou referência histórica isolada.

## Escopo e fronteiras

| IDs | Trabalho |
| --- | --- |
| REM-001 | Retirar profiler, killOp, validate/compact, parâmetros runtime e gestão de usuários/papéis; preservar diagnóstico/topologia necessários às operações mantidas. |
| REM-002/003 | Retirar compromissos de administração do host, SSH administrativo, operação de clusters e provisionamento/recuperação operacional. Conectar e consultar esses ambientes permanece. |
| REM-004 | Retirar a meta de cofre genérico/KMS; preservar EnvironmentVault, configuração de ambientes e secret stores usados pelo Studio. |
| REM-005 | Cancelar o chat separado por workflow; preservar painel Agente IA, conversas e runtime. |
| REM-006 | Retirar os modos separados Script/mongosh e Agregação, com recuperação explícita de rascunhos antigos. Preservar Console JavaScript, aggregate e arquivos de scripts. |
| REM-007 | Remover infraestrutura exclusiva da IA automática na digitação; preservar ghost determinístico, Ctrl+Espaço, IA explícita Ctrl+; e agente local. |
| REM-008/009 | Retirar metas de backup operacional, sincronização/jobs, SQL, geração de dados, migrações, Git integrado e serviços de ecossistema; preservar transferência lógica, schema e ferramentas de desenvolvimento do repositório. |

Itens apenas planejados não geram exclusões fictícias. Registrar os locais pesquisados e o resultado; se houver código parcial, identificar sua exclusividade antes de cortar. Não implementar funcionalidades de Melhorias nesta tarefa.

**Preservar em todos os lotes:** CRUD, BSON/Extended JSON/UUID, coleções, índices, views, validadores, importação/exportação lógica, arquivos, histórico e rascunhos; Copilot e agente local; Claude, Codex, APIs externas e MCP externo com composição/disponibilidade atuais; permissões, confirmações, auditoria e opt-outs.

Agentes nunca consultam documentos nem escrevem diretamente no MongoDB, mesmo com aprovação. Explorer não executa consulta automaticamente. Respostas permanecem vinculadas à origem; cancelar não é rollback. LiteDB conserva proprietário único em DI, migrações aditivas/versionadas e proteção de sessões ilegíveis.

## Sequência de implementação

Executar L1 → L2 → L3 → L4 → L5. Os IDs abaixo referenciam o TODO oficial; registrar lá o progresso detalhado e aqui o aceite dos lotes. Não marcar tarefas sem evidência.

### L1 — Dependências e compatibilidade (L1-T01 a L1-T05; F01-E01/E02)

- [x] Registrar baseline do checkout e preservar alterações locais preexistentes. Mapear cada corte até UI, bindings, localização, contratos, implementação, DI, consumidores, projetos/pacotes, persistência e testes.
- [x] Classificar unidades como exclusivas a remover, compartilhadas a separar/preservar ou compatibilidade temporária justificada. Procurar referências também em testes, ferramentas, scripts e empacotamento.
- [x] Definir e documentar recuperação explícita de rascunhos Script/Agregação: texto, modo/origem, perfil, banco, coleção, arquivo, codificação e estado alterado preservados; sem conversão ou execução silenciosa.
- [x] Definir leitura/recuperação das capturas persistidas de profiler antes de retirar seus fluxos. Não apagar capturas, ambientes, credenciais ou sessões como efeito colateral da limpeza.

**Saída:** mapa revisado e estratégia de compatibilidade registrada. Implementar e testar a compatibilidade antes da exclusão de seus consumidores.

### L2 — Modos legados (L2-T01 a L2-T03; REM-006)

- [x] Implementar a recuperação definida em L1, com falha visível, preservação de sessão ilegível, opt-outs e proprietário LiteDB único.
- [x] Remover despacho, UI, contratos, DI, runner, template, parser e testes exclusivos dos modos retirados após verificar consumidores. Manter somente o código de compatibilidade necessário e justificar sua permanência.
- [x] Verificar Console/aggregate, inferência de campos, explain compartilhado, histórico e abrir/salvar scripts. Não transformar consultas ou arquivos antigos em execução automática.

**Saída:** modos removidos sem perda de texto/origem; falha, concorrência e recuperação testadas; Console preservado.

### L3 — Administração pesada (L3-T01 a L3-T03; REM-001)

- [x] Remover comandos, formulários, bindings, DTOs, métodos, DI e traduções exclusivos da administração retirada, seguindo a cadeia do inventário.
- [x] Aplicar a estratégia de recuperação das capturas. Separar serviços compartilhados antes de apagar classes como MongoServerAdministrator; não eliminar topologia necessária às jornadas mantidas.
- [x] Validar bancos/coleções, índices, views, validadores e transferência, incluindo somente leitura, confirmação, auditoria e publicação do resultado na origem correta.

**Saída:** nenhuma entrada ou execução exclusiva dos recursos cortados; capacidades cotidianas e dados anteriores preservados.

### L4 — IA automática legada (L4-T01 a L4-T03; REM-007)

- [x] Separar geração automática de configuração, status, teste de modelo e outras responsabilidades compartilhadas antes de remover referências e DI exclusivos.
- [x] Tratar preferências antigas sem perda de configurações úteis; manter pipeline de inferência, prioridade/preempção, cancelamento, agente local e avaliação necessária no KapiLab.
- [x] Comprovar que digitar não dispara inferência, Ctrl+Espaço/ghost funcionam sem modelo e Ctrl+; mantém prévia, aceite, descarte e Undo.

**Saída:** caminho automático exclusivo eliminado; autocomplete determinístico e IA explícita continuam independentes e funcionais.

### L5 — Fechamento e documentação (L5-T01 a L5-T05; REM-002–005/008–009; F01-E03/E04)

- [x] Registrar os cortes documentais das metas sem implementação e eventuais componentes parciais encontrados, sem remover configurações/providers preservados.
- [x] Buscar referências residuais em código, DI, UI, localização, projetos e pacotes; remover apenas as exclusivas comprovadas. Não remover pacotes compartilhados nem alterar lockfiles manualmente para ocultar problemas.
- [x] Atualizar requisitos, decisões, guia, design system e fase em docs-new conforme o comportamento entregue. Preservar cronologia/evidências em docs; regenerar o índice histórico somente se seus documentos mudarem.
- [x] Concluir a validação aplicável e registrar explicitamente os ambientes/cenários ainda não homologados.

## Validação obrigatória da implementação

Executar testes significativos por lote e os comandos finais prescritos:

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
git diff --check
```

Se a telemetria Avalonia for bloqueada pelo ambiente, registrar o uso de `-p:UsedAvaloniaProducts=` no build. Não desabilitar analisadores nem testes. Manter o backend consistente entre restore/build/test e o opt-in de Benchmarks desativado.

- Sessão: rascunhos dos dois modos legados, origem/texto intactos, arquivo ilegível, falha de gravação, cancelamento, reabertura e operações concorrentes entre abas.
- Profiler: recuperação de captura pendente e falhas, sem perda silenciosa de dados ou alteração implícita do servidor ao abrir a sessão.
- Base: abrir coleção sem consultar, CRUD/BSON/UUID, aggregate, índices/views/validadores, transferência lógica, arquivos e credenciais/configurações preservadas.
- IA/agentes: digitação sem inferência, IA explícita, ghost, cancelamento isolado e composição/disponibilidade dos providers preservados.
- UI alterada: gerar e inspecionar PNGs reais nos temas, dimensões e escalas aplicáveis. Não ajustar goldens ou asserções apenas para ocultar regressões.
- Executar IntegrationTests e guardas estáticas aplicáveis, incluindo RealModelHubAccessIsConfinedToExplicitLocalModelTests, localmente antes de push/release. CI continua restrito às suítes unitárias previstas.
- Não baixar/carregar modelos nem executar benchmarks no CI/release. Ensaios reais são manuais e opt-in; usar fixtures descartáveis para MongoDB, sem operar dados do usuário.

Headless não homologa MongoDB real, modelos, leitor de tela ou diálogos nativos. Resultado ignorado ou ambiente indisponível permanece pendente; não declarar aceites além da evidência obtida.

## Critérios de conclusão e entrega

- [x] REM-001–009 possuem resultado explícito: código exclusivo removido, compatibilidade justificada ou meta documental retirada.
- [x] Jornadas preservadas e recuperação passam nos testes aplicáveis; referências residuais possuem consumidor e justificativa documentados.
- [x] Nenhuma credencial, resultado, sessão ou configuração foi descartada silenciosamente; nenhuma segunda conexão LiteDB foi introduzida.
- [x] TODO oficial, requisitos e fase refletem o resultado real, sem declarar toda a fase concluída se faltar aceite obrigatório.
- [x] Entregar resumo dos arquivos/símbolos removidos e preservados, decisões de compatibilidade, comandos/resultados, capturas inspecionadas e pendências com destino explícito.

Registrar evidências por lote com commit/snapshot, ambiente, procedimento, esperado/observado e artefatos sanitizados. Não publicar release, renumerar versões ou executar funcionalidades adiadas como parte desta tarefa.

## Resultado da execução

Lotes executados na ordem com Sol, Terra e Luna. Resultado, comandos, contagens e limitações em [TODO oficial](../docs-new/Remover/TODO.md). Suíte completa: 5.268 aprovados, zero falhas, 40 ignorados; builds solução/KapiLab aprovados. Sem release, alteração de versão/tag ou remoção de providers preservados. A conclusão de implementação desta tarefa não conclui a homologação da fase 01 (F01-E04).
