# Fixture de referência KapiSim v1

Três domínios fictícios: loja, logística e IoT. Cada banco `kapi_sim_store`, `kapi_sim_logistics` e `kapi_sim_iot` tem 36 documentos determinísticos, coleção `items` e três índices comuns (incluindo `_id_`). Não contém PII real. IDs e ordem de inserção são fixos; `fixtureSha256` cobre o JSON serializado dos domínios na ordem do manifesto.

`cases/reference.json` contém 315 casos: 20 execuções por tool e cenários de autorização, fim do grant de turno, confirmação, argumentos inválidos, cota, truncamento e destinos inexistentes quando aplicáveis. `cases/trajectory.json` contém 44 respostas esperadas versionadas. São contratos de referência, sem modelo ou dataset de treino.

```powershell
./tools/KapiLab/Fixtures/kapi-sim/seed.ps1
dotnet tools/KapiLab/bin/Debug/net10.0/EsilvaSoft.KapibaraStudio.KapiLab.dll --workspace . catalog invoke --fixture tools/KapiLab/Fixtures/kapi-sim/cases/reference.json --out reports/lab/pr-3/reference.json
dotnet tools/KapiLab/bin/Debug/net10.0/EsilvaSoft.KapibaraStudio.KapiLab.dll --workspace . agent replay --in tools/KapiLab/Fixtures/kapi-sim/cases/trajectory.json --out reports/lab/pr-3/replay.json
```

A semeadura **recria** apenas os bancos listados. O host de invocação/replay também semeia antes de exportar resultados: assim nenhuma saída vem de dados preexistentes desconhecidos. `KAPILAB_MONGO_URI` admite somente `mongodb://` com IP loopback e porta explícita, sem credenciais, SRV, DNS ou opções arbitrárias. O host fixa `directConnection=true`, impedindo descoberta de outros servidores. Todos os casos e domínios são validados antes da primeira operação MongoDB; `sample_mflix` é recusado. A descoberta de bancos passa pela fonte de produção e filtra os nomes aos três bancos semeados; os limites de cursor são preservados.

Os campos do manifesto `kapilab-tool-fixture-v1` são `schema`, `domains` (paths relativos ao workspace), `maxToolResultBytes` (1–65536) e `cases` (1–500). Cada caso tem `id`, `tool`, `arguments` (objeto JSON), `database`, `collection`, `scenario`, `expectedStatus`, `expectedErrorCode` e, opcionalmente, `expectedAuditReason`, `expectedToolResponse` e `volatileFields`. Conexões usam o placeholder `$fixture`; banco/coleção devem pertencer ao domínio semeado. Paths são nomes simples contidos no diretório temporário. Campos desconhecidos/duplicados são recusados.

Os grants de metadados/schema/documentos são os **grants transitórios reais de produção**, gerados por `NativeChatTurnPolicyProvider` a partir do plano e permissões sintéticos capturados no host. O anchor interno no LiteDB temporário fica vazio. `grant_expired` e `grant_revoked` removem a scope de turno antes do dispatch: representam o término do grant transitório, não um mecanismo novo de TTL de grants persistidos. O host e as portas sintéticas existem somente na assembly KapiLab; não são registrados no Desktop.

`get_query_results`/`get_query_diagnostics` usam `IMongoWorkspaceService.QueryAsync` na preparação humana sintética, com find fixo e ordenado, antes de enviar o turno. `MetadataCache.SampleSchemaAsync` prepara o schema com o mesmo serviço de inferência do autocomplete. Tools nunca executam essa query ou amostragem. Todos os resultados, IDs de origem e autorizações são capturados por caso; cada caso usa uma sessão e pasta temporária próprias. `create_workspace_file` mantém o ADR-067: cria um arquivo novo **somente nessa pasta temporária**, após confirmação sintética; `propose_file_edit` registra uma proposta e não salva a alteração.

O relatório `kapilab-tool-invocation-report-v2` contém, por caso, argumentos resolvidos, status/erro, auditoria real, validação do output schema, propostas registradas, limite aplicado e `toolResponse` produzido por `LocalAgentPromptBuilder.BuildToolResponse`. Não inclui documentos no ledger de auditoria. `grantsSynthetic=true` distingue o laboratório. Cota excedida termina o turno **antes** de uma nova entrega: `toolResponse=null`, `runtimeErrorCode=ToolCallLimitExceeded`; `priorToolResponses` guarda as mensagens das chamadas admitidas. Não se inventa uma resposta de tool para esse caso.

O Community 8.0.32 não tem `mongot`: `get_search_indexes` preserva `Failed/ExecutionFailed` e auditoria `ExecutionFailed`, sem `$search` ou fallback. Há 20 falhas reais, mas **nenhuma saída de sucesso Atlas** para validar contra o output schema. Esse aceite requer ambiente Atlas separado.

Replay aceita `kapilab-agent-trajectory-v1` com os mesmos campos, exigindo `expectedToolResponse` salvo para cada entrega (exceto cota que não entrega mensagem). Sem tolerâncias, compara a string exata. Com tolerâncias, compara o envelope e o JSON de `result` por campos, normalizando apenas `executionId`, `proposalId`, `durationMs`, `observedAt`, `workspaceFolder`, `connectionId` e `id` explicitamente listados. Status, erros, truncamento e documentos EJSON nunca são ignorados. Cada passo reconstitui seu estado sintético; não reaplica edições de passos anteriores.

## Adaptador externo kapisim

```powershell
python scripts/lab/parity_kapisim.py --simulator C:/IA/models/KapibaraStudio.MongoIA/kapisim_adapter.py --workspace . --backend Cpu
# Desenvolvimento explícito, com build Cpu atual:
python scripts/lab/parity_kapisim.py --simulator <adaptador.py> --workspace . --backend Cpu --dotnet dotnet
```

O script executa 500 casos pelo host real via `kapilab.run` (apphost/hash verificados por padrão). Executa o adaptador Python uma vez, sem shell, com timeout. O stdin contém uma linha JSON `{schema:"kapilab-kapisim-input-v1", domains:[objetos dos domínios], maxToolResultBytes, calls:[casos]}`. Os casos enviados ao simulador omitem todos os campos `expected*`; não há resposta real nesse input. O stdout deve ter 500 linhas `{id,status,errorCode,toolResponse}`, com IDs exatos e únicos; uma chamada que encerra por cota tem `toolResponse:null`.

O comparador normaliza os mesmos sete campos voláteis documentados, calcula concordância e reprova abaixo de 99%. Qualquer `Succeeded` do simulador diante de recusa/falha real reprova independentemente da taxa (gate conservador, incluindo indisponibilidade de serviço). O relatório guarda hashes do fixture/adaptador e resumo do runtime. Sem adaptador externo, não há declaração de paridade. Testes unitários do comparador não representam execução de `kapisim`.
