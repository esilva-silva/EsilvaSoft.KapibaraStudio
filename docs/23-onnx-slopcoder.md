# ONNX: SlopCoder, CPU/GPU e chat do editor

## Escopo

Qwen2.5-Coder continua disponível. O catálogo também aceita `llama` com manifesto `slopcoder_manifest.json` no formato `deepseek-coder-fim`, validando os textos/IDs BOS, EOS, FIM, EOT e os tokens de parada. A exportação SlopCoder exige o decoder e seu arquivo externo `.data`, ambos não vazios. Isso valida o contrato e a presença dos arquivos; não é verificação criptográfica integral dos pesos.

Os pacotes `C:\SlopStudio.MongoAI-artifacts\release\SlopCoder-Mongo-6.7B-v1\onnx-int4-kquant-mixed-cpu` e `F:\models\SlopCoder-Mongo-1.5B-full-ONNX-INT8` ([seção própria](#slopcoder-mongo-15b-full-qwen2--13092026)) não são mais descobertos por caminhos fixos no código: selecione o diretório pai e a pasta, ou use **Outra pasta…** ([catálogo](26-ia-local-multimodelo.md)). Uma preferência de modelo já salva prevalece. Pesos, tokenizer e vetores permanecem externos ao Git e à distribuição; a licença MIT do EsilvaSoft.SlopStudio permanece inalterada.

## Uso

1. Abra **Preferências → Autocomplete**, informe o diretório pai em **Diretório de modelos** e escolha a pasta do pacote em **Modelo** (ou **Outra pasta…**).
2. Habilite o autocomplete em **Automático** ou **IA local**. Para esta exportação, selecione **CPU**. Hardware **Automático** tenta DirectML e retorna à CPU se necessário; **GPU** explícita não retorna à CPU e informa a falha.
3. Use **Testar modelo** e confira o provider efetivo no status. CPU automática agora deixa o ONNX Runtime escolher os núcleos, sem o antigo teto de duas threads.
4. No editor, o dicionário continua imediato e prioritário. A geração local alimenta o ghost text; Tab aceita incrementalmente, Escape descarta.
5. No painel **Assistente IA**, envie uma instrução. A resposta do modelo gera uma proposta com diff. Aplicar exige confirmação; não executa código nem altera o Explorer.

O chat compartilha a sessão carregada e a fila de inferência com o autocomplete. Cada chamada mantém seu cancelamento. Sem modelo, desabilitado ou em Básico, o chat informa explicitamente que usa transformações determinísticas. Falhas ONNX são visíveis e não são apresentadas como uma resposta de IA bem-sucedida.

## Contrato do modelo

`DeepSeekModelTokenizer` implementa splits Unicode isolados, ByteLevel GPT-2, BPE e added tokens em .NET. Não instancia o tokenizer nativo incompatível com a regex deste pacote. O decoder aplica ByteLevel inclusive aos added tokens de caracteres; três vetores da referência produzem UTF-8 substituído e não fazem round-trip. Esse comportamento da referência é preservado, não normalizado silenciosamente.

Prompt: `[32013, 32016] + fim(prefixIds) + [32015] + início(suffixIds) + [32017]`. Prefixo e sufixo são tokenizados separadamente; quatro marcadores reservados, 25% inicial ao sufixo e redistribuição da sobra. Paradas: 32014, 32015, 32016, 32017 e 32021. Rejeitar marcadores `<|` e `<｜` tanto na entrada de inferência quanto na saída.

Os vetores revelaram duas diferenças entre o treino e o editor atual: Input vazio não cria uma linha `INPUT PANEL`, e o treino usa a lista original `AVAILABLE COMMANDS` também para JSON. O Input vazio foi alinhado; a lista original é aplicada somente ao cabeçalho DeepSeek, preservando a lista por dialeto do Qwen e o comportamento do dicionário.

## Distribuições de hardware

ONNX Runtime GenAI permanece em **0.15.2**. Cada build inclui uma única família de bibliotecas nativas, com lockfiles separados:

| Build | Dependência | Uso |
| --- | --- | --- |
| `WinML` | Microsoft.ML.OnnxRuntimeGenAI.WinML + Microsoft.Windows.AI.MachineLearning | Padrão Windows; CPU e tentativa DirectML |
| `Cpu` | Microsoft.ML.OnnxRuntimeGenAI | Padrão Linux; opção explícita Windows |
| `Cuda` | Microsoft.ML.OnnxRuntimeGenAI.Cuda | Opção NVIDIA Windows/Linux; exige runtime/driver CUDA compatíveis |

Use o mesmo `-p:SlopOnnxBackend=Cpu`, `WinML` ou `Cuda` em restore, build, test e publish ao sobrescrever o padrão. Restore usa `--locked-mode`; build usa `--no-restore`; testes usam `--no-build --no-restore`. `packages.lock.json` corresponde a CPU; `packages.WinML.lock.json` e `packages.Cuda.lock.json` correspondem às variantes. Não misture DLLs manualmente. [Distribuições oficiais](https://onnxruntime.ai/docs/genai/howto/install.html) e [WinML 0.15.2](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntimeGenAI.WinML/0.15.2).

No hardware **Automático**, o runtime tenta os providers disponíveis e retorna à CPU em falhas de carga **ou de execução nativa GPU**; o status informa o fallback e o provider efetivo, e a sessão CPU recuperada permanece carregada, evitando repetir a falha GPU a cada tecla. Desde 13/09/2026, **CPU, GPU ou NPU explícitos não fazem fallback**: a falha é exibida com o motivo e as alternativas. QNN/OpenVINO/VitisAI são selecionáveis se o runtime os expuser, mas nenhum build os distribui. [Hardware e seleção](26-ia-local-multimodelo.md#hardware).

## Evidência e limites

- Paridade externa: **547 strings e 40 prompts**, todos os IDs, round-trips e os três casos de decode com substituição UTF-8. Teste `TokenizerAndFullPromptsMatchEveryReferenceVector`.
- SlopCoder real CPU: geração repetida, cancelamento, recuperação e rejeição de contexto truncado. Prompt curto sintético produziu 12 tokens em aproximadamente 1,2 s na primeira execução medida; não é benchmark de TTFT nem garantia de latência no editor.
- DirectML local: o grafo CPU misto carregou, mas falhou em `DmlFusedNode_0_4` com `80070057` na geração. O teste estrito GPU falhou; o teste separado de recuperação CPU passou. **Não há homologação de geração GPU deste pacote**. Uma exportação compatível com DirectML ainda precisa ser produzida e validada; CUDA compilou, mas não foi executado em uma GPU NVIDIA nesta máquina.
- Chat real: gerou proposta completa, mas acrescentou um filtro de data não solicitado ao pedir apenas um limite. Este é um modelo treinado para continuação FIM, não para conversa/instruções gerais. A integração do chat não comprova fidelidade semântica; não se deve aplicar propostas sem revisar o diff. O histórico do painel não é enviado como conversa ao modelo.
- Chat limita o contexto serializado a 8192 caracteres, exige que ele caiba integralmente na janela de tokens e rejeita geração interrompida pelo teto de 256 tokens. Autocomplete pode aceitar continuação parcial. Dados transitórios não entram em snapshots de sessão.
- Testes regulares cobrem compartilhamento da sessão, cancelamento do chat com autocomplete em fila, filtros de privacidade, tokens reservados, rejeição de proposta truncada, manifesto inválido e ausência dos pesos externos.

Integrações reais são `Explicit`: defina `SLOP_DEEPSEEK_MODEL` para o diretório do pacote e filtre o nome do teste. `SLOP_TEST_GPU=1` no teste de inferência exige provider GPU; não transforma fallback CPU em aprovação GPU. Testes de UI Headless não homologam MongoDB real, Linux, leitores de tela ou diálogos nativos. Contagem final da suíte na [matriz](15-matriz-de-validacao.md).

## SlopCoder-Mongo-1.5B-full (Qwen2) — 13/09/2026

Pacote `F:\models\SlopCoder-Mongo-1.5B-full-ONNX-INT8`: Qwen2.5-Coder-1.5B Base (Apache-2.0) ajustado por LoRA e mesclado no pipeline externo
`SlopStudio.MongoAI-Compact`, com dados destilados do SlopCoder 6.7B e chat bilíngue, exportado com ONNX Runtime GenAI 0.15.2 e quantizado em
INT8 (MatMulNBits, blocos de 32). Arquitetura `qwen2`: o catálogo o valida pelo caminho Qwen (tokens FIM do Qwen, `QwenFimPromptBuilder`); o
`slopcoder_manifest.json` do pacote é metadado e não ativa o caminho DeepSeek. Pesos permanecem fora do Git e da distribuição.

**Nota (25/09/2026):** `LocalModelAiChatService` foi **removido pela [ADR-055](10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)**; as medições abaixo são evidência histórica do treino/benchmark feito quando o serviço existia e permanecem válidas como registro, mas o serviço não está mais no código.

O treino usou exatamente o contrato deste repositório: `AutocompleteContextBuilder.ModelPrefix`, `QwenFimPromptBuilder` e o prefixo JSON do
`LocalModelAiChatService`. Os arquivos que definem esse contrato (`LocalModelAiChatService`, `AutocompleteContextBuilder`,
`QwenFimPromptBuilder`, `OnnxLocalModelRuntime`, `LocalModelCatalog`, `AutocompleteService`, `Core/Autocomplete.cs`, `Core/AiAssistant.cs`)
foram comparados com a versão usada na validação e diferem apenas em fim de linha.

Por que este pacote (medições do pipeline externo, CPU AMD Ryzen 9 7900):

| pacote | chat em pedidos livres escritos à mão (120) | conjunto cego (40) | TTFT médio / p95 (chat) | tokens/s | pico de RAM |
|---|---:|---:|---:|---:|---:|
| SlopCoder-Mongo-0.5B INT4 | 67 | 20 | ~206 / ~250 ms | ~115 | 1,5 GB |
| **SlopCoder-Mongo-1.5B-full INT8** | **87** | **29** | ~486 / ~578 ms | ~32 | 3,6 GB |

No teste do sinal pareado, o 1.5B-full INT8 acerta 24 pedidos que o 0.5B erra e erra 4 que ele acerta (p < 0,001). A troca prioriza o
**Assistente IA**: o autocomplete não ganha precisão (tokens aceitos no benchmark do pipeline equivalentes ou ligeiramente menores) e fica mais
lento. Pelo harness externo que executa as DLLs da IDE (`LocalModelCatalog → AiAutocompleteProvider → OnnxLocalModelRuntime` e
`LocalModelAiChatService`), 120 pedidos: 0 resultados nulos, 0 falhas do runtime, latência média de 980 ms no autocomplete e 1.937 ms no chat.
Esses números são do pipeline externo, não da suíte deste repositório.

Limites e pendências conhecidas:

- DirectML: em 13/09/2026 a sessão DirectML deste pacote foi criada na RX 7800 XT, mas a geração falhou em `DmlFusedNode_0_0`. Use CPU;
  Automático recupera em CPU. [Evidência](26-ia-local-multimodelo.md#evidência--13092026).
- Em pedidos livres o chat ainda erra cerca de 1 em 4: condição invertida ou omitida, parte do pedido ignorada, campo errado. Revise sempre o diff.
- Ocupa 2,52 GB em disco e ~3,6 GB de RAM durante a inferência; em máquinas com pouca memória, prefira o 0.5B INT4 do mesmo pipeline.
- ~~`Decode` com saída vazia quando o primeiro token é de parada~~ — corrigido na [revisão multimodelo](26-ia-local-multimodelo.md): parada imediata
  gera sugestão vazia, sem descarga nem cooldown.
- ~~Contexto que excede a janela no chat descarrega o modelo~~ — corrigido na mesma revisão: `LocalModelContextException` é erro do pedido; o
  modelo continua carregado para o autocomplete.

## Pacotes SlopCoder-Mongo DirectML (GPU) — 14/09/2026

O pipeline externo gerou exportações compatíveis com DirectML, validadas nesta GPU (AMD Radeon RX 7800 XT) com o mesmo ONNX Runtime GenAI WinML
0.15.2 do build Windows: `SlopCoder-Mongo-1.5B-full-ONNX-DML-FP16`, `-1.5B-full-ONNX-DML-INT4`, `-0.5B-ONNX-DML-FP16` e `-0.5B-ONNX-DML-INT4`.
A falha em `DmlFusedNode_0_0` do pacote INT8 vinha de uma exportação feita para CPU; os pacotes DML são exportados com o provider `dml` do
builder, e o INT4 mantém a embedding fora da quantização (`GatherBlockQuantized` não roda sob a captura de grafo do DirectML).

Todos os pacotes do pipeline (inclusive os CPU, versão 1.1.0) trazem `slopstudio-model.json`: `hardware` `["gpu"]` nos DML e `["cpu"]` nos
INT4/INT8, `capabilities` autocomplete/chat/fim, contexto recomendado 2048, 32 tokens no autocomplete e 256 no chat.

Medições do pipeline externo (um processo por vez na GPU; chat livre nos mesmos 120 pedidos escritos à mão da seção anterior):

| pacote | chat livre (120) | cego (40) | TTFT médio / p95 | tokens/s (chat) | disco |
|---|---:|---:|---:|---:|---:|
| **SlopCoder-Mongo-1.5B-full DML-FP16** | **87** | **29** | ~156 / ~178 ms | ~95 | 2,9 GB |
| SlopCoder-Mongo-1.5B-full DML-INT4 | 85 | 27 | ~168 / ~202 ms | ~139 | 1,3 GB |
| SlopCoder-Mongo-0.5B DML-FP16 | 72 | 21 | ~113 / ~133 ms | ~141 | 963 MB |
| SlopCoder-Mongo-1.5B-full INT8 (CPU, referência) | 87 | 29 | ~486 / ~578 ms | ~32 | 2,5 GB |

O DML-FP16 acerta os mesmos pedidos que o modelo mesclado; com GPU, o 1.5B-full responde mais rápido que o 0.5B INT4 em CPU (~206 ms). O
0.5B DML-INT4 perde precisão no autocomplete sem ganhar TTFT; prefira o DML-FP16 do 0.5B.

Evidência neste checkout (build Debug WinML existente, sem alteração de código):

| Verificação | Resultado |
|---|---|
| `RealModelTestRunsOnTheRequestedHardware(Gpu)` com `SLOP_QWEN_MODEL` = 1.5B-full DML-FP16, 1.5B-full DML-INT4 e 0.5B DML-FP16 | Aprovado nos três: provider DirectML, etapas pasta/tokenizer/sessão/geração OK |
| `RealModelTestRunsOnTheRequestedHardware(Auto)` com 1.5B-full DML-FP16 | Aprovado em DirectML |
| `RealModelTestRunsOnTheRequestedHardware(Auto)` com 1.5B-full INT8 (`hardware` `["cpu"]`) | Aprovado direto em CPU, sem tentativa DirectML |
| Harness externo (DLLs da IDE, GPU explícita, 120 pedidos por pacote DML) | 0 nulos, 0 falhas do runtime, texto idêntico ao benchmark Python |

TRX em TestResults do projeto de testes: `slopcoder-1.5b-full-dml-fp16-gpu.trx`, `slopcoder-1.5b-full-dml-int4-gpu.trx`,
`slopcoder-0.5b-dml-fp16-gpu.trx`, `slopcoder-1.5b-full-dml-fp16-auto.trx`, `slopcoder-1.5b-full-int8-cpu-auto.trx`.

Limites: não houve uso interativo da janela com esses pacotes; desde 14/09/2026 os pacotes DML dos dois tamanhos constam na lista do
**Baixar modelo** ([ADR-039](10-decisoes-arquiteturais.md)), sem download completo homologado pela janela. Nos testes do pipeline externo, carregar um segundo modelo DirectML no mesmo processo depois de liberar o primeiro encerrou o
processo; a troca de modelo da [IA local multimodelo](26-ia-local-multimodelo.md) com dois pacotes DML não foi exercitada — se o carregamento
falhar após a troca, reinicie a aplicação. NPU e CUDA seguem não homologados.

## Enquadramento de versão — 13/09/2026

🧪 Experimental no roadmap v0.9.0: integração local disponível, mas fidelidade conversacional, cobertura pt-BR/en das ações e GPU continuam gates próprios. O MVP v0.5.0 usa autocomplete determinístico sem exigir modelo. Consolidação de release v1.0.0 não deve depender de experimentos não aprovados. [Roadmap](09-plano-de-implementacao.md) e [inventário](24-inventario-roadmap.md).
