# Autocomplete local — análise e contrato de implementação

> **Nota de estrutura (ADR-040, 17/09/2026):** as seções abaixo registram a análise original de 11/09/2026, quando a solução tinha quatro projetos (`Core`, `Application`, `Infrastructure`, `Desktop`). Desde a ADR-040, o núcleo determinístico de autocomplete/highlighting foi extraído para `EsilvaSoft.KapibaraStudio.Autocomplete.Core`, os contratos e políticas puras de IA local para `EsilvaSoft.KapibaraStudio.LocalAi.Core`, e os adaptadores ONNX Runtime GenAI para `EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi`. Ver [arquitetura atual](05-arquitetura.md) e [ADR-040](10-decisoes-arquiteturais.md).

## Análise inicial da solução (11/09/2026)

`EsilvaSoft.KapibaraStudio.slnx` contém Core, Application, Infrastructure, Desktop e UnitTests. Todos usam .NET 10, nullable, analisadores como erros, pacotes centralizados e lockfiles. `tools/BrandAssets` é uma ferramenta auxiliar fora da solução. O CI existente executa restore travado, build e NUnit em Windows/Linux; publicação ARM64 e instalação nativa não eram gates implementados.

Core contém DTOs e regras de BSON, conexão, sessão e ambiente. Application expõe contratos específicos e WorkspaceService como fachada. Infrastructure implementa driver MongoDB, runtime Console Jint, processo mongosh, arquivos, segredos de sessão e persistência. Desktop usa MVVM/CommunityToolkit; App é composition root, com descarte do contêiner no encerramento. WorkspaceViewModel coordena abas; cada WorkspaceTabViewModel captura contexto antes de executar. Ferramentas administrativas permanecem no MainWindowViewModel contextual. O novo recurso não altera execução nem roteamento.

Na análise de 11/09, o editor era TextBox Avalonia, com MenuFlyout para completion. ConsoleAutocompleteService consulta metadados explicitamente; MqlAutocompleteService fornece operadores/campos. Naquela data, AvaloniaEdit, highlighting e folding eram planos. No checkout revisado em 13/09, MongoTextEditor usa AvaloniaEdit 12.0.0 e highlighting está implementado; folding não foi identificado como entrega completa. Ctrl+Espaço solicita sugestões; F6 sai do editor. Temas usam recursos semânticos de App.axaml, código 14 configurável e UI compacta. A integração já acrescentou sugestões preemptivas aceitas por Tab e descartadas por Escape; detalhes históricos de TextPresenter abaixo não descrevem integralmente o editor atual.

WorkspacePreferences é serializado no documento workspaceSession/current de versão 1. LiteDbConnectionProfileRepository é o único dono da conexão Direct, registrado por aliases em DI. O novo campo de preferências será aditivo e terá versão própria; falhas de recuperação continuam impedindo sobrescrita. Autosave não persistirá cache, prompts, resultados ou objetos de runtime. Não existe pipeline geral de telemetria/logging configurado; a infraestrutura referencia Logging.Abstractions. Logs novos serão locais, técnicos e sem texto do editor ou mensagens brutas do runtime.

Decisão: reutilizar Core/Application para abstrações/regras e concentrar Microsoft.ML.OnnxRuntime/GenAI em Infrastructure. Não criar um projeto paralelo de domínio. ONNX Runtime GenAI fornece tokenizer nativo, geração e KV cache; QwenFimPromptBuilder centraliza FIM. Os pacotes 0.15.2 incluem binários CPU win-x64/win-arm64/linux-x64/linux-arm64, o que ainda precisa ser distinguido de homologação em hardware real. Modelos são externos, não recursos do app nem conteúdo de publicação.

Referências oficiais: [API GenAI C#](https://onnxruntime.ai/docs/genai/api/csharp.html), [formato de configuração](https://onnxruntime.ai/docs/genai/reference/config.html), [fontes da versão 0.15.2](https://github.com/microsoft/onnxruntime-genai/tree/v0.15.2). A API é preview; versão fixada para manter o contrato reproduzível.

## Estado da Fase 2 tradicional

### Ghost automático (inline) — lote 5.1, 18/09/2026

Desde o lote 5.1, o **ghost automático** (sugestão inline, sem abrir a lista) segue uma ordem de fonte distinta da lista explícita: **determinístico** (`TraditionalPreemptiveCompletionProvider`, reaproveita `CompletionService`/ranker com `Peek`) → **dicionário lexical local** → **IA**. A IA automática só entra com a flag `InlineUseAi = true` **e** modelo já carregado (`Ready`, política LoadedOnly); nenhum caminho de digitação inicia carga, troca ou warmup de modelo. `AiAutocompleteProvider`, descrito nas seções seguintes deste guia, foi preservado e continua servindo o ghost automático quando habilitado, mas deixou de ser a fonte padrão. Detalhe, evidência de teste e limites de latência em [preemptive-autocomplete](auto-complite/preemptive-autocomplete.md) e [phase-5-preemptive](auto-complite/phases/phase-5-preemptive.md).

**Atualização W2c (18/09/2026):** `AutocompleteSettingsWindow` passa a expor as seis opções que já existiam apenas no JSON persistido. As três flags anuláveis (`InlineEnabled`, `InlineUseTraditional`, `InlineUseAi`) usam um par de CheckBox "efetivo" + botão **Usar padrão**: o toggle materializa um valor explícito, e só **Usar padrão** volta ao estado ausente — abrir a janela não reescreve nada que o usuário não tocou. `InlineUseTraditional` sem override segue `UseDictionary` ao vivo. Com "Sugerir automaticamente após uma pausa na digitação" desligado, as duas opções de origem (determinística/IA) ficam visualmente desabilitadas sem perder o valor salvo, e um texto explica a IA automática exigir modelo já carregado (política LoadedOnly). `CompletionAutoOpenOnTrigger` e `CompletionEnterAccepts` (lista Ctrl+Espaço) usam CheckBox comum. Evidência: `AutocompleteUiTests` regenerou os 18 PNGs `autocomplete-settings-*` e acrescentou `autocomplete-settings-inline-overrides-<tema>.png`; inspeção em 660×680 claro/escuro confirmou legibilidade, contraste e alinhamento dos botões "Usar padrão", e o estado desabilitado preservando o valor da IA marcada.

Pendências desta entrega que **não** podem ser lidas como resolvidas: o portão de composição de IME (`InlineCompletionEditorState.Composing`) existe e é testado no coordinator, mas o editor real não publica esse estado, então a inibição por IME não vale em produção; e a meta de latência edição → ghost (p95 ≤ 20 ms) foi medida e **não** atendida (17,6–27,1 ms de excedente sobre o debounce em Headless), ver [performance](auto-complite/performance.md). Homologação com leitor de tela real dos novos controles (CheckBox/botão condicional) segue pendente.

O caminho tradicional atual é separado do fluxo legado de IA: `AvaloniaTextSnapshot` captura o documento sem materializá-lo, e a cadeia segue lexer compartilhado → parser tolerante → contexto → `CompletionService` → catálogo/ranking → `CompletionWindowPresenter`. Desde o lote W0 (18/09/2026), `Ctrl+Espaço` é o único gatilho padrão da lista — `Ctrl+.` saiu dos padrões (um override salvo continua funcionando); a lista é determinística, filtrável e não executa consultas. `Ctrl+;` já é reconhecido para IA explícita, mas sem runtime, e apenas informa indisponibilidade. Este documento contém seções históricas sobre IA local e o antigo menu; elas não são evidência de que esses fluxos pertençam à lista tradicional. Consulte o [estado detalhado da Fase 2](auto-complite/phases/phase-2-traditional-autocomplete.md) e a [política de atalhos](auto-complite/editor-integration.md#arbitragem-de-teclado).

## Arquitetura implementada

O diagrama abaixo é histórico do fluxo de IA local. Para a lista tradicional, consulte a cadeia documentada na seção anterior; não há concatenação de sugestões de IA/básicas nessa lista.

Core contém requests, resultados, definições e preferências. Application contém contratos, seleção AI/básico, cache, proteção simples do contexto e CompletionSession. Desktop só conhece contratos de autocomplete; código do editor não acessa ONNX, tokenizer, providers ou caminhos físicos. **Desde a ADR-040 (17/09/2026):** contratos e políticas puras de IA local (`ILocalAiModelService`, `ILocalModelRuntime`, `ITokenizer`, `ICompletionPromptBuilder`, estados e exceções) ficam em `LocalAi.Core`; o runtime ONNX e seu SDK nativo estão em `Infrastructure.LocalAi.OnnxAdapter`, enquanto `Infrastructure.LocalAi` compõe o catálogo, adaptadores por arquitetura e fonte remota de modelos. As implementações concretas de orquestração (`LocalAiModelService`, os construtores FIM) permanecem em `Application`; `LocalModelAiChatService` foi **removido em 25/09/2026 pela [ADR-055](10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)** junto com o painel "Assistente IA · por aba" (Fase 5). Ver [arquitetura](05-arquitetura.md) e [ADR-040](10-decisoes-arquiteturais.md).

Uma CompletionSession por editor mantém debounce (150 ms), CancellationTokenSource próprio e versão monotônica. Edição, cursor, seleção, contexto, troca de preferências e saída da árvore visual invalidam sugestões. A resposta só aparece na view que capturou o pedido; a aceitação verifica novamente texto/cursor. A alteração do explorer não redireciona a aba e autocomplete nunca executa consultas.

### Chat revisável do Console (histórico — painel removido)

**Removido em 25/09/2026 pela [ADR-055](10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026).** O texto abaixo descreve o painel "Assistente IA · por aba" da Fase 5, hoje substituído pelo painel **Agente IA** (Fase 7, [16-chat-nativo](phases/phase-07-v0.11.0/README.md)), que não compartilha o conteúdo completo do editor nem oferece diff/**Aplicar ao editor**. `IAiChatService`, `AiEditorContext`, `AiChatResponse`, `AiChatService` e `WorkspaceTabViewModel.AiChat` foram removidos do código.

O chat era uma superfície independente do autocomplete preditivo. `AiEditorContext` capturava instrução, cabeçalho, texto, linguagem, dialeto, banco, coleção, tipo de operação e contexto auxiliar em um snapshot bounded. `IAiChatService` retornava `AiChatResponse` com explicação, código completo proposto, diff e risco. `WorkspaceTabViewModel.AiChat` mantinha histórico, `CancellationTokenSource` e geração próprios por aba; uma resposta fora de ordem ou com texto/destino alterado era descartada.

O painel não executava código. A aplicação era um segundo passo explícito; risco de escrita/destruição abria uma confirmação adicional. O editor substituía o texto pelo caminho normal de seleção para conservar undo/foco, e o conteúdo não relacionado só era alterado quando o usuário confirmava a proposta completa. O serviço `AiChatService` era um baseline local seguro para a solicitação de filtro/limite; provedores conversacionais podiam implementar `IAiChatService` sem tocar na view ou no runtime do Console.

AiAutocompleteProvider serializa inferências entre abas com SemaphoreSlim. Solicitações canceladas abandonam a espera. A sessão é carregada no primeiro pedido, reutilizada e descartada ao trocar configurações, desabilitar IA ou encerrar a aplicação. Falha transitória usa fallback básico e cooldown de 30 segundos; Testar modelo permite tentar novamente. Cache em memória: até 64 respostas AI, expiração de 30 segundos, chaves SHA-256 do request completo e revisão das preferências, incluindo contexto auxiliar e dicionário. Nada disso entra no LiteDB.

### Context Builder e dicionário

WorkspaceTabViewModel captura texto, cursor, modo, Input, campos dos resultados, nomes conhecidos e três comandos recentes da mesma conexão/banco antes de qualquer await. AutocompleteContextBuilder limita o prefixo a 4096 caracteres e o sufixo a 1024; sem contexto ampliado, mantém apenas 128 caracteres antes do cursor. O tokenizer aplica depois o orçamento real de tokens. Input contribui com até 1024 caracteres; resultados contribuem com até 128 nomes de campos extraídos de oito documentos limitados a 8192 caracteres por documento. A extração é reutilizada enquanto o resultado não muda. Valores dos resultados não entram no prompt.

Perfis fornecem somente nomes; bancos e coleções vêm dos nós já carregados do explorer da conexão da aba, sem nova consulta de metadados a cada tecla. A seleção do explorer não altera o destino capturado. O histórico contribui com até três comandos de 256 caracteres. Dados auxiliares reconhecidos como sensíveis são excluídos. Contexto auxiliar é limitado a 8192 caracteres; o dicionário a 256 termos de 128 caracteres. Tudo permanece temporário, sem treinamento ou persistência de resultados.

A DSL real usa `ConnectionPool`, `getConnection(name).getDatabase(name).getCollection(name)` e `db.getCollection(name)`. Não existe alias `ConnectionPull` nem método `GetDatabase`. Esses contratos e construtores BSON entram no contexto como referência. O modelo é uma continuação FIM curta, não um chat.

Desde 13/09/2026, `AiAutocompleteProvider` e o chat consomem `ILocalAiModelService`, dono único do modelo carregado, da fila com prioridade e do teste de modelo. Para outro runtime, implemente ILocalModelRuntime e injete sua fábrica em LocalAiModelService; uma nova família de modelo é um `IModelAdapter` (validação, tokenizer, prompt e paradas). IAutocompleteService permite substituir a estratégia completa sem alterar a view. Não introduzir tipos ONNX no editor. [IA local multimodelo](26-ia-local-multimodelo.md).

## Instalação e troca de modelos

Nesta máquina, os modelos preparados ficam em `F:\models`, entre eles `SlopCoder-Mongo-1.5B-full-ONNX-INT8` (Qwen2.5-Coder-1.5B especializado em MongoDB e no contrato de autocomplete/chat deste editor, exportação ONNX Runtime GenAI INT8, 2,52 GB, com `model.onnx`, `model.onnx.data`, tokenizer, `genai_config.json` e `slopcoder_manifest.json`) e `Qwen2.5-Coder-0.5B-onnx-int4-cpu`. Desde 13/09/2026 não há caminhos de máquina fixos no código nem sugestão automática: informe `F:\models` como diretório de modelos e escolha a pasta, ou use **Outra pasta…**. Um caminho já salvo continua válido como pasta externa. A descoberta não carrega os pesos no startup. Não substitua o config desses pacotes pelo exemplo da exportação alternativa abaixo. [Catálogo e seleção](26-ia-local-multimodelo.md#diretório-e-seleção).

Instale separadamente uma **exportação ONNX Runtime GenAI do Qwen2.5-Coder**, preferencialmente 0.5B para começar. Pesos originais safetensors, GGUF, exportações Transformers.js e pacotes exclusivos de RyzenAI/QNN não são automaticamente compatíveis. Um `.onnx` isolado não basta. O aplicativo não baixa ou converte pesos, não executa código de repositório de modelo e não depende de Python, Ollama, LM Studio, Docker ou servidor de IA.

Diretório padrão de descoberta:

- Windows: `%LOCALAPPDATA%\EsilvaSoft\KapibaraStudio\Models`.
- Linux: `$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio/Models`, com fallback para `~/.local/share`.

Exemplo de instalação externa:

```text
Models/
  qwen2.5-coder-0.5b/
    genai_config.json
    tokenizer.json
    tokenizer_config.json
    model.onnx          # ou o nome indicado em model.decoder.filename
    model.onnx.data     # se a exportação usar dados externos
    ...                 # demais arquivos fornecidos pela exportação
```

1. Obtenha uma exportação compatível de origem confiável e confira licença/integridade. Conserve **todos** os arquivos e dados externos do pacote.
2. Extraia em um diretório fora do repositório e da instalação do Kapibara Studio.
3. Em **Preferências → Autocomplete**, informe o diretório pai em **Diretório de modelos** e escolha a pasta em **Modelo**, ou use **Outra pasta…**. Atualizar lista até 100 subpastas e informa as ignoradas com o motivo (arquivos ausentes, inválido, não suportado).
4. Use **Testar modelo**. A ação salva a configuração, recarrega, valida arquivos, inicializa tokenizer/sessão/provider e gera até oito tokens de `db.Users.find({`, sem dados do usuário.
5. Mantenha **Automático**, ou escolha **Básico** para não carregar IA. Salvar aplica a configuração; fechar descarta edições ainda não salvas.

O catálogo exige `model.type = qwen2`, decoder existente dentro do diretório e tokens FIM no tokenizer. Isso é uma verificação estrutural; **Arquivos encontrados** não significa inferência comprovada. Qwen2.5-Coder-1.5B, 3B e maiores podem ser selecionados pelo mesmo caminho se exportação, operadores, tokenizer e memória forem compatíveis; não há limite de parâmetros codificado em 0.5B. O 0.5B Q4 foi validado com pesos reais em CPU Windows x64; tamanhos maiores ainda não foram medidos. [Modelo base Qwen](https://huggingface.co/Qwen/Qwen2.5-Coder-0.5B) descreve a família, mas seus pesos originais não substituem a exportação GenAI.

### Receita reproduzível validada para 0.5B Q4

Use a exportação [onnx-community/Qwen2.5-Coder-0.5B-ONNX na revisão c0bbc5c5](https://huggingface.co/onnx-community/Qwen2.5-Coder-0.5B-ONNX/tree/c0bbc5c5a8c86c9aa4e2e6e3cbd23219042b2351). Baixe `onnx/model_q4.onnx` (776.999.645 bytes), `tokenizer.json`, `tokenizer_config.json`, `config.json`, `special_tokens_map.json`, `added_tokens.json`, `vocab.json` e `merges.txt`. Coloque todos na mesma pasta externa, com o graph chamado `model_q4.onnx`. Copie [este exemplo de configuração](models/qwen2.5-coder-0.5b-q4-genai.example.json) para essa pasta com o nome `genai_config.json`.

Esse arquivo mapeia as entradas/saídas efetivamente inspecionadas da revisão indicada e não é configuração universal para outros graphs. O SHA-256 verificado de `model_q4.onnx` é `83de18b39df438edbfc536efa82ebf53a9277d19f6179359122986155e8fcfdc`. Selecione a pasta em Preferências e teste com CPU. Não é preciso converter o modelo nem instalar Python. A fixture de desenvolvimento foi baixada no diretório temporário do sistema; nenhum peso foi colocado no Git ou nos pacotes publicados.

## Hardware e ONNX Runtime

| Provider | Estado nesta distribuição |
| --- | --- |
| CPU | Binários incluídos; mínimo em Windows x64/ARM64 e Linux x64/ARM64. Duas threads intra-op e uma inter-op para reduzir contenção. |
| DirectML | Seleção/ tentativa de carregamento preparada se o runtime expuser DmlExecutionProvider. Binários DML não incluídos nem homologados nesta distribuição CPU. |
| CUDA | Seleção preparada se CUDAExecutionProvider estiver disponível. Não inclui CUDA/cuDNN nem pacote CUDA; não homologado. |
| OpenVINO | Enum/ponto de extensão; inicialização não implementada. |
| QNN / NPU AMD, Intel, Qualcomm | Enum/ponto de extensão; não implementado. ARM64 não exige NPU. |

Tabela histórica de 11/09/2026; o estado atual por build está em [23](23-onnx-slopcoder.md#distribuições-de-hardware) e a detecção em [26](26-ia-local-multimodelo.md#hardware). Desde 13/09/2026, Automático tenta NPU, GPU e CPU entre os dispositivos reportados pelo ONNX Runtime e compatíveis com o modelo, com fallback na carga e na geração. CPU, GPU ou NPU explícitos usam somente aquele backend e informam a falha, sem fallback silencioso. Falha de ONNX/modelo/tokenizer resulta em básico. Pacotes acelerados precisam de uma distribuição compatível, não basta editar o nome do provider.

`Microsoft.ML.OnnxRuntimeGenAI` 0.15.2 traz ONNX Runtime 1.28.0 transitivo. Publicações por RID incluem apenas binários do destino, não pesos. O build rejeita modelos ONNX/safetensors/GGUF encontrados nos itens de publicação; `.gitignore` também exclui pesos. Não há instalador de modelos nem recursos embutidos.

## FIM, tokenizer e geração

ITokenizer encapsula o tokenizer nativo carregado junto ao modelo. Não há tokenização por Split ou por caracteres na implementação real. QwenFimPromptBuilder monta `<|fim_prefix|>prefix<|fim_suffix|>suffix<|fim_middle|>` usando IDs do tokenizer e exige cada marcador como token único.

Orçamento padrão de entrada: 2048 tokens, incluindo três marcadores FIM. Reserva inicial de 25% para o começo do sufixo, restante para o fim do prefixo; sobras são reaproveitadas. A view limita cada lado a 32768 caracteres antes da tokenização, e o runtime respeita também a janela declarada pelo modelo. Nenhum documento completo é coletado por padrão. Geração greedy, até 32 tokens por padrão, aceita texto multiline e interrompe em marcadores finais/FIM. Sugestões vazias, delimitadores de chat/código e segredos reconhecíveis são rejeitados.

Cancelamento chega ao gerador nativo por `terminate_session`, com descarte seguro do callback antes do gerador e recuperação da sessão. A construção inicial da sessão nativa não oferece interrupção imediata; ocorre no worker, verifica cancelamento antes/depois e nunca bloqueia o startup da UI. [Contrato nativo de cancelamento](https://github.com/microsoft/onnxruntime-genai/blob/v0.15.2/docs/RuntimeOptions.md).

## Preferências e UX

AutocompleteSettings tem versão 1, aditiva em WorkspacePreferences/workspaceSession versão 1. Campos: habilitado, modo (Automático/Básico/IA), diretório, aceleração, provider avançado, tokens de contexto (64–1.048.576 no limite absoluto; 8192 quando o modelo não declara janela), geração (1–1.048.576 no limite absoluto; 256 quando o modelo não declara teto), atraso (50–2000 ms), dicionário, contexto de Input, campos de Resultados, contexto ampliado do editor/histórico e Tab incremental. A janela expõe os orçamentos como ComboBox editáveis, com sete perfis de tier pré-carregados; o tier detectado seleciona automaticamente um orçamento inicial e o usuário ainda pode escolher outra sugestão ou digitar livremente dentro da validação. Os valores são exibidos e persistidos sem separador de milhar: somente dígitos, como `8192`; `8.192` e `8,192` são inválidos. `context_length` do `genai_config.json` é o limite rígido; `generation.autocomplete.maxTokens` limita a saída quando declarado; contexto + saída + overhead do contrato não podem exceder a janela. Perfis manuais persistidos por fabricante/nome/VRAM só servem para estimativa. A classificação usa 0–2 GiB Sem acelerador, >2–4 Básico, >4–8 Intermediário, >8–12 Avançado, >12–16 Alto, >16–24 Muito alto e >24 Workstation, reservando margem para sistema, driver e runtime. As opções são ligadas por padrão, inclusive ao ler preferências antigas. Padrões: habilitado, Automático, hardware Auto, 2048/32/150. Não há carga de modelo no startup.

A sugestão aparece como ghost text no cursor do TextBox. Uma projeção visual usa o layout real do TextPresenter e mantém o texto existente antes/depois com PrimaryText; somente a inserção usa SecondaryText. A projeção acompanha layout/rolagem, é recortada ao viewport e nunca muda o documento. **Tab** aceita um identificador/caminho ou uma expressão até delimitador/linha e mantém o restante disponível; por exemplo `ConnectionPool.` → `Production.` → `Customers` → `.find({`. Desligar Tab incremental aceita tudo. **Esc** descarta antes de cancelar qualquer consulta. Digitação, Backspace, seleção, destino, Input, Resultados e preferências invalidam previsões antigas. **Ctrl+Espaço** mantém o menu explícito e F6 sai do editor. Sem sugestão, os atalhos normais continuam. Aceitar nunca executa código.

O gerador interrompe ao reconhecer o sufixo existente em uma saída que o repetiu; o provider remove essa repetição antes da inserção. Isso evita duplicar fechamento de função/consulta e gerar funções adicionais depois do trecho preenchido.

As configurações distinguem não instalado, não carregado, arquivos encontrados, arquivos ausentes, carregando, carregado (backend/provider/dispositivo/métricas), inválido, não suportado e falha. Salvar/Testar exibem falhas de persistência; snapshot ilegível não é substituído. Não há métrica inventada: dispositivo e memória de vídeo vêm do ONNX Runtime; carga, primeiro token e tokens/s são medidos; memória é o working set do processo; VRAM usada não é medida.

## Privacidade e logging

Nenhum código, query, banco, documento, senha ou prompt é enviado a serviço de IA. APIs de telemetria opcionais de GenAI e ONNX são desabilitadas ao inicializar. Autocomplete não recebe ConnectionProfile, URI resolvida ou valores de Key Vault/ENV. Recebe nomes conhecidos e, quando habilitados, Input e nomes de campos dos resultados da aba. ENV.get permanece texto não resolvido.

Uma detecção conservadora evita inferência quando o contexto contém URI MongoDB, atribuições comuns de password/secret/token/API key, Bearer ou chave privada. Também evita reaproveitar palavras desse contexto no fallback. A verificação percorre até 65 536 caracteres com o motor de regex sem backtracking (tempo linear) e limite de 1 s como proteção; se o limite for atingido, o contexto é tratado como sensível e não segue para a IA. O limite anterior de 50 ms falhava em máquinas ocupadas, como runners de CI, porque pausas de GC e preempção contam no tempo. Isso não é DLP e não reconhece segredos arbitrários; evite colá-los no editor.

IAutocompleteDiagnostics registra por Trace local eventos técnicos de carga, caminho selecionado, provider, fallback, tipo de falha, cancelamento e duração. Não registra mensagens brutas de exceções nativas, código ou prompt. Não há upload de logs. Resultados/cache de autocomplete não são persistidos e não alteram opt-outs existentes de rascunhos/histórico.

## Troubleshooting

| Sintoma | Ação |
| --- | --- |
| Modelo não encontrado | Confira o diretório externo e selecione a pasta que contém genai_config.json. |
| Modelo ONNX inválido / arquivos ausentes | Copie o pacote completo, incluindo dados externos, mantendo caminhos relativos do decoder. |
| Tokenizer ausente ou sem FIM | Use tokenizer.json e tokenizer_config.json da mesma exportação Qwen Coder. |
| Provider/GPU indisponível | Use CPU; esta distribuição não inclui bibliotecas aceleradas. |
| Arquitetura não suportada | Use uma exportação qwen2 com FIM; OpenVINO/QNN/NPU permanecem futuros. |
| Falta de memória / falha de inicialização | Feche outras cargas, reduza contexto ou escolha modelo menor/quantizado compatível com CPU. |
| Sugestão não aparece | Confira habilitação, posição/seleção no editor, cooldown e status. Texto sensível reconhecido não entra na IA. Testar modelo usa um exemplo fixo. |
| Preferências não salvas | Confira estado da sessão/permissões. Não substitua manualmente uma sessão ilegível por vazia. |

Em qualquer caso, **Básico** permite continuar editando sem IA. Sugestões são propostas que precisam de revisão, não prova de correção do código.

## Testes e limites de evidência

Unitários usam ILocalModelRuntime/ITokenizer fakes, sem baixar modelos. Cobrem seleção, fallback/cooldown, configuração, FIM/trimming, cancelamento, cache limitado, ausência/invalidez, privacidade e desabilitação. Headless usa controles reais e gera 18 imagens do editor e 18 de preferências nos dois temas/três tamanhos/três escalas; teclado Tab/Esc é exercitado.

Integração opcional: instale um modelo compatível externamente, defina `SLOP_QWEN_MODEL` e execute o teste `RealQwenGeneratesWithCpuAndReusesNativeSession` pelo nome completo. Ele é Explicit/LocalModelIntegration e não depende de download no CI. A suíte comum não comprova a inferência de um Qwen real. Publicação ARM64 cruzada e presença de DLL ARM64 não substituem execução em hardware ARM64. Evidência atual e pendências estão na [matriz](15-matriz-de-validacao.md).
## Rastreabilidade do pedido original

| Itens | Implementação e prova |
| --- | --- |
| 1–2, análise/arquitetura | Análise inicial acima; contratos nos projetos existentes, sem UI em Core/Application; solução/DI/lifecycle preservados. |
| 3–4, contrato/providers | IAutocompleteService/AutocompleteRequest e providers AI/básico; LocalAutocompleteTests valida seleção e fallback. |
| 5–6, Qwen/modelos externos | LocalModelDefinition/LocalModelCatalog; receita 0.5B real, parâmetros maiores não fixados em código, pesos fora do repo/publicação. |
| 7–9, ONNX/hardware | OnnxLocalModelRuntime/ILocalModelRuntime, AiProviderSelector, CPU real e fallback; acelerados claramente limitados conforme permitido no pedido. |
| 10–12, tokenizer/FIM/contexto | Tokenizer nativo encapsulado, QwenFimPromptBuilder; orçamento em IDs e teste near-cursor, contexto limitado antes do runtime. |
| 13–16, debounce/cache/lifetime | CompletionSession, cache 64/30s, AiAutocompleteProvider lazy com sessão reutilizada; testes de obsolescência, expiração, cancelamento e unload. |
| 17–20, preferências/Auto/status/teste | AutocompleteSettingsWindow/ViewModel, persistência versionada, catálogo, teste real via UI; Automatic é default e básico funciona sem modelo. |
| 21–22, UX/multiline | Ghost text no cursor, projeção multiline preservando sufixo, Tab incremental/Esc e menu existente; revisão preditiva substitui a prévia abaixo do editor. |
| 23–27, privacidade/concorrência/logging | Sem rede no pipeline de completion; nenhum perfil/ENV resolvido no request; reconhecimento simples de segredos; gate único de inferência; Trace técnico e telemetria opcional desativada. |
| 28, testes | LocalAutocompleteTests/AutocompleteReliabilityTests/AutocompleteUiTests; fakes sem pesos na suíte regular, dois Explicit com modelo real externo. |
| 29–30, publicação/ARM64 | Quatro RIDs publicados; binários CPU ARM64 presentes e arquitetura PE conferida; NPU não é requisito. Limite de homologação nativa registrado. |
| 31, extensibilidade | Interfaces de serviço, catálogo, runtime, tokenizer e prompt; detalhes Qwen permanecem fora do editor. |
| 32–35, documentação | Este guia contém arquitetura, instalação, requisitos, privacidade, modelos, hardware, configuração e troubleshooting; README, guia geral, design system, ADR, plano, acompanhamento e matriz atualizados. |
| 36–38, conclusão/restrições/ordem | Gates acima e matriz final: testes regulares/nativos, publicação sem pesos, UI inspecionada, preservação do produto/MIT e ausência de servidores/Python. |


## Integração com syntax highlighting — 12/09/2026

Ghost text usa Syntax.GhostText. Prefixo e sufixo reutilizam a classificação do editor quando o snapshot corresponde ao texto atual; a projeção mantém o conteúdo intacto e Tab continua sendo edição normal. A divisão do sufixo em Runs coloridos não muda sua concatenação nem os contratos de cancelamento/versão. [Detalhes](22-syntax-highlighting.md).

## ONNX SlopCoder e chat — 13/09/2026

Revisão implementada: [contrato, uso, distribuições CPU/WinML/CUDA e limites](23-onnx-slopcoder.md). DeepSeek-Coder FIM com tokenizer .NET e manifesto validado; chat e autocomplete compartilham sessão, preservando cancelamento e revisão das propostas. A exportação CPU fornecida foi executada em CPU; a tentativa DirectML falhou na geração e o fallback CPU foi validado. O modelo FIM pode acrescentar alterações não solicitadas no chat; fidelidade conversacional não está homologada.

## Status no roadmap — 13/09/2026

✅ Autocomplete determinístico integra v0.5.0; 🚧 consolidação contextual pertence à v0.6.0 e segue o [plano de autocomplete MongoDB](auto-complite/README.md). 🧪 Inferência local e chat pertencem à v0.9.0, com evidências reais limitadas ao modelo/hardware/cenário registrado. Ghost text e aceitação por Tab já têm implementação e não devem ser reimplementados por mudar a fase. [Inventário](24-inventario-roadmap.md) e [roadmap](09-plano-de-implementacao.md).


## Revisão do plano de autocomplete — 15/09/2026

O plano foi revisto contra a base já implementada de catálogo/cache/schema/métricas. Define quatro modalidades e dois preemptivos independentes, infraestrutura compartilhada, híbrido sequencial e LoadedOnly. Complemento Schema Learning reaproveita find sem consulta extra e persiste estrutura probabilística via dono LiteDB atual. O comportamento disponível descrito neste guia continua legado; novos providers/flags/analyzer ainda não implementados. **Exceção:** a política de atalhos por escopo (lote W0, 18/09/2026) já está implementada e validada — ver [política de atalhos](auto-complite/editor-integration.md#arbitragem-de-teclado). [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).
