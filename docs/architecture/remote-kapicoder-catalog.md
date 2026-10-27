# Catálogo e pacotes ONNX KapiCoder-Mongo

O catálogo padrão usa `esilva/KapiCoder-Mongo-Nano-ONNX` e `esilva/KapiCoder-Mongo-Small-ONNX`, nessa ordem. Nano `cpu-int4` é a primeira opção. Os links de detalhes apontam aos cards base [Nano](https://huggingface.co/esilva/KapiCoder-Mongo-Nano) e [Small](https://huggingface.co/esilva/KapiCoder-Mongo-Small). A seleção existente do usuário é preservada; não há troca automática do modelo instalado.

## Pacotes preparados em 26/10/2026

No MongoIA, `scripts/publish/stage_hf_package.py --tier both --target onnx --write-manifest --stage` montou:

| Staging local | Variantes | Arquivos | Tamanho total aproximado |
|---|---|---:|---:|
| `cache/hf_stage/nano-onnx` | cpu-int4 (padrão), cpu-int8, dml-fp16 | 51 | 2,4 GB |
| `cache/hf_stage/small-onnx` | cpu-int8, dml-fp16 | 35 | 5,4 GB |

Cada variante contém pesos ONNX e dados externos, `genai_config.json`, tokenizer, metadata da IDE e manifesto SHA-256. O staging inclui licença e cards; remove caminhos locais do manifesto de exportação e verifica o hash de cada cópia. Não há pacote eosfix nem novo treino/conversão. São os exports v3 existentes. O tamanho inclui todas as variantes, não representa RAM nem o download de uma única variante.

Os manifestos de upload estão em `Repos/KapiCoder-Mongo-{Nano,Small}/hf/upload_manifest_onnx.json`. O check independente e os dry-runs do uploader público conferiram os arquivos; logs MongoIA: `tmp/onnx-preparation-stage.log`, `tmp/onnx-preparation-check.log`, `tmp/onnx-publication-plan-{nano,small}.log`. Os testes Python do publicador passaram: 82 aprovados e dois ignorados (`tmp/onnx-publish-tests.log`). Na etapa inicial de preparação nenhum upload foi executado. A publicação posterior foi autorizada pelo usuário e está registrada abaixo; o staging local, isoladamente, não comprova disponibilidade pública.

## Contrato da IDE

A listagem fixa uma revisão do Hub; o downloader verifica tamanho/hash, instala uma única variante completa e publica a pasta somente após concluir. `ModelDisplayNames` reconhece `cpu-int4` e `cpu-int8`, além das convenções anteriores. O catálogo local exige pacote compatível com GenAI 0.15.2 e `editor-context-v1`. O runtime corrigido só consome tokens efetivamente anexados após gerar, preservando EOS e último token válido.

O cache de prefixo permanece desabilitado. As variantes CPU Nano INT4 e Small INT8 têm prova de paridade exata em 500 prompts sintéticos de cada tier ([evidência](local-ai-eos-prefix-cache.md)); não é nova avaliação humana ou de task success. DirectML continua experimental, sem homologação de adaptador efetivo, latência ou memória. A preparação dos arquivos não altera esse estado.

## Evidência histórica e limites

A consulta anterior às publicações base encontrou somente Safetensors: Nano revisão `c03bfe15ba1ff4e5b58a4b0953349eca82bb43f1` e Small `4ba57bc1f56a5b3cbe486dd314321ce4cdb2cae6`, ambas Apache-2.0. Fontes personalizadas continuam podendo exibir essas publicações como informativas, com download bloqueado. Não há conversão silenciosa no cliente.

O teste anterior `kapicoder-live-metadata.trx` comprova somente os metadados dessas publicações base, sem pesos. Não comprova publicação dos novos repositórios ONNX. A verificação pós-upload dos dois repositórios foi executada; os resultados e revisões estão registrados abaixo.

## Verificação dos pacotes preparados

`onnx-staged-roundtrip.trx` aprovou Nano CPU INT4 e Small CPU INT8 com os arquivos reais de staging: o transporte Hub foi simulado com streams de arquivos, mas o downloader, armazenamento, conferência SHA-256 e catálogo local foram os mesmos da IDE. Não é uma prova de rede ou publicação. Os pacotes individuais somam 410.666.723 e 2.703.298.435 bytes, respectivamente.

A geração nativa também usou as pastas de staging: wrapper KapiLab CPU com Nano, 714 aprovados, e os quatro testes nativos de paridade/EOS/cancelamento com Small, todos aprovados (`tmp/onnx-staged-native-nano.log` e `tools/KapiLab.Tests/TestResults/onnx-staged-native-small.trx`). A referência de oito prompts de cada tier exige a mesma DLL ORT por hash; não é benchmark ou homologação de qualidade real.

Restore bloqueado e build passaram sem avisos. A execução da solução aprovou 403 testes de agentes, 3.781 unitários e 1.068 de integração (20 unitários e 20 de integração ignorados). Quatro testes de integração falharam ao remover arquivos temporários LiteDB; as quatro fixtures passaram na repetição isolada, 98 testes, sem mudança de código ou asserções (`onnx-storage-recheck.trx`). A execução completa não foi inteiramente verde. Catálogo, localização, instalação simulada e UI passaram nessa execução. Não há benchmark no CI.

Após ajustar somente a fixture visual para representar os tamanhos dos pacotes, o rebuild passou sem avisos e os 25 testes focados de catálogo/localização/UI passaram (`onnx-ide-catalog-final.trx`). O wrapper WinML passou 708 testes e ignorou seis opcionais (`tmp/onnx-ide-winml.log`), sem declarar seleção de GPU homologada. As capturas finais ficam em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/Debug/net10.0/ui-evidence/render-ab76b87d51e34674b6862eae691cc235/kapicoder-onnx-ready-*.png`; usam dados sintéticos de listagem, sem baixar pesos. Não substituem janela nativa ou leitor de tela.

Os 18 PNGs foram inspecionados individualmente: Light/Dark, larguras 520/660/900 e escalas 1/1,5/2. Nome Nano CPU INT4 e ações permanecem íntegros; nas menores janelas os detalhes ficam na área rolável. Em 660/900, tamanho de 411 MB, dica CPU, licença, caminho e links são legíveis. As imagens mostram seleção CPU; a dica GPU experimental foi verificada por teste, não visualmente nessa seleção.

## Publicação autorizada — 26/10/2026

O usuário autorizou explicitamente o envio e a verificação de download pela IDE. O publicador oficial executou create/upload/tag/verify, mantendo o modelo base e os exports padrão. Todos os 51 arquivos do Nano e os 35 do Small conferiram com os manifestos; `.gitattributes` gerado pelo Hub é permitido pela verificação.

| Repositório público | Tag | Revisão |
|---|---|---|
| [Nano ONNX](https://huggingface.co/esilva/KapiCoder-Mongo-Nano-ONNX) | v0.1.0-preview | b34d90bb97067ae888d51c391898a27c1707fafe |
| [Small ONNX](https://huggingface.co/esilva/KapiCoder-Mongo-Small-ONNX) | v0.1.0-preview | 913ef5fc78e70000e118d4384cadb401fcac4f4f |

Uma consulta independente sem autenticação confirmou `private=false` e a igualdade entre as revisões de main e da tag. Evidências MongoIA: `tmp/onnx-publish-{nano,small}.log` e `reports/export/onnx-published-revisions-20261026.json`. Não há promessa de que main nunca mude: cada listagem da IDE fixa a revisão usada pelo download.

`KapiCoderPublicDownloadManualTests.PublicCatalogDownloadsNanoAndSmallThroughPreferences` passou contra o Hub público, sem token ou transporte simulado. O teste abre a janela Headless de Preferências, afirma Nano `cpu-int4` como seleção inicial, executa `DownloadModelCommand` para Nano INT4 e Small INT8, exige operação concluída, verifica os hashes/tamanhos dos manifestos e o reconhecimento pelo catálogo. Evidência: `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/TestResults/onnx-public-download.trx`. O teste permanece explícito, fora do CI, com cancelamento cooperativo; não grava preferências do usuário.

Os arquivos baixados foram mantidos em `tmp/public-onnx-models/KapiCoder-Mongo-Nano-ONNX-cpu-int4` e `tmp/public-onnx-models/KapiCoder-Mongo-Small-ONNX-cpu-int8`. As duas imagens `ui-evidence/render-0e21642de0ad486b96cca3fdfcd1bb30/kapicoder-public-installed-{Light,Dark}.png` foram inspecionadas: catálogo com dois modelos, Small selecionado após o segundo download e botão de download desabilitado para o modelo já instalado. É a UI Headless com rede e arquivos reais; não substitui inspeção de uma janela desktop nativa ou leitor de tela. A seleção inicial padrão continua Nano INT4; a escolha do Small dentro do teste não altera preferências reais.

A geração nativa foi repetida a partir desses arquivos efetivamente baixados: 714 testes aprovados no wrapper CPU com Nano (`tmp/onnx-public-native-nano.log`) e quatro testes nativos aprovados com Small (`tools/KapiLab.Tests/TestResults/onnx-public-native-small.trx`). Incluem paridade de IDs com Python, EOS, parada e cancelamento. Restore bloqueado e build padrão da solução passaram sem avisos após as provas CPU (`tmp/onnx-public-restore-final.log`, `tmp/onnx-public-build-final.log`). A publicação não muda o estado experimental do DirectML ou a homologação pendente de cache/latência/RAM.

Após a autorização para comitar, os scripts MongoIA foram versionados em `2b0ee093a5cf224a0d9d2d07ab5d0c6349ad2df6`. Com a árvore limpa, os locks Nano/Small foram regenerados com `--require-clean`, `scripts_dirty=false` e referência a esse commit anterior à gravação dos locks. A pendência histórica de versionamento dos locks foi encerrada, sem mudar os pesos publicados. A nova verificação no Hub reconfirmou todos os arquivos da tag.
