# Remoções e metas fora do MVP

Este diretório acompanha os nove itens do tópico 2 de [MVP, cortes e próximas metas](../../docs/mvp-status.md). A Task-R 1 executou cortes delimitados em REM-001, REM-006 e REM-007; REM-002–005 e REM-008–009 retiram metas do escopo do MVP, sem declarar exclusão de código. Registros históricos, backlogs e evidências em `docs/` permanecem como contexto e não são reescritos como estado atual.

| ID | Estado Task-R 1 | Decisão e evidência |
|---|---|---|
| REM-001 | Executado em L3 | Cortados fluxos exclusivos de administração avançada. Preservados diagnóstico compartilhado, operações cotidianas e recuperação protegida de tickets do profiler. [Evidência L3](evidencias-task-r1-profiler.md). |
| REM-002 | Meta retirada do MVP | Administração do host, SSH administrativo, configuração/reinício de servidor e monitoramento externo não são entregas do MVP. Isso não remove conexão por connection string nem proteção de áreas `.ssh`. [Auditoria L1](evidencias-task-r1-metas.md). |
| REM-003 | Meta retirada do MVP | Operação/provisionamento de clusters, Atlas/nuvem, snapshots e recuperação operacional não são entregas do MVP. Conexões e leitura de metadados/topologia suportadas permanecem. [Auditoria L1](evidencias-task-r1-metas.md). |
| REM-004 | Meta retirada do MVP | Cofre criptográfico genérico e KMS não são entregas do MVP. `EnvironmentVault`, configuração de ambientes, secret stores e credenciais existentes permanecem. [Auditoria L1](evidencias-task-r1-metas.md). |
| REM-005 | Meta cancelada | Não será criado chat separado por workflow. O painel Agente IA, sessões e providers atuais permanecem. [Auditoria L1](evidencias-task-r1-metas.md). |
| REM-006 | Executado em L2 | Retirados os modos separados Script/mongosh e Agregação; rascunhos legados são recuperados em Texto, com origem e sem execução automática. Console JavaScript, `aggregate`, arquivos e histórico permanecem. [Compatibilidade](compatibilidade-modos-legados.md) · [evidências e acompanhamento](TODO.md). |
| REM-007 | Executado em L4 | Retirada a geração automática de IA durante a digitação. `Ctrl+;`, ghost determinístico, agente local, configuração/teste de modelo e uso de `AiAutocompleteProvider` no KapiLab permanecem. [Evidência L4](evidencias-task-r1-l4.md). |
| REM-008 | Meta retirada do MVP | Backup operacional por Database Tools, sincronização automática e jobs agendados não são entregas do MVP. Importação/exportação lógica permanece e não equivale a backup operacional. [Auditoria L1](evidencias-task-r1-metas.md). |
| REM-009 | Meta retirada do MVP | SQL para MQL, gerador de dados, migrações versionadas, Git integrado e Federation/Archive/Charts/conectores não são entregas do MVP. Console JavaScript, `aggregate`, schema/inferência mantidos e Git usado no desenvolvimento permanecem. [Auditoria L1](evidencias-task-r1-metas.md). |

## Execução e limites

L1 distinguiu fluxos implementados, componentes parciais e metas documentais. L2 e L3 têm evidências focadas registradas; L4 também tem build e testes focados registrados. Isso não declara homologação completa: permanecem as pendências e limites descritos em [TODO e evidências](TODO.md), com suíte completa automatizada aprovada no L5 e pendências de ambiente nativo Windows/Linux, acessibilidade, modelo CPU real e cenários MongoDB real opt-in.

As metas REM-002–005 e REM-008–009 foram retiradas documentalmente; a inspeção não encontrou jornadas integradas completas, mas não prova ausência universal de protótipos ou código parcial. Não apagar projetos, pacotes, dados, configurações, providers ou código adjacente por associação de nome. Verificar consumidores atuais e compatibilidade antes de qualquer mudança técnica.

O escopo vigente do produto está em [requisitos MVP-001–015](../MVP/02-requisitos-e-estado.md), [fase 01](../MVP/fases/fase-01-v0.10.0.md) e [conceitos de melhorias](../Melhorias/conceitos.md). Preservar Console, operações cotidianas, importação/exportação lógica, configuração/credenciais, painel Agente IA e providers existentes. Claude Code conserva autenticação oficial; não ler credenciais, reutilizar tokens, acessar endpoints privados ou fazer fallback à Anthropic API. As ferramentas de agentes continuam sob as invariantes de segurança em `AGENTS.md`.
