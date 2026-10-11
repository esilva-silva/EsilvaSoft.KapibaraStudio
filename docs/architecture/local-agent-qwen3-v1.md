# Contrato de prompt do agente local — v1

O contrato público `local-agent-qwen3-v1` prepara trajetórias da Fase 04 externa para o runtime existente do EsilvaSoft.KapibaraStudio. A referência é a IDE com GenAI **0.15.2**, não a serialização original do Hugging Face. Decisões Q2/Q3/Q5/Q6 aprovadas pelo usuário em 11/10/2026; [ADR-072](../10-decisoes-arquiteturais.md#adr-072--contrato-de-prompt-do-agente-local-11102026).

`LocalAgentPromptBuilder` é público e puro. A sessão continua capturando contexto, filtrando tools pelo plano/registry, serializando turnos pelo gate existente e mantendo raciocínio transitório. O builder não concede permissões, consulta MongoDB nem executa tools. O adapter continua aplicando o template do pacote; o fallback é somente um espelho fixo para comparação offline, sem fallback silencioso no produto.

## Bytes e histórico

- Sistema em pt-BR: texto existente mais `\n` e extra não vazio; acima de 2.048 bytes UTF-8 usa a versão curta existente.
- Usuário: `System.Text.Json` padrão, com campos ordenados `message`, `context`, `attachments`; cada anexo contém `kind`, `name`, `content`. Mantém escapes Unicode/HTML do encoder .NET, limite de 65.536 caracteres e 32 anexos.
- Tools: schemas do registry sem `$schema`/`$id`; envelope `type/function`, campos `name/description/parameters`. Descrição vem de `AgentToolDescriptions`, em inglês e até 200 caracteres para as tools do produto; nomes desconhecidos preservam o nome. `catalog export` usa a mesma tabela, e `catalog check` detecta mudanças na descrição. A tabela não muda os prompts dos providers externos.
- Mensagens enviadas ao template têm somente `role`/`content`. Assistant conserva os envelopes textuais `<tool_call>`, sem `tool_calls`/`reasoning_content` estruturados.
- **Sequência efetiva preservada:** `BuildMessages` começa com o sistema e o histórico. Reanexa o usuário atual se a última mensagem não for esse mesmo usuário. Assim, após tool/assistant/force-close, o próximo passo contém novamente o usuário. O Python precisa preservar isso.
- Histórico entre turnos é fornecido já sem raciocínio pela sessão. Durante um turno, o force-close pode existir no histórico; o template nativo conserva o conteúdo bruto e não separa `reasoning_content`. O builder não remove raciocínio indiscriminadamente de passos em andamento.
- Resultado de tool: `status/result/error/truncated`, com limite UTF-8 por rune. Erro de envelope: `{"error":"InvalidToolCallFormat"}`, um retry existente.
- Raciocínio desligado: o runtime aplica o template com generation prompt e depois acrescenta o `disabledGenerationSuffix` do pacote. Exemplo/goldens usam `<think>\n\n</think>\n\n`; não se passa `enable_thinking=false` ao template nativo.
- `ForceCloseText` continua sendo o do pacote. O exemplo adota `Considering the limited time by the user, I have to give the solution based on the thinking directly now.\n</think>\n\n`, sem regravar pacotes existentes.

## Divergência nativa descoberta

`Tokenizer.ApplyChatTemplate` do GenAI 0.15.2 transforma tools **antes** do Jinja/minja. O envelope `type/function` vira um objeto `name/description/parameters`. `parameters.properties` vira o mapa de parâmetros; cada parâmetro conserva somente `type` e `description`, nessa ordem. `string` vira `str`; demais tipos textuais permanecem; um `type` não textual vira `str`. Objetos aninhados, `required`, `enum`, limites e `items` não chegam ao template. Um parâmetro sem `type` não recebe tipo sintético.

Outra diferença comprovada: o template nativo conserva `<think>...</think>` bruto em `content`; o HF tenta extrair `reasoning_content`. O espelho Python fornece `reasoning_content=""` apenas na chamada ao template HF, para impedir essa extração; esse campo não é serializado para a IDE nem faz parte do contrato de entrada. O fallback C# conserva o texto. A sessão continua removendo mensagens de raciocínio no fim do turno.

Exemplo observado com sonda nativa sintética:

```json
{"name": "fixture_metadata_0", "description": "Read synthetic metadata fixture 0.", "parameters": {"collection": {"type": "str"}, "options": {"type": "object"}}}
```

Portanto aplicar o template HF ao envelope original produz tokens diferentes. O script de paridade reproduz essa transformação **somente no espelho de renderização** e depois chama `AutoTokenizer.apply_chat_template`; schemas do registry e do catálogo permanecem completos. Não corrigir essa diferença mudando produção ou relaxando o golden. Se a versão GenAI/template mudar, repetir a prova nativa e versionar o contrato quando os bytes mudarem.

## Metadata, fixtures e evidência

Pacotes novos declaram `agentContract: "local-agent-qwen3-v1"`, independente de `contextContract: "editor-context-v1"`. O leitor é aditivo; `model validate --strict-kapi` exige o contrato conhecido em pacotes com bloco `agent`. Pacotes antigos continuam utilizáveis pelo runtime, embora não satisfaçam esse novo gate estrito. Exemplo: [qwen3-agent-v1.example.json](../models/qwen3-agent-v1.example.json); seu hash de template refere-se ao template Qwen3-8B local usado nesta prova, e deve ser recalculado no pacote exportado.

Os 200 diretórios em `tools/KapiLab.Tests/Fixtures/local-agent-qwen3-v1` contêm `input.json` (envelope JSONL de um turno), `rendered.txt` UTF-8 sem BOM e `tokens.json` com IDs Qwen3. `turns.jsonl` é o corpus equivalente. `.gitattributes` impede conversão CRLF desses bytes. `reference.json` identifica os hashes do tokenizer/config, sem pesos.

O script `scripts/lab/parity_local_agent_qwen3.py` não acessa rede, não carrega pesos e não regrava fixtures durante validação. `--generate-fixtures` é autoria explícita, usada inicialmente para fixar os casos e ajustar o espelho à transformação nativa observada. O teste de equivalência preserva a montagem pré-refatoração e compara bytes/tokens com descrições antigas, isolando a alteração Q2.

Testes nativos são `Explicit`, habilitados por `KAPI_QWEN3_TOKENIZER_PACKAGE`. O teste de `RewindTo` compara a sequência após reanexo com um generator novo e com a tokenização do zero. A prova pode usar um carrier sintético com tokenizer/template Qwen3 e decoder Qwen2.5 apenas para contrato/sequência; isso **não homologa** geração Qwen3/T4, qualidade, logits/KV equivalente, tools reais, GPU/NPU ou task success. O runtime da IDE ainda não reutiliza KV entre esses pedidos; o teste verifica a API nativa, sem introduzir cache de sessão.

Aceite executado e limitações ficam em [aceite do PR 2](../phases/phase-07b-kapilab/aceite-pr-2.md). PC1 originalmente cobre o contrato FIM legado; a nova captura Qwen3 complementa esse item e não substitui seu escopo original. F7B-18 também inclui um loop experimental, fora deste PR.

A tokenização também aceita o envelope original de um turno, para casos inéditos sem exportação textual. Recusa templates cujos tokens nativos divirjam do fallback correspondente antes de gravar o snapshot; não pode atribuir hashes de um template a tokens de outro.
