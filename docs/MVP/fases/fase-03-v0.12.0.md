# Fase 3 — v0.12.0: Conclusão do GitHub Copilot

**Situação:** planejada no novo plano; nenhuma execução ou release declarada por este documento. **Requisitos:** MVP-011, MVP-014.

## Objetivo e dependências

Concluir lacunas de conta, conversa, contexto, sessões, tools e experiência com runtime oficial, preservando aceite funcional histórico.

Dependências: IA CPU da Fase 2; reutilizar registry, sessões, permissões e adapters existentes.

## Entregas TODO

- [ ] F03-E01 — Consolidar baseline GH/tool vigente e diagnosticar intermitência LiteDB sem mascarar falha por reexecução isolada.
- [ ] F03-E02 — Validar CLI oficial ausente/inválida/incompatível, override sem fallback, login/logout/expiração, modelos e políticas aplicáveis.
- [ ] F03-E03 — Demonstrar streaming/falha sem replay, duas conversas, snapshot de origem, cancelamento isolado e restart/retomada.
- [ ] F03-E04 — Demonstrar persistência/opt-out, exclusão com retry, revogação/expiração/auditoria e propostas com conflito/Undo nas lacunas existentes.
- [ ] F03-E05 — Inspecionar PNGs e registrar jornadas oficiais/nativas, termos/licenças e lacunas de ambiente sem ampliar tools.

## Evidências existentes e pendências

Em 07/10/2026, o usuário declarou aceitos o funcionamento das 11 tools vigentes e a conexão MongoDB; isso não aprova todos os cenários de falha nem conta/CLI, Atlas, Linux ou acessibilidade. A [baseline](../09-baseline-de-validacao.md) registra rodadas sintéticas, falhas LiteDB e limites. O catálogo e entradas estão no [contrato de tools](../contratos/tools-agentes.md); criação exclusiva no [contrato de arquivos](../contratos/arquivos-agentes.md). Não reabrir o aceite funcional sem regressão e não reintroduzir consulta direta para completar teste antigo.

## Critério de saída

Override inválido não faz fallback. Morte do runtime encerra turno sem replay e evento tardio não reabre conclusão. Duas conversas isolam destino/cancelamento. Falha de reserva impede envio durável; exclusão falha mantém referências para retry. Revogação durante await impede entrega indevida. Proposta respeita conflito/Undo; catálogo exclui tools removidas. Cada GH/tool aplicável tem evidência individual ou pendência bloqueante.

Cada entrega aponta procedimento, esperado/observado e evidência no snapshot final conforme [validação](../06-validacao-e-evidencias.md). Requisitos afetados permanecem coerentes com [arquitetura](../03-arquitetura-e-invariantes.md), [design system](../04-design-system.md) e [estado](../02-requisitos-e-estado.md).

Separar aceite automatizável de serviços/modelos reais e Desktop nativo; cenário obrigatório pendente mantém a entrega aberta. Não repetir trabalho funcional já aceito sem regressão; completar lacunas do recorte atual.

## Limites

Não incluir novos providers, funcionalidades de Melhorias, administração retirada ou pesquisa completa de modelos. Não enfraquecer segurança/privacidade, fidelidade BSON, ownership LiteDB e isolamento de origem para obter aprovação. A versão acima é alvo documental, sem renumerar artefatos históricos.


## Matriz residual obrigatória

| Área | Cenários e resultado exigido |
| --- | --- |
| Conta/runtime | CLI ausente, override inválido/incompatível, login/logout/cancelamento/expiração, precedência de autenticação `user`, modelos elegíveis e política corporativa. Falha de override não escolhe outra instalação nem API key; checagem de conta não faz inferência nem lê credenciais. Registrar SDK/CLI/protocolo/configuração efetivos. |
| Conversa | Streaming, erro e morte de processo encerram turno sem replay; evento tardio não reabre conclusão. Duas conversas isolam destino, captura e cancelamento. Create/resume revalida permissões antes de enviar contexto. |
| Sessão/retenção | Reserva durável antes do envio; falha impede envio. Restart/crash, sessão perdida/ilegível, lock ocupado, falha de gravação e exclusão com retry preservam referências e erro visível. Opt-out e mudança de KeepHistory têm efeito local, sem promessa de exclusão remota. |
| Tools | Para cada tool aplicável: sucesso, checkbox desmarcado, consentimento ausente, grant insuficiente, revogação durante await/auditoria, confirmação negada/expirada, falha de ponte/auditoria, limites/cancelamento/eventos tardios e roundtrip runtime real. Marcar N/A com motivo quando a tool não usa confirmação ou serviço real. Nunca cruzar aprovação de metadados com leitura direta de documentos. |
| Proposta/criação | Revisão, conflito e Undo no buffer; arquivo novo com opt-in, confirmação exata, contenção/links nativos, concorrência, não sobrescrita e efeito possível após falha. |
| Experiência/uso | Métricas oficiais distinguem ausente, zero e parcial; não inferir saldo/cobrança. PNGs dos estados conta/modelo/permissão/contexto/proposta/criação/erro/recovery nos idiomas/temas/dimensões do design system. Confirmar foco, teclado/IME, rolagem e leitor de tela em desktop nativo. |
| Estabilidade/release | Diagnosticar causa de `WORKSPACE-LOG.DB.tmp`/`PENDING.JSON.tmp` em uso; foco ou repetição verde não basta. Quatro RIDs em staging limpo, SBOM oficial, hashes, MIT/notices/transitivas/nativos e ausência de CLI/runtime Copilot, inclusive dentro do single-file. Registrar responsável e decisão de termos. |

Registrar resultado e evidência por cenário/tool, sem somar snapshots diferentes. Fixtures oficiais que ainda chamem `get_document` ou outros nomes removidos são obsoletas; selecionar métodos exatos compatíveis, nunca reativar tool para passar teste. Aceite local automatizado e aceite oficial/nativo são marcos separados; pendência de ambiente não fecha o segundo.
