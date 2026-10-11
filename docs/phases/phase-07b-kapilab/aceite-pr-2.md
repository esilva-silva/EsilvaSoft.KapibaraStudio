# Aceite PR 2 — contrato de prompt do agente local

Executado em 11/10/2026 no checkout `f345c393dac1e619b5a9a24e30905af828cb8ed3`, com alterações locais. Escopo: preparar a IDE para a Fase 04 de trajetórias do pipeline externo, conforme `pr-2-contrato-prompt-agente.md`. [Contrato detalhado](../../architecture/local-agent-qwen3-v1.md), [ADR-072](../../10-decisoes-arquiteturais.md#adr-072--contrato-de-prompt-do-agente-local-11102026).

| Entregável | Evidência executada | Resultado |
|---|---|---|
| Builder público/puro e provider compartilhado | `LocalAgentPromptBuilder`; 200 casos de equivalência da montagem antiga; teste nativo compara tokens antigos/novos com descrições antigas para isolar Q2 | Atendido |
| Descrições prompt/catálogo | `AgentToolDescriptions`, export e check; teste cobre igualdade, drift e teto 200; captura do provider confirma a descrição real no passo de tool | Atendido |
| 200 goldens independentes | `input.json`, `rendered.txt`, `tokens.json`, corpus JSONL e hashes tokenizer/config; 200 comparações fallback × golden e teste explícito percorre nativo × fallback × golden | Atendido em Cpu |
| TA5, captura real e recuperação | Provider com recorder: force-close, segundo turno sem canário, falha, turnos concorrentes/cancelamento, recuperação e tool/resultado; `Generator.RewindTo` + reanexo compara sequência com generator novo e render do zero | Atendido para mensagens/tokens; sem prova de logits/KV |
| CLI render/tokenize/diff | CLI render 200, tokenize via formatter/adapter real 200 e entrada direta inédita sem texto, `contract diff` retorna 0; testes de roteamento, saída somente hashes, opt-in verificado e preservação de saída após recusa | Atendido |
| Espelho Python | `AutoTokenizer.apply_chat_template`, tokenizer Qwen3-8B local, normalização equivalente ao GenAI; 200 registros, zero divergências de texto/IDs; relatório `matches=true`, `contractDiffExitCode=0` | Atendido |
| Q2/Q3/Q5/Q6 e metadata | Padrões aprovados pelo usuário; ADR-072; `agentContract` aditivo; exemplo com force-close de referência; strict valida contrato conhecido e recusa ausente/desconhecido | Atendido |
| Build/test KapiLab Cpu/WinML | Scripts com lockfiles existentes, offline; 679 aprovados/3 ignorados/zero falhas por backend; build sem avisos/erros | Atendido |
| Prova nativa Cpu | 2 testes Explicit aprovados: 200 goldens/tokens e equivalência anterior; sequência RewindTo | Atendido |
| Prova nativa WinML adicional | 2 Explicit falharam ao carregar `onnxruntime-genai`, `DllNotFoundException`, HResult `0x8007045A`, antes do template/tokenizer | Não homologada neste host; não esconder com skip/fallback |

## Reexecução

```powershell
./scripts/lab/build_kapilab.ps1 -Backend Cpu -Test -Offline
$env:KAPI_QWEN3_TOKENIZER_PACKAGE = '<pacote-GenAI-com-tokenizer-template-Qwen3>'
dotnet test tools/KapiLab.Tests/KapiLab.Tests.csproj --no-build --no-restore -p:SlopOnnxBackend=Cpu --filter 'FullyQualifiedName=EsilvaSoft.KapibaraStudio.KapiLab.Tests.LocalAgentPromptContractTests.NativeTemplateAndTokenizerMatchAllGoldensAndLegacyRefactor|FullyQualifiedName=EsilvaSoft.KapibaraStudio.KapiLab.Tests.LocalAgentPromptContractTests.NativeRewindAndReappendMatchFreshRenderTokens' -- NUnit.NumberOfTestWorkers=0
./scripts/lab/build_kapilab.ps1 -Backend WinML -Test -Offline
```

Comandos de render/tokenize/paridade estão no [README KapiLab](../../../tools/KapiLab/README.md#contrato-de-agente-local-qwen3). Não regenerar fixtures para validar. VSTest/testhost exigiu execução fora do sandbox para permitir a conexão local; o primeiro `-Test` no sandbox foi abortado após 90 segundos. O restore inicial sem offline falhou em NU1900 por acesso ao NuGet; `-Offline` usa os pacotes já presentes, sem mudar lockfiles/analisadores.

## Proveniência e limites

Relatório: [summary.json](../../../reports/lab/parity-local-agent-qwen3/summary.json); [diff](../../../reports/lab/parity-local-agent-qwen3/token-diff.json); [proveniência e hashes](../../../reports/lab/parity-local-agent-qwen3/provenance.json). Os snapshots textuais contêm exclusivamente os casos sintéticos verificados. TRXs nativos são mantidos junto do relatório; os gates padrão ficam em `artifacts/f7b/test-results/{Cpu,WinML}`.

Tokenizer/config Qwen3-8B vieram de `BaseRepos`, somente leitura. Como não existe pacote ONNX T4 disponível, a prova nativa usou um **carrier sintético** em `tmp/pr2-qwen3-tokenizer`: cópia do decoder Qwen2.5-Coder-0.5B INT4 local, config com tipo Qwen3 e tokenizer/template Qwen3 originais. O tokenizer e o template são os de referência; não houve geração de resposta nem chamada real de tools. A comparação RewindTo exercita a sequência nativa, sem inferência de resposta ou alegação de qualidade/estado numérico KV. Esse carrier não é pacote publicável nem modelo T4 homologado.

Foram registradas duas divergências GenAI × HF: transformação/simplificação de schemas e conservação de raciocínio bruto no assistant. Os espelhos foram ajustados à IDE; produção continua usando o template do pacote e a mesma sequência de mensagens. Descrições são a única mudança intencional do prompt de produção. Autenticação, grants, MongoDB, UI, persistência e modelo selecionado não foram alterados.

PC1 originalmente exige extração do prefixo FIM legado; a captura Qwen3 complementa esse item sem encerrá-lo integralmente. F7B-18 inclui um loop experimental ainda pendente. Treino/trajectórias externas, T4/Agent-Lite, inferência WinML, GPU/NPU, Linux, task success e homologação de tools reais conservam seus gates próprios.


## Gate da solução principal

Restore locked com `-p:NuGetAudit=false -m:1` passou; build `--no-restore -p:UsedAvaloniaProducts= -m:1` passou com zero avisos/erros. O primeiro `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -m:1 -- NUnit.NumberOfTestWorkers=0` terminou com 2 falhas de teardown de `WORKSPACE-LOG.DB.tmp`, sem falha de asserção do contrato: `CorruptPolicyDeniesAndCannotBeOverwrittenEvenWithEmptyGrants("missingNamespace")` e `DeletingAProfileLeavesAnUnrelatedCorruptConsentDocumentUntouched`.

O foco desses métodos passou 21/21, e a repetição integral de IntegrationTests passou **1.049 aprovados, 20 ignorados, zero falhas**. Infrastructure.Agents.Tests passou **403/403** e UnitTests passou **3.767 aprovados, 20 ignorados, zero falhas** na execução integral original. Total combinado dos resultados finais por projeto: **5.219 aprovados, 40 ignorados**; não afirmar que o comando integral original terminou com código 0 ou que a intermitência LiteDB foi corrigida. TRXs preservados em `artifacts/pr2-solution`.

`python -m unittest discover -s scripts/lab/tests -v` passou **20/20**; inclui testes dos wrappers publicados existentes, não uma publicação nova do PR 2. Nenhum benchmark foi solicitado ou executado. A publicação/lock de distribuição existente permanece inalterada; o código novo foi validado no build local.
