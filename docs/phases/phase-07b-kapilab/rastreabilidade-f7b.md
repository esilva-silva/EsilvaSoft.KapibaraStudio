# Rastreabilidade — Fase 7B / F7B-KAPILAB

**Data:** 09/10/2026. **Estado inicial:** 25 tarefas planejadas; nenhum aceite técnico nesta preparação.

Fonte: [kapilab-console.md](C:/IA/models/KapibaraStudio.MongoIA/docs/kapilab-console.md). As referências §/B/D/K abaixo pertencem à fonte; o enquadramento e os critérios conciliados estão na [meta](meta-de-implementacao.md). IDs F7B são locais a esta fase e não substituem os IDs do backlog da 7A.

## Tarefas e aceite

| ID | Marco | Entrega | Origem | Depende de | Evidência mínima de aceite | Estado |
| --- | --- | --- | --- | --- | --- | --- |
| F7B-00 | K0 | Auditoria, baseline e proposta arquitetural | §2/3/10/12 | Checkout e AGENTS.md | APIs/composição conferidas; falhas prévias separadas; decisões e fronteiras 7A/Python documentadas | Planejado |
| F7B-01 | K0 | Projeto CLI, testes, dependências e builds Cpu/WinML | §3.1–3.3/8; B8 | 00 | Restore locked e build limpo por backend/RID adotado; fingerprint; sem Desktop/Benchmarks Exe; nativos corretos | Planejado |
| F7B-02 | K0 | Opções/exit codes, schemas, JSONL, workspace, privacidade e cancelamento | §4/6/7 | 01 | stdout puro; erros/timeout mapeados; entradas limitadas; traversal/junction/segredo recusados; escrita interrompida não vira resultado completo | Planejado |
| F7B-03 | K0 | `env` e `npu inventory` | §5.1/5.10; B7 | 01/02 | Versão esperada divergente → 7; ambiente sem segredos; inventário sem download/carga; falta de dispositivo explícita | Planejado |
| F7B-04 | K1 | `contract render` v1 e captura do chat legado | §5.2/8.3; B4 | 02 | Builders/provider reais; golden independente; CRLF/escape/contexto preservados; nenhuma inferência para renderização textual | Planejado |
| F7B-05 | K1 | `contract tokenize/diff` e host tokenizer | §5.2; B3; D5 | 03/04; pacote/tokenizer | Hash/família/configuração verificados; orçamento igual à IDE; diferenças por campo/ID; 2.000 registros reais 100% ou falha 11 | Planejado |
| F7B-06 | K1 | `catalog export/check` por provider | §5.3/6.3; B1/B2 | 00/02; canal local da 7A quando usado | Catálogo real canônico; removidas/approve fora da exposição; drift → 7; ausência de descrições não se disfarça de catálogo completo | Planejado |
| F7B-07 | K1 | `model inspect/validate` básico | §5.5 | 02/03 | Mesmo catálogo/estados; nenhum load implícito; arquivos inválidos/ausentes e contenção testados; `--load` explícito | Planejado |
| F7B-08 | K1 | `model run` autocomplete e serviço de medição | §5.5/5.6; B5 | 04/07; exclusão GPU de 21 em recorte mínimo | Texto final do provider; raw separado; decorador equivalente; provider/fallback reais; falha explícita → 5; cancelamento descarta sessão | Planejado |
| F7B-09 | K1 | `bench autocomplete` e estatística | §5.6/6.4; B9/B10 | 08 | Warmup fora da amostra; percentis conhecidos; nulos/falhas/recusas contados; TTFT observado/runtime distintos; dados ausentes não viram zero | Planejado |
| F7B-10 | K1 | `report` mínimo e avaliação de gates JSON | §5.11; D6 | 09 | Dono/limiar/evidência/proveniência; gate ausente ou rodada desqualificada não passa; código 6 quando obrigatório | Planejado |
| F7B-11 | K2 | `samples generate` e contratos experimentais adotados | §5.4/6.1 | 04/06; contratos reais disponíveis | Seed/índice reproduzíveis; operadores/schema/cursor reais; `split:lab`; sintaxe validada; sem treino/blind; exec marcado skipped | Planejado |
| F7B-12 | K2 | `model validate --strict-kapi/--verify-hashes` | §5.5; D4 | 07; schemas versionados | Schema/template/manifesto/contenção; mínimo GenAI semântico; validade do schema separada de suporte da IDE | Planejado |
| F7B-13 | K2 | `model run --role chat` e `bench chat` | §5.5/5.6 | 08/09; provider existente | Sessão real, streaming/códigos, contexto/limites; nenhuma execução Mongo; sem comparar contrato legado como se fosse agente T4 | Planejado |
| F7B-14 | K2 | `parity` tokenizer/prompt/greedy/teacher | §5.8/6.6 | 05/08; referência Python/pacote | Tolerâncias versionadas; normalização por nível; teacher marcado genai-direct; divergência → 11; sem tokens/logits fabricados | Planejado |
| F7B-15 | K2 | `guidance smoke` | §5.9; B6 | 03/08; pacote/EP | Supported/grammar/error/overhead reais; indisponibilidade explícita; não substitui parser seguro nem prova rota IDE | Planejado |
| F7B-16 | K2 | `npu smoke` e comparação CPU ARM64 | §5.10; B7 | 03/12; dispositivo/EP/pacote | Registro só por ação explícita; EP efetivo; sem fallback NPU; memória e motivos; viable/not_viable/inconclusive | Planejado |
| F7B-17 | K3 | `catalog plans/invoke` | §5.3; B1/R4 | 06; política/grants da 7A | Matriz ≥200 planos e 500 chamadas ≥99% conforme referência externa; permissão proibida nunca tolerada pelo percentual; negativa/expiração/revogação | Planejado |
| F7B-18 | K3 | `agent --loop reference` e contrato ChatML experimental | §5.2/5.7; B6/R8 | 12/15/17; template/metadata | Protótipo identificado; parser/janela/orçamento; sem novo provider do produto; 200 vetores independentes; sem autoridade sobre segurança da IDE | Planejado |
| F7B-19 | K3 | `agent --loop ide` e bench integrado | §5.7; B6/B9 | 17/20; provider/geração 7A | 20 cenários × modos/seeds; ciclo pelo runtime/registry; todas as tools vigentes filtradas; métricas/canários; indisponível até capacidade real | Planejado |
| F7B-20 | K3 | Simulador, replay, portas de fixture e verificação externa | §5.7/6.5; D7 | 02/06/17 | Replay sem modelo e replay-tools distintos; confirmação por caso; subprocesso controlado; task success ausente não passa gate; nenhuma query/escrita por tool | Planejado |
| F7B-21 | K0/K1 + K4 | Exclusão GPU inicial; matriz, supervisor, heartbeat e fila completos | §3.2/4.5/6.7/9.3 | 02/03 no MVP; 09 para matriz | Lock antes da carga; testes de disputa/crash/órfão/pausa; um pacote DML por filho; códigos 9/10/12; resultados parciais preservados | Planejado |
| F7B-22 | K4 | `report` completo, merge e proveniência | §5.11/9.4 | 10; relatórios dos trilhos adotados | Sem misturar versões/pacotes; owners preservados; qualified/skipped/inconclusive explícitos; resumo compatível; sem editar model.lock implicitamente | Planejado |
| F7B-23 | K1 + K4 | Contrato wrappers Python, build lock e integração local | §9.1–9.3 | 03/05/06 para início; 21/22 para fechamento | Wrapper valida schemas/códigos com 20 registros; build/hash/backend conferidos; conversão gates e ledger/timeout; mudanças externas rastreadas | Planejado |
| F7B-24 | K4 | Runbook, regressão, notices e aceite por marco | §8.3/10/11 | Tarefas aplicáveis | Comandos reais, TRX e relatórios; limites por plataforma/pacote; documentação sincronizada; benchmark permanece manual | Planejado |

## Interface com a Fase 7A

| Itens de origem da 7A | Responsabilidade preservada na 7A | Consumo/entrega 7B |
| --- | --- | --- |
| K1/K2/K4 e A13a–d | Superfície pública, política, descriptors, grants e segurança do registry | F7B-06/17/20: extrator, check, planos, invocação e replay; sem segunda política |
| K3 / PC1 / C6 | Builders públicos, contrato legado e eventual contrato v2 aprovado | F7B-04/05/11: captura/render/tokenize/diff |
| R7 / C8 / A6 / A12 / PC2 | Runtime de produto, geração/KV/template/amostragem | F7B-14/15/18/19: smoke, paridade e evidência identificada; protótipo não fecha aceite da IDE |
| R0 / R1–R5 / C1 / C5b | Decisão de NPU, versão GenAI, catálogo/pipeline e composição de produto | F7B-03/16: inventário/smoke de laboratório; upgrade não é feito autonomamente pelo console |
| TA/TE, U7 e ADR-070 | Testes/homologação do produto; retenção configurável da UI; ferramentas autorizadas | KapiLab produz evidências sintéticas e manualmente qualificadas; não abre histórico do usuário nem modifica sua retenção |

## Bloqueios/decisões da fonte cobertos

| Referência | Tratamento |
| --- | --- |
| B1/B2 e D2 | F7B-06/17: superfície pública preferida; reflexão temporária limitada e erro de drift; sem identidade falsa de assembly de teste |
| B3 e D5 | F7B-05: host pequeno com identidade completa do tokenizer |
| B4/B5 | F7B-04/08: gravador de chat e decorador de métricas com equivalência independente |
| B6/B9 | F7B-18/19: laço explícito e features reais; IDE depende da 7A |
| B7 | F7B-03/16: inventário primeiro; rede/registro EP explícitos; ausência de hardware registrada |
| B8 e D1 | F7B-00/01: arquitetura proposta, CPM e lockfiles próprios por backend |
| B10 e D3 | F7B-09/24: vocabulário compatível; extração do harness posterior e sem referência ao projeto de benchmark |
| D4/D6/D7 | F7B-12/10/20: validação estrita adicional, gates em JSON, verificação Python separada |

## Registro futuro de aceite

Para cada ID preencher commit/fingerprint, cenário e comando, resultado, caminho/hash da evidência e limites. Não confundir teste com fake com execução em modelo real. Itens condicionais ausentes são pendentes ou retirados por decisão documentada; não são aprovados por terem sido ignorados. Estimativas pertencem aos marcos da fonte, sem estimativa inventada por tarefa.
