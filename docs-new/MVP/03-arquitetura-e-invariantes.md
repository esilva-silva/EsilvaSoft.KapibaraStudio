# Arquitetura e invariantes

O Desktop Avalonia compõe apresentação, casos de uso/contratos e adaptadores de MongoDB, persistência, arquivos, runtime e agentes. Reusar abstrações e composição DI existentes; não criar providers, autenticação ou engines paralelas para refazer fluxos presentes.

## Contexto e execução

O Explorer navega e lê metadados; selecionar ou abrir coleção nunca consulta documentos automaticamente. Cada aba mantém seu destino. Antes de await, capturar perfil, banco, coleção quando aplicável, texto, opções e identidade/geração da solicitação. Resposta e progresso só atualizam a aba/turno que os originou; descartar respostas obsoletas.

CancellationTokenSource pertence à operação/aba, sem compartilhamento entre abas. Cancelar não prova rollback de operações já enviadas. Runtime Console suporta subconjunto explícito de JavaScript/MongoDB; não expor CLR, arquivos, rede JS ou comandos genéricos como fallback. O processo mongosh dos modos separados foi retirado na Task-R 1/L2; compatibilidade legada recupera texto e origem, sem executar o runtime removido.

## Dados e persistência

Preservar BSON/Extended JSON, ordem aplicável e representações UUID. Edição deve preservar destino, proteção contra conflito, somente leitura, confirmações e auditoria. Exportar página não significa exportar o banco; transferência lógica não é backup operacional.

LiteDB tem proprietário único registrado em DI. Migrações são aditivas/versionadas; nunca abrir segunda conexão ao mesmo arquivo local para contornar ownership. Persistência ilegível permanece preservada com falha visível e recuperação explícita; não sobrescrever com sessão vazia.

Rascunhos respeitam opt-out geral/por conexão. Entrada JSON exige opt-in. Não persistir resultados nem credenciais em snapshots de sessão. Mudanças de contexto, sessão e retenção exigem falha, concorrência e recuperação verificadas.

## Agentes

Tools nunca leem documentos nem escrevem diretamente no MongoDB, mesmo com aprovação. Podem consultar metadados/índices comuns e Atlas Search; resultados, erros e logs vêm exclusivamente de execução humana já capturada. Cache/schema não amostra documentos nem atualiza por consulta implícita.

Registry, grants, auditoria e confirmação são fronteiras obrigatórias. Revogação, expiração e eventos tardios devem negar ou descartar acesso conforme identidade capturada. Proposta de edição é revisável, com conflito/Undo; criação de arquivo exige opt-in e confirmação pontual, arquivo novo dentro da workspace, sem sobrescrita ou escape por links.

Copilot usa CLI/runtime oficial instalado pelo usuário e autenticação oficial. Override inválido não seleciona silenciosamente outra instalação. Ferramentas nativas não autorizadas permanecem bloqueadas. Providers adiados preservam contratos e gates; Claude Code mantém autenticação oficial, sem leitura de credenciais/tokens, endpoints privados ou fallback Anthropic API.

IA local é opcional, compartilha infraestrutura vigente e só recebe texto pelo comando explícito de autocomplete ou envio humano do agente. Modelo ausente não bloqueia edição/consulta determinística. Sessões e recursos de inferência precisam isolamento, cancelamento e liberação segura.

Contratos históricos: [tools/ADR-066](../../docs/architecture/agent-tool-safety.md), [arquivos/ADR-067](../../docs/architecture/workspace-file-creation.md) e [ADRs](../../docs/10-decisoes-arquiteturais.md).

## Projetos e composição concretos

| Projeto | Responsabilidade e fronteira |
| --- | --- |
| Core | Modelos e contratos fundamentais do domínio, sem UI. |
| Application | Casos de uso/serviços de aplicação e contratos consumidos pelos adapters. |
| Autocomplete.Core | Contexto, catálogo e lógica determinística de sugestões, independente de pesos. |
| LocalAi.Core | Contratos de inferência/modelo/sessão e protocolo do agente local. |
| Infrastructure | MongoDB, Console e registro principal de infraestrutura, incluindo composição das portas de repositório. |
| Infrastructure.System | Adapters de sistema operacional, arquivos, processos, caminhos, proprietário/repositórios LiteDB e integração nativa. |
| Infrastructure.LocalAi | Serviços/composição de catálogo, instalação e runtime local. |
| Infrastructure.LocalAi.OnnxAdapter | Adapter ONNX GenAI e dependências nativas específicas de backend. |
| Infrastructure.Agents | Providers, runtime/sessões, registry/tools e infraestrutura compartilhada dos agentes. |
| Desktop | Views/ViewModels Avalonia, captura de contexto da UI e composition root. |
| tools/KapiLab | CLI interna de avaliação/relatórios e ensaios; não substitui jornada do Desktop. |

A composição concreta em App.axaml.cs registra ILocalWorkspacePaths, chama AddKapibaraStudioInfrastructure com o caminho do banco, AddKapibaraStudioLocalAiInfrastructure e AddDesktopAgentServices; WorkspaceService/WorkspaceViewModel são singletons. A janela recebe o ViewModel do container. A saída interrompe disponibilidade dos providers e dispõe o ServiceProvider; novos caminhos devem respeitar esse ciclo e o owner LiteDB já composto.

Fontes de estrutura: [solution](../../EsilvaSoft.KapibaraStudio.slnx), [composition root](../../src/EsilvaSoft.KapibaraStudio.Desktop/App.axaml.cs), [adapter ONNX](../../src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter.csproj), [KapiLab](../../tools/KapiLab/KapiLab.csproj). A proibição de amostragem/refresh do schema acima se aplica às tools de agentes; não proíbe uma jornada humana explícita suportada de inferência de schema.
