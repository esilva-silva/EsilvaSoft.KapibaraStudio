# Configuração e compatibilidade

> **Definição vigente — 27/10/2026 ([DEC-AI-SPLIT](decisions.md#dec-ai-split)).** O autocomplete é dividido em dois:
> o **autocomplete normal** (determinístico: catálogo, schema, dicionário) sugere sempre em texto fantasma enquanto o
> usuário digita e abre mais opções com `Ctrl+Espaço`; o **autocomplete com IA** (modelo local) só envia texto ao
> modelo quando o usuário aperta `Ctrl+.`. A IA nunca participa da digitação. As linhas da tabela e da precedência
> abaixo já refletem essa definição; os parágrafos de "Estado de implementação" anteriores são registro histórico.

Especificação de 15/09/2026; os campos de orçamento e hardware descritos nesta seção estão implementados de forma aditiva em `AutocompleteSettings`, mantendo versão 1, validação e o proprietário LiteDB. Não criar árvore de preferências paralela.

## Opções necessárias

| Conceito | Campo real/proposto | Default novo | Justificativa |
| --- | --- | --- | --- |
| Autocomplete normal | `Enabled` existente | true | Ghost determinístico ao digitar + lista `Ctrl+Espaço`; não afeta IA nem chat |
| Tradicional explícito | `TraditionalEnabled` | true | Sem controle visual; governa só o fallback da IA |
| Autocomplete com IA | `AiCompletionEnabled` novo (27/10/2026) | ausente = `Mode != Basic` | Caixa própria; só `Ctrl+.` envia ao modelo; independente de `Enabled` |
| Modo legado | `Mode` existente | Automatic | Sem controle visual; só deriva `AiCompletionEnabled` ausente e o agente legado (`Basic` desliga o agente). A tela grava `Automatic` |
| Preemptivo geral/tradicional/IA | `InlineEnabled`, `InlineUseTraditional`, `InlineUseAi` | — | **Legados, sem efeito desde 27/10/2026**: lidos e preservados para compatibilidade de sessão |
| Dicionário legado | `UseDictionary` existente | true | Fallback lexical do ghost e da lista; não controla o catálogo inteiro |
| Atraso do fallback | `DelayMilliseconds` existente | 150 ms | Validar 50–2000; o determinístico é imediato, só o fallback do dicionário espera |
| Janela / saída IA | `ContextTokens`, `MaximumCompletionTokens` existentes | 2048 / 32 | ComboBox editável; limites do modelo vencem os limites conservadores e a estimativa de memória orienta sugestões |
| Modelo / hardware | `SelectedModel`, `ModelPath`, `Acceleration`, `ExecutionProvider` existentes | Preservados | Não criar PreferredModel/PreferredProvider equivalentes |
| Timeout IA explícita | `AiTimeoutMilliseconds` novo | 10000 | Limite de espera incluindo fila; faixa proposta 1000–60000 |
| Lista ao digitar | `CompletionAutoOpenOnTrigger` novo | false | Não confundir popup automático com ghost tradicional |
| Enter na lista | `CompletionEnterAccepts` novo | true | Preferência justificada pela navegação |
| Aceite parcial | `IncrementalTab` existente | true | Mesmo comportamento para ambos os ghosts |
| Amostra | `WorkspacePreferences.SchemaSamplingProfileIds` existente | vazio | Opt-in por conexão; sem campo duplicado em AutocompleteSettings |

Delay adaptativo, pesos, margem de confiança, número de candidatos, linhas do ghost e orçamento total automático são constantes internas em `LanguageServiceOptions`, medidas antes de expor controles. Não acrescentar opções de ociosidade, warmup, alternativas ou Ctrl+→ nesta entrega.

**Estado de implementação — lote A43 (19/09/2026):** `AiTimeoutMilliseconds` e `TraditionalEnabled` existem em
`AutocompleteSettings`, de forma aditiva — o primeiro como inteiro validado em 1 000–60 000 ms (ausente = 10 000), o
segundo no mesmo formato anulável das flags inline (ausente = true). `AiTimeoutMilliseconds` já governa o prazo
rígido da IA explícita ([DEC-A43-TIMEOUT](decisions.md#dec-a43-timeout)). `TraditionalEnabled` já governa o
**fallback** da IA explícita ([DEC-A43-TRADITIONAL](decisions.md#dec-a43-traditional)), mas ainda **não** bloqueia o
`Ctrl+Espaço` pedido diretamente: a linha `traditional.manual` da precedência abaixo continua pendente em T08.
Nenhum dos dois tem controle visual em `AutocompleteSettingsWindow`.

**Estado de implementação — lote de 18/09/2026 (5.1):** `InlineEnabled`, `InlineUseTraditional` e `InlineUseAi` existem como propriedades anuláveis de `AutocompleteSettings` e são lidas/gravadas pelo `InlineCompletionCoordinator`/`InlineCompletionPolicy`, com a migração descrita abaixo. **Nenhuma das três tem controle visual em `AutocompleteSettingsWindow`**: só são editáveis por quem altera o JSON persistido diretamente. A UI para essas flags é pendência de implementação, não apenas de homologação.

## Precedência

Vigente desde 27/10/2026 (`InlineCompletionPolicy` é a única fonte):

```text
traditional.manual = Enabled                       (Ctrl+Espaço)
traditional.inline = Enabled                       (ghost; sempre ligado com o autocomplete normal)
ai.explicit        = AiCompletionEnabled           (Ctrl+.; único caminho do editor que pode carregar o modelo)
ai.inline          = false                         (a IA nunca participa da digitação)
modelo em uso      = AiCompletionEnabled || (ChatEnabled && Mode != Basic)
```

Histórico (18/09/2026, substituído): `traditional.inline = Enabled && InlineEnabled && InlineUseTraditional`;
`ai.inline = Enabled && InlineEnabled && InlineUseAi && Mode != Basic && LoadedOnly`; `ai.manual = Enabled && Mode != Basic`.

Com `AiCompletionEnabled = false`, `Ctrl+.` abre a lista do autocomplete normal com o motivo e nunca consulta o modelo. Tradicional inline não depende de TraditionalEnabled; desligar lista não elimina ghost. `UseDictionary` não impede sugestões contextuais explícitas. Se fallback de IA encontra tradicional explícito desligado, informar indisponibilidade sem abrir lista ou mudar preferência. Opções desabilitadas conservam valor salvo.

## Migração

- **27/10/2026:** `AiCompletionEnabled` ausente deriva de `Mode != Basic` (Básico era "sem IA"). Ao salvar a tela, o valor é gravado explícito, `Mode` é gravado `Automatic` e a caixa do Agente IA reflete `ChatEnabled && Mode != Basic`, de modo que uma sessão em Básico não religa IA nem agente. `InlineEnabled`/`InlineUseTraditional`/`InlineUseAi` são lidos e preservados, mas ignorados. O padrão da IA passou de `Ctrl+;` para `Ctrl+.`; um override salvo que já usava `Ctrl+.` em outro comando continua vencendo (`EditorKeyBindings.YieldingDefaults`), e a IA fica sem atalho até o usuário escolher outro.
- Documento v1 antigo sem flags: preservar Enabled/Mode e derivar InlineUseTraditional de UseDictionary. **InlineUseAi ausente é false também na migração** (decisão de 18/09/2026, W3): derivá-lo de Mode != Basic, como previa a redação anterior, ligaria inferência automática para praticamente toda instalação existente, o oposto do opt-in exigido pela meta; a IA explícita continua disponível e inalterada. Instalação nova usa false. Aviso textual nas preferências explica o estado efetivo (pendente).
- Distinguir **campo ausente de false explícito** no DTO de leitura ou por presença JSON; não inferir migração apenas pelo inicializador da propriedade. `TraditionalEnabled` ausente = true, `InlineEnabled` ausente = true.
- Modo Ai legado já permite dicionário; não reinterpretá-lo como exclusão do tradicional.
- Opções UseEditorContext/UseResultPanelContext/UseInputPanelContext continuam governando contexto transitório. Persistir entrada JSON permanece opt-in separado; habilitar contexto não autoriza salvar Input.
- Mesmas regras de falha: configuração ilegível não vira sessão vazia; erro de gravação fica visível e preserva estado. Nenhum resultado, prompt, credencial ou cache entra nos rascunhos.
- Atalhos em `EditorKeyBindings` aditivo, sem tela nova: implementado em W0 (18/09/2026) com `Ctrl+Espaço` (básico), `Ctrl+;` (IA explícita até 27/10/2026, quando passou a `Ctrl+.`), `Tab`/`Shift+Tab`/`Enter`/`Esc` por escopo. `Ctrl+.` saiu dos padrões; um override explícito já salvo continua legível e não é reescrito. Validar conflitos/layouts conforme [editor](editor-integration.md#atalhos).

## Aceite

Testar v1 ausente, v1 com false explícito, Enabled=false, Mode=Basic/Ai/Automatic, UseDictionary=false, quatro combinações de flags inline, ausência/troca de modelo, opt-outs por conexão, round-trip, versão inválida e falha de persistência. Fakes comprovam zero geração/carga quando inelegível. [Tarefa T08](execution-plan.md).


## Schema Learning

Adicionar SchemaLearningEnabled e SchemaLearningPersistenceEnabled (true por padrão nesta funcionalidade solicitada), mais exclusões por ProfileId; são preferências de fonte, independentes dos quatro providers. UseResultPanelContext=false impede nova coleta dos resultados. Respeitar política geral/por conexão aplicável; persistência de Input permanece separada. Desligar coleta/persistência não apaga dados já gravados; ação Limpar aprendizado explícita e erro de gravação visível. Conferir revisão da política antes de enfileirar e novamente no commit. [Migração, retenção e privacidade](schema-learning.md).
