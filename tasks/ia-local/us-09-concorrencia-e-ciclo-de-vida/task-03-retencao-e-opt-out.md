# KS-US09-T03 — Validar retenção e opt-out

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US09 — Garantir concorrência, liberação e recuperação](README.md). **Requisitos relacionados:** MVP-011, MVP-012, MVP-013. **Entregas oficiais:** F02-E04.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Validar que conversas, raciocínio e rascunhos seguem as preferências de retenção e opt-out.

**Depende de:** [KS-US05-T03 — Adicionar o canário de privacidade do raciocínio](../us-05-tools-permissoes-e-privacidade/task-03-canario-de-privacidade-do-raciocinio.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [ ] Validar opt-out geral e por conexão para rascunhos e opt-in para entrada JSON.
- [ ] Validar retenção e expurgo do raciocínio conforme dias configurados.
- [ ] Conferir que resultados, diagnósticos e credenciais não entram em sessão, contexto serializado ou log.
- [ ] Usar o proprietário LiteDB registrado em DI; migração apenas aditiva e versionada.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada preferência tem teste de respeito e de expurgo; nenhuma segunda conexão ao arquivo local.

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
