# IA local multimodelo: catálogo, hardware e serviço central

Revisão de 22/09/2026. O subsistema deixou de ser "um caminho para um modelo ONNX" e passou a ser:

```text
Catálogo de modelos → Modelo selecionado → Capacidades → Hardware/provider → ONNX Runtime GenAI → Autocomplete / Chat
```

Nenhuma parte da aplicação fora de `LocalAiModelService` e `OnnxLocalModelRuntime` sabe se a inferência roda em CPU, GPU ou NPU. Nomes de modelo, pastas e fornecedores não estão fixos no código.

## Diretório e seleção

- **Diretório de modelos**: padrão `%LOCALAPPDATA%\EsilvaSoft\KapibaraStudio\Models` (Linux: `$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio/Models`). Pode ser trocado em Preferências; vazio usa o padrão.
- **Modelo**: cada subpasta do diretório é um candidato. O nome exibido é o nome da pasta, ou `name` do metadata opcional.
- **Pasta externa** (**Outra pasta…**): aceita um modelo fora do diretório. Se a pasta escolhida estiver diretamente dentro do diretório de modelos, é gravada pelo nome, não pelo caminho absoluto.
- **Baixar modelo** (14/09/2026, [ADR-039](10-decisoes-arquiteturais.md)): lista as variantes publicadas em `esilva/SlopCoder-Mongo-0.5B-ONNX` e `esilva/SlopCoder-Mongo-1.5B-full-ONNX` (pastas de primeiro nível com `genai_config.json`) e instala a escolhida no diretório efetivo como `<repositório>-<variante>`. Baixa em `.<pasta>.download`, verifica cada arquivo pelo hash do hub, move a pasta pronta e reescaneia; nunca sobrescreve uma pasta existente. As variantes trazem `kapibarastudio-model.json` (hardware, capacidades e orçamentos). **Abrir pasta** abre o diretório efetivo, criando-o se necessário.
- **Nomes**: seleção e download mostram "família — hardware precisão" (ex.: `SlopCoder-Mongo-1.5B-full — GPU DirectML FP16`), derivado da pasta `<família>-ONNX-<variante>` ou do `name` do metadata; a segunda linha traz parâmetros, arquitetura e pasta. Modelos fora dessa convenção continuam com o nome do metadata ou da pasta.

```text
Models/
├── SlopCoder-Mongo-0.5B/
├── SlopCoder-Mongo-1.5B/
├── Qwen2.5-Coder-1.5B/
└── SlopCoder-Test/        ← aparece em "Pastas ignoradas: SlopCoder-Test — arquivos ausentes"
```

Abrir a janela ou clicar **Atualizar** reescaneia até 100 subpastas visíveis (ordem alfabética), revalida cada uma, remove as excluídas e preserva a seleção atual. A seleção ausente permanece visível como "— não encontrado". Atualizar não descarrega o modelo ativo. Não há `FileSystemWatcher`.

## Configuração persistida

Campos aditivos em `AutocompleteSettings`, dentro de `WorkspacePreferences` (sessão versão 1). Sessões antigas recebem os padrões:

| Campo | Padrão | Significado |
| --- | --- | --- |
| `ModelDirectory` | `""` | Diretório escaneado; vazio usa o padrão da máquina |
| `SelectedModel` | `""` | Nome de uma pasta do diretório; nunca caminho, `..` ou separador |
| `ModelPath` | `""` | Pasta externa; usada somente quando `SelectedModel` está vazio (compatível com sessões anteriores) |
| `ChatModel` | `""` | Pasta de um modelo separado para o chat; vazio reutiliza `SelectedModel` |
| `ChatEnabled` | `true` | O Assistente IA pode usar o modelo local |
| `LocalAiContextEnabled` | `false` | Consentimento global para contexto de aba no Assistente local; requer também a permissão da conexão |
| `IncludeInputJsonInLocalAiContext` | `false` | Opt-in separado para incluir o JSON da aba Input; respeita o limite de 8.192 caracteres |
| `Acceleration` | `Auto` | Automático, CPU, GPU ou NPU |

A estrutura conceitual `Ai { Enabled, ModelDirectory, SelectedModel, Hardware, Autocomplete, Chat }` foi mapeada nesses campos, em vez de um novo documento, para manter a migração aditiva e o versionamento existentes. `ChatModel` é configurado em Preferências → Autocomplete e IA local, na seção do agente, quando `ChatEnabled` está ativo. Campo vazio reutiliza o modelo do autocomplete. Um modelo de embeddings existe apenas como capacidade declarável.

O contexto do Assistente exige consentimento global e por conexão; `UseEditorContext` e `UseResultPanelContext` controlam suas fontes, e Input JSON permanece em opt-in separado. Resultado contribui somente nomes de campos inferidos, nunca valores. Antes da inferência manual, a pessoa revisa uma prévia do snapshot limitado que o pedido carregará; editar instrução, código, destino, conexão ou política invalida a revisão e exige nova prévia. Propostas continuam efêmeras, ligadas à revisão de origem, com diff revalidado, confirmação explícita e undo. Ver [ADR-052](10-decisoes-arquiteturais.md#adr-052--consentimento-local-e-prévia-do-contexto-de-chat-22092026).

`ConnectionProfile.LocalAiContextEnabled` é aditivo (padrão `true` para preservar a preferência histórica por conexão); `AutocompleteSettings.LocalAiContextEnabled` é o opt-in global desligado por padrão, então uma instalação antiga não passa a enviar contexto automaticamente.

**Migração:** a lista de caminhos conhecidos desta máquina (`F:\models\…`, `C:\KapibaraStudio.MongoAI-artifacts\…`) e a sugestão automática de caminho foram removidas. Um `ModelPath` já salvo continua funcionando como pasta externa. Para usar o catálogo, informe o diretório pai (por exemplo `F:\models`) e escolha a pasta.

## Validação

Validação estrutural, sem carregar pesos. Uma pasta inválida nunca impede a listagem ou o carregamento das demais.

| Resultado | Quando |
| --- | --- |
| `Valid` | `genai_config.json` legível, decoder contido na pasta, `tokenizer.json`/`tokenizer_config.json` legíveis e aceitos por um adapter |
| `MissingFiles` | Falta `genai_config.json`, o decoder, um arquivo do tokenizer ou os pesos externos exigidos (`model.onnx.data` do DeepSeek); a mensagem lista os arquivos |
| `Unsupported` | Nenhum adapter aceita a arquitetura, ou o tokenizer não tem os tokens FIM exigidos |
| `Invalid` | JSON malformado, decoder fora da pasta, manifesto com IDs incorretos ou `kapibarastudio-model.json` inválido |

Diferenças de arquitetura ficam em `IModelAdapter` (`Infrastructure.LocalAi` desde a ADR-040, 17/09/2026; antes em `Infrastructure`): validação específica, tokenizer, prompt e tokens de parada. Hoje existem `QwenCoderModelAdapter` (`qwen2`) e `DeepSeekCoderModelAdapter` (`llama` com `slopcoder_manifest.json`). Cada modelo usa sempre o próprio tokenizer. Uma nova família é um novo adapter, não um `if` espalhado.

## Metadata opcional

`kapibarastudio-model.json` na pasta do modelo. Todos os campos são opcionais; o arquivo inteiro também. Nomes desconhecidos de capacidade ou hardware são ignorados; tipos errados ou valores fora dos limites tornam a pasta `Invalid`.

```json
{
  "name": "SlopCoder Mongo 1.5B",
  "version": "1.0.0",
  "architecture": "qwen2",
  "parameters": "1.5B",
  "domain": ["mongodb", "json", "atlas-search"],
  "capabilities": ["autocomplete", "chat", "fim"],
  "hardware": ["cpu", "gpu"],
  "recommendedContextTokens": 2048,
  "recommendedCompletionTokens": 128,
  "generation": {
    "autocomplete": { "maxTokens": 64, "temperature": 0.1 },
    "chat": { "maxTokens": 512, "temperature": 0.3 }
  }
}
```

| Campo | Efeito |
| --- | --- |
| `name` (≤ 128) | Nome exibido; a identidade continua sendo a pasta |
| `capabilities` | `autocomplete`, `chat`, `fim`, `embeddings`. Sem o campo: autocomplete, chat e FIM (contrato atual) |
| `hardware` | `cpu`, `gpu`, `npu` suportados pela exportação. Automático ignora os demais; escolha explícita incompatível falha com mensagem |
| `recommendedContextTokens` (64–8192), `recommendedCompletionTokens` (1–256) | Preenchem os campos da tela quando o usuário escolhe o modelo; preferências salvas não são sobrescritas ao abrir |
| `generation.chat.maxTokens` (1–1024) | Teto de geração do chat; padrão 256 |
| `generation.*.temperature` (0–2) | 0 mantém decodificação gulosa (padrão); acima de 0 ativa amostragem |
| `version`, `architecture`, `parameters`, `domain` | Informativos na tela |

Configuração específica de uma máquina (diretório, hardware escolhido) nunca fica no metadata.

## Hardware

`OnnxHardwareProbe` consulta o ONNX Runtime carregado pelo processo (`GetAvailableProviders` e `GetEpDevices`). A tela mostra somente o que ele reporta e desabilita as opções ausentes, por exemplo:

```text
CPU — AMD Ryzen 9 7900 12-Core Processor
GPU — AMD Radeon RX 7800 XT (DirectML, 15,8 GB)
NPU — indisponível
```

| Tipo | Providers configuráveis | Distribuição |
| --- | --- | --- |
| CPU | CPUExecutionProvider | Todos os builds |
| GPU | DirectML (`dml`), CUDA (`cuda`) | WinML (DirectML) e Cuda |
| NPU | QNN, OpenVINO (dispositivo NPU), VitisAI | Nenhum build atual inclui; aparecem se o runtime os expuser |

Com várias GPUs, DirectML usa o adaptador 0 do DXGI e é esse o dispositivo informado. Não há escolha de adaptador.

**Automático:** tenta NPU, GPU e CPU, nessa ordem, entre os dispositivos disponíveis e compatíveis com o `hardware` do metadata. Falha ao carregar passa ao próximo; falha nativa na geração com acelerador recarrega em CPU e mantém a sessão CPU. O provider efetivo e o fallback ficam no status e no diagnóstico local (`provider.fallback`, `model.ready`).

**CPU, GPU ou NPU explícitos:** somente aquele backend. Indisponibilidade ou falha (na carga ou na geração) não recorre à CPU:

```text
Não foi possível executar este modelo utilizando GPU.
Motivo: DirectML provider unavailable.
Você pode selecionar: Automático ou CPU.
```

A opção avançada persistida `ExecutionProvider` (DirectML, CUDA, QNN, OpenVINO) segue a mesma regra: exatamente aquele provider.

## Serviço central

`ILocalAiModelService` (Application), implementado por `LocalAiModelService`, é compartilhado por autocomplete, chat e Preferências:

| Operação | Comportamento |
| --- | --- |
| `DiscoverModelsAsync`, `ValidateModelAsync` | Catálogo; não alteram o modelo carregado |
| `GetAvailableHardwareAsync` | Resultado da sonda, calculado uma vez por processo |
| `LoadModelAsync`, `UnloadModelAsync` | Carga/descarga explícitas |
| `SwitchModelAsync` | Aplicado ao salvar: cancela gerações; descarrega só se pasta ou hardware mudaram, ou se o autocomplete foi desabilitado/Básico |
| `GenerateAsync` | Carga sob demanda, verificação de capacidade, fila com prioridade, cancelamento por chamada |
| `CancelGeneration`, `GetCapabilities`, `TestModelAsync` | Cancelamento da geração ativa, capacidades do modelo carregado, teste completo |

- **Um modelo por vez.** Pedir outro modelo (troca de seleção, ou chat com `ChatModel` diferente) cancela gerações, descarta sessão, tokenizer e provider anteriores, e só então carrega o novo.
- **Sob demanda.** O startup só lê preferências. A primeira chamada de IA valida e carrega. Modelo, tokenizer, configuração de geração, provider e sessão são reutilizados; nenhuma sessão é recriada por sugestão.
- **Carga desacoplada.** A carga não usa o token do editor: continuar digitando cancela a espera daquela sugestão, não o carregamento. A carga pode ser cancelada pela barra de atividades.
- **Concorrência.** Uma geração por vez. Chat e teste (`Interactive`) entram antes do autocomplete (`Background`) na fila; um autocomplete em andamento é interrompido quando uma ação explícita chega e devolve "sem sugestão", sem descarregar o modelo.
- **Falhas.** Falha de modelo ou provider: status visível, descarga e nova tentativa após 30 s (ou imediatamente ao salvar/testar). Contexto maior que a janela é erro do pedido: o modelo continua carregado. Uma parada imediata do modelo gera sugestão vazia, não falha.
- **Capacidades.** Autocomplete exige `autocomplete` ou `fim`; chat exige `chat`. Sem a capacidade, o chat informa "Chat indisponível: o modelo X não declara a capacidade chat" e o autocomplete segue no dicionário.

Autocomplete continua: dicionário/schema/vocabulário primeiro; IA quando insuficiente; básico em qualquer indisponibilidade. O chat usa IA local quando o modo não é Básico, `ChatEnabled` está ligado e há modelo selecionado; caso contrário informa que usa a transformação determinística. Desabilitar só o autocomplete não desliga o chat.

## Barra de atividades e estado

A carga aparece na barra inferior global: "Validando modelo X…", "Carregando X…", "Inicializando GPU (DirectML) — X…" e, ao final, "Modelo carregado — GPU", falha ou cancelamento. A UI não bloqueia: validação, sonda e carga rodam fora do dispatcher.

O estado em Preferências mostra modelo selecionado, modelo em uso (se diferente e não salvo), estado, hardware pedido e, antes da carga, o provider e o dispositivo previstos. Depois da carga: backend, provider, dispositivo, tempo de carregamento, memória do processo e, após gerar, primeiro token e tokens/s.

Métricas medidas: tempo de criação da sessão com o provider efetivo, working set do processo após a carga (não é VRAM nem memória exclusiva do modelo), tempo até o primeiro token e taxa de decodificação. VRAM não é medida. Métricas indisponíveis são omitidas.

## Testar modelo

Salva as preferências, descarrega a sessão atual e executa: pasta e arquivos → tokenizer → sessão ONNX e provider → geração de até 8 tokens para `db.Users.find({`. O objetivo é funcionamento, não qualidade. O resultado lista etapas (OK/falhou), modelo, hardware, provider, dispositivo, carregamento, primeiro token e tokens/s, omitindo o que não foi medido.

## Limites e pendências

- Sem `FileSystemWatcher` e sem "Adicionar ao catálogo"; **Atualizar** é o mecanismo do MVP.
- embeddings apenas como capacidade declarável.
- NPU preparado na seleção e na sonda, mas nenhum build distribui QNN/OpenVINO/VitisAI e nada foi executado em NPU.
- Uma exportação carregar em DirectML não prova geração correta em GPU; homologar por pacote (evidência abaixo).
- Metadata não é verificação de integridade ou licença dos pesos.

## Evidência — 13/09/2026

Build sem restore: 0 avisos, 0 erros. Suíte regular: **564 aprovados, 0 falhas, 2 ignorados** (`multimodel-regular.trx` em TestResults do projeto de testes). `LocalAiModelServiceTests` cobre: descoberta com nomes de pasta, metadata, `MissingFiles`/`Invalid`/`Unsupported` isolados; nomes de pasta que não escapam do diretório; resolução de modelo de chat separado; ordem do Automático e compatibilidade declarada; hardware explícito sem fallback e mensagem; sonda só com providers reportados; texto de estado; troca liberando o modelo anterior antes do próximo, sem descarga ao atualizar catálogo ou mudar orçamentos; chat recusado sem capacidade; chat interrompendo autocomplete sem recarga; carga não abortada pelo editor; barra de atividades; teste de modelo com etapas e métricas; falha de provider sem geração; persistência do nome da pasta e seleção preservada ao atualizar. Testes existentes de autocomplete, chat, cache, cancelamento, privacidade e UI continuam aprovados.

Execução real `Explicit` nesta máquina (Windows, build WinML, AMD Ryzen 9 7900, AMD Radeon RX 7800 XT), `SLOP_QWEN_MODEL=F:\models\SlopCoder-Mongo-1.5B-full-ONNX-INT8` (`multimodel-real.trx`):

| Teste | Resultado |
| --- | --- |
| Sonda de hardware | CPU — AMD Ryzen 9 7900; GPU — AMD Radeon RX 7800 XT (DirectML, 15,8 GB); NPU — indisponível |
| Testar modelo, CPU | Aprovado: carga 2.561 ms, primeiro token 42 ms, 33 tokens/s |
| Testar modelo, GPU explícita | **Falhou, como esperado para este pacote**: sessão DirectML criada (2.452 ms), geração falhou em `DmlFusedNode_0_0`; relatório com motivo e "Automático ou CPU", sem recorrer à CPU |
| Testar modelo, Automático | Aprovado em CPU após a falha DirectML: "aceleração preferida indisponível", 33 tokens/s |
| `RealQwenGeneratesWithCpuAndReusesNativeSession` | Aprovado: `a + b` duas vezes na mesma sessão, cancelamento e recuperação |

Conclusão: a exportação INT8 do SlopCoder-Mongo-1.5B **não gera em DirectML** nesta GPU; CPU é o hardware homologado para ela. GPU funcional depende de uma exportação compatível com DirectML. Não homologados: NPU, CUDA, Linux, uso interativo com MongoDB real e a janela com diálogos nativos; a UI foi verificada por testes Headless e PNGs.
