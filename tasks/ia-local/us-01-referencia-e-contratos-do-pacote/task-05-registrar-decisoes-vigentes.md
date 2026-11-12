# KS-US01-T05 — Registrar as decisões já tomadas no código

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E01.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Documentar nas decisões vigentes o que o código já faz e que o plano original recomendava de outra forma.

**Depende de:** nenhuma tarefa anterior desta árvore.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Decisões vigentes](../../../docs/MVP/08-decisoes-vigentes.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)

## Execução

- [ ] Registrar a exposição do `LocalAgentProvider` na composição de Release e sua indisponibilidade quando não há pacote válido.
- [ ] Registrar a retenção opcional de raciocínio (`AgentReasoningPreference` com persistência e dias) e seu padrão.
- [ ] Registrar que o destino local recebe as tools de resultados e diagnósticos apenas com opt-in de documentos e que um modelo é carregado por vez.
- [ ] Conferir coerência com o contrato de IA local, o guia e o design system; não alterar autenticação nem permissões.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada decisão tem registro com alcance e limite; nenhum documento contradiz o comportamento do código.

- **Automatizável:** trabalho documental: `node scripts/check-docs-reader.cjs` e `git diff --check`; não alegar execução das suítes do produto.
- **Real/manual:** não aplicável; decisão que o usuário queira reverter vira tarefa de código própria.
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
