# Contratos de IA local e avaliação

IA local é opcional. Digitação não carrega modelo nem gera inferência; `Ctrl+;` solicita autocomplete explicitamente. O agente usa envio humano e o [catálogo seguro](tools-agentes.md). Sem modelo, editor, consultas e sugestões determinísticas continuam funcionando.

## Prompt `local-agent-qwen3-v1`

`LocalAgentPromptBuilder` é puro; não concede permissões nem executa tools. O adapter aplica o template do pacote; o fallback de renderização é espelho para comparação offline, nunca substituição silenciosa em produção.

- Sistema pt-BR, mais newline e extra não vazio; acima de 2.048 bytes UTF-8 usa a versão curta existente.
- Usuário usa `System.Text.Json` padrão, campos ordenados `message`, `context`, `attachments`; anexos com `kind`, `name`, `content`, até 32 anexos e 65.536 caracteres. Preservar escapes Unicode/HTML do encoder .NET.
- Schemas do registry retiram somente `$schema`/`$id` para o envelope `type/function`, com `name/description/parameters`. Descrições compartilhadas em inglês, até 200 caracteres para tools do produto; catálogo e runtime usam a mesma tabela.
- Mensagens do template têm `role/content`; assistant conserva `<tool_call>` textual, sem campos estruturados `tool_calls`/`reasoning_content`. Montar sistema e histórico; reanexar o usuário atual quando a última mensagem não for esse mesmo usuário, inclusive após resposta de tool ou force-close.
- Raciocínio entre turnos é removido pela sessão; não apagar indiscriminadamente raciocínio bruto de passos em andamento. Quando desativado, acrescentar `disabledGenerationSuffix` do pacote após o generation prompt. `ForceCloseText` também pertence ao pacote.
- Saída de tool tem `status/result/error/truncated` e truncamento UTF-8 por rune. Envelope inválido produz `InvalidToolCallFormat`, com o retry existente.

A prova de referência de 11/10/2026 usou GenAI 0.15.2. Seu template nativo simplifica schemas antes do Jinja: `type/function` vira `name/description/parameters`; propriedades conservam só `type/description`, nessa ordem, `string` vira `str`, tipo não textual vira `str`, e tipo ausente continua ausente. `required`, `enum`, limites, objetos aninhados e `items` não chegam ao template. A transformação só pertence ao espelho de renderização; schemas do registry permanecem completos. O template nativo conserva `<think>` bruto; o espelho HF recebe `reasoning_content=""` apenas para impedir extração automática.

Pacotes novos declaram `agentContract: "local-agent-qwen3-v1"` separadamente de `contextContract: "editor-context-v1"`. `model validate --strict-kapi` exige contrato conhecido no bloco agent. Pacotes anteriores continuam carregáveis sem satisfazer o gate estrito. Alteração de runtime/template exige repetir paridade; versionar o contrato se os bytes mudarem. A versão atualmente fixada é determinada por Directory.Build.props/locks; a prova antiga não homologa uma atualização.

As fixtures versionadas em `tools/KapiLab.Tests/Fixtures/local-agent-qwen3-v1` contêm entrada, texto renderizado UTF-8 sem BOM e IDs de tokens. O script `scripts/lab/parity_local_agent_qwen3.py` não acessa rede/pesos nem regrava fixtures na validação. Autoria exige `--generate-fixtures`. Provas nativas são Explicit com `KAPI_QWEN3_TOKENIZER_PACKAGE`; um carrier sintético prova sequência, não qualidade Qwen3, logits/KV, GPU/NPU ou task success.

## EOS, cancelamento e cache experimental

Após `GenerateNextToken`, consumir token apenas se a sequência cresceu. EOS pode concluir sem anexar ID; reler o último token duplicaria texto. Preservar último token válido mesmo quando a geração terminou. Streaming e coleta seguem o mesmo laço/marcadores.

`PrefixCacheState` tem identidade opaca de sessão, modo, teto e lifetime; tokens antigos não autorizam reuso. Capturar identidade por aba/documento/conexão/banco/coleção/linguagem e invalidar ao mudar contexto. Não serializar identidade, tokens de cancelamento, KV, texto ou resultados.

No caminho CPU experimental, até quatro generators LRU por modelo, prefixo comum calculado por IDs e reprocessamento mínimo de 32 tokens; prompts menores fazem prefill completo. Chat/agente, sampling e GPU/NPU não usam esse caminho. Serviço mantém serialização. Cancelamento/falha/abandono invalida sessão; fechar aba invalida, mas trabalhador em uso libera ao retornar; trocar modelo/provider/configuração libera runtimes anteriores sem dispose concorrente.

Produto exige simultaneamente `PrefixCacheMode=Reuse` e metadata `runtime.prefixCache="reuse"` em pacote CPU homologado. Padrão Disabled. `runtime` presente precisa ser objeto; ausência não impede carregar nem concede reuso. Desktop não usa `allowUnvalidatedPrefixCache` do laboratório. `PrefixCacheReusedTokens`, hit/miss, TTFT e invalidações não devem registrar texto/identidade em tags.

Checar cancelamento antes de cada chunk, mesmo com trabalhador concluído e saída enfileirada. Consumir chunk final aguarda limpeza sem cancelar sessão concluída normalmente. Prova de paridade não habilita cache por conta própria. Gates experimentais: equivalência greedy ≥99%, sem perda de qualidade, hit real ≥95%, média/p95 T0 ≤150/250 ms e T1 ≤500/650 ms; TTFT/RAM e aceite por pacote permanecem necessários.

## Contexto, fallback e medições

`editor-context-v1` é o contrato de contexto do autocomplete e o padrão de pacotes que não declaram contrato. `editor-context-v2` (pacotes que o declaram, alunos cursor-in-call v4) acrescenta a linha `TARGET FIELDS: <coleção>: caminho:tipo, ...` logo após `KNOWN NAMES`, com os campos e tipos do schema inferido já em cache da coleção alvo do statement (sem I/O; `_id` e caminhos sensíveis fora; até 96 campos e 2048 caracteres; sem campos, bytes iguais ao v1), e só nele a saída passa pelo fechamento de delimitadores internos: quando o sufixo traz apenas os fechamentos externos e termina o statement, acrescentam-se de 1 a 4 internos que faltam (`CompletionOutputProcessor.CloseInnerDelimiters`, espelho de `close_inner_delimiters` do repositório de modelos). Pacotes v2 ficam inválidos em builds anteriores. O orçamento do tier do modelo vem do pacote (`recommendedContextTokens`/`recommendedCompletionTokens`; alunos cursor-in-call: Nano 4096/64, com 8192 opcional digitado, Small 8192/128, Big 16384/128) e, ao escolher o modelo, vence a estimativa do perfil de hardware, limitado apenas pela janela declarada do modelo; pacotes sem recomendação continuam com a estimativa por memória. Variantes experimentais de seções (formato B) e JSON (formato E) permanecem hipóteses de laboratório, não novo padrão do editor. Compartilhar somente fontes habilitadas e captura autorizada; respeitar opt-in de entrada JSON, limites de contexto/saída, origem e descarte de resposta obsoleta. Erro/ausência de modelo não autoriza trocar silenciosamente de provider.

Orçamento manual de referência da busca de nomes: 64 KiB por tecla; workloads devem usar a mesma dimensão/carga declarada na medição, sem promover testes de benchmark ao CI. Avaliação de contexto exige modelos reais e corpus/versão explicitados; não inferir qualidade a partir de fake ou hipótese experimental. Os detalhes executáveis, formatos e comandos do laboratório estão em [KapiLab](../../../tools/KapiLab/README.md) e [fixture kapi-sim](../../../tools/KapiLab/Fixtures/kapi-sim/README.md).

`bench autocomplete --session-mode --device cpu` mede snapshots sucessivos fornecidos pelo autor, na ordem JSONL, sem inventar hit rate. Excluir aquecimento e separar hit/miss/mistura; preservar hash da mesma biblioteca ORT entre referência e IDE. Benchmark, download Hub e pesos reais são manuais/opt-in, nunca CI/release. A [baseline](../09-baseline-de-validacao.md) registra o alcance das provas já realizadas.
