# KS-US03-T01 — Definir a porta de sessão de geração

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US03 — Reutilizar o estado de geração entre os passos do turno](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Definir em `LocalAi.Core` a abstração de sessão de geração que o provider usará, sem expor tipos do runtime.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](../us-02-robustez-do-laco-do-agente/task-01-testes-do-ciclo-com-runtime-falso.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)

## Execução

- [ ] Definir operações de iniciar com prompt, anexar tokens ou texto renderizado, gerar em streaming, voltar a um ponto anterior e liberar.
- [ ] Fixar as regras: uma sessão por turno, nunca compartilhada entre abas, sem serialização de KV, tokens ou texto.
- [ ] Declarar como o runtime informa que não suporta sessão, para o provider cair no caminho atual.
- [ ] Cobrir o contrato com um falso em memória.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Contrato compilado e testado com falso; nenhuma dependência de ONNX em `LocalAi.Core`.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
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
