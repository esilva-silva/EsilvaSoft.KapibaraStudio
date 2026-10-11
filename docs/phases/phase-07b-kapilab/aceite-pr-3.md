# Preparação PR 3 — catálogo e execução de tools em fixture

Implementação local em 11/10/2026 para o pipeline T4, preservando ADRs 066/067/070/072. O documento externo é referência de escopo; os contratos vigentes determinam o comportamento de produção. [Contrato executável, formatos e comandos](../../../tools/KapiLab/Fixtures/kapi-sim/README.md).

| Critério | Evidência e estado |
| --- | --- |
| Catálogo local completo por modo | Implementado: 11 tools em Agent/Automatic/AskConfirmations e 9 em Planning; descrição compartilhada, input/output schemas versionados e hashes, categorias, escopos, cota e limite do prompt. `approve` omitida com motivo; nenhuma tool removida exposta nos modos/providers testados. `complete=true` significa cobertura do máximo por modo, não autorização de sessão. |
| Schemas contra saídas reais | Pelo menos 20 saídas validadas para cada uma das dez tools que têm sucesso no MongoDB Community. `get_search_indexes`: 20 falhas reais de serviço, sem retorno de sucesso; validação do output schema em Atlas permanece pendente. Nenhum schema foi relaxado. |
| Host real e matriz | 315/315 casos de referência e 500/500 casos no lote adicional pelo runtime/registry/política/auditoria reais, MongoDB Community 8.0.32 no WSL. Grants e confirmações sintéticos; LiteDB e workspace temporários. Preparação de resultados pelo serviço de query da IDE e de schema pelo cache/inferência de produção. |
| Segurança | URI loopback com conexão direta; validação antes de seed; bancos semeados `kapi_sim_*`; nomes de banco retornados filtrados. Sem source de leitura/escrita direta de documentos por tool. Nomes removidos continuam ausentes. Arquivo criado apenas em pasta temporária; proposta não salva. |
| Resposta exata | `toolResponse` usa o builder congelado no PR 2. Cota excedida termina o runtime sem mensagem adicional; o relatório registra null e erro do runtime, com respostas anteriores separadas. `create_workspace_file` preserva criação real no workspace temporário conforme ADR-067. Essas diferenças do documento externo são explícitas. |
| Replay e comparador | 44/44 passos do replay versionado aprovados. Comparador de 500 chamadas entregue, com gate ≥99% e zero permissões extras; seis testes Python aprovados. Paridade com `kapisim` ainda não executada: adaptador pertence ao workspace de treino. |

Evidências locais: `reports/lab/pr-3/reference-test.json`, `reports/lab/pr-3/real-500.json`, `reports/lab/pr-3/replay.json` e TRXs em `reports/lab/pr-3/tests-real/`. A primeira tentativa no sandbox não alcançou o loopback/testhost; a primeira execução explícita fora do sandbox encontrou o encaminhamento WSL inativo. Manter uma sessão WSL ativa e permitir o loopback resolveu; falhas anteriores permanecem nos artefatos. Teste explícito real passou 1/1 e verificou ≥20 saídas por tool aplicável. Não equivale a inferência, treino ou task success.

Os cenários de expiração/revogação encerram o **grant transitório de turno**: não provam TTL de grants externos/persistidos. Dois casos adicionais capturam o erro real de um find sintético inválido (`errorCode=2`) e validam resultados vazios e diagnóstico sanitizado. Logs de mongosh, timeout/cancelamento humano e execuções com origens secundárias mantêm seus aceites próprios. Testes existentes de produção mantêm sua cobertura própria. Nenhuma política de produção foi relaxada.

Pendente para aceite literal integral do documento externo: 20 saídas de sucesso reais de `get_search_indexes` em Atlas e paridade ≥99% com o simulador externo. A preparação do host e do comparador não encerra esses gates.

Validação completa: **707 testes KapiLab aprovados por backend Cpu/WinML**, **26/26 Python**, **5.219 testes de produto aprovados**, zero falhas. Os [hashes e contadores](../../../reports/lab/pr-3/summary.json) e as [evidências detalhadas](evidencias-locais.md#pr-3-catálogo-e-execução-real-em-fixture--11102026) preservam os limites e as tentativas anteriores.
