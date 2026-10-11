# Aceite PR 1 — catálogo de conhecimento e paridade

Auditoria executada em 10/10/2026 sobre o checkout `0dbb4dd` com alterações locais. Escopo: requisitos do documento externo `pr-1-catalogo-conhecimento-e-paridade.md`, preparação do Kapibara Studio para a Fase 03 do pipeline externo.

| Requisito | Evidência atual | Resultado |
|---|---|---|
| Export CLI/Core, schema v1 e todos os campos estáticos de CatalogSymbol | `KnowledgeCatalogCommand`, `KnowledgeCatalogSnapshot`, `data/lab/knowledge-catalog.json`; API pública real do editor, sem duplicar listas ou ler JSON de linguagem por fora | Atendido |
| Todos os kinds presentes no catálogo do editor e ordenação estável | Teste `ExportMatchesEveryEditorKindAndIsDeterministicWithoutMetadata`: 475 símbolos, 20 kinds; kind/name/id ordinal | Atendido |
| Hash do conteúdo sem o próprio campo e determinismo entre exportações | Duas exportações via apphost: `snapshotSha256=c40be7a8e431bf33ec6514c47940cd093988802b0b172df4c17244a778faa13a`; teste de roundtrip e hash inválido | Atendido |
| Dialeto all padrão, mql/console/dsl filtrados | Registro CLI e três casos `DialectFiltersStaticSymbols`; aliases exclusivamente visuais documentados | Atendido |
| Metadados de usuário ausentes | Fonte de metadata que anuncia Field/QueryOperator recusada antes da consulta; teste verifica ausência de propriedades Field/Scope; DTO só contém conhecimento estático | Atendido |
| Escrita atômica e caminhos de laboratório | Export usa LabWorkspace e move de temporário; teste de saída externa e remoção de temporário. Script Python valida cada relatório e colisão com insumos; teste de falha conserva relatório anterior | Atendido |
| Check códigos 0/7 e added/removed/changed por ID | `CliWritesAtomicSnapshotAndReturnsZeroOrSeven`, `CheckReportsAddedRemovedAndChangedIds`; smoke publicado confirma 0 sem mudança e 7 com dialeto diferente | Atendido |
| Mapeamento e Python externo somente leitura | `parity_editor_context_v1.py`, runbook; prefix/suffix preservados, context → editor, CRLF da IDE rematerializado, contexto ausente materializado explicitamente; import sem gravar bytecode externo | Atendido |
| 2.000 registros conforme fallback permitido | 1.300 fim/completion reais + 700 `samples generate`, seed 20261010. Contrato legado de chat captura LocalAgentProvider, incompatível com AiEditorContext da referência | Atendido pelo fallback do PR 1 |
| Hashes/IDs por referência independente, tokenizer real, diff=0 | HF AutoTokenizer + prompt_text/prompt_ids/QwenFim externos versus adapter GenAI. Quatro arquivos com 2.000 IDs únicos; diff matches/complete/identity=true, differences=[]; nova execução apphost retornou 0 | Atendido |
| Seis saídas nomeadas e provenance | `reports/lab/parity-editor-context-v1/{render-ide.jsonl,render-py.jsonl,tokens-ide.jsonl,tokens-py.jsonl,diff.json,summary.md}`; summary inclui hashes dos insumos, commit/dirty e GenAI 0.15.2 | Atendido |
| Privacidade de logs e ausência de queries/alteração de autocomplete | Log só com contagens/erros por tipo; render persistido só com hashes; texto transitório removido. Nenhum arquivo em src alterado, nenhuma execução MongoDB nesta prova | Atendido |
| Testes novos Cpu/WinML e Python | TRX com os seis testes de catálogo aprovados em ambos; suítes 268 aprovados e 3 opt-in ignorados por backend; Python atual 20/20 | Atendido |
| README, runbook, F7B-05/11 e evidências atualizados | README KapiLab, runbook-local, rastreabilidade-f7b, evidencias-locais; ADR-071 e meta atualizados | Atendido |

Os [hashes dos TRX e detalhes de execução](evidencias-locais.md#pr-1-catálogo-de-conhecimento-e-paridade--10102026) e o [summary da paridade](../../../reports/lab/parity-editor-context-v1/summary.md) delimitam o aceite. O tokenizer BaseRepos difere do pacote ONNX disponível; HF e GenAI usam o tokenizer do pacote ONNX validado, sem pressupor identidade em toda a família.

O PR 1 está atendido com seu fallback explícito. O requisito original da fase de 2.000 registros exclusivamente reais, paridade de chat, treino e homologação GPU/Linux não é comprovado por este aceite e mantém seu estado próprio. KapiLab permanece fora da solução principal; autenticação, pacotes NuGet e comportamento do produto não foram alterados.
