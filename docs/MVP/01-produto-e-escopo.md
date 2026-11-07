# Produto e escopo

O EsilvaSoft.KapibaraStudio é um **Studio — MongoDB Database Management & Development Tool** desktop, em .NET 10/Avalonia, para Windows e Linux, sob licença MIT. Interface e documentação usam pt-BR; identificadores de código usam inglês. A localização existente em en, es e zh-CN é preservada.

## Resultado esperado

Uma pessoa conecta a um MongoDB, explora metadados, escreve e executa consultas e scripts, edita documentos com proteção, salva seu trabalho e transfere dados lógicos. Pode usar autocomplete determinístico sem modelo e, opcionalmente, IA local ou GitHub Copilot.

O MVP compreende exatamente MVP-001..015 do [catálogo](02-requisitos-e-estado.md): conexões, Explorer, consulta, CRUD, resultados/exportação de página, Console, arquivos/histórico/workspace, autocomplete determinístico, gestão de bancos/coleções/índices/views/validadores, transferência lógica, sessão/experiência, IA explícita, agente local, Copilot e atualização.

## Cortes aprovados para execução futura

Retirar administração pesada: profiler, killOp, validate/compact, parâmetros runtime e usuários/papéis. Preservar gestão cotidiana de coleções, índices, views e validadores. Retirar metas de administração de host/SSH administrativo, configuração/reinício, operação de replica sets/sharding, provisionamento Atlas/nuvem, snapshots e recuperação operacional. Conectar a esses ambientes continua permitido.

Retirar cofre genérico/KMS, chat separado por workflow, modos separados Script/mongosh e Agregação e infraestrutura obsoleta de IA automática durante digitação. Preservar Console JavaScript com aggregate, proteção de credenciais, autocomplete determinístico e IA explícita.

Backup por Database Tools, sincronização/jobs, SQL para MQL, geração de dados, migrações versionadas, Git integrado e Federation/Archive/Charts/conectores não são metas do MVP. A retirada exige inventário de dependências e tratamento dos rascunhos/sessões antigos antes de apagar contratos. [Remover](../Remover/README.md) concentra execução e rastreabilidade.

## Depois do MVP

Claude, Codex, APIs externas e MCP externo são preservados e adiados, sem fase/versão comprometida; infraestrutura compartilhada necessária a IA local/Copilot permanece. Scripts entre conexões, explain, exportação de código, transações, comparação de dados/estrutura, jornadas Search/Vector/GridFS/séries temporais/change streams e autenticações especializadas podem evoluir por caso de uso. [Melhorias](../Melhorias/README.md) registra esses conceitos.

GPU/NPU são evoluções condicionais por pacote/equipamento. KapiLab é ferramenta interna de avaliação; sua pesquisa completa não bloqueia o Studio. Treino/publicação de pesos pertencem ao projeto externo KapiCoder-Mongo.
