# Runbook local — KapiLab / Fase 7B

## Catálogo de conhecimento e paridade — PR 1 (10/10/2026)

O [aceite por critério](aceite-pr-1.md) registra a auditoria vigente. O script valida cada arquivo de relatório contra links/junctions e recusa colisão com corpus/referência; a escrita Python usa temporários exclusivos, substituição atômica e limpeza em falha, inclusive para summary. A suíte Python atual passa 20/20.

Build/teste pelo wrapper oficial, com restore locked. Em isolamento use `-Offline`; VSTest precisa de loopback local para conectar ao testhost. Não executar a prova de paridade no CI/release: ela inicializa o modelo GenAI real para tokenização.

```powershell
./scripts/lab/build_kapilab.ps1 -Backend Cpu -Offline -Test -Publish
./scripts/lab/build_kapilab.ps1 -Backend WinML -Offline -Test -Publish
python -m unittest discover -s scripts/lab/tests -v
python -c "import sys; sys.path.insert(0,'scripts/lab'); import kapilab; kapilab.run('catalog','knowledge','export',workspace='.',backend='Cpu')"
python -c "import sys; sys.path.insert(0,'scripts/lab'); import kapilab; kapilab.run('catalog','knowledge','check','--expected','data/lab/knowledge-catalog.json','--actual','data/lab/knowledge-catalog.json',workspace='.',backend='Cpu')"
python scripts/lab/parity_editor_context_v1.py --workspace . --corpus C:/IA/models/KapibaraStudio.MongoIA/bakeoff/data/v1_benchmark.jsonl --reference C:/IA/models/KapibaraStudio.MongoIA/bakeoff/v1/scripts/compact/contracts.py --package C:/IA/Qwen2.5-Coder-0.5B-onnx-int4-cpu --tokenizer C:/IA/Qwen2.5-Coder-0.5B-onnx-int4-cpu
```

O padrão usa o apphost Cpu publicado e conferido pelo lock. Para desenvolvimento, faça build Cpu por último e acrescente `--dotnet dotnet` à prova; o wrapper exige backend Cpu. A evidência inicial foi executada nessa modalidade explícita de desenvolvimento, com GenAI 0.15.2; a reexecução pelo apphost publicado também passou 2.000/0/0. A exportação retorna `kapilab-knowledge-catalog-v1`; `check` retorna 7 em drift. O hash é do JSON UTF-8 indentado sem o próprio campo; commit/dirty integram o hash do snapshot, mas não representam alteração de símbolo em `check`. Dialetos e aliases visuais estão descritos no README KapiLab.

O benchmark possui 901 fim, 399 completion, 566 chat e 134 correction. A prova usa os 1.300 primeiros tipos e 700 amostras `samples generate`, seed 20261010. Chat/correction da referência usam `LocalModelAiChatService`/`AiEditorContext`; o renderer legado atual captura `LocalAgentProvider` e não representa aquele contrato. O fallback sintético satisfaz o recorte PR 1; o critério original F7B-05 de 2.000 registros exclusivamente reais permanece distinto.

Mapeamento explícito: prefix/suffix preservados; context → editor; context.language → language. Os 367 contextos LF são rematerializados como CRLF, porque esse é o cabeçalho fixo da IDE; os 117 contextos ausentes recebem o cabeçalho padrão do editor. Não há normalização de prefix/suffix. Sintéticos separam statement no caret UTF-16. A referência externa não é copiada nem modificada; `AutoTokenizer` local e `prompt_text`/`prompt_ids`/`QwenFim` calculam hashes e IDs independentes.

O `tokenizer.json` do BaseRepos difere do pacote ONNX disponível. Use o tokenizer HF do próprio pacote ONNX validado, conforme o comando acima, sem afirmar identidade de toda a família. O script bloqueia hashes de tokenizer incompatíveis. A prova validada retornou 0 com 2.000 registros e zero diferenças em hashes/IDs/orçamentos/identidade. Relatórios em `reports/lab/parity-editor-context-v1` incluem render apenas com hashes, tokens, diff e summary com hashes de insumos. Textos transitórios usados pela tokenização ficam em `tmp/parity-*` e são removidos ao terminar; o log só emite contagens/erros por tipo.

Este runbook descreve a validação reproduzível no checkout Windows x64. Ele não declara aceite da fase, de modelos ou de hardware. A validação manual em Linux está fora do escopo deste chat.

## Pré-requisitos observados

- Windows x64 e .NET SDK 10.0.401.
- O checkout e seus lockfiles Cpu/WinML.
- Rede NuGet para auditoria online. Se o feed não estiver acessível, use `-Offline`, que mantém restore locked e desativa apenas a auditoria remota de vulnerabilidades.
- Test runner VSTest precisa de loopback local entre runner e datacollector/testhost. Se o sandbox local bloquear esse loopback, executar os mesmos comandos em ambiente local com loopback permitido.
- Para inferência, pacote ONNX Runtime GenAI compatível e selecionado explicitamente. Pacotes sintéticos de teste só validam estrutura e contratos.

## Restore, build e testes

Execute ambos os backends para evitar validar somente a configuração padrão do Windows:

```powershell
./scripts/lab/build_kapilab.ps1 -Backend Cpu -Offline -Test
./scripts/lab/build_kapilab.ps1 -Backend WinML -Offline -Test
```

Na revalidação após F7B-09/11, KapiLab Cpu e WinML passaram com 255 aprovados e 3 testes opt-in ignorados (258 total) em cada backend; UnitTests isolado passou com 3.767 aprovados. O TRX de UnitTests lista 23 NotExecuted e o de IntegrationTests 91; os consoles informaram 20 ignorados em cada suíte. O build da solução terminou com 0 avisos/erros. Uma execução agregada paralela teve duas falhas de teardown LiteDB em WORKSPACE-LOG.DB.tmp; ambas passaram isoladamente. A reexecução sequencial `-m:1` terminou com exit 0: Infrastructure.Agents.Tests 403/403, IntegrationTests 1.049/20 ignorados e UnitTests 3.767/20 ignorados. TRX e SHA-256 estão em [evidências locais](evidencias-locais.md). Os apphosts Cpu/WinML foram republicados pelo wrapper oficial, que passou 14/14 testes Python; o smoke real raw/ghost passou pelo apphost Cpu publicado com fixture allowlisted e está detalhado na evidência F7B-08. A validação não inclui Linux, ARM64, CUDA, MongoDB real, nem inferência GPU/NPU ou benchmark representativo de corpus; decorador/overlay visual e paridade com Python continuam pendentes.

Os testes com modelo real são opt-in e exigem pacote CPU previamente obtido e aprovado para uso local. No Windows, defina o caminho absoluto em `KAPILAB_REAL_CPU_MODEL` e rode os três focos abaixo; o primeiro valida o cancelamento cooperativo do runtime, e o segundo envia `CTRL_C_EVENT` à ação de `model run autocomplete` após o primeiro token nativo. As fixtures chamam diretamente a ação/runner, sem parser/arranque do apphost:

```powershell
$env:KAPILAB_REAL_CPU_MODEL = "<pacote-cpu-onnx-genai>"
dotnet test tools/KapiLab.Tests/KapiLab.Tests.csproj --no-restore -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false -m:1 --filter "FullyQualifiedName~RealModelInferenceCancellationTests|FullyQualifiedName~RealModelCliCancellationTests|FullyQualifiedName~LocalProcessMatrixTests.CancellingDuringRealCpuInferenceKillsTheCurrentChildAndKeepsEarlierResults"
```

Sem a variável, os testes são ignorados. Linux continua validação manual e fora da meta executável deste chat.

Para validar a captura raw/ghost no mesmo caminho de autocomplete da IDE, use somente a fixture allowlisted (hash conferido pelo comando) e um pacote CPU local. A retenção é explícita, grava um único registro JSONL atomicamente sob o workspace e não habilita persistência para o Desktop:

```powershell
kapilab model run autocomplete --package "<pacote-cpu-onnx-genai>" --in tools/KapiLab/Fixtures/autocomplete-raw-ghost-v1.jsonl --workspace . --device cpu --context-tokens 1024 --max-tokens 8 --limit 1 --keep-text --out reports/lab/raw-ghost.jsonl
```

Sem `--keep-text`, o JSONL mantém o ghost filtrado e hashes/contagens, sem texto bruto. A fixture e a saída raw/ghost são amostras sintéticas; esse comando não valida overlay visual, decorador de latência na UI, GPU nem paridade Python.

Para publicar o apphost local `win-x64` e atualizar o lock por backend:

```powershell
./scripts/lab/build_kapilab.ps1 -Backend Cpu -Offline -Publish
./scripts/lab/build_kapilab.ps1 -Backend WinML -Offline -Publish
```

Isso gera publicação framework-dependent em `artifacts/f7b/publish/<Backend>/win-x64/` e atualiza `tools/kapilab.lock.json` atomicamente. O lock registra o SHA-256 do apphost e um hash ordenado do conteúdo publicado; o wrapper confere ambos antes de iniciar o CLI. .NET 10 é pré-requisito. O script só publica win-x64 em host Windows x64; Cuda, ARM64 e Linux continuam pendentes.

Com o apphost Cpu publicado e lock atualizado, execute o foco ponta a ponta de Ctrl+C em `matrix run`:

```powershell
dotnet test tools/KapiLab.Tests/KapiLab.Tests.csproj --no-restore -p:SlopOnnxBackend=Cpu -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false -m:1 --filter FullyQualifiedName~PublishedMatrixProcessCancellationTests
```

Esse foco Windows inicia o apphost com hash conferido pelo lock, envia `CTRL_C_EVENT` real e verifica o relatório parcial e a terminação do filho ativo. Sem a publicação Cpu local, o teste é ignorado; não valida terminação universal de descendentes arbitrários.

## Smokes sem modelo

Execute a partir da raiz do repositório:

```powershell
dotnet run --project tools/KapiLab/KapiLab.csproj --no-build -- env
dotnet run --project tools/KapiLab/KapiLab.csproj --no-build -- npu inventory
dotnet run --project tools/KapiLab/KapiLab.csproj --no-build -- catalog plans --help
dotnet run --project tools/KapiLab/KapiLab.csproj --no-build -- guidance smoke --help
python -m unittest discover -s scripts/lab/tests -v
```

`env` informa versão/backend e estado do checkout. `npu inventory` enumera os providers desta build sem registrar EP, baixar dependência ou carregar modelo. Neste host WinML encontrou AMD Ryzen 9 7900 e GPU DirectML RX 7800 XT, sem provider NPU. Isso não equivale a `npu smoke` nem comprova suporte a um pacote.

## Matriz local de processos

`matrix run --in <arquivo> [--out <relatório>]` consome `kapilab-process-matrix-v1` e inicia sequencialmente os processos declarados, sem shell, com cwd no workspace e `argv` separado. Caminhos relativos do executável são resolvidos dentro do workspace; caminhos absolutos só são aceitos quando o caso declara explicitamente que o executável é externo. O ambiente herdado é filtrado por allowlist e cada execução tem timeout. O relatório opcional é publicado atomicamente.

Na fixture Windows, NTSTATUS reconhecido como crash retorna código 10, saída comum não zero retorna 1, e timeout/cancelamento/término não confirmado retorna 12, com `complete=false` e preservação dos resultados anteriores. A fixture comprova o contrato de classificação e preservação; o foco separado `PublishedMatrixProcessCancellationTests` verifica Ctrl+C real no apphost Cpu publicado e término do filho ativo. Nenhum deles demonstra crash da IDE ou de provider real.

### Coordenação local de GPU nas rotas de inferência

As rotas `model run autocomplete`, `model run chat`, `bench autocomplete` e `bench chat` aceitam `--via-queue` ou `--standalone-gpu` quando o plano de providers pode usar GPU. Sem um desses modos, a execução GPU falha antes da inicialização nativa. Os dois flags são mutuamente exclusivos; planos CPU-only não precisam adquirir lease GPU.

- `--via-queue` exige que `KAPILAB_GPU_TOKEN` esteja presente e não vazio. O KapiLab somente valida esse marcador local e mantém seu lease exclusivo; não lê, persiste ou ecoa o valor. Isso não integra nem valida `gpu_queue.ps1`, fila/ledger, janelas, treino ou medição de carga.
- `bench autocomplete --matrix` e `bench chat --matrix` aceitam até 32 células `{package,hardware,ep?,scenario}` e iniciam um processo por célula em sequência. O relatório `kapilab-bench-matrix-v1` é salvo após cada resultado para preservar parcial. `ep=dml` fica registrado como hint e não força seleção do EP. Para validação sem pesos, pacotes ausentes exercitam somente a orquestração/falha; inferência real segue manual e depende de pacote/hardware.
- `--standalone-gpu` aguarda enquanto existir `tmp/gpu.pause` no workspace e respeita cancelamento (por exemplo, Ctrl+C). Remover o marcador permite continuar.
- Durante o lease GPU, `tmp/kapilab.heartbeat` publica `kapilab-gpu-heartbeat-v1` a cada 30 segundos. Um guard exclusivo estabelece ownership; dispose normal remove o heartbeat e o guard liberado permite substituir registro stale após encerramento abrupto. O lock GPU permanece responsável pela exclusão entre processos.

Os testes exercitam modo ausente antes da carga, token de fila ausente/presente sem vazamento ao heartbeat, pausa/cancelamento, pulsos, limpeza e recuperação após crash. Isso não valida o coordenador externo, nem carga concorrente ou inferência GPU/NPU com pesos reais. Os smokes CPU reais são descritos em [evidências locais](evidencias-locais.md). Linux é validação manual e está fora da meta deste chat.

`catalog plans` consome arquivo JSON versionado dentro do workspace. Cada entrada é sintética e avaliada por `AgentModePolicy.Plan`; a saída sinaliza `synthetic-policy-input` e `toolInvocationPerformed=false`. `catalog invoke` aceita somente `list_connections` permitida ou `list_databases` negada sem grant e executa pelo runtime/registry compostos; veja o allowlist e os limites em [README do KapiLab](../../../tools/KapiLab/README.md).

`guidance smoke` aceita somente pacote, prompt e gramática explicitamente fornecidos. Sem pacote compatível, o resultado é inconclusivo e nenhuma inferência ocorre. Quando houver pacote, a saída `path=genai-direct` também não constitui evidência da rota IDE; `grammar_ok` exige verificador independente.

O wrapper Python usa a publicação selecionada no lock e verifica apphost e conteúdo antes de execução. Para desenvolvimento sem publicar, passe `dotnet_executable` explicitamente para optar por `dotnet run --no-build`. A execução usa `shell=False`, timeout finito, workspace, ambiente controlado e pode exigir backend/GenAI com preflight `env`.

## Execuções com pacote

Antes de executar um comando de inferência, confirme que o pacote foi obtido e selecionado explicitamente pelo operador e está fora de `data/eval/blind`. Use um workspace de laboratório dedicado e arquivos UTF-8 sem segredos ou dados de usuário. Não coloque URI MongoDB, credenciais ou texto privado na linha de comando.

```powershell
$labWorkspace = "<workspace-lab>"
$package = "<pacote-onnx-genai>"
dotnet run --project tools/KapiLab/KapiLab.csproj --no-build -- model inspect --package $package --hardware --workspace $labWorkspace
dotnet run --project tools/KapiLab/KapiLab.csproj --no-build -- model validate --package $package --strict-kapi --verify-hashes --workspace $labWorkspace
```

Inspeção/validação não fazem geração. A segunda linha só se aplica a pacotes que declarem os contratos locais KapiLab e manifesto de hashes; `--strict-kapi` e `--verify-hashes` não afirmam equivalência com schema externo. Para executar `model run`, `bench`, `contract tokenize` ou `guidance smoke`, confira primeiro o `--help` do subcomando, limites de entrada/saída e o estado do lock GPU. Registre pacote, backend, versão GenAI, commit, código de saída e relatório sem incluir texto do modelo quando o comando assim especificar.

## Resultado e pendências

- Mantenha builds/testes separados por backend e confira os TRX antes de registrar contagens ou hashes.
- Registre comandos e códigos observados em `evidencias-locais.md`; registre estado por critério em `rastreabilidade-f7b.md`.
- Não converta ausência de pacote, NPU, provider ou resultado externo em aprovação. Use `inconclusive`/pendente quando não houver evidência necessária.
- Grant metadata transitório e revogação por atualização persistida de permissões têm um teste local pelo `IAgentRuntime`; grants expirados, approval, comparação externa via `catalog invoke`, loop `reference`/`ide`, replay-tools com aprovações, Exec Match, integração/validação da fila GPU externa, supervisão de processos e gates consolidados seguem pendentes até suas dependências e evidências reais existirem. O heartbeat/pausa standalone local é somente a fatia descrita acima; não constitui fila/ledger.
- Não execute benchmarks de latência/alocação em CI ou release. Eles são medições manuais de desenvolvimento.

Revalidação após F7B-20: KapiLab Cpu e WinML passaram com 262/265 testes (3 opt-in ignorados); `AgentReplayCommandTests` passou 19/19 e o wrapper Python 14/14 incluindo replay v2 grant/negação pelo apphost. TRXs, hashes e limites desse recorte estão em [evidências locais](evidencias-locais.md).
