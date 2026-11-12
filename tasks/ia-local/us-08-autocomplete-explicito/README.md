# KS-US08 — Concluir o autocomplete explícito com o pacote v4

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-008, MVP-012. **Entregas oficiais:** F02-E02.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que escreve consultas no editor, **quero** pedir uma sugestão de IA com `Ctrl+;` e decidir se aceito, **para** ter ajuda sem que o editor gere nada enquanto digito.

## Contexto verificado

- `editor-context-v2` e o fechamento de delimitadores (`CompletionOutputProcessor.CloseInnerDelimiters`) já existem para pacotes que os declaram.
- O orçamento do tier vem do pacote (Nano 4096/64, Small 8192/128).
- Os pacotes v4 reprovam o TTFT de CPU sem cache de prefixo; nenhum pacote tem aceite de cache.
- Produto exige `PrefixCacheMode=Reuse` e metadata `runtime.prefixCache="reuse"` em pacote homologado.

## Critérios de aceite

- [ ] `Ctrl+;` produz prévia da origem capturada; Tab aceita com Undo; Escape descarta sem inserir ponto e vírgula; resposta obsoleta é rejeitada.
- [ ] Pacote v4 funciona com contexto v2, campos do alvo e fechamento de delimitadores.
- [ ] Cache de prefixo só é habilitado para pacote que passou nos gates; caso contrário permanece desativado.
- [ ] Digitação não carrega modelo nem infere.

## Fora de escopo

- Formatos experimentais de contexto.
- Autocomplete automático.

## Dependências

- [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](../us-01-referencia-e-contratos-do-pacote/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US08-T01 — Validar o fluxo de prévia e aceite](task-01-fluxo-ctrl-ponto-e-virgula.md) | Fluxo explícito comprovado | KS-US01-T01 |
| [KS-US08-T02 — Validar contexto v2 e fechamento de delimitadores](task-02-contexto-v2-e-delimitadores.md) | Pacote v4 com contexto v2 validado | KS-US01-T02 |
| [KS-US08-T03 — Homologar ou manter desativado o cache de prefixo](task-03-cache-de-prefixo-por-pacote.md) | Decisão de cache por pacote | KS-US08-T01, KT-US08-T03 (modelos) |
| [KS-US08-T04 — Guardar que digitar não infere](task-04-digitacao-nao-infere.md) | Guarda automatizada | — |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
