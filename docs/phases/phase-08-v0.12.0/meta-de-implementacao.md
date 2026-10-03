# Meta de implementação — Fase 8 / v0.12.0

**Reorganização de 03/10/2026:** esta meta pertence agora à Fase 8 / v0.12.0, prioridade atual. IDs `F5-*` e tarefas/contagens de 22/09 permanecem históricos. O assistente por aba foi removido posteriormente (ADRs 055/056); este plano não autoriza recriá-lo. Novas entregas devem conciliar o aceite original com os contratos e a composição vigentes; homologação real pertence à Fase 10 / v0.14.0.

**Título:** IA local e produtividade contextual (Local AI and contextual productivity).
**Estado:** escopo automatizável implementado e validado em 22/09/2026; inferência com pacotes ONNX reais e homologação nativa permanecem experimentais e rastreadas na Fase 10.
**Data:** 22/09/2026.
**Coordenação:** registro histórico; os nomes de agentes especializados abaixo descrevem o plano original e não são instruções de delegação atuais. Os contratos de `/agents/` citados na época foram removidos. Para o trabalho atual de Claude, use [documentação Claude](../phase-05-v0.9.0/README.md).

## Resultado esperado

Concluir o escopo funcional automatizável de ADV-09 e da extensão preemptiva de EDT-02 no EsilvaSoft.KapibaraStudio: assistência local opcional, catálogo multimodelo, seleção de hardware disponível, sugestões inline e propostas revisáveis com diff. Preservar o autocomplete determinístico, a privacidade, o contexto por aba e a operação sem modelo instalado. Encerrar a fase somente com evidência rastreável e pendências de homologação real registradas na Fase 10.

## Ponto de partida e limites

- A [Fase 4](../phase-04-v0.8.0/README.md) está arquivada por escopo funcional. Seu aceite automatizado é a dependência já documentada; homologação nativa continua pendente.
- A [Fase 8](README.md) permanece experimental. Há runtime ONNX, catálogo, serviço compartilhado, chat com propostas e ghost text. Existência de código não equivale a aceite.
- A [subfase 5 de autocomplete](../../auto-complite/phases/phase-5-preemptive.md) já está implementada no escopo automatizado. Reutilizar e verificar essa entrega; sua numeração não significa que a versão v0.12.0 esteja concluída.
- O catálogo `ADV-09` inclui prévia da informação enviada. O consentimento configurado não substitui tornar o contexto exato visível antes da inferência.
- As evidências de modelos reais são específicas de pacote, provider e ambiente. Os documentos registram alterações extras não solicitadas em propostas de modelos FIM; tratar isso como risco conhecido de fidelidade, sem anunciar correção ainda não demonstrada.
- O trabalho consultou a documentação, auditou as trilhas existentes e executou build/testes após as alterações. Evidências históricas estão separadas da validação final abaixo.

F5-01 deve conciliar pendências históricas com o estado atual: a matriz de 18/09 registra flags inline sem controles, mas `AutocompleteSettingsWindow.axaml` já contém os bindings correspondentes e o design system descreve sua entrega. Verificar a cobertura e atualizar a rastreabilidade, sem reimplementar controles existentes. A mesma matriz registra composição IME não publicada pelo editor e o gate edição → ghost p95 ≤ 20 ms não atendido; auditar sua situação atual, corrigir a integração em F5-04 quando necessário e envolver `performance-agent` (reasoning) para medir e resolver o gate. Não confundir custo do provider com latência até apresentação, nem contabilizar validação da integração IME como homologação nativa.

Ficam fora desta meta: MCP e agentes externos da Fase 5, chat por workflow da Fase 7, chatbot genérico, execução automática de sugestões, treinamento/distribuição de pesos, obrigatoriedade de GPU, embeddings/RAG e novos backends NPU. Não ampliar o escopo para controles ou capacidades apenas porque existem campos preparados no código.

## Entregas e dependências

Os responsáveis e capacidades na matriz abaixo foram registrados no plano original de 22/09/2026. São apenas rastreabilidade histórica; os arquivos de agente correspondentes não fazem parte do checkout atual.

| ID | Entrega e aceite específico | Responsável / capacidade | Depende de |
| --- | --- | --- | --- |
| F5-01 | Auditar implementação, testes e ADRs; inventariar todas as ações expostas do assistente; mapear cada critério a teste existente ou lacuna; executar baseline e separar falhas preexistentes. | architecture-agent + qa-testing-agent / reasoning | Aceite funcional da Fase 4 |
| F5-02 | Consolidar contratos de contexto e privacidade: snapshot antes de await, revisão e origem da solicitação, opt-out geral/por conexão, Input JSON somente com opt-in; testes de vazamento e mudança de contexto. | architecture-agent + persistence-security-agent / reasoning | F5-01 |
| F5-03 | Fechar lacunas do catálogo/runtime: validação isolada por modelo, capacidades, carga sob demanda, troca/descarga segura, providers e mensagens de falha. Verificar download existente, integridade, cancelamento e recuperação sem sobrescrever instalações. | onnx-ai-agent / reasoning | F5-01, F5-02 |
| F5-04 | Consolidar ghost text sobre o coordinator existente: determinístico → lexical → IA, LoadedOnly no fluxo automático, debounce, Tab/Escape/undo, invalidação por edição/contexto e ausência de carga por digitação. | autocomplete-agent / reasoning | F5-02, F5-03 |
| F5-05 | Consolidar ações contextuais do assistente: prévia exata e anterior à inferência do contexto a enviar; proposta limitada ao pedido, diff, confirmação e aplicação na revisão de origem; recusar proposta obsoleta, truncada ou inválida; nenhuma execução MongoDB ao aplicar. | onnx-ai-agent + mongodb-domain-agent / reasoning | F5-02, F5-03 |
| F5-06 | Revisar UI e estados de modelo ausente, carga, falha, fallback, cancelamento, prévia e proposta; teclado/foco e textos nos quatro idiomas; gerar e inspecionar PNGs reais claro/escuro das jornadas alteradas. | ui-ux-agent / balanced | F5-04, F5-05 |
| F5-07 | Fechar matriz automatizada: todas as ações × quatro idiomas, isolamento entre abas e entre chat/autocomplete, falhas/recuperação, privacidade e integridade BSON; distinguir fakes de inferência real. | qa-testing-agent / reasoning | F5-03 a F5-06 |
| F5-08 | Revisar diffs, invariantes e riscos; corrigir achados; executar restore/build/test oficiais e registrar resultados. | code-review-agent / advanced-reasoning | F5-07 |
| F5-09 | Atualizar catálogo, plano, guia, acompanhamento e matriz; ADR/design system somente onde houver mudança de decisão/comportamento; registrar aceite funcional e pendências precisas da Fase 10, atualizar índice offline. | documentation-agent / balanced | F5-08 |

A revisão avançada de F5-08 se justifica por concorrência, privacidade e propostas que modificam consultas. F5-04 e F5-05 podem avançar em paralelo após estabilizar os contratos e o serviço compartilhado. QA acompanha desde F5-01; F5-07 é o fechamento da cobertura, não seu início.

## Critérios obrigatórios de conclusão

1. **Ações e idiomas:** inventário fechado em F5-01, com cenários positivos, negativos e ambíguos para cada ação em `pt-BR`, `en`, `es` e `zh-CN`. Verificar mudança solicitada e preservação do restante por expectativas independentes. Testar respostas inválidas, incompletas e com alterações adicionais conhecidas. Fakes validam contratos/proteções; não comprovam fidelidade de um modelo real.
2. **Privacidade visível:** antes de qualquer inferência manual, a pessoa consegue abrir e revisar uma prévia do snapshot exato que será enviado à IA local; ela reflete consentimentos global/por conexão e opt-ins por painel, mostra se Input JSON está incluído, e nunca revela/envia credenciais ou valores dos resultados. A prévia não aciona inferência e uma edição/política nova exige atualização do snapshot.
3. **Aplicação segura:** diff e confirmação antes da alteração; revisão de origem verificada novamente ao aplicar; undo restaura o documento; nenhuma consulta ou escrita é executada automaticamente. Preservar BSON/Extended JSON, UUIDs e proteções existentes.
4. **Editor:** Tab aceita segundo a política vigente, Escape descarta, undo recupera, edição/seleção/troca/fechamento de aba invalidam respostas antigas. A UI permanece responsiva; o preemptivo usa apenas modelo já pronto e nunca inicia carga ou rede ao digitar. A sugestão determinística deve alcançar o ghost no gate edição→apresentação p95 ≤20 ms, enquanto a rota IA conserva seu debounce e a coalescência (20 teclas abaixo do debounce = nenhuma inferência); o caminho híbrido deve cumprir ambos.
5. **Concorrência:** cancelamento por solicitação; cancelar chat não cancela outra aba. A prioridade interativa pode interromper autocomplete segundo contrato, sem compartilhar CTS ou descarregar indevidamente o modelo. Testar fila, troca de modelo, falha, recuperação e conclusão tardia.
6. **Privacidade:** opt-out impede coleta e uso de contexto vedado, inclusive cache; Input exige opt-in. Não enviar credenciais, ENV resolvido ou valores de resultados ao modelo; não persistir resultados, prompts sensíveis ou credenciais em snapshots/diagnósticos. Falhas de persistência são visíveis e sessão ilegível é preservada; usar o único proprietário LiteDB registrado em DI.
7. **Degradação:** sem modelo, IA desabilitada, capacidade ausente ou backend indisponível, autocomplete determinístico segue funcional e o assistente informa sua condição. Falha de inferência não aparece como sucesso de IA. Automático informa fallback compatível; hardware explícito falha de forma visível sem fallback silencioso.
8. **Apresentação:** textos, placeholders e estados coerentes nos quatro idiomas; foco e teclado cobertos; PNGs gerados pelos testes e inspecionados nos dois temas conforme o design system. Headless não comprova acessibilidade nativa.
9. **Evidência:** verificações oficiais aprovadas, sem novos warnings; falhas preexistentes não ocultadas; nenhum requisito automatizável pendente ou defeito crítico aberto. Atualizações documentais distinguem implementado, automatizado e homologado.

## Validação executada

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

Build integrado com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. Suíte da solução sem restore: 2.742 aprovados (2.699 UnitTests + 43 Benchmarks), 0 falhas, 20 ignorados. Os testes focados da UI/contexto passaram com 73 aprovados; runtime/corridas/override de chat passaram com 4 aprovados. Uma primeira suíte integral expôs regressão no override de ChatModel, corrigida e coberta por teste; a execução integral seguinte passou. Não se alteraram golden files para encobrir falhas.

O restore locked-mode foi concluído usando um `NuGet.Config` temporário apontado ao cache hierárquico local, pois o sandbox bloqueia leitura do `C:\Users\chuke\AppData\Roaming\NuGet\NuGet.Config` e a conexão TLS com NuGet.org não está disponível. Comandos: `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode --configfile <config-temporário> -p:UsedAvaloniaProducts= -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true`; isso preservou o lockfile e usou dependências já em cache, mas não verificou auditoria online de vulnerabilidades. PNGs reais Headless/Skia de chat/proposta e prévia foram gerados nos quatro idiomas × dois temas em `tests/EsilvaSoft.KapibaraStudio.UnitTests/bin/Debug/net10.0/ui-evidence/ai-chat-localization/`; amostras de cada idioma/tema foram inspecionadas. Nenhum modelo externo real foi carregado.

Testes com modelo externo são separados da suíte determinística e executados quando o ambiente estiver disponível; registrar pacote, hash, provider efetivo e limites. A [Fase 10](../phase-10-v0.14.0/README.md) mantém GPU/NPU, fidelidade de todas as ações em modelos reais, revisão linguística de domínio, latência/memória reais, Linux gráfico, leitores de tela e diálogos nativos. Não transformar ausência dessas evidências em alegação de suporte. Não transferir para a Fase 10 falhas dos critérios automatizados acima.

## Registro de execução e aceite automatizado

- **F5-01:** auditoria confirmou que controles inline já estavam nos AXAML; corrigiu-se a rastreabilidade desatualizada sem reimplementar controles.
- **F5-02/F5-05:** opt-in global desligado por padrão, permissão por conexão e opt-in de Input JSON separados; contexto bounded revisado antes de inferir. Alteração de texto, política, instrução ou destino invalida a prévia. Diff é recalculado; proposta no-op/inválida/obsoleta é recusada; risco requer confirmação adicional; aplicação é undoável e não dispara executor Mongo.
- **F5-03:** requisições de chat/autocomplete rastreadas durante fila e carga; troca publica chaves aceitas atomicamente para impedir recarga tardia de modelo superado, preservando override ChatModel enquanto seleção/hardware base não mudou.
- **F5-04:** editor propaga preedit IME e limpa composição ao perder foco/desanexar; deterministic lookup imediata coexiste com fallback lexical/IA debounced. Rajada de 20 teclas gerou 20 lookups locais, zero inferências de IA antes do debounce e apenas um fallback depois. Quatro medições Headless edição→ghost p95 deram 3,85–8,42 ms; máximos de 22–25 ms são outliers. Sem alegação nativa.
- **F5-06/F5-07:** chat, contexto e proposta cobertos em pt-BR/en/es/zh-CN e claro/escuro; opt-out, preview sem dispatch, edição invalidante, revisão, apply, undo, confirmação e mensagens obsoletas verificadas. Rótulos localizados após revisão independente.
- **F5-08:** revisão independente não encontrou outro defeito confirmado após corrigir os rótulos da prévia. Regressão global de troca de modelos foi corrigida e testada.
- **F5-09:** matriz, catálogo, roadmap, guia multimodelo, subfase de autocomplete, ADR-052 e acompanhamento atualizados; índice offline atualizado após validação.

Permanecem para Fase 10: pesos/modelos reais por pacote e provider, fidelidade das ações, GPU/NPU, teclado/IME nativos, leitor de tela, Linux gráfico, MongoDB/mongosh reais e latência/memória em hardware real. A implementação automatizada está aceita; não se declara a IA local como suporte estável.

## Acompanhamento inicial e histórico

- Concluído: todos os itens automatizáveis F5-01 a F5-09 conforme registro de execução acima.
- Limites transferidos: apenas homologações reais enumeradas para a Fase 10; não são falhas automatizáveis pendentes.
- Validação inicial: `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode` não iniciou porque o sandbox negou leitura de `C:\Users\chuke\AppData\Roaming\NuGet\NuGet.Config`. Os assets locais de restore estão presentes; build/testes sem restore e nova tentativa do restore serão registrados quando viáveis.
- Baseline do binário já compilado (`dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`): 2.674 aprovados, 0 falhas e 20 ignorados em 1m27s. Isso é baseline anterior às alterações, não valida código novo.
- Build incremental inicial bloqueou em `MongoTextEditor.cs` com dois CS8765; a frente F5-04 corrigiu as assinaturas anuláveis e confirmou compilação da camada Desktop. Build integrado ainda pendente.
- Bloqueios confirmados: restore por perfil do usuário depende de acesso externo ao workspace; sem evidência de falha de dependência ainda.
- Descobertas, falhas e escalonamentos: registrar durante a execução.

**Estado:** recorte automatizável aceito; a fase continua experimental até validação real aplicável na Fase 10.

## Referências

- [Catálogo funcional](../../03-catalogo-funcional.md), [roadmap](../../09-plano-de-implementacao.md), [ADRs](../../10-decisoes-arquiteturais.md) e [matriz de validação](../../15-matriz-de-validacao.md).
- [ONNX/chat](../../23-onnx-slopcoder.md), [IA multimodelo](../../26-ia-local-multimodelo.md), [preemptivo](../../auto-complite/preemptive-autocomplete.md) e [design system](../../17-design-system-ui-ux.md).
- ADR-027/030/033/037/039/040/043/044 e ADR-052 orientam as decisões existentes; propostas de agentes externos das ADR-046 a ADR-051 pertencem à Fase 5.
