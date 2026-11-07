# REM-006 — compatibilidade dos modos legados

Inspeção L1 em 06/11/2026. Estratégia proposta antes da remoção L2; este registro não comprova implementação ou validação.

## Recuperação

Rascunhos Script e Agregação devem abrir como Texto, com indicação explícita do modo de origem e de que a execução está desativada. O texto não é convertido nem executado. O usuário pode copiar, salvar ou escolher explicitamente Console para adaptar o conteúdo. O modo de origem permanece em campo aditivo do rascunho, inclusive após salvar e reabrir a sessão.

Preservar identificador, perfil ausente ou presente, host, banco, coleção, texto, caminho, codificação, BOM, revisão do arquivo, texto salvo e estado alterado. Entrada JSON mantém opt-in; opt-outs gerais e por conexão continuam aplicados pelo fluxo existente. A recuperação não grava resultados ou credenciais. Histórico de execução legado deve seguir a mesma recuperação explícita, sem disparar consulta.

Não introduzir proprietário LiteDB, migração destrutiva ou substituição de sessão ilegível. Reusar a persistência existente e sua sinalização de falhas.

## Inventário revalidado

| Unidade | Classificação e destino |
| --- | --- |
| `WorkspaceTabViewModel.cs` | Compartilhada: preservar Console, arquivos, snapshots e cancelamento isolado; retirar despachos Script/Agregação e bloquear execução de modos retirados. |
| `WorkspaceTabViewModel.Analysis.cs` | Explain exclusivo do editor de pipeline: retirar superfície; preservar serviços explain compartilhados, sem alegar entrada explain no Console. |
| `WorkspaceDraft`, `WorkspaceSession`, persistência LiteDB | Compatibilidade: leitura das strings antigas e metadados aditivos de origem; sem apagar documentos existentes. |
| `WorkspaceTabView`, histórico e localização | Compartilhados: retirar formulários e bindings exclusivos, acrescentar aviso recuperável localizado. |
| `IScriptExecutionService`, `MongoshScriptExecutionService`, `IMongoshScriptProcessRunner`, `LocalMongoshScriptProcessRunner` | Cadeia exclusiva a remover após substituição da cobertura e consumidores de teste. |
| `MongoshScriptTemplate`, `MongoshOutputParser`, `ParsedMongoshOutput`, `ScriptExecutionResult` | Exclusivos à cadeia removida; testes hoje compartilham referência com datas, UUID e ambientes, cuja cobertura mantida deve sobreviver no Console. |
| `WorkspaceService` e DI | Separar injeção/execução exclusiva sem remover histórico, serviço de arquivos ou runtime Console. |
| `AggregationQuery`, executor Mongo, inferência de campos e runtime Console | Compartilhados: preservar aggregate e inferência do Console e contratos explain compartilhados. |
| Dialetos e vocabulário mongosh do autocomplete | Verificar consumidores de arquivos e sintaxe antes de retirar; nome mongosh isolado não comprova exclusividade. |

## Validação prevista

Recuperação e roundtrip dos dois modos; texto e origem intactos; metadados de arquivo e estado alterado; perfil ausente; entrada opt-in e opt-outs; histórico sem execução; execução negada para modos removidos; sessão ilegível e falha de gravação visíveis; independência entre abas e cancelamentos. Preservar os testes de Console aggregate, BSON, UUID, ambientes, abertura e gravação de arquivos. Inspecionar PNGs reais da superfície alterada.

Resultados e pendências devem ser acrescentados após a implementação; Headless não homologa MongoDB real ou diálogos nativos.

## Limite explain

O runtime Console mantém aggregate, mas não expõe método explain. Explain de consulta continua na ferramenta compartilhada. O contrato e executor ExplainAggregationAsync permanecem cobertos por chamadas diretas dos testes de integração, sem entrada humana no Console ou no editor separado removido.
