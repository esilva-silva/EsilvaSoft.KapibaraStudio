# Requisitos e estado

Baseline documental em 06/11/2026, com os quinze requisitos, os aceites específicos e as decisões vigentes consolidados neste catálogo. A execução posterior da Task-R 1 registra testes de regressão em [Remover/TODO](../Remover/TODO.md); não substitui as pendências de homologação abaixo.

**Implementação:** implementado = recorte integrado; parcial = fluxo integrado com lacunas; planejado = sem fluxo entregue. **Validação:** evidência existente é limitada ao snapshot e ambiente indicados; pendente nunca equivale a aprovado. A tabela registra síntese, sem transportar contagens antigas como baseline atual.

| ID | Requisito e aceite funcional | Implementação | Validação / lacuna |
| --- | --- | --- | --- |
| MVP-001 | Cadastrar, conectar/desconectar e usar múltiplas conexões com destino explícito. | Implementado | Autenticações/topologias completas e homologação Windows/Linux pendentes. |
| MVP-002 | Navegar bancos, coleções e índices; abrir coleção prepara consulta sem executar. | Implementado | Evidência automatizada existente; jornada nativa multiplataforma pendente. |
| MVP-003 | Filtro, projeção, ordenação, paginação, contagem e distinct com limites. | Implementado | Ampliar evidência real por permissões/topologia e falhas. |
| MVP-004 | Inserir/editar/excluir conservando BSON/UUID e detectando conflito. | Implementado | Homologação real concorrente, somente leitura e cancelamento aplicável pendente. |
| MVP-005 | JSON/árvore e exportação JSON/CSV da página carregada, sem prometer export integral. | Implementado | Fluxo nativo e fidelidade em ambientes suportados pendentes. |
| MVP-006 | Console JavaScript com variáveis, funções, tratamento de dados e aggregate suportado. | Implementado | Não equivale à API inteira de mongosh; homologação real dos caminhos suportados pendente. |
| MVP-007 | Abrir/salvar scripts, histórico, consultas salvas e workspace de uma pasta. | Implementado | Falhas de arquivo, diálogos nativos e preservação entre sessões precisam evidência final. |
| MVP-008 | Autocomplete contextual/ghost determinístico; Ctrl+Espaço sem IA. | Implementado | Evidência automatizada existente; IME/layouts físicos e acessibilidade pendentes. |
| MVP-009 | Criar/remover bancos/coleções e gerir índices, views e validadores. | Implementado | Marco A histórico aceito; RBAC/topologias/UI nativa pendentes no recorte mantido. |
| MVP-010 | Importar JSON/NDJSON/CSV e pacotes lógicos de banco. | Implementado | Evidências históricas de recuperação; homologação final e limitações por ambiente pendentes; não é backup operacional. |
| MVP-011 | Abas independentes, rascunhos recuperáveis, formatação, temas, atalhos e localização. | Implementado | Falha/concorrência/recuperação e experiência nativa devem ser consolidadas no estado final. |
| MVP-012 | Autocomplete IA somente por Ctrl+;, com prévia descartável e cancelamento. | Parcial | Qualidade, desempenho e memória com pacote CPU real pendentes. |
| MVP-013 | Agente local usa contratos/tools seguros e funciona com contexto autorizado. | Parcial | Ciclo completo com modelo real, retenção, falhas e recuperação pendentes. |
| MVP-014 | Copilot com runtime oficial, conversas, propostas e permissões existentes. | Parcial | Aceite funcional de tools existente; conta, sessões, falhas, runtime/ambientes e gates residuais pendentes. |
| MVP-015 | Atualizar Windows/Linux preservando instalação e dados conforme modo. | Parcial | Seleção/integridade/download Windows e pacotes Linux têm evidências parciais; ciclo completo/recuperação pendentes. |

## Fontes de evidência e limites

A [baseline de validação](09-baseline-de-validacao.md) registra o aceite funcional de gestão/transferência, tools e conexão MongoDB; as provas de prompt/catálogo/cache; os ciclos Linux; as falhas de teardown e os limites de cada ambiente. Os procedimentos cotidianos estão no [guia](05-guia-de-uso.md), e entradas/autorizações nos [contratos](contratos/tools-agentes.md). As evidências recentes de cortes estão no [TODO de remoção](../Remover/TODO.md).

Não reabrir aceite funcional Copilot sem regressão demonstrada; completar seus cenários residuais. Suíte verde isolada não explica a intermitência histórica LiteDB. Não inferir Linux nativo de Windows, WSL, contêiner ou Headless.

## Atualização do estado

Toda mudança desta tabela deve apontar requisito, commit/snapshot, ambiente, procedimento, resultado e evidência sanitizada conforme [validação](06-validacao-e-evidencias.md). As fases apontam estes IDs; não mantêm cópias concorrentes do estado.

## Rastreabilidade histórica e fase responsável

IDs históricos amplos não são equivalência integral: incluem recortes adiados/cortados. F05 consolida homologação e F06 aceite final de todos os requisitos.

| ID | Correspondência histórica | Fase nova |
| --- | --- | --- |
| MVP-001 | CON-01/02/03/06/07/08/09 | F01 base; F05 ambientes |
| MVP-002 | DAT-01, IDX-01, CON-08/09 | F01 |
| MVP-003 | DAT-03/09, EDT-01 | F01 |
| MVP-004 | DAT-04/05/06/08, EDT-03 | F01 |
| MVP-005 | TRF-01, EDT-03, UX-01 | F01 |
| MVP-006 | EDT-01/06, AGG-01 no recorte aggregate | F01; não retoma mongosh/editor separado |
| MVP-007 | EDT-04, CON-10 | F01 |
| MVP-008 | EDT-02 | F01; F02 preservação |
| MVP-009 | DAT-02/10/11, IDX-01/02/03/04 no recorte mantido | F01; F05 permissões |
| MVP-010 | TRF-02/03 | F01; F05 real |
| MVP-011 | UX-01/02/03, EDT-04/08 nos recortes aplicáveis | F01 base; F02/F03 sessões; F04 update |
| MVP-012 | ADV-09, EDT-02, ADR-076 | F02 |
| MVP-013 | ADV-09, ADR-056/066/067/070/072 | F02 |
| MVP-014 | ADV-09, F5-GH-01..30, TOOL-01..06/14..18 vigentes | F03 |
| MVP-015 | UX-03, ADR-038/078 | F04 |

Os IDs da coluna histórica servem somente à rastreabilidade. O requisito completo e seu aceite estão definidos na primeira tabela; tools removidas não são entregas pendentes.
