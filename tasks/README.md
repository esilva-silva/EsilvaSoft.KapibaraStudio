# Tarefas para concluir o MVP

Plano aprovado em 06/11/2026 para o **EsilvaSoft.KapibaraStudio**, desktop .NET 10/Avalonia, Windows/Linux, pt-BR e MIT. Versões v0.10.0–v0.15.0 são alvos documentais; não alteram binários, tags, releases ou evidências. v1.0.0 fica fora deste plano.

## Como executar

- [Documentação oficial](../docs/README.md), [fases](../docs/MVP/fases/README.md) e [catálogo MVP](../docs/MVP/02-requisitos-e-estado.md) definem escopo, contratos e estado dos requisitos. Estas tarefas organizam a execução, sem criar outra fonte normativa.
- Execute na ordem das dependências explícitas. Preparação de fase posterior pode avançar com suas dependências comprovadas, mas não fecha critérios obrigatórios pendentes da anterior. IA local CPU precede Copilot.
- As 30 novas tarefas começam abertas. Marque apenas o que executar e comprovar; evidência histórica mantém data/snapshot/alcance. Bloqueio deve indicar cenário, ambiente/recurso faltante, impacto e próximo passo.
- Registre evidências sanitizadas na árvore oficial `docs` e vincule-as à tarefa, à entrega da fase e ao catálogo quando o estado do requisito mudar. Não duplique estados de requisitos nos índices de tarefas.
- Reaproveite implementação existente; não reimplemente funcionalidades aceitas sem regressão. Mudança de comportamento exige atualizar guia, design system e contratos/decisões afetados.

A [Task-R 1](Task-R%201%20-%20Limpeza%20e%20remoção%20de%20código.md) permanece no caminho original: limpeza implementada e validação automatizada registradas, com homologação real/nativa ainda distinta. As novas tarefas da fase 01 reconciliam e completam lacunas; não reabrem os cortes concluídos.

## Regras comuns de validação

Aplicar [AGENTS.md](../AGENTS.md) e o [procedimento de evidências](../docs/MVP/06-validacao-e-evidencias.md). Toda execução começa identificando checkout e alterações preexistentes. Testes relevantes por alteração e checks finais devem registrar comandos e resultados, incluindo ignorados. Para alterações de código, usar:

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

Em isolamento que bloqueie telemetria Avalonia, registrar `-p:UsedAvaloniaProducts=`. Preservar backend/lock consistentes. Alterações apenas documentais exigem `node scripts/check-docs-reader.cjs` e `git diff --check`, sem declarar execução das suítes do produto.

- Sessão/contexto: testar falha, concorrência e recuperação, com proprietário LiteDB único e migração aditiva/versionada; sessão ilegível nunca é substituída por vazia.
- UI: inspecionar PNGs reais conforme [design system](../docs/MVP/04-design-system.md). Não alterar goldens/asserções para esconder regressões. Headless não homologa desktop nativo, leitor de tela ou diálogos.
- CI Windows/Linux roda somente UnitTests e Infrastructure.Agents.Tests. IntegrationTests e guardas de fronteira são manuais antes de push/release; executar localmente a guarda RealModelHubAccessIsConfinedToExplicitLocalModelTests.
- CI nunca baixa/carrega modelos. Hub/pesos reais exigem Explicit/LocalModelIntegration e opt-in manual; demais testes usam HttpMessageHandler falso. Benchmarks são manuais, nunca CI/release; ferramentas de benchmark exigem EnableBenchmarkTests=true.
- Tools de agentes nunca consultam documentos nem escrevem MongoDB. Usar somente metadados/índices permitidos e resultados humanos já capturados; autorização não amplia essa fronteira. Testes MongoDB reais usam ambiente descartável e execução humana, não tools de agentes proibidas.
- Preservar BSON/UUID, confirmações, auditoria, opt-outs, origem capturada e cancelamento isolado. Providers externos e autenticação oficial existentes permanecem; não criar providers ou autenticações paralelos.

## Fase 01 — v0.10.0 — Conclusão da base

**Requisitos:** MVP-001..011. **Dependência:** Base funcional existente e cortes registrados pela Task-R 1; não repetir a limpeza.

[Índice da fase](fase-01-v0.10.0/README.md) · [Contrato de entrega](../docs/MVP/fases/fase-01-v0.10.0.md)

- [F01-T01 — Reconciliar cortes e evidências](fase-01-v0.10.0/task-01-reconciliar-cortes.md)
- [F01-T02 — Validar jornadas afetadas com MongoDB real](fase-01-v0.10.0/task-02-jornadas-mongodb.md)
- [F01-T03 — Validar recuperação legada e jornadas nativas](fase-01-v0.10.0/task-03-recuperacao-e-desktop.md)
- [F01-T04 — Corrigir regressões e fechar a base](fase-01-v0.10.0/task-04-corrigir-e-fechar-base.md)

## Fase 02 — v0.11.0 — IA local CPU

**Requisitos:** MVP-008, MVP-011..013. **Dependência:** Base e contexto seguros comprovados na fase 01; homologações não relacionadas podem continuar abertas.

[Índice da fase](fase-02-v0.11.0/README.md) · [Contrato de entrega](../docs/MVP/fases/fase-02-v0.11.0.md)

- [F02-T01 — Fixar referência e orçamentos CPU](fase-02-v0.11.0/task-01-referencia-cpu.md)
- [F02-T02 — Concluir autocomplete explícito](fase-02-v0.11.0/task-02-autocomplete-explicito.md)
- [F02-T03 — Concluir ciclo do agente local](fase-02-v0.11.0/task-03-ciclo-agente.md)
- [F02-T04 — Validar concorrência e ciclo de vida](fase-02-v0.11.0/task-04-ciclo-de-vida.md)
- [F02-T05 — Avaliar modelo real e experiência nativa](fase-02-v0.11.0/task-05-avaliacao-real.md)

## Fase 03 — v0.12.0 — GitHub Copilot

**Requisitos:** MVP-011, MVP-014. **Dependência:** IA local da fase 02 aceita nos critérios aplicáveis; reutilizar registry, sessões, permissões e adapters.

[Índice da fase](fase-03-v0.12.0/README.md) · [Contrato de entrega](../docs/MVP/fases/fase-03-v0.12.0.md)

- [F03-T01 — Reconciliar baseline e diagnosticar locks LiteDB](fase-03-v0.12.0/task-01-baseline-e-litedb.md)
- [F03-T02 — Concluir conta e runtime oficial](fase-03-v0.12.0/task-02-conta-e-runtime.md)
- [F03-T03 — Validar conversas e streaming](fase-03-v0.12.0/task-03-conversas-e-streaming.md)
- [F03-T04 — Validar sessões, persistência e recuperação](fase-03-v0.12.0/task-04-sessoes-e-recuperacao.md)
- [F03-T05 — Completar tools, permissões, propostas e arquivos](fase-03-v0.12.0/task-05-tools-e-propostas.md)
- [F03-T06 — Validar experiência e requisitos de distribuição](fase-03-v0.12.0/task-06-experiencia-e-distribuicao.md)

## Fase 04 — v0.13.0 — Instalação e atualização

**Requisitos:** MVP-011, MVP-015. **Dependência:** Contratos estabilizados e critérios aplicáveis das fases 01–03 aceitos; ambientes descartáveis disponíveis.

[Índice da fase](fase-04-v0.13.0/README.md) · [Contrato de entrega](../docs/MVP/fases/fase-04-v0.13.0.md)

- [F04-T01 — Preparar e auditar artefatos por RID](fase-04-v0.13.0/task-01-artefatos-e-auditoria.md)
- [F04-T02 — Validar atualização e recuperação Windows](fase-04-v0.13.0/task-02-windows.md)
- [F04-T03 — Validar Linux portátil](fase-04-v0.13.0/task-03-linux-portatil.md)
- [F04-T04 — Validar Linux gerenciado](fase-04-v0.13.0/task-04-linux-gerenciado.md)
- [F04-T05 — Comprovar dados, migrações e instruções](fase-04-v0.13.0/task-05-dados-e-instrucoes.md)

## Fase 05 — v0.14.0 — Homologação e polimento

**Requisitos:** MVP-001..015. **Dependência:** Entregas funcionais e atualização das fases anteriores verificadas nos critérios aplicáveis.

[Índice da fase](fase-05-v0.14.0/README.md) · [Contrato de entrega](../docs/MVP/fases/fase-05-v0.14.0.md)

- [F05-T01 — Consolidar matriz de suporte e lacunas](fase-05-v0.14.0/task-01-matriz-de-suporte.md)
- [F05-T02 — Homologar jornadas MongoDB e BSON](fase-05-v0.14.0/task-02-jornadas-e-bson.md)
- [F05-T03 — Homologar falhas, concorrência e recuperação](fase-05-v0.14.0/task-03-falhas-e-concorrencia.md)
- [F05-T04 — Validar acessibilidade e desktop nativo](fase-05-v0.14.0/task-04-acessibilidade-e-desktop.md)
- [F05-T05 — Corrigir defeitos e consolidar homologação](fase-05-v0.14.0/task-05-correcoes-e-consolidacao.md)

## Fase 06 — v0.15.0 — Aceite final do MVP

**Requisitos:** MVP-001..015. **Dependência:** Fases 01–05 com critérios obrigatórios aplicáveis aceitos e evidências reconciliadas ao candidato final.

[Índice da fase](fase-06-v0.15.0/README.md) · [Contrato de entrega](../docs/MVP/fases/fase-06-v0.15.0.md)

- [F06-T01 — Reconciliar requisitos e critérios obrigatórios](fase-06-v0.15.0/task-01-reconciliar-aceite.md)
- [F06-T02 — Executar verificações finais e guardas](fase-06-v0.15.0/task-02-verificacao-final.md)
- [F06-T03 — Auditar pacotes finais e ciclos de instalação](fase-06-v0.15.0/task-03-auditar-pacotes-finais.md)
- [F06-T04 — Concluir documentação e obter aceite](fase-06-v0.15.0/task-04-documentacao-e-aceite.md)
- [F06-T05 — Registrar decisão e procedimento de liberação](fase-06-v0.15.0/task-05-decisao-de-liberacao.md)
