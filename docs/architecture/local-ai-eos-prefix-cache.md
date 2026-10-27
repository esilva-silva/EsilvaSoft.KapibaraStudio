# EOS e cache experimental de prefixo — 26/10/2026

O runtime lê um token somente quando a sequência cresce após `GenerateNextToken`. GenAI 0.15.2 pode concluir por EOS sem anexá-lo; reler o último ID duplicava saída. A verificação de crescimento preserva também um último token válido, mesmo se a geração já estiver concluída. Streaming e coleta usam o mesmo laço, com os mesmos marcadores e contratos.

## Reuso por sessão

`PrefixCacheState` acrescenta `SessionKey`, `Mode`, `CapTokens` e `SessionLifetime`; `Tokens` permanece por compatibilidade e não concede reuso. A identidade opaca é capturada por aba/documento/conexão/banco/coleção/linguagem; mudança desse contexto encerra a sessão anterior. Identidades e tokens de cancelamento não são serializados no pedido do editor. Não há persistência de KV, texto ou resultados.

O runtime mantém até quatro generators por modelo, LRU. Recalcula o prefixo comum por IDs, respeita o teto opcional e reprocessa pelo menos 32 tokens; prompts menores fazem prefill completo. A geração usa o orçamento efetivo de contexto, sem limitar pedidos futuros ao tamanho da primeira saída. Chat/agente, sampling e GPU/NPU continuam fora desse caminho. O serviço mantém a serialização existente. Fechamento de aba invalida a sessão; um generator em uso é liberado pelo trabalhador ao retornar, sem descarte concorrente. Cancelamento/falha invalida a sessão; troca de modelo/provider libera todas.

O produto exige simultaneamente `AutocompleteSettings.PrefixCacheMode=Reuse` e metadata `runtime.prefixCache="reuse"` no pacote CPU. O padrão é Disabled e nenhum pacote foi homologado nesta rodada. A declaração de metadata é uma afirmação de homologação do produtor, que precisa ser revalidada ao alterar pacote/runtime. O laboratório pode solicitar `allowUnvalidatedPrefixCache` para medir candidatos; a composição Desktop não usa essa opção.

Modo e limite de sessões integram a chave da configuração carregada. Ao alterar essas preferências, o serviço descarta o runtime anterior antes de atender novos pedidos, aplicando a configuração atual e liberando os caches. Metadata sem declaração de cache continua carregável sem conceder reuso; o campo `runtime`, quando presente, preserva a exigência de objeto.

Resultados e pedaços finais expõem `PrefixCacheReusedTokens`. Os instrumentos locais registram lookup hit/miss, TTFT separado e invalidações por motivo, sem texto ou identidade da sessão em tags.

## Laboratório e evidência

`bench autocomplete --session-mode --device cpu` usa a ordem do JSONL como pedidos sucessivos de uma aba. Para simular digitação, o autor fornece os snapshots sucessivos; o comando não inventa uma taxa de acerto de 95%. O relatório inclui tokens reutilizados por amostra, hit rate e TTFT de hit/miss/mistura, excluindo aquecimento. Use sessões separadas para medir 80%, 90% e ≥95% de hit. O modo é manual, não CI/release.

A referência independente externa `scripts/export/generation_reference.py` usa `get_next_tokens()` em Python e aceita `--ort-library` para carregar exatamente a DLL da IDE, sem modificar o ambiente Python instalado. Registra o SHA256; `RealModelPrefixCacheTests` exige o mesmo hash na IDE.

Foi reproduzido EOS sem crescimento em 6/8 prompts sintéticos T0 e 7/8 T1. A primeira comparação Python/IDE usava ORT diferente (Python 1.30), portanto não era uma prova controlada. Com a mesma DLL, a política inicial de cauda de um token divergiu no caso 3 repetido. A política atual reprocessa no mínimo 32 tokens e passou na comparação exata dos oito casos de cada tier, incluindo prompts longos com reuso real. Fontes: `artifacts/f7b/test-results/Cpu/kapilab-Cpu.trx`, `tools/KapiLab.Tests/TestResults/prefix-real-t1.trx` e `reports/export/generation-reference-t{0,1}.json` no MongoIA. Isso não substitui os 500 prompts do val v2c, FIM/exec-valid, RAM nem medições ociosas de TTFT. Nenhum pacote recebeu metadata de homologação.

Os gates continuam: equivalência greedy ≥99%, qualidade sem perda, hit real ≥95%, média/p95 T0 ≤150/250 ms e T1 ≤500/650 ms. Ausência de medição não é aprovação. O T0 int4 permanece a variante escolhida; não há pacotes eosfix, treino ou upload.

**Validação ampliada T0 e T1:** os 500 prompts sintéticos já preparados de cada tier em `reports/export/work/t{0,1}_prompts.json` passaram na comparação estrita Python × IDE e nas duas repetições com cache, usando o mesmo ORT CPU 1.28.0 por hash. Fontes: `tools/KapiLab.Tests/TestResults/prefix-val500-t0.trx` e `prefix-val500-t1.trx`; referências e hashes dos conjuntos em `reports/export/generation-reference-val-t{0,1}.json` no MongoIA. Trata-se de paridade de geração, sem novo ensaio de task success ou avaliação humana. TTFT e pico de RAM com quatro sessões continuam pendentes.

O leitor verifica cancelamento antes de entregar cada chunk, inclusive quando o trabalhador já terminou e o canal contém saída enfileirada. Abandono antes do final invalida a sessão também nesse caso. Ao consumir o chunk final, aguarda a limpeza sem cancelar uma sessão concluída normalmente; falha/cancelamento ainda invalida. A prova nativa de cancelamento permanece estrita (`prefix-cancel-final.trx`).

O wrapper CPU final passou 714 testes, com os modelos locais habilitados para as provas nativas (`artifacts/f7b/test-results/Cpu/kapilab-Cpu.trx`). Inclui marcador textual coincidindo com o último token permitido: a saída encerra completa pelo marcador, enquanto o mesmo orçamento sem marcador indica limite. O wrapper WinML passou 708 testes e ignorou seis provas opcionais (`artifacts/f7b/test-results/WinML/kapilab-WinML.trx`); não é homologação de seleção de GPU. Restore bloqueado e build da solução passaram sem avisos com `UsedAvaloniaProducts=`.

A execução da solução aprovou 403 testes de agentes e 3.779 unitários (20 ignorados). A repetição completa de integração aprovou 1.064 e ignorou 20, mas revelou uma expectativa nova incorreta de aceitar `runtime` escalar. O contrato anterior foi preservado e o teste passou a exigir rejeição. Após essa correção, build sem avisos e 24 testes das fixtures de catálogo e armazenamento passaram (`ai-final-catalog-storage.trx`); a suíte completa não foi repetida após essa última correção. As duas falhas anteriores de acesso a arquivos temporários não reapareceram. O primeiro recheck foi abortado por timeout de conexão com testhost; a execução seguinte conectou normalmente.

## GenAI e GPU

Avaliados documentalmente: [0.16.0](https://github.com/microsoft/onnxruntime-genai/releases/tag/v0.16.0) e [0.17.0](https://github.com/microsoft/onnxruntime-genai/releases/tag/v0.17.0). A primeira amplia Engine/KV e a segunda anuncia prefix caching para layouts paginados suportados. Isso não prova seleção DirectML nem compatibilidade dos nossos pacotes/API C#.

Foram executados probes CPU isolados com os pacotes NuGet oficiais 0.16.0 e 0.17.0, sem alterar as dependências da IDE. Ambos carregaram T0 int4 e obtiveram cache=fresh em 8/8 casos com cauda mínima de 32 tokens; somente 5/8 saídas foram iguais à referência 0.15.2. São diferenças observadas, não uma medida de queda de qualidade. Fontes locais: `tmp/genai-evaluation-0.16.log`, `tmp/genai-evaluation-0.17.log` e código do probe em `tmp/genai-evaluation/`. Nenhum teste híbrido foi executado.

**Recomendação:** manter 0.15.2 no produto; nenhuma candidata satisfez uma prova de compatibilidade/qualidade suficiente para adoção. 0.16.0 é a menor candidata posterior avaliada; 0.17.0 não resolveu a diferença de saída nesses casos. Antes de adotar: locks por backend, templates/tokens congelados, EOS/cancelamento/cache, pacotes T0/T1, qualidade na suíte completa e prova do adaptador efetivo por contadores em hardware híbrido. Atualizar `KapibaraGenAiVersion` e locks Cpu/WinML/Cuda somente após esse aceite e decisão de adoção. Não há hooks DXGI ou escrita no registro.
