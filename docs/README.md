# Documentação oficial — EsilvaSoft.KapibaraStudio

**MongoDB Database Management & Development Tool.** Studio desktop .NET 10/Avalonia, Windows e Linux, interface pt-BR e licença MIT.

## Comece por aqui

| Divisão | Finalidade | Documento de trabalho |
| --- | --- | --- |
| [MVP](MVP/README.md) | Gestão cotidiana e desenvolvimento assistido | [Requisitos e estado](MVP/02-requisitos-e-estado.md) · [Fases](MVP/fases/README.md) |
| [Remover](Remover/README.md) | Cortes executados, compatibilidade e preservações | [Inventário](Remover/inventario-tecnico.md) · [TODO e evidências](Remover/TODO.md) |
| [Melhorias](Melhorias/README.md) | Evoluções sem fase/versão comprometida | [Conceitos](Melhorias/conceitos.md) |

## Contratos e validação

- [Arquitetura e invariantes](MVP/03-arquitetura-e-invariantes.md): contexto, execução, dados, persistência e composição.
- [Ferramentas de agentes](MVP/contratos/tools-agentes.md): catálogo, argumentos, limites, consentimentos e recusa de consultas/escritas MongoDB.
- [Criação de arquivos](MVP/contratos/arquivos-agentes.md): opt-in, confirmação exata, publicação exclusiva, erros e recuperação.
- [IA local](MVP/contratos/ia-local.md): prompt, contexto, EOS, cancelamento, cache experimental e avaliação.
- [Design system](MVP/04-design-system.md) e [guia de uso](MVP/05-guia-de-uso.md): interface, atalhos, BSON, sessão e fluxos de trabalho.
- [Validação](MVP/06-validacao-e-evidencias.md), [baseline](MVP/09-baseline-de-validacao.md), [distribuição](MVP/07-distribuicao-e-suporte.md) e [decisões](MVP/08-decisoes-vigentes.md).

## Precedência e manutenção

`docs` é a única árvore oficial de documentação. A consolidação de 06/11/2026 removeu o acervo anterior e seu leitor HTML, incorporando aqui os contratos e os resultados necessários ao escopo atual. Não há dependência de redirecionamento para versões antigas. As instruções de [AGENTS.md](../AGENTS.md) continuam aplicáveis.

Estado de implementação e validação fica exclusivamente no catálogo MVP; fases descrevem entregas residuais. Contratos descrevem entradas, limites e recuperação. Evidências mantêm data, ambiente, procedimento, resultado e limitações: uma consolidação documental não declara nova execução, nova homologação ou release.

Atualize os documentos responsáveis quando comportamento ou decisão mudar, use links relativos com caixa exata e verifique os links com `node scripts/check-docs-reader.cjs`. O nome do script é compatibilidade de comando; a verificação atual é de Markdown, sem leitor HTML. Não alterar goldens/asserções para esconder regressões nem transportar resultados antigos como baseline atual.

A [Task-R 1](../tasks/Task-R%201%20-%20Limpeza%20e%20remoção%20de%20código.md) executou os cortes delimitados, preservando Console/aggregate, sessões e providers. Remoção de documentos não autoriza apagar configurações, credenciais, rascunhos ou histórico do usuário.

## Sequência de entregas

| Fase | Versão-alvo | Resultado |
| --- | --- | --- |
| 01 | v0.10.0 | Limpeza aprovada, compatibilidade e base do Studio |
| 02 | v0.11.0 | Autocomplete explícito com IA e agente local, CPU como referência |
| 03 | v0.12.0 | GitHub Copilot e critérios residuais |
| 04 | v0.13.0 | Instalação, atualização e recuperação |
| 05 | v0.14.0 | Polimento e homologação |
| 06 | v0.15.0 | Aceite final do MVP |

As versões são alvos documentais; não renumeram tags, binários, pacotes ou evidências. O plano aprovado em 06/11/2026 fecha o MVP em v0.15.0; v1.0.0 fica fora deste plano. A existência destes documentos não conclui uma fase. Claude, Codex, APIs externas e MCP externo permanecem em Melhorias, com implementações/composição/disponibilidade preservadas, sem criar autenticação paralela.

O [índice de tarefas](../tasks/README.md) organiza uma pasta por fase e múltiplas tarefas executáveis, com dependências, critérios e registro de evidências. Contratos e estado dos requisitos continuam oficiais nesta árvore; tarefas acompanham execução.
