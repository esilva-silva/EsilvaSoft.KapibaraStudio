# KS-US03-T02 — Implementar a sessão no runtime ONNX

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US03 — Reutilizar o estado de geração entre os passos do turno](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Implementar a porta de sessão em `OnnxLocalModelRuntime`, mantendo um gerador vivo durante o turno.

**Depende de:** [KS-US03-T01 — Definir a porta de sessão de geração](task-01-porta-de-sessao.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [ ] Confirmar na versão fixada do GenAI as operações de anexar tokens e de voltar a posição, com um smoke no KapiLab antes de depender delas.
- [ ] Implementar a sessão respeitando a regra de EOS: consumir token apenas se a sequência cresceu e preservar o último token válido.
- [ ] Manter a serialização do serviço e liberar o gerador ao fim do turno, no cancelamento e na troca de modelo, sem dispose concorrente.
- [ ] Não registrar texto nem identidade em métricas.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Sessão funciona em CPU com o pacote de referência; fechar aba, cancelar e trocar modelo liberam o gerador.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** provas com modelo real são Explicit; o CI usa apenas o falso.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Preencher ao executar; nenhum resultado novo é declarado neste arquivo.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | A registrar |
| Data, responsável, ambiente e versões | A registrar |
| Cenários, comandos, esperado e observado | A registrar |
| Resultado por critério | Pendente |
| Evidência oficial e artefatos sanitizados | A vincular em docs |
| Limitações e bloqueios | A registrar com impacto, recurso faltante e próximo passo |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
